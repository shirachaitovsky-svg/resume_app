namespace ResumeApp.Features.Email;

/// <summary>Story 4.2: how long a pending row may stay due before it counts as stuck (OUTBOX_STUCK_AFTER_MINUTES, default 15).</summary>
public sealed record OutboxMonitorOptions(TimeSpan StuckAfter)
{
    public static readonly TimeSpan DefaultStuckAfter = TimeSpan.FromMinutes(15);
}

/// <summary>A dead or stuck outbox row: ids, state and counters only. Never an address, text or token.</summary>
public sealed record StuckEmailRow(Guid Id, string TemplateKey, Guid? ProfileId, Guid? RequestId, string Status, DateTime CreatedAt, long AgeSeconds, int Attempts, string? LastErrorCategory);

public sealed record StuckEmailPage(IReadOnlyList<StuckEmailRow> Items, int Total);

public sealed record OutboxHealth(int Dead, int StuckPending, long? OldestAgeSeconds);

public enum RetryStatus { Retried, NotFound, NotDead }

public sealed record AuditLogRow(Guid Id, DateTime At, Guid ActorId, string? ActorName, string Action, string EntityType, Guid EntityId);

public sealed record AuditLogPage(IReadOnlyList<AuditLogRow> Items, int Total);

public interface IOutboxMonitor
{
    Task<StuckEmailPage> ListStuckAsync(int limit, int offset, DateTime nowUtc, CancellationToken ct);

    /// <summary>Counts of dead and stuck-pending rows, the alert rows themselves excluded.</summary>
    Task<OutboxHealth> GetHealthAsync(DateTime nowUtc, CancellationToken ct);

    /// <summary>
    /// Moves a dead row back to pending in one conditional update (counters reset, the deduplication key untouched) and records the audit entry
    /// in the same transaction.
    /// </summary>
    Task<RetryStatus> RetryAsync(Guid id, Guid actorId, DateTime nowUtc, CancellationToken ct);

    /// <summary>Enqueues the manager alert when something is dead or stuck: at most one row per UTC day. Returns true when a row was added.</summary>
    Task<bool> EnqueueAlertIfNeededAsync(DateTime nowUtc, CancellationToken ct);
}

public interface IAuditLogReader
{
    Task<AuditLogPage> ListAsync(int limit, int offset, CancellationToken ct);
}

/// <summary>The "outbox-alert" template: numbers and a link to the admin area only.</summary>
public static class OutboxAlertEmail
{
    public const string Subject = "תקלה בשליחת מיילים באתר";

    public static string AdminLink(string publicBaseUrl) => $"{publicBaseUrl.TrimEnd('/')}/admin";

    public static string Body(string publicBaseUrl, OutboxHealth health)
        => string.Join("\n",
            "זוהתה תקלה בתור המיילים.",
            "",
            $"מיילים שנכשלו סופית: {health.Dead}",
            $"מיילים תקועים: {health.StuckPending}",
            "",
            "לטיפול במסך הניהול:",
            AdminLink(publicBaseUrl),
            "");
}
