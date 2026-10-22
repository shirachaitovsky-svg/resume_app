using ResumeApp.Domain.Contacts;

namespace ResumeApp.Domain.Email;

public enum OutboxStatus { Pending, Sent, Dead }

/// <summary>
/// A queued outgoing email (AD-4, AD-15). Holds the template key and entity ids only: never a raw token,
/// an email address, a phone number or any message content.
/// </summary>
public sealed class EmailOutboxEntry
{
    public const int TemplateKeyMaxLength = 40;
    public const int DeduplicationKeyMaxLength = 100;
    public const int ErrorCategoryMaxLength = 40;

    public const string SubmissionReturnedTemplate = "submission-returned";
    public const string ContactVerificationTemplate = "contact-verification";
    public const string ContactRequestNotificationTemplate = "contact-request-notification";
    public const string ContactDecisionRequestTemplate = "contact-decision-request";
    public const string ContactDeliveryTemplate = "contact-delivery";

    public Guid Id { get; private set; }
    public string TemplateKey { get; private set; } = string.Empty;
    public Guid? ProfileId { get; private set; }
    /// <summary>The revision a template is about (story 2.4); the note itself is read from it at send time.</summary>
    public Guid? RevisionId { get; private set; }
    /// <summary>The contact request a template is about (story 3.1); the visitor's address and name are read from it at send time.</summary>
    public Guid? RequestId { get; private set; }
    public string DeduplicationKey { get; private set; } = string.Empty;
    public OutboxStatus Status { get; private set; }
    public int Attempts { get; private set; }
    /// <summary>How many times a worker claimed the row; a poison row that keeps crashing workers ends up dead.</summary>
    public int ClaimCount { get; private set; }
    public DateTime NextAttemptAt { get; private set; }
    public DateTime? LeaseUntil { get; private set; }
    public string? LastErrorCategory { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? SentAt { get; private set; }

    private EmailOutboxEntry() { }

    public static string SubmissionReturnedDeduplicationKey(Guid revisionId) => $"submission-returned:{revisionId:D}";

    public static string ContactVerificationDeduplicationKey(Guid requestId) => $"contact-verification:{requestId:D}";

    public static string ContactNotificationDeduplicationKey(Guid requestId) => $"contact-notification:{requestId:D}";

    public static string ContactDecisionDeduplicationKey(Guid requestId) => $"contact-decision:{requestId:D}";

    public static string ContactDeliveryDeduplicationKey(Guid requestId) => $"contact-delivery:{requestId:D}";
}

/// <summary>A personal link token (AD-6): contact verification and contact decision only. Only the SHA-256 hash is stored.</summary>
public sealed class PersonalToken
{
    public const string VerifyContactEmailPurpose = "verify-contact-email";
    public static readonly TimeSpan VerifyContactEmailLifetime = TimeSpan.FromHours(24);
    public const string ContactDecisionPurpose = "contact-decision";
    public static readonly TimeSpan ContactDecisionLifetime = ContactRequest.DecisionLifetime;

    public Guid Id { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public string Purpose { get; private set; } = string.Empty;
    public Guid ProfileId { get; private set; }
    /// <summary>Set for tokens scoped to one contact request (story 3.1); null for profile-scoped tokens.</summary>
    public Guid? RequestId { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? UsedAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private PersonalToken() { }

    public static PersonalToken Create(string tokenHash, string purpose, Guid profileId, DateTime nowUtc, TimeSpan lifetime, Guid? requestId = null) => new()
    {
        RequestId = requestId,
        Id = Guid.NewGuid(),
        TokenHash = tokenHash,
        Purpose = purpose,
        ProfileId = profileId,
        ExpiresAt = nowUtc + lifetime,
        CreatedAt = nowUtc,
    };
}
