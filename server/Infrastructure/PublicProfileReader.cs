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
            ProfileId = p.Id, SubmittedAt = r.SubmittedAt, Name = r.Name, ProductName = r.ProductName, ProductUrl = r.ProductUrl,
            Description = r.Description, AvatarId = r.AvatarId,
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
}
