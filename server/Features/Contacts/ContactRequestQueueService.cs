using ResumeApp.Domain.Contacts;

namespace ResumeApp.Features.Contacts;

/// <summary>One verified request in the admin queue (story 3.2). Never carries a participant's email or phone.</summary>
public sealed record ContactRequestQueueItem(
    Guid RequestId, string VisitorName, string VisitorEmail, string Reason, Guid ProfileId, string ProfileName, string ProductName,
    string CohortName, string ProfileVisibility, DateTime CreatedAt, DateTime VerifiedAt, string Status,
    string? ParticipantDecision, DateTime? ParticipantDecidedAt, DateTime? ManagerApprovedAt, string NotificationStatus,
    string DeliveryStatus = ContactRequestQueueService.NotificationNone, string? DeliveryErrorCategory = null);

public sealed record ContactRequestQueuePage(IReadOnlyList<ContactRequestQueueItem> Items, int Total);

public interface IContactRequestQueueRepository
{
    /// <summary>Verified requests only (verified_at set) in the given states, oldest verification first (then id).</summary>
    Task<ContactRequestQueuePage> ListAsync(IReadOnlyCollection<ContactRequestStatus> statuses, int limit, int offset, CancellationToken ct);

    /// <summary>One verified request; null when it does not exist or was never verified.</summary>
    Task<ContactRequestQueueItem?> GetAsync(Guid requestId, CancellationToken ct);
}

/// <summary>The manager's queue of verified contact requests (FR-14). Read only: no write action exists yet.</summary>
public sealed class ContactRequestQueueService(IContactRequestQueueRepository repository)
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 100;
    public const string Placeholder = "(ללא שם)";
    /// <summary>notificationStatus when no notification row exists; otherwise pending, sent or dead (the outbox row of the notification).</summary>
    public const string NotificationNone = "none";

    private static readonly ContactRequestStatus[] Open =
        [ContactRequestStatus.Submitted, ContactRequestStatus.SentToParticipant, ContactRequestStatus.ParticipantApproved];

    /// <summary>
    /// <c>open</c> (the default) is submitted, sent_to_participant and participant_approved; <c>all</c> is every verified request.
    /// awaiting_email_verification is never a filter value: those requests are never exposed. Returns false for anything else.
    /// </summary>
    public static bool TryParseStatusFilter(string? text, out IReadOnlyCollection<ContactRequestStatus> statuses)
    {
        statuses = Open;
        if (text is null or "open") return true;
        if (text == "all")
        {
            statuses = Enum.GetValues<ContactRequestStatus>().Where(s => s != ContactRequestStatus.AwaitingEmailVerification).ToArray();
            return true;
        }
        if (text == ContactRequestStatusNames.AwaitingEmailVerification) return false;
        foreach (var s in Enum.GetValues<ContactRequestStatus>())
            if (ContactRequestStatusNames.ToWire(s) == text) { statuses = [s]; return true; }
        return false;
    }

    public Task<ContactRequestQueuePage> ListAsync(IReadOnlyCollection<ContactRequestStatus> statuses, int? limit, int? offset, CancellationToken ct)
        => repository.ListAsync(statuses, Math.Min(limit ?? DefaultLimit, MaxLimit), offset ?? 0, ct);

    public Task<ContactRequestQueueItem?> GetAsync(string? requestId, CancellationToken ct)
        => Guid.TryParseExact(requestId, "D", out var id) ? repository.GetAsync(id, ct) : Task.FromResult<ContactRequestQueueItem?>(null);
}
