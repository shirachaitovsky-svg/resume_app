namespace ResumeApp.Features.Email;

/// <summary>
/// A claimed outbox row: ids and the claim marker only. <paramref name="LeaseUntil"/> (as stored) is the ownership marker:
/// results are written only while the row still carries exactly this lease.
/// </summary>
public sealed record ClaimedEmail(Guid Id, string TemplateKey, Guid? ProfileId, int Attempts, int ClaimCount, DateTime CreatedAt, DateTime LeaseUntil, Guid? RevisionId = null, Guid? RequestId = null);

public enum ReturnNoteStatus { Ok, RevisionMissing, NotReturned, EmptyNote }

/// <summary>The note of a returned revision, or why none can be sent.</summary>
public sealed record ReturnNoteResult(ReturnNoteStatus Status, string? Note = null);

public sealed record DeadEmailRow(Guid Id, string TemplateKey, int Attempts, string? LastErrorCategory, DateTime CreatedAt);

public sealed record OutboxOverview(int Pending, int Sent, int Dead, IReadOnlyList<DeadEmailRow> DeadItems);

/// <summary>Recipient and cohort name of a returned-for-correction mail, read at send time from the private tables.</summary>
public sealed record ReturnedMailRecipient(string Email, string CohortName);

/// <summary>The visitor to verify, read at send time from the contact request (never stored in the outbox).</summary>
public sealed record ContactVerificationRecipient(string Email, string Name, Guid ProfileId, bool ProfileActive);

/// <summary>What the manager notification mentions, read at send time: verification time, cohort and the requested profile display name.</summary>
public sealed record ContactNotificationInfo(DateTime VerifiedAtUtc, string CohortName, string ProfileName);

/// <summary>
/// The request and participant a decision mail is about, read at send time. <see cref="ParticipantEmail"/> is null when the private row is gone.
/// </summary>
public sealed record ContactDecisionRecipient(string? ParticipantEmail, string VisitorName, string Reason, Guid ProfileId, bool ProfileActive, bool RequestSent);

/// <summary>
/// Everything a delivery needs, read at send time. The participant's address and phone exist only inside this record and the mail body.
/// </summary>
public sealed record ContactDeliveryInfo(
    ResumeApp.Domain.Contacts.ContactRequestStatus Status, DateTime? ManagerApprovedAt, DateTime? ParticipantDecidedAt, int? ParticipantContactVersion,
    string VisitorEmail, bool ProfileActive, string? ParticipantEmail, string? ParticipantPhone, int? ContactVersion, string? ParticipantName);

public interface IEmailOutbox
{
    /// <summary>Story 3.4: null when the request does not exist.</summary>
    Task<ContactDeliveryInfo?> GetContactDeliveryAsync(Guid requestId, CancellationToken ct);

    /// <summary>
    /// Story 3.4: like <see cref="MarkSentAsync"/> (same ownership condition) and, in the same transaction, moves the request
    /// <c>participant_approved</c> to <c>delivered</c>. Only ever called after a successful send.
    /// </summary>
    Task<int> MarkDeliverySentAsync(ClaimedEmail claim, DateTime nowUtc, CancellationToken ct);

    /// <summary>Story 3.3: null when the request does not exist; otherwise the facts the decision mail needs (the participant's address comes from ParticipantPrivate now).</summary>
    Task<ContactDecisionRecipient?> GetContactDecisionRecipientAsync(Guid requestId, CancellationToken ct);

    /// <summary>The visitor of a contact request that still awaits verification; null when the request is missing or no longer awaiting (removed, expired, verified).</summary>
    Task<ContactVerificationRecipient?> GetContactVerificationRecipientAsync(Guid requestId, DateTime nowUtc, CancellationToken ct);

    /// <summary>The request a notification is about; null when it is missing or was never verified.</summary>
    Task<ContactNotificationInfo?> GetContactNotificationAsync(Guid requestId, CancellationToken ct);

    public const int MaxClaims = 10;
    public const string TooManyClaims = "too-many-claims";

    /// <summary>
    /// Claims ONE due row in one atomic statement (<c>FOR UPDATE SKIP LOCKED</c>), sets its lease and increments its claim count.
    /// Rows whose lease expired (the claiming instance crashed) are claimable again; rows already claimed
    /// <see cref="MaxClaims"/> times are first moved to dead (<see cref="TooManyClaims"/>). Null when nothing is due.
    /// </summary>
    Task<ClaimedEmail?> ClaimNextAsync(TimeSpan lease, DateTime nowUtc, CancellationToken ct);

    /// <summary>The recipient, read at send time. Null when the profile is not Active or has no private row.</summary>
    Task<ReturnedMailRecipient?> GetReturnedMailRecipientAsync(Guid profileId, CancellationToken ct);

    /// <summary>The note of a returned revision, read at send time; otherwise the reason it cannot be sent (missing, not returned, empty note).</summary>
    Task<ReturnNoteResult> GetReturnNoteAsync(Guid revisionId, CancellationToken ct);

    /// <summary>Marks sent only if the row is still Pending and still holds this claim's lease. Returns rows affected (0 = lost ownership).</summary>
    Task<int> MarkSentAsync(ClaimedEmail claim, DateTime nowUtc, CancellationToken ct);

    /// <summary>
    /// Records a failed send, with the same ownership condition as <see cref="MarkSentAsync"/>: increments attempts when
    /// <paramref name="countAttempt"/>, schedules the next attempt, and moves the row to <c>dead</c> once
    /// <paramref name="maxAttempts"/> failed attempts are reached or <paramref name="terminal"/> is set. Returns rows affected.
    /// </summary>
    Task<int> RecordFailureAsync(ClaimedEmail claim, string errorCategory, bool countAttempt, bool terminal, int maxAttempts, DateTime nextAttemptAt, CancellationToken ct);

    Task<OutboxOverview> GetOverviewAsync(int limit, int offset, CancellationToken ct);
}
