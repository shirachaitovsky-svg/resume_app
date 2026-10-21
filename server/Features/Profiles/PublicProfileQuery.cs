using ResumeApp.Domain.Profiles;
using ResumeApp.Features.Markdown;

namespace ResumeApp.Features.Profiles;

/// <summary>A gallery card as read from the approved revision (no private or admin fields).</summary>
public sealed record GalleryCardRow(
    Guid ProfileId, string Name, string ProductName, string ProductUrl, string? Description,
    string AvatarId, bool HasPhoto, Guid CohortId, string CohortName);

public sealed record GalleryPage(IReadOnlyList<GalleryCardRow> Items, int Total);

public sealed record GalleryCohortRow(Guid Id, string Name);

/// <summary>A full public profile as stored in the approved revision (free text not yet cleaned).</summary>
public sealed record ProfileDetailRow(
    Guid ProfileId, string Name, string ProductName, string ProductUrl, string? Description, string? Role, string? Workplace,
    string? IntroText, string? IntroAnswer1, string? IntroAnswer2, string? IntroAnswer3, string AvatarId, bool HasPhoto,
    string? Cv, Guid CohortId, string CohortName);

public sealed record IntroAnswerRow(int Number, string Text);

/// <summary>A full public profile ready to return: answered intro questions only, and a cleaned CV (null when empty or rejected).</summary>
public sealed record ProfileDetail(
    Guid ProfileId, string Name, string ProductName, string ProductUrl, string? Description, string? Role, string? Workplace,
    string? IntroText, string AvatarId, bool HasPhoto, Guid CohortId, string CohortName,
    IReadOnlyList<IntroAnswerRow> IntroAnswers, string? Cv);

/// <summary>
/// Implemented in Infrastructure. Every implementation must return only Active profiles with an approved revision,
/// reading data from that approved revision alone (AD-13).
/// </summary>
public interface IPublicProfileReader
{
    Task<GalleryPage> GetPageAsync(Guid? cohortId, int limit, int offset, CancellationToken ct);

    Task<IReadOnlyList<GalleryCohortRow>> GetCohortsAsync(CancellationToken ct);

    Task<ProfileDetailRow?> GetByIdAsync(Guid profileId, CancellationToken ct);

    /// <summary>The photo of the approved revision of a publicly visible profile; null in every other case.</summary>
    Task<PublicPhoto?> GetPhotoAsync(Guid profileId, CancellationToken ct);
}

/// <summary>The single public read path for profiles (AD-13): gallery cards and the cohorts that have any.</summary>
public sealed class PublicProfileQuery(IPublicProfileReader reader)
{
    public const int DefaultLimit = 100;
    public const int MaxLimit = 200;

    /// <summary>Clamps <paramref name="limit"/> to <see cref="MaxLimit"/>; callers validate the lower bounds.</summary>
    public Task<GalleryPage> GetGalleryAsync(Guid? cohortId, int limit, int offset, CancellationToken ct)
        => reader.GetPageAsync(cohortId, Math.Clamp(limit, 1, MaxLimit), Math.Max(0, offset), ct);

    public Task<IReadOnlyList<GalleryCohortRow>> GetCohortsAsync(CancellationToken ct) => reader.GetCohortsAsync(ct);

    public Task<PublicPhoto?> GetPhotoAsync(Guid profileId, CancellationToken ct) => reader.GetPhotoAsync(profileId, ct);

    /// <summary>One visible profile, or null for every unavailable case (the caller cannot tell them apart).</summary>
    public async Task<ProfileDetail?> GetProfileAsync(Guid profileId, CancellationToken ct)
    {
        var r = await reader.GetByIdAsync(profileId, ct);
        if (r is null) return null;

        var answers = new List<IntroAnswerRow>(3);
        foreach (var (number, text) in new[] { (1, r.IntroAnswer1), (2, r.IntroAnswer2), (3, r.IntroAnswer3) })
        {
            var clean = text?.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
            if (!string.IsNullOrEmpty(clean)) answers.Add(new IntroAnswerRow(number, clean));
        }

        // Cleaned on every read, so a value written around the save path can never reach a visitor unchecked.
        var cv = SafeMarkdown.Clean(r.Cv, ProfileRevision.CvMaxLength);
        return new ProfileDetail(r.ProfileId, r.Name, r.ProductName, r.ProductUrl, r.Description, r.Role, r.Workplace, r.IntroText,
            r.AvatarId, r.HasPhoto, r.CohortId, r.CohortName, answers, cv.Outcome == MarkdownOutcome.Ok ? cv.Text : null);
    }
}
