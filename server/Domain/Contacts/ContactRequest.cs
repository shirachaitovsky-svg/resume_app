namespace ResumeApp.Domain.Contacts;

/// <summary>AD-3 states of a contact request, stored by their snake_case wire names (see <see cref="ContactRequestStatusNames"/>).</summary>
public enum ContactRequestStatus
{
    AwaitingEmailVerification,
    Submitted,
    SentToParticipant,
    ParticipantApproved,
    ParticipantDeclined,
    Delivered,
    Expired,
}

public static class ContactRequestStatusNames
{
    public const string AwaitingEmailVerification = "awaiting_email_verification";
    public const string Submitted = "submitted";
    public const string SentToParticipant = "sent_to_participant";
    public const string ParticipantApproved = "participant_approved";
    public const string ParticipantDeclined = "participant_declined";
    public const string Delivered = "delivered";
    public const string Expired = "expired";

    /// <summary>SQL predicate of the open states (everything except expired, participant_declined and delivered): the partial unique index.</summary>
    public const string OpenStatesSql = "status IN ('awaiting_email_verification', 'submitted', 'sent_to_participant', 'participant_approved')";

    /// <summary>SQL predicate of every valid status: the CHECK constraint on <c>contact_requests.status</c>.</summary>
    public const string AllStatesSql = "status IN ('awaiting_email_verification', 'submitted', 'sent_to_participant', 'participant_approved', 'participant_declined', 'delivered', 'expired')";

    public static string ToWire(ContactRequestStatus status) => status switch
    {
        ContactRequestStatus.AwaitingEmailVerification => AwaitingEmailVerification,
        ContactRequestStatus.Submitted => Submitted,
        ContactRequestStatus.SentToParticipant => SentToParticipant,
        ContactRequestStatus.ParticipantApproved => ParticipantApproved,
        ContactRequestStatus.ParticipantDeclined => ParticipantDeclined,
        ContactRequestStatus.Delivered => Delivered,
        ContactRequestStatus.Expired => Expired,
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static ContactRequestStatus Parse(string wire) => wire switch
    {
        AwaitingEmailVerification => ContactRequestStatus.AwaitingEmailVerification,
        Submitted => ContactRequestStatus.Submitted,
        SentToParticipant => ContactRequestStatus.SentToParticipant,
        ParticipantApproved => ContactRequestStatus.ParticipantApproved,
        ParticipantDeclined => ContactRequestStatus.ParticipantDeclined,
        Delivered => ContactRequestStatus.Delivered,
        Expired => ContactRequestStatus.Expired,
        _ => throw new ArgumentOutOfRangeException(nameof(wire)),
    };
}

public static class ParticipantDecisionNames
{
    public const string Approved = "approved";
    public const string Declined = "declined";

    /// <summary>SQL predicate of the CHECK constraint on <c>contact_requests.participant_decision</c> (null passes).</summary>
    public const string AllowedSql = "participant_decision IN ('approved', 'declined')";

    /// <summary>The decision and its time are set together or not at all.</summary>
    public const string DecisionPairSql = "(participant_decision IS NULL) = (participant_decided_at IS NULL)";

    /// <summary>The manager can approve delivery only after the participant approved.</summary>
    public const string ManagerApprovalSql = "manager_approved_at IS NULL OR COALESCE(participant_decision, '') = 'approved'";
}

/// <summary>
/// A visitor's request for a participant's contact details (AD-3). This story drives the states up to <c>submitted</c> and <c>expired</c>;
/// the rest of the machine is declared here so later stories only add callers. The verified address is locked: nothing changes
/// <see cref="VisitorEmail"/> after creation.
/// </summary>
public sealed class ContactRequest
{
    public const int NameMaxLength = 100;
    public const int ReasonMaxLength = 1000;
    public static readonly TimeSpan VerificationLifetime = TimeSpan.FromHours(24);
    /// <summary>How long the participant's link (and so her consent) stays valid after the request is sent to her.</summary>
    public static readonly TimeSpan DecisionLifetime = TimeSpan.FromDays(14);

    public Guid Id { get; private set; }
    public Guid ProfileId { get; private set; }
    public string VisitorName { get; private set; } = string.Empty;
    public string VisitorEmail { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public ContactRequestStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? VerifiedAt { get; private set; }
    /// <summary>The participant's decision (story 3.3 fills it): <see cref="ParticipantDecisionNames"/>, or null while undecided.</summary>
    public string? ParticipantDecision { get; private set; }
    public DateTime? ParticipantDecidedAt { get; private set; }
    /// <summary>The participant's <c>ContactVersion</c> at the moment she approved (story 3.3); null until then and when she declined. Story 3.4 compares it at delivery.</summary>
    public int? ParticipantContactVersion { get; private set; }
    /// <summary>The manager's approval to deliver (story 3.4 fills it); independent of the participant's decision.</summary>
    public DateTime? ManagerApprovedAt { get; private set; }

    private ContactRequest() { }

    public static ContactRequest Create(Guid profileId, string visitorName, string visitorEmail, string reason, DateTime nowUtc) => new()
    {
        Id = Guid.NewGuid(),
        ProfileId = profileId,
        VisitorName = visitorName,
        VisitorEmail = visitorEmail,
        Reason = reason,
        Status = ContactRequestStatus.AwaitingEmailVerification,
        CreatedAt = nowUtc,
        ExpiresAt = nowUtc + VerificationLifetime,
    };

    /// <summary>The transitions AD-3 allows. Every real transition is still a conditional UPDATE (AD-14); this is the rule they follow.</summary>
    public static bool CanTransition(ContactRequestStatus from, ContactRequestStatus to) => (from, to) switch
    {
        (ContactRequestStatus.AwaitingEmailVerification, ContactRequestStatus.Submitted) => true,
        (ContactRequestStatus.AwaitingEmailVerification, ContactRequestStatus.Expired) => true,
        (ContactRequestStatus.Submitted, ContactRequestStatus.SentToParticipant) => true,
        // Removal of the profile (AD-13) expires an open request wherever it is.
        (ContactRequestStatus.Submitted, ContactRequestStatus.Expired) => true,
        (ContactRequestStatus.SentToParticipant, ContactRequestStatus.ParticipantApproved) => true,
        (ContactRequestStatus.SentToParticipant, ContactRequestStatus.ParticipantDeclined) => true,
        (ContactRequestStatus.SentToParticipant, ContactRequestStatus.Expired) => true,
        (ContactRequestStatus.ParticipantApproved, ContactRequestStatus.Delivered) => true,
        (ContactRequestStatus.ParticipantApproved, ContactRequestStatus.Expired) => true,
        _ => false,
    };

    public static bool IsOpen(ContactRequestStatus status)
        => status is not (ContactRequestStatus.Expired or ContactRequestStatus.ParticipantDeclined or ContactRequestStatus.Delivered);
}
