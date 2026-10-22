using Microsoft.EntityFrameworkCore;
using ResumeApp.Domain.Audit;
using ResumeApp.Domain.Email;
using ResumeApp.Features.Email;

namespace ResumeApp.Infrastructure;

/// <summary>Story 4.2: the admin view of dead and stuck outbox rows, the manual retry, the alert row and the read-only audit log.</summary>
public sealed class OutboxMonitorRepository(AppDbContext db, OutboxMonitorOptions options) : IOutboxMonitor, IAuditLogReader
{
    private const string Alert = EmailOutboxEntry.OutboxAlertTemplate;

    private IQueryable<EmailOutboxEntry> Stuck(DateTime nowUtc)
    {
        var cutoff = nowUtc - options.StuckAfter;
        return db.EmailOutbox.AsNoTracking().Where(e => e.Status == OutboxStatus.Dead || (e.Status == OutboxStatus.Pending && e.NextAttemptAt <= cutoff));
    }

    public async Task<StuckEmailPage> ListStuckAsync(int limit, int offset, DateTime nowUtc, CancellationToken ct)
    {
        var query = Stuck(nowUtc);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(e => e.CreatedAt).ThenBy(e => e.Id).Skip(offset).Take(limit)
            .Select(e => new { e.Id, e.TemplateKey, e.ProfileId, e.RequestId, e.Status, e.CreatedAt, e.Attempts, e.LastErrorCategory }).ToListAsync(ct);
        var items = rows.Select(e => new StuckEmailRow(e.Id, e.TemplateKey, e.ProfileId, e.RequestId, e.Status.ToString().ToLowerInvariant(), e.CreatedAt,
            Math.Max(0, (long)(nowUtc - e.CreatedAt).TotalSeconds), e.Attempts, e.LastErrorCategory)).ToList();
        return new(items, total);
    }

    public async Task<OutboxHealth> GetHealthAsync(DateTime nowUtc, CancellationToken ct)
    {
        var query = Stuck(nowUtc).Where(e => e.TemplateKey != Alert);
        var dead = await query.CountAsync(e => e.Status == OutboxStatus.Dead, ct);
        var stuck = await query.CountAsync(e => e.Status == OutboxStatus.Pending, ct);
        var oldest = await query.MinAsync(e => (DateTime?)e.CreatedAt, ct);
        return new(dead, stuck, oldest is { } o ? Math.Max(0, (long)(nowUtc - o).TotalSeconds) : null);
    }

    // Note: SQL SET expressions read the old row, so the CASE above sees the category before it is cleared.
    public async Task<RetryStatus> RetryAsync(Guid id, Guid actorId, DateTime nowUtc, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // The conditional update is the arbiter: a concurrent retry blocks on the row lock, then matches nothing (409).
        var updated = await db.EmailOutbox.Where(e => e.Id == id && e.Status == OutboxStatus.Dead)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, OutboxStatus.Pending)
                                      .SetProperty(e => e.Attempts, 0)
                                      // claim_count stays monotonic: the provider key is {id}:{claimCount}, so a reset would reuse the first send's key and a
                                      // provider that honours keys could drop the retry. Only a too-many-claims death must reset, or the row would die again at once.
                                      .SetProperty(e => e.ClaimCount, e => e.LastErrorCategory == IEmailOutbox.TooManyClaims ? 0 : e.ClaimCount)
                                      .SetProperty(e => e.LastErrorCategory, (string?)null)
                                      .SetProperty(e => e.LeaseUntil, (DateTime?)null)
                                      .SetProperty(e => e.NextAttemptAt, nowUtc), ct);
        if (updated != 1)
        {
            await tx.RollbackAsync(ct);
            return await db.EmailOutbox.AsNoTracking().AnyAsync(e => e.Id == id, ct) ? RetryStatus.NotDead : RetryStatus.NotFound;
        }

        db.AuditLog.Add(AuditLogEntry.Create(actorId, nowUtc, AuditLogEntry.RetryEmail, AuditLogEntry.EmailOutboxEntity, id));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return RetryStatus.Retried;
    }

    public async Task<bool> EnqueueAlertIfNeededAsync(DateTime nowUtc, CancellationToken ct)
    {
        var health = await GetHealthAsync(nowUtc, ct);
        if (health.Dead + health.StuckPending == 0) return false;

        // Ids only: the counts are read when the mail is built. The daily key makes a repeat within the window a no-op.
        var key = EmailOutboxEntry.OutboxAlertDeduplicationKey(nowUtc);
        var pending = OutboxStatus.Pending.ToString();
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO email_outbox (id, template_key, deduplication_key, status, attempts, claim_count, next_attempt_at, created_at)
            VALUES ({Guid.NewGuid()}, {Alert}, {key}, {pending}, 0, 0, {nowUtc}, {nowUtc})
            ON CONFLICT (deduplication_key) DO NOTHING", ct);
        return inserted == 1;
    }

    public async Task<AuditLogPage> ListAsync(int limit, int offset, CancellationToken ct)
    {
        var total = await db.AuditLog.AsNoTracking().CountAsync(ct);
        var rows = await db.AuditLog.AsNoTracking()
            .OrderByDescending(a => a.At).ThenByDescending(a => a.Id).Skip(offset).Take(limit)
            .Select(a => new AuditLogRow(a.Id, a.At, a.ActorId, db.Users.Where(u => u.Id == a.ActorId).Select(u => u.UserName).FirstOrDefault(), a.Action, a.EntityType, a.EntityId))
            .ToListAsync(ct);
        return new(rows, total);
    }
}
