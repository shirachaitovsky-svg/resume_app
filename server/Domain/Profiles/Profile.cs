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

/// <summary>Optional profile content submitted with a revision (story 1.8). All text is plain text.</summary>
public sealed record ProfileExtras(
    string? Description,
    string? Role,
    string? Workplace,
    string? IntroText,
    string? IntroAnswer1,
    string? IntroAnswer2,
    string? IntroAnswer3,
    string AvatarId,
    byte[]? Photo,
    string? PhotoContentType,
    string? Cv = null)
{
    public static ProfileExtras None { get; } = new(null, null, null, null, null, null, null, ProfileAvatars.Default, null, null);
}

/// <summary>The fixed set of avatar ids; the artwork itself is a client asset (AD-8).</summary>
public static class ProfileAvatars
{
    public const string Default = "default";

    public static IReadOnlyList<string> Selectable { get; } = ["girl-1", "girl-2", "girl-3", "boy-1", "boy-2", "boy-3"];

    public static bool IsSelectable(string id) => Selectable.Contains(id, StringComparer.Ordinal);
}

/// <summary>An immutable submission of the public profile fields (AD-2).</summary>
public sealed class ProfileRevision
{
    public const int NameMaxLength = 100;
    public const int ProductNameMaxLength = 100;
    public const int ProductUrlMaxLength = 2048;
    public const int DescriptionMaxLength = 120;
    public const int RoleMaxLength = 100;
    public const int WorkplaceMaxLength = 100;
    public const int IntroMaxLength = 1000;
    public const int AvatarIdMaxLength = 20;
    public const int PhotoContentTypeMaxLength = 20;
    public const int CvMaxLength = 20_000;

    public Guid Id { get; private set; }
    public Guid ProfileId { get; private set; }
    public RevisionStatus Status { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string ProductName { get; private set; } = string.Empty;
    public string ProductUrl { get; private set; } = string.Empty;
    public DateTime SubmittedAt { get; private set; }
    public string? Description { get; private set; }
    public string? Role { get; private set; }
    public string? Workplace { get; private set; }
    public string? IntroText { get; private set; }
    public string? IntroAnswer1 { get; private set; }
    public string? IntroAnswer2 { get; private set; }
    public string? IntroAnswer3 { get; private set; }
    public string AvatarId { get; private set; } = ProfileAvatars.Default;
    /// <summary>Re-encoded JPEG or WebP bytes; part of the revision, served only once the revision is approved (AD-19).</summary>
    public byte[]? Photo { get; private set; }
    public string? PhotoContentType { get; private set; }
    /// <summary>Cleaned restricted Markdown (story 1.9); never rendered to HTML on the server.</summary>
    public string? Cv { get; private set; }

    private ProfileRevision() { }

    public static ProfileRevision CreatePending(Guid profileId, string name, string productName, string productUrl, DateTime nowUtc, ProfileExtras? extras = null) => CreateCore(profileId, name, productName, productUrl, nowUtc, extras ?? ProfileExtras.None);

    private static ProfileRevision CreateCore(Guid profileId, string name, string productName, string productUrl, DateTime nowUtc, ProfileExtras x) => new()
    {
        Id = Guid.NewGuid(),
        ProfileId = profileId,
        Status = RevisionStatus.Pending,
        Name = name,
        ProductName = productName,
        ProductUrl = productUrl,
        SubmittedAt = nowUtc,
        Description = x.Description,
        Role = x.Role,
        Workplace = x.Workplace,
        IntroText = x.IntroText,
        IntroAnswer1 = x.IntroAnswer1,
        IntroAnswer2 = x.IntroAnswer2,
        IntroAnswer3 = x.IntroAnswer3,
        AvatarId = x.AvatarId,
        Photo = x.Photo,
        PhotoContentType = x.PhotoContentType,
        Cv = x.Cv,
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
