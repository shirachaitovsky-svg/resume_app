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
            await using (var sweep = new NpgsqlCommand(@"
                UPDATE email_outbox SET status = @dead, last_error_category = @cat, lease_until = NULL
                WHERE status = @pending AND claim_count >= @max AND (lease_until IS NULL OR lease_until < @now)", conn))
            {
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
                RETURNING id, template_key, profile_id, attempts, claim_count, created_at, lease_until", conn);
            cmd.Parameters.AddWithValue("pending", S(OutboxStatus.Pending));
            cmd.Parameters.Add(new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = nowUtc });
            cmd.Parameters.Add(new NpgsqlParameter("lease", NpgsqlDbType.TimestampTz) { Value = nowUtc + lease });

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return null;
            return new ClaimedEmail(reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetGuid(2), reader.GetInt32(3),
                reader.GetInt32(4), reader.GetDateTime(5), reader.GetDateTime(6));
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
        return await db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE email_outbox
            SET attempts = attempts + {inc},
                status = CASE WHEN {terminal} OR attempts + {inc} >= {maxAttempts} THEN {S(OutboxStatus.Dead)} ELSE {S(OutboxStatus.Pending)} END,
                next_attempt_at = {nextAttemptAt},
                lease_until = NULL,
                last_error_category = {category}
            WHERE id = {id} AND status = {S(OutboxStatus.Pending)} AND lease_until = {lease}", ct);
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
}
