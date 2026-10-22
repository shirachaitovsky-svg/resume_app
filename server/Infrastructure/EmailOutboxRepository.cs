using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using ResumeApp.Domain.Email;
using ResumeApp.Domain.Profiles;
using ResumeApp.Features.Email;

namespace ResumeApp.Infrastructure;

public sealed class EmailOutboxRepository(AppDbContext db) : IEmailOutbox
{
    private static string S(OutboxStatus status) => status.ToString(); // stored as the enum name (HasConversion<string>)

    public async Task<int> EnqueueEditLinksAsync(string normalizedEmail, DateTime nowUtc, CancellationToken ct)
    {
        // Only Active profiles; a profile with no approved revision yet is still included (AD-18, story 2.1).
        var profileIds = await (from pp in db.ParticipantPrivates.AsNoTracking()
                                where pp.Email == normalizedEmail
                                join p in db.Profiles.AsNoTracking() on pp.ProfileId equals p.Id
                                where p.Visibility == ProfileVisibility.Active
                                select p.Id).ToListAsync(ct);

        var written = 0;
        foreach (var profileId in profileIds)
        {
            var key = EmailOutboxEntry.EditLinkDeduplicationKey(profileId, nowUtc);
            // The unique deduplication key makes concurrent and repeated requests in the same hour collapse into one row.
            written += await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO email_outbox (id, template_key, profile_id, deduplication_key, status, attempts, claim_count, next_attempt_at, created_at)
                VALUES ({Guid.NewGuid()}, {EmailOutboxEntry.EditLinkTemplate}, {profileId}, {key}, {S(OutboxStatus.Pending)}, 0, 0, {nowUtc}, {nowUtc})
                ON CONFLICT (deduplication_key) DO NOTHING", ct);
        }
        return written;
    }

    public async Task<ClaimedEmail?> ClaimNextAsync(TimeSpan lease, DateTime nowUtc, CancellationToken ct)
    {
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        var opened = false;
        if (conn.State != System.Data.ConnectionState.Open) { await conn.OpenAsync(ct); opened = true; }
        try
        {
            // A poison row (one that keeps crashing its worker) is claimed again and again because a claim is not an attempt:
            // after MaxClaims claims it is moved to dead before anything can claim it once more.
            // A dead contact-verification row also closes its awaiting request (same statement), so the visitor can ask again.
            await using (var sweep = new NpgsqlCommand(@"
                WITH dead AS (
                    UPDATE email_outbox SET status = @dead, last_error_category = @cat, lease_until = NULL
                    WHERE status = @pending AND claim_count >= @max AND (lease_until IS NULL OR lease_until < @now)
                    RETURNING template_key, request_id)
                UPDATE contact_requests SET status = @expired
                WHERE status = @awaiting AND id IN (SELECT request_id FROM dead WHERE template_key = @template)", conn))
            {
                sweep.Parameters.AddWithValue("expired", ResumeApp.Domain.Contacts.ContactRequestStatusNames.Expired);
                sweep.Parameters.AddWithValue("awaiting", ResumeApp.Domain.Contacts.ContactRequestStatusNames.AwaitingEmailVerification);
                sweep.Parameters.AddWithValue("template", EmailOutboxEntry.ContactVerificationTemplate);
                sweep.Parameters.AddWithValue("dead", S(OutboxStatus.Dead));
                sweep.Parameters.AddWithValue("cat", IEmailOutbox.TooManyClaims);
                sweep.Parameters.AddWithValue("pending", S(OutboxStatus.Pending));
                sweep.Parameters.AddWithValue("max", IEmailOutbox.MaxClaims);
                sweep.Parameters.Add(new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = nowUtc });
                await sweep.ExecuteNonQueryAsync(ct);
            }

            // One statement: pick one due row with SKIP LOCKED, take the lease and count the claim. Rows whose lease expired are due again.
            // The stored lease_until (read back from the database) is the ownership marker for MarkSent / RecordFailure.
            await using var cmd = new NpgsqlCommand(@"
                UPDATE email_outbox SET lease_until = @lease, claim_count = claim_count + 1
                WHERE id = (
                    SELECT id FROM email_outbox
                    WHERE status = @pending AND next_attempt_at <= @now AND (lease_until IS NULL OR lease_until < @now)
                    ORDER BY next_attempt_at, id
                    LIMIT 1
                    FOR UPDATE SKIP LOCKED)
                RETURNING id, template_key, profile_id, attempts, claim_count, created_at, lease_until, revision_id, request_id", conn);
            cmd.Parameters.AddWithValue("pending", S(OutboxStatus.Pending));
            cmd.Parameters.Add(new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = nowUtc });
            cmd.Parameters.Add(new NpgsqlParameter("lease", NpgsqlDbType.TimestampTz) { Value = nowUtc + lease });

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return null;
            return new ClaimedEmail(reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetGuid(2), reader.GetInt32(3),
                reader.GetInt32(4), reader.GetDateTime(5), reader.GetDateTime(6), reader.IsDBNull(7) ? null : reader.GetGuid(7), reader.IsDBNull(8) ? null : reader.GetGuid(8));
        }
        finally
        {
            if (opened) await conn.CloseAsync();
        }
    }

    public async Task<EditLinkRecipient?> GetEditLinkRecipientAsync(Guid profileId, CancellationToken ct)
        => await (from pp in db.ParticipantPrivates.AsNoTracking()
                  where pp.ProfileId == profileId
                  join p in db.Profiles.AsNoTracking() on pp.ProfileId equals p.Id
                  where p.Visibility == ProfileVisibility.Active
                  join c in db.Cohorts.AsNoTracking() on p.CohortId equals c.Id
                  select new EditLinkRecipient(pp.Email, c.Name)).FirstOrDefaultAsync(ct);

    public async Task<ContactVerificationRecipient?> GetContactVerificationRecipientAsync(Guid requestId, DateTime nowUtc, CancellationToken ct)
        => await db.ContactRequests.AsNoTracking()
            .Where(r => r.Id == requestId && r.Status == ResumeApp.Domain.Contacts.ContactRequestStatus.AwaitingEmailVerification && r.ExpiresAt > nowUtc)
            .Select(r => new ContactVerificationRecipient(r.VisitorEmail, r.VisitorName, r.ProfileId,
                db.Profiles.Any(p => p.Id == r.ProfileId && p.Visibility == ProfileVisibility.Active)))
            .FirstOrDefaultAsync(ct);

    public async Task<ContactDecisionRecipient?> GetContactDecisionRecipientAsync(Guid requestId, CancellationToken ct)
        => await db.ContactRequests.AsNoTracking()
            .Where(r => r.Id == requestId)
            .Select(r => new ContactDecisionRecipient(
                db.ParticipantPrivates.Where(p => p.ProfileId == r.ProfileId).Select(p => p.Email).FirstOrDefault(),
                r.VisitorName, r.Reason, r.ProfileId,
                db.Profiles.Any(p => p.Id == r.ProfileId && p.Visibility == ProfileVisibility.Active),
                r.Status == ResumeApp.Domain.Contacts.ContactRequestStatus.SentToParticipant))
            .FirstOrDefaultAsync(ct);

    public async Task<ContactDeliveryInfo?> GetContactDeliveryAsync(Guid requestId, CancellationToken ct)
        => await db.ContactRequests.AsNoTracking()
            .Where(r => r.Id == requestId)
            .Select(r => new ContactDeliveryInfo(r.Status, r.ManagerApprovedAt, r.ParticipantDecidedAt, r.ParticipantContactVersion, r.VisitorEmail,
                db.Profiles.Any(p => p.Id == r.ProfileId && p.Visibility == ProfileVisibility.Active),
                db.ParticipantPrivates.Where(p => p.ProfileId == r.ProfileId).Select(p => p.Email).FirstOrDefault(),
                db.ParticipantPrivates.Where(p => p.ProfileId == r.ProfileId).Select(p => p.Phone).FirstOrDefault(),
                db.ParticipantPrivates.Where(p => p.ProfileId == r.ProfileId).Select(p => (int?)p.ContactVersion).FirstOrDefault(),
                (from v in db.ProfileRevisions
                 where v.ProfileId == r.ProfileId
                 orderby (db.Profiles.Any(p => p.Id == r.ProfileId && p.ApprovedRevisionId == v.Id) ? 0 : 1), v.SubmittedAt descending, v.Id descending
                 select v.Name).FirstOrDefault()))
            .FirstOrDefaultAsync(ct);

    public async Task<int> MarkDeliverySentAsync(ClaimedEmail claim, DateTime nowUtc, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var affected = await MarkSentAsync(claim, nowUtc, ct);
        if (affected > 0 && claim.RequestId is { } requestId)
        {
            var approved = ResumeApp.Domain.Contacts.ContactRequestStatusNames.ParticipantApproved;
            var delivered = ResumeApp.Domain.Contacts.ContactRequestStatusNames.Delivered;
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE contact_requests SET status = {delivered} WHERE id = {requestId} AND status = {approved}", ct);
        }
        await tx.CommitAsync(ct);
        return affected;
    }

    public async Task<ContactNotificationInfo?> GetContactNotificationAsync(Guid requestId, CancellationToken ct)
    {
        // Verified requests only, whatever state they moved to since (a removal expires a verified request, and the manager is still told).
        var row = await db.ContactRequests.AsNoTracking()
            .Where(r => r.Id == requestId && r.VerifiedAt != null)
            .Select(r => new
            {
                VerifiedAt = r.VerifiedAt!.Value,
                CohortName = db.Profiles.Where(p => p.Id == r.ProfileId).Join(db.Cohorts, p => p.CohortId, c => c.Id, (p, c) => c.Name).FirstOrDefault(),
                // The approved revision (the pointer) when it exists, otherwise the latest revision: its display name, nothing private.
                ProfileName = (from v in db.ProfileRevisions
                               where v.ProfileId == r.ProfileId
                               orderby (db.Profiles.Any(p => p.Id == r.ProfileId && p.ApprovedRevisionId == v.Id) ? 0 : 1), v.SubmittedAt descending, v.Id descending
                               select v.Name).FirstOrDefault(),
            }).FirstOrDefaultAsync(ct);
        return row is null ? null : new ContactNotificationInfo(row.VerifiedAt, row.CohortName ?? string.Empty, row.ProfileName ?? string.Empty);
    }

    public async Task<ReturnNoteResult> GetReturnNoteAsync(Guid revisionId, CancellationToken ct)
    {
        var rev = await db.ProfileRevisions.AsNoTracking().Where(r => r.Id == revisionId).Select(r => new { r.Status, r.ReturnNote }).FirstOrDefaultAsync(ct);
        if (rev is null) return new(ReturnNoteStatus.RevisionMissing);
        if (rev.Status != RevisionStatus.Returned) return new(ReturnNoteStatus.NotReturned);
        return string.IsNullOrWhiteSpace(rev.ReturnNote) ? new(ReturnNoteStatus.EmptyNote) : new(ReturnNoteStatus.Ok, rev.ReturnNote);
    }

    // Ownership: the row must still be Pending and still carry exactly the lease this claim took. A stale worker
    // (its lease expired and another claimed the row) therefore changes nothing.
    public async Task<int> MarkSentAsync(ClaimedEmail claim, DateTime nowUtc, CancellationToken ct)
        => await db.EmailOutbox.Where(e => e.Id == claim.Id && e.Status == OutboxStatus.Pending && e.LeaseUntil == claim.LeaseUntil)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, OutboxStatus.Sent)
                                      .SetProperty(e => e.SentAt, nowUtc)
                                      .SetProperty(e => e.LeaseUntil, (DateTime?)null)
                                      .SetProperty(e => e.LastErrorCategory, (string?)null), ct);

    public async Task<int> RecordFailureAsync(ClaimedEmail claim, string errorCategory, bool countAttempt, bool terminal, int maxAttempts, DateTime nextAttemptAt, CancellationToken ct)
    {
        var inc = countAttempt ? 1 : 0;
        var id = claim.Id;
        var lease = claim.LeaseUntil;
        var category = errorCategory.Length > EmailOutboxEntry.ErrorCategoryMaxLength ? errorCategory[..EmailOutboxEntry.ErrorCategoryMaxLength] : errorCategory;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var affected = await db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE email_outbox
            SET attempts = attempts + {inc},
                status = CASE WHEN {terminal} OR attempts + {inc} >= {maxAttempts} THEN {S(OutboxStatus.Dead)} ELSE {S(OutboxStatus.Pending)} END,
                next_attempt_at = {nextAttemptAt},
                lease_until = NULL,
                last_error_category = {category}
            WHERE id = {id} AND status = {S(OutboxStatus.Pending)} AND lease_until = {lease}", ct);
        if (affected > 0)
        {
            // A contact-verification row that ended dead closes its awaiting request in the same transaction (the visitor may ask again).
            var dead = S(OutboxStatus.Dead);
            var template = EmailOutboxEntry.ContactVerificationTemplate;
            var expired = ResumeApp.Domain.Contacts.ContactRequestStatusNames.Expired;
            var awaiting = ResumeApp.Domain.Contacts.ContactRequestStatusNames.AwaitingEmailVerification;
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE contact_requests SET status = {expired}
                WHERE status = {awaiting} AND id IN (SELECT request_id FROM email_outbox WHERE id = {id} AND status = {dead} AND template_key = {template})", ct);
        }
        await tx.CommitAsync(ct);
        return affected;
    }

    public async Task<OutboxOverview> GetOverviewAsync(int limit, int offset, CancellationToken ct)
    {
        var counts = await db.EmailOutbox.AsNoTracking().GroupBy(e => e.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        int Of(OutboxStatus s) => counts.FirstOrDefault(c => c.Status == s)?.Count ?? 0;
        var dead = await db.EmailOutbox.AsNoTracking().Where(e => e.Status == OutboxStatus.Dead)
            .OrderByDescending(e => e.CreatedAt).ThenBy(e => e.Id).Skip(offset).Take(limit)
            .Select(e => new DeadEmailRow(e.Id, e.TemplateKey, e.Attempts, e.LastErrorCategory, e.CreatedAt)).ToListAsync(ct);
        return new(Of(OutboxStatus.Pending), Of(OutboxStatus.Sent), Of(OutboxStatus.Dead), dead);
    }
}

public sealed class PersonalTokenIssuer(AppDbContext db) : IPersonalTokenIssuer
{
    public async Task<string> IssueEditTokenAsync(Guid profileId, DateTime nowUtc, CancellationToken ct)
    {
        var token = PersonalTokens.Generate();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Serialise issuance per profile (as approval does), so exactly one unrevoked token survives concurrent sends.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM profiles WHERE id = {profileId} FOR UPDATE", ct);
        await db.PersonalTokens
            .Where(t => t.ProfileId == profileId && t.Purpose == PersonalToken.EditProfilePurpose && t.UsedAt == null && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, nowUtc), ct);
        db.PersonalTokens.Add(PersonalToken.Create(PersonalTokens.Hash(token), PersonalToken.EditProfilePurpose, profileId, nowUtc, PersonalToken.EditProfileLifetime));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return token;
    }

    public async Task<string?> IssueContactVerificationTokenAsync(Guid requestId, Guid profileId, DateTime nowUtc, CancellationToken ct)
    {
        var token = PersonalTokens.Generate();
        var purpose = PersonalToken.VerifyContactEmailPurpose;
        var awaiting = ResumeApp.Domain.Contacts.ContactRequestStatusNames.AwaitingEmailVerification;
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Lock order (profile, request, tokens) as in verification and removal. Serialises issuance per request, so one unrevoked token survives.
        var visibility = await db.Database.SqlQuery<string>($"SELECT visibility AS \"Value\" FROM profiles WHERE id = {profileId} FOR SHARE").ToListAsync(ct);
        if (visibility.Count != 1 || visibility[0] != ProfileVisibility.Active.ToString())
        {
            await tx.RollbackAsync(ct);
            return null;
        }
        var stillAwaiting = await db.Database.SqlQuery<Guid>($@"
            SELECT id AS ""Value"" FROM contact_requests WHERE id = {requestId} AND status = {awaiting} AND expires_at > {nowUtc} FOR UPDATE").ToListAsync(ct);
        if (stillAwaiting.Count == 0)
        {
            await tx.RollbackAsync(ct);
            return null;
        }

        await db.PersonalTokens
            .Where(t => t.RequestId == requestId && t.Purpose == purpose && t.UsedAt == null && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, nowUtc), ct);
        db.PersonalTokens.Add(PersonalToken.Create(PersonalTokens.Hash(token), purpose, profileId, nowUtc, PersonalToken.VerifyContactEmailLifetime, requestId));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return token;
    }

    public async Task<string?> IssueContactDecisionTokenAsync(Guid requestId, Guid profileId, DateTime nowUtc, CancellationToken ct)
    {
        var token = PersonalTokens.Generate();
        var purpose = PersonalToken.ContactDecisionPurpose;
        var sent = ResumeApp.Domain.Contacts.ContactRequestStatusNames.SentToParticipant;
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Lock order (profile, request, tokens), as in verification, decision and removal. Serialises issuance per request.
        var visibility = await db.Database.SqlQuery<string>($"SELECT visibility AS \"Value\" FROM profiles WHERE id = {profileId} FOR SHARE").ToListAsync(ct);
        if (visibility.Count != 1 || visibility[0] != ProfileVisibility.Active.ToString())
        {
            await tx.RollbackAsync(ct);
            return null;
        }
        var stillSent = await db.Database.SqlQuery<Guid>($@"
            SELECT id AS ""Value"" FROM contact_requests WHERE id = {requestId} AND profile_id = {profileId} AND status = {sent} FOR UPDATE").ToListAsync(ct);
        if (stillSent.Count == 0)
        {
            await tx.RollbackAsync(ct);
            return null;
        }

        await db.PersonalTokens
            .Where(t => t.RequestId == requestId && t.Purpose == purpose && t.UsedAt == null && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, nowUtc), ct);
        db.PersonalTokens.Add(PersonalToken.Create(PersonalTokens.Hash(token), purpose, profileId, nowUtc, PersonalToken.ContactDecisionLifetime, requestId));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return token;
    }

    public async Task<Guid?> TryRedeemEditTokenAsync(string? token, DateTime nowUtc, CancellationToken ct)
    {
        if (!PersonalTokens.HasValidShape(token)) return null;
        var hash = PersonalTokens.Hash(token!);
        var purpose = PersonalToken.EditProfilePurpose;
        var active = ProfileVisibility.Active.ToString();

        // One conditional UPDATE is the arbiter (AD-6, AD-14): concurrent redemptions of the same token match one row at most.
        var rows = await db.Database.SqlQuery<Guid>($@"
            UPDATE personal_tokens AS t SET used_at = {nowUtc}
            FROM profiles AS p
            WHERE t.profile_id = p.id AND p.visibility = {active}
              AND t.token_hash = {hash} AND t.purpose = {purpose}
              AND t.used_at IS NULL AND t.revoked_at IS NULL AND t.expires_at > {nowUtc}
            RETURNING t.profile_id AS ""Value""").ToListAsync(ct);
        return rows.Count == 1 ? rows[0] : null;
    }
}
