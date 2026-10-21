namespace ResumeApp.Domain.Profiles;

public enum ProfileVisibility { Active, Removed }

public enum RevisionStatus { Pending, Approved, Returned, Superseded }

public sealed class Profile
{
    public Guid Id { get; private set; }
    /// <summary>Set once from the invite link and never changed (AD-7).</summary>
    public Guid CohortId { get; private set; }
    public ProfileVisibility Visibility { get; private set; }
    public Guid? ApprovedRevisionId { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private Profile() { }

    public static Profile Create(Guid cohortId, DateTime nowUtc) => new()
    {
        Id = Guid.NewGuid(), // v4 (random)
        CohortId = cohortId,
        Visibility = ProfileVisibility.Active,
        CreatedAt = nowUtc,
    };
}

/// <summary>An immutable submission of the public profile fields (AD-2).</summary>
public sealed class ProfileRevision
{
    public const int NameMaxLength = 100;
    public const int ProductNameMaxLength = 100;
    public const int ProductUrlMaxLength = 2048;

    public Guid Id { get; private set; }
    public Guid ProfileId { get; private set; }
    public RevisionStatus Status { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string ProductName { get; private set; } = string.Empty;
    public string ProductUrl { get; private set; } = string.Empty;
    public DateTime SubmittedAt { get; private set; }

    private ProfileRevision() { }

    public static ProfileRevision CreatePending(Guid profileId, string name, string productName, string productUrl, DateTime nowUtc) => new()
    {
        Id = Guid.NewGuid(),
        ProfileId = profileId,
        Status = RevisionStatus.Pending,
        Name = name,
        ProductName = productName,
        ProductUrl = productUrl,
        SubmittedAt = nowUtc,
    };
}

/// <summary>Email and phone. Stored only here, never in a public model, response or log (AD-1, AD-18).</summary>
public sealed class ParticipantPrivate
{
    public const int EmailMaxLength = 254;
    public const int PhoneMaxLength = 20;

    public Guid ProfileId { get; private set; }
    /// <summary>Copy of the profile's cohort, used only for the unique (cohort, email) index.</summary>
    public Guid CohortId { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string Phone { get; private set; } = string.Empty;

    private ParticipantPrivate() { }

    public static ParticipantPrivate Create(Guid profileId, Guid cohortId, string normalizedEmail, string normalizedPhone) => new()
    {
        ProfileId = profileId,
        CohortId = cohortId,
        Email = normalizedEmail,
        Phone = normalizedPhone,
    };
}
