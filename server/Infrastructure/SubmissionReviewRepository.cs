using Microsoft.EntityFrameworkCore;
using ResumeApp.Domain.Profiles;
using ResumeApp.Features.Profiles;

namespace ResumeApp.Infrastructure;

public sealed class SubmissionReviewRepository(AppDbContext db) : ISubmissionReviewRepository
{

    public async Task<SubmissionQueuePage> ListPendingAsync(int limit, int offset, CancellationToken ct)
    {
        var total = await (from r in db.ProfileRevisions.AsNoTracking()
                           where r.Status == RevisionStatus.Pending
                           join p in db.Profiles.AsNoTracking() on r.ProfileId equals p.Id
                           join c in db.Cohorts.AsNoTracking() on p.CohortId equals c.Id
                           join pp in db.ParticipantPrivates.AsNoTracking() on p.Id equals pp.ProfileId
                           select r.Id).CountAsync(ct);
        var items = await (from r in db.ProfileRevisions.AsNoTracking()
                           where r.Status == RevisionStatus.Pending
                           join p in db.Profiles.AsNoTracking() on r.ProfileId equals p.Id
                           join c in db.Cohorts.AsNoTracking() on p.CohortId equals c.Id
                           join pp in db.ParticipantPrivates.AsNoTracking() on p.Id equals pp.ProfileId
                           orderby r.SubmittedAt, r.Id
                           select new SubmissionQueueRow(r.Id, r.Name, r.ProductName, c.Name, p.ApprovedRevisionId != null, r.SubmittedAt))
            .Skip(offset).Take(limit).ToListAsync(ct);
        return new(items, total);
    }

    public async Task<SubmissionDetailRow?> GetPendingAsync(Guid revisionId, CancellationToken ct)
        => await (from r in db.ProfileRevisions.AsNoTracking()
                  where r.Id == revisionId && r.Status == RevisionStatus.Pending
                  join p in db.Profiles.AsNoTracking() on r.ProfileId equals p.Id
                  join c in db.Cohorts.AsNoTracking() on p.CohortId equals c.Id
                  join pp in db.ParticipantPrivates.AsNoTracking() on p.Id equals pp.ProfileId
                  select new SubmissionDetailRow(r.Id, p.Id, r.Name, r.ProductName, r.ProductUrl, c.Name, p.ApprovedRevisionId != null,
                      r.SubmittedAt, r.Description, r.Role, r.Workplace, r.IntroText, r.IntroAnswer1, r.IntroAnswer2, r.IntroAnswer3,
                      r.AvatarId, r.Photo != null, r.Cv, pp.Email, pp.Phone))
            .FirstOrDefaultAsync(ct);

    public async Task<PublicPhoto?> GetPendingPhotoAsync(Guid revisionId, CancellationToken ct)
    {
        var row = await db.ProfileRevisions.AsNoTracking()
            .Where(r => r.Id == revisionId && r.Status == RevisionStatus.Pending && r.Photo != null && r.PhotoContentType != null)
            .Select(r => new { r.Photo, r.PhotoContentType })
            .FirstOrDefaultAsync(ct);
        return row is null ? null : new PublicPhoto(row.Photo!, row.PhotoContentType!);
    }
}
