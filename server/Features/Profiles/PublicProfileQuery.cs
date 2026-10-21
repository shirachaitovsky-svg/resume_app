namespace ResumeApp.Features.Profiles;

/// <summary>A gallery card as read from the approved revision (no private or admin fields).</summary>
public sealed record GalleryCardRow(
    Guid ProfileId, string Name, string ProductName, string ProductUrl, string? Description,
    string AvatarId, bool HasPhoto, Guid CohortId, string CohortName);

public sealed record GalleryPage(IReadOnlyList<GalleryCardRow> Items, int Total);

public sealed record GalleryCohortRow(Guid Id, string Name);

/// <summary>
/// Implemented in Infrastructure. Every implementation must return only Active profiles with an approved revision,
/// reading data from that approved revision alone (AD-13).
/// </summary>
public interface IPublicProfileReader
{
    Task<GalleryPage> GetPageAsync(Guid? cohortId, int limit, int offset, CancellationToken ct);

    Task<IReadOnlyList<GalleryCohortRow>> GetCohortsAsync(CancellationToken ct);
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
}
