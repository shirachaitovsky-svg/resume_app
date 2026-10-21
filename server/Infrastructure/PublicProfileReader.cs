using Microsoft.EntityFrameworkCore;
using ResumeApp.Domain.Profiles;
using ResumeApp.Features.Profiles;

namespace ResumeApp.Infrastructure;

public sealed class PublicProfileReader(AppDbContext db) : IPublicProfileReader
{
    // Member-init (not constructor) projection so EF can keep composing over it.
    private sealed class Visible
    {
        public Guid ProfileId { get; init; }
        public Guid RevisionId { get; init; }
        public string? Role { get; init; }
        public string? Workplace { get; init; }
        public string? IntroText { get; init; }
        public string? IntroAnswer1 { get; init; }
        public string? IntroAnswer2 { get; init; }
        public string? IntroAnswer3 { get; init; }
        public string? Cv { get; init; }
        public DateTime SubmittedAt { get; init; }
        public string Name { get; init; } = "";
        public string ProductName { get; init; } = "";
        public string ProductUrl { get; init; } = "";
        public string? Description { get; init; }
        public string AvatarId { get; init; } = "";
        public bool HasPhoto { get; init; }
        public Guid CohortId { get; init; }
        public string CohortName { get; init; } = "";
    }

    // The one definition of "publicly visible": an Active profile joined to its own approved revision.
    // Photo bytes are never loaded (only a null/length check in the projection).
    private IQueryable<Visible> VisibleProfiles() =>
        from p in db.Profiles.AsNoTracking()
        where p.Visibility == ProfileVisibility.Active && p.ApprovedRevisionId != null
        join r in db.ProfileRevisions.AsNoTracking() on p.ApprovedRevisionId equals r.Id
        where r.ProfileId == p.Id && r.Status == RevisionStatus.Approved
        join c in db.Cohorts.AsNoTracking() on p.CohortId equals c.Id
        select new Visible
        {
            ProfileId = p.Id, RevisionId = r.Id, SubmittedAt = r.SubmittedAt, Name = r.Name, ProductName = r.ProductName, ProductUrl = r.ProductUrl,
            Description = r.Description, AvatarId = r.AvatarId,
            Role = r.Role, Workplace = r.Workplace, IntroText = r.IntroText,
            IntroAnswer1 = r.IntroAnswer1, IntroAnswer2 = r.IntroAnswer2, IntroAnswer3 = r.IntroAnswer3, Cv = r.Cv,
            HasPhoto = r.Photo != null && r.Photo.Length > 0 && r.PhotoContentType != null,
            CohortId = c.Id, CohortName = c.Name,
        };

    public async Task<GalleryPage> GetPageAsync(Guid? cohortId, int limit, int offset, CancellationToken ct)
    {
        var rows = VisibleProfiles();
        if (cohortId is { } cid) rows = rows.Where(x => x.CohortId == cid);

        var total = await rows.CountAsync(ct);
        var items = await rows
            .OrderByDescending(x => x.SubmittedAt).ThenByDescending(x => x.ProfileId)
            .Select(x => new GalleryCardRow(x.ProfileId, x.Name, x.ProductName, x.ProductUrl, x.Description, x.AvatarId, x.HasPhoto, x.CohortId, x.CohortName))
            .Skip(offset).Take(limit).ToListAsync(ct);
        return new(items, total);
    }

    public async Task<IReadOnlyList<GalleryCohortRow>> GetCohortsAsync(CancellationToken ct)
        => await VisibleProfiles()
            .GroupBy(x => new { x.CohortId, x.CohortName })
            .Select(g => new { Id = g.Key.CohortId, Name = g.Key.CohortName })
            .OrderBy(c => c.Name).ThenBy(c => c.Id)
            .Select(c => new GalleryCohortRow(c.Id, c.Name))
            .ToListAsync(ct);

    public async Task<ProfileDetailRow?> GetByIdAsync(Guid profileId, CancellationToken ct)
        => await VisibleProfiles()
            .Where(x => x.ProfileId == profileId)
            .Select(x => new ProfileDetailRow(x.ProfileId, x.Name, x.ProductName, x.ProductUrl, x.Description, x.Role, x.Workplace,
                x.IntroText, x.IntroAnswer1, x.IntroAnswer2, x.IntroAnswer3, x.AvatarId, x.HasPhoto, x.Cv, x.CohortId, x.CohortName))
            .FirstOrDefaultAsync(ct);

    // Same visibility helper as everything else, so the photo and the profile page can never disagree.
    public async Task<PublicPhoto?> GetPhotoAsync(Guid profileId, CancellationToken ct)
    {
        var row = await (from v in VisibleProfiles()
                         where v.ProfileId == profileId && v.HasPhoto
                         join r in db.ProfileRevisions.AsNoTracking() on v.RevisionId equals r.Id
                         select new { r.Photo, r.PhotoContentType })
            .FirstOrDefaultAsync(ct);
        return row?.Photo is null || row.PhotoContentType is null ? null : new PublicPhoto(row.Photo, row.PhotoContentType);
    }
}
