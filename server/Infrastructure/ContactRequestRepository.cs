using Microsoft.EntityFrameworkCore;
using ResumeApp.Domain.Contacts;
using ResumeApp.Domain.Email;
using ResumeApp.Domain.Profiles;
using ResumeApp.Features.Contacts;

namespace ResumeApp.Infrastructure;

/// <summary>
/// Contact requests up to email verification (story 3.1). Lock order everywhere: profile row, then request row, then token rows
/// (removal, verification and token issuance all follow it). Paths that take fewer locks: the expiry sweep takes no profile lock and
/// uses SKIP LOCKED on request rows (it never waits on one); outbox dead-ending updates a single request row after the outbox row.
/// </summary>
public sealed class ContactRequestRepository(AppDbContext db) : IContactRequestRepository
{
    public async Task<bool> TryCreateAsync(Guid profileId, ValidContactRequest request, DateTime nowUtc, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Row-share lock: submissions do not block each other, but a concurrent removal (FOR UPDATE) is serialised with this transaction,
        // so a request can never be written for a profile after its open requests were expired.
        var active = ProfileVisibility.Active.ToString();
        var found = await db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM profiles WHERE id = {profileId} AND visibility = {active} FOR SHARE").ToListAsync(ct);
        if (found.Count == 0)
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        var requestId = Guid.NewGuid();
        var awaiting = ContactRequestStatusNames.AwaitingEmailVerification;
        var expired = ContactRequestStatusNames.Expired;
        var expiresAt = nowUtc + ContactRequest.VerificationLifetime;

        // A stale unverified request of this visitor must not hold the unique index for the rest of its sweep delay: close it first.
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE contact_requests SET status = {expired}
            WHERE profile_id = {profileId} AND visitor_email = {request.Email} AND status = {awaiting} AND expires_at <= {nowUtc}", ct);

        // The partial unique index is the arbiter: concurrent and repeated submissions for one (profile, address) collapse into one row.
        // The conflict predicate is the single OpenStatesSql constant (a SQL fragment, so concatenated, never user input).
        var inserted = await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO contact_requests (id, profile_id, visitor_name, visitor_email, reason, status, created_at, expires_at) " +
            "VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}) " +
            "ON CONFLICT (profile_id, visitor_email) WHERE " + ContactRequestStatusNames.OpenStatesSql + " DO NOTHING",
            [requestId, profileId, request.Name, request.Email, request.Reason, awaiting, nowUtc, expiresAt], ct);
        if (inserted == 0)
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        // Ids only: no address, name, reason or token in the outbox (AD-15).
        var key = EmailOutboxEntry.ContactVerificationDeduplicationKey(requestId);
        var pending = OutboxStatus.Pending.ToString();
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO email_outbox (id, template_key, request_id, deduplication_key, status, attempts, claim_count, next_attempt_at, created_at)
            VALUES ({Guid.NewGuid()}, {EmailOutboxEntry.ContactVerificationTemplate}, {requestId}, {key}, {pending}, 0, 0, {nowUtc}, {nowUtc})", ct);

        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<bool> VerifyAsync(string tokenHash, DateTime nowUtc, CancellationToken ct)
    {
        var purpose = PersonalToken.VerifyContactEmailPurpose;

        // Non-locking peek to learn which profile and request to lock first. Not an arbiter: the conditional update below is.
        var peek = await db.PersonalTokens.AsNoTracking()
            .Where(t => t.TokenHash == tokenHash && t.Purpose == purpose && t.RequestId != null)
            .Select(t => new { t.ProfileId, t.RequestId })
            .FirstOrDefaultAsync(ct);
        if (peek?.RequestId is not { } requestId) return false;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var visibility = await db.Database.SqlQuery<string>($"SELECT visibility AS \"Value\" FROM profiles WHERE id = {peek.ProfileId} FOR SHARE").ToListAsync(ct);
        var profileActive = visibility.Count == 1 && visibility[0] == ProfileVisibility.Active.ToString();
        await db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM contact_requests WHERE id = {requestId} FOR UPDATE").ToListAsync(ct);

        // One conditional UPDATE decides which concurrent redemption wins (AD-6, AD-14).
        var consumed = await db.Database.SqlQuery<Guid>($@"
            UPDATE personal_tokens SET used_at = {nowUtc}
            WHERE token_hash = {tokenHash} AND purpose = {purpose} AND request_id = {requestId}
              AND used_at IS NULL AND revoked_at IS NULL AND expires_at > {nowUtc}
            RETURNING request_id AS ""Value""").ToListAsync(ct);
        if (consumed.Count != 1)
        {
            // The token is not usable. If the request itself has run out while still awaiting (we hold its lock), it expires now,
            // whatever the reason the token failed; the answer stays uniform.
            var stale = ContactRequestStatusNames.AwaitingEmailVerification;
            var closed = ContactRequestStatusNames.Expired;
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE contact_requests SET status = {closed} WHERE id = {requestId} AND status = {stale} AND expires_at <= {nowUtc}", ct);
            await tx.CommitAsync(ct);
            return false;
        }

        var awaiting = ContactRequestStatusNames.AwaitingEmailVerification;
        var submitted = ContactRequestStatusNames.Submitted;
        var moved = profileActive
            ? await db.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE contact_requests SET status = {submitted}, verified_at = {nowUtc}
                WHERE id = {requestId} AND status = {awaiting} AND expires_at > {nowUtc}", ct)
            : 0;
        if (moved == 1)
        {
            // Story 3.2 (AD-4): the admin notification row is written in the very transaction of the move to submitted, so a request
            // never becomes submitted without it (a failed write throws and rolls everything back, token consumption included).
            // Ids only, no name, address or reason. The unique key makes a repeat or a race a no-op.
            var key = EmailOutboxEntry.ContactNotificationDeduplicationKey(requestId);
            var pending = OutboxStatus.Pending.ToString();
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO email_outbox (id, template_key, request_id, deduplication_key, status, attempts, claim_count, next_attempt_at, created_at)
                VALUES ({Guid.NewGuid()}, {EmailOutboxEntry.ContactRequestNotificationTemplate}, {requestId}, {key}, {pending}, 0, 0, {nowUtc}, {nowUtc})
                ON CONFLICT (deduplication_key) DO NOTHING", ct);
            await tx.CommitAsync(ct);
            return true;
        }

        // A valid token on a request that ran out, or whose profile is no longer Active: the request expires, the answer stays uniform.
        var expired = ContactRequestStatusNames.Expired;
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE contact_requests SET status = {expired} WHERE id = {requestId} AND status = {awaiting}", ct);
        await tx.CommitAsync(ct);
        return false;
    }

    public const int SweepBatch = 500;

    public async Task<int> ExpireStaleAsync(DateTime nowUtc, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var awaiting = ContactRequestStatusNames.AwaitingEmailVerification;
        var expired = ContactRequestStatusNames.Expired;
        var purpose = PersonalToken.VerifyContactEmailPurpose;

        // Bounded and non-blocking: SKIP LOCKED means the sweep never waits on a request row held by a verification, a token issuance,
        // a removal or another instance (it takes no profile lock, and never waits, so it cannot join a lock cycle). Conditional (AD-14).
        var ids = await db.Database.SqlQuery<Guid>($@"
            UPDATE contact_requests SET status = {expired}
            WHERE status = {awaiting} AND id IN (
                SELECT id FROM contact_requests WHERE status = {awaiting} AND expires_at <= {nowUtc}
                ORDER BY id LIMIT {SweepBatch} FOR UPDATE SKIP LOCKED)
            RETURNING id AS ""Value""").ToListAsync(ct);
        if (ids.Count > 0)
        {
            await db.PersonalTokens
                .Where(t => t.Purpose == purpose && t.UsedAt == null && t.RevokedAt == null && t.RequestId != null && ids.Contains(t.RequestId.Value))
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, nowUtc), ct);
        }
        await tx.CommitAsync(ct);
        return ids.Count;
    }
}
