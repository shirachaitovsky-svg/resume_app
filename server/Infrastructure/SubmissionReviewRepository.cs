using Microsoft.EntityFrameworkCore;
using ResumeApp.Domain.Audit;
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

    public async Task<ApproveOutcome> ApproveAsync(Guid revisionId, Guid actorId, DateTime nowUtc, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Serialise approvals per profile: lock the profile row before reading the pointer.
        var owner = await db.ProfileRevisions.AsNoTracking().Where(r => r.Id == revisionId).Select(r => (Guid?)r.ProfileId).SingleOrDefaultAsync(ct);
        if (owner is null)
        {
            await tx.RollbackAsync(ct);
            return new(ApproveStatus.NotFound);
        }
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM profiles WHERE id = {owner.Value} FOR UPDATE", ct);

        // AD-14: the conditional update is the arbiter; concurrent callers block on the row lock and then match nothing.
        var updated = await db.ProfileRevisions
            .Where(r => r.Id == revisionId && r.Status == RevisionStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RevisionStatus.Approved), ct);
        if (updated != 1)
        {
            await tx.RollbackAsync(ct);
            var exists = await db.ProfileRevisions.AsNoTracking().AnyAsync(r => r.Id == revisionId, ct);
            return new(exists ? ApproveStatus.NotPending : ApproveStatus.NotFound);
        }

        var profileId = await db.ProfileRevisions.AsNoTracking().Where(r => r.Id == revisionId).Select(r => r.ProfileId).SingleAsync(ct);
        var previous = await db.Profiles.AsNoTracking().Where(p => p.Id == profileId).Select(p => p.ApprovedRevisionId).SingleAsync(ct);
        if (previous is { } prev && prev != revisionId)
        {
            var superseded = await db.ProfileRevisions
                .Where(r => r.Id == prev && r.Status == RevisionStatus.Approved)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RevisionStatus.Superseded), ct);
            if (superseded != 1)
            {
                await tx.RollbackAsync(ct);
                return new(ApproveStatus.Inconsistent);
            }
        }

        var moved = await db.Profiles.Where(p => p.Id == profileId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.ApprovedRevisionId, revisionId)
                                      .SetProperty(p => p.Visibility, ProfileVisibility.Active), ct);
        if (moved != 1) throw new InvalidOperationException("Profile row missing for revision.");

        db.AuditLog.Add(AuditLogEntry.Create(actorId, nowUtc, AuditLogEntry.ApproveRevision, AuditLogEntry.RevisionEntity, revisionId));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(ApproveStatus.Approved, profileId);
    }
}
