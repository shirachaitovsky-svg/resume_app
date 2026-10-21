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

    public const string EditLinkTemplate = "edit-link";

    public Guid Id { get; private set; }
    public string TemplateKey { get; private set; } = string.Empty;
    public Guid? ProfileId { get; private set; }
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

    public static string EditLinkDeduplicationKey(Guid profileId, DateTime nowUtc)
        => $"edit-link:{profileId:D}:{nowUtc:yyyyMMddHH}";
}

/// <summary>A personal link token (AD-6). Only the SHA-256 hash is stored.</summary>
public sealed class PersonalToken
{
    public const string EditProfilePurpose = "edit-profile";
    public static readonly TimeSpan EditProfileLifetime = TimeSpan.FromHours(24);

    public Guid Id { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public string Purpose { get; private set; } = string.Empty;
    public Guid ProfileId { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? UsedAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private PersonalToken() { }

    public static PersonalToken Create(string tokenHash, string purpose, Guid profileId, DateTime nowUtc, TimeSpan lifetime) => new()
    {
        Id = Guid.NewGuid(),
        TokenHash = tokenHash,
        Purpose = purpose,
        ProfileId = profileId,
        ExpiresAt = nowUtc + lifetime,
        CreatedAt = nowUtc,
    };
}
