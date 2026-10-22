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

/// <summary>Recipient and cohort name, read at send time from the private tables.</summary>
public sealed record EditLinkRecipient(string Email, string CohortName);

/// <summary>The visitor to verify, read at send time from the contact request (never stored in the outbox).</summary>
public sealed record ContactVerificationRecipient(string Email, string Name, Guid ProfileId, bool ProfileActive);

public interface IEmailOutbox
{
    /// <summary>The visitor of a contact request that still awaits verification; null when the request is missing or no longer awaiting (removed, expired, verified).</summary>
    Task<ContactVerificationRecipient?> GetContactVerificationRecipientAsync(Guid requestId, DateTime nowUtc, CancellationToken ct);

    public const int MaxClaims = 10;
    public const string TooManyClaims = "too-many-claims";

    /// <summary>
    /// Writes one "edit-link" row per Active profile with this (normalised) email. Duplicates within the same hour are dropped
    /// by the unique deduplication key. Returns the number of rows newly written. No email or token is stored.
    /// </summary>
    Task<int> EnqueueEditLinksAsync(string normalizedEmail, DateTime nowUtc, CancellationToken ct);

    /// <summary>
    /// Claims ONE due row in one atomic statement (<c>FOR UPDATE SKIP LOCKED</c>), sets its lease and increments its claim count.
    /// Rows whose lease expired (the claiming instance crashed) are claimable again; rows already claimed
    /// <see cref="MaxClaims"/> times are first moved to dead (<see cref="TooManyClaims"/>). Null when nothing is due.
    /// </summary>
    Task<ClaimedEmail?> ClaimNextAsync(TimeSpan lease, DateTime nowUtc, CancellationToken ct);

    /// <summary>The recipient, read at send time. Null when the profile is not Active or has no private row.</summary>
    Task<EditLinkRecipient?> GetEditLinkRecipientAsync(Guid profileId, CancellationToken ct);

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
