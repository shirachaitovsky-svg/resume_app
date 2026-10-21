using Microsoft.EntityFrameworkCore;
using Npgsql;
using ResumeApp.Domain.Profiles;
using ResumeApp.Features.Profiles;

namespace ResumeApp.Infrastructure;

public sealed class ProfileRepository(AppDbContext db) : IProfileRepository
{
    public async Task<bool> TryAddAsync(Profile profile, ProfileRevision revision, ParticipantPrivate priv, CancellationToken ct)
    {
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            db.Profiles.Add(profile);
            await db.SaveChangesAsync(ct);

            // A duplicate (cohort, email) is detected without throwing, so the Postgres error (whose detail
            // contains the email) is never raised or logged. A concurrent twin waits on the unique index here.
            var inserted = await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO participant_private (profile_id, cohort_id, email, phone)
                VALUES ({priv.ProfileId}, {priv.CohortId}, {priv.Email}, {priv.Phone})
                ON CONFLICT (cohort_id, email) DO NOTHING", ct);
            if (inserted == 0)
            {
                await tx.RollbackAsync(ct);
                db.ChangeTracker.Clear();
                return false;
            }

            db.ProfileRevisions.Add(revision);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ix_participant_private_cohort_email" })
        {
            // Last-resort guard; the transaction is disposed (rolled back) on leaving the scope.
            db.ChangeTracker.Clear();
            return false;
        }
        catch
        {
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<bool> AddPendingRevisionAsync(ProfileRevisionUpdate u, DateTime nowUtc, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // Serialise edits and approvals per profile; the visibility read under the lock is the removed check.
            var locked = await db.Database.SqlQuery<string>($"SELECT visibility AS \"Value\" FROM profiles WHERE id = {u.ProfileId} FOR UPDATE").ToListAsync(ct);
            if (locked.Count != 1 || locked[0] != ProfileVisibility.Active.ToString())
            {
                await tx.RollbackAsync(ct);
                return false;
            }

            var current = await db.ParticipantPrivates.AsNoTracking().Where(p => p.ProfileId == u.ProfileId)
                .Select(p => new { p.Email, p.Phone }).SingleOrDefaultAsync(ct);
            if (current is null)
            {
                await tx.RollbackAsync(ct);
                return false;
            }

            byte[]? photo = null;
            string? photoType = null;
            switch (u.PhotoChange)
            {
                case PhotoChange.Replace:
                    photo = u.NewPhoto; photoType = u.NewPhotoContentType;
                    break;
                case PhotoChange.Keep:
                    var pendingId = await db.ProfileRevisions.AsNoTracking()
                        .Where(r => r.ProfileId == u.ProfileId && r.Status == RevisionStatus.Pending).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct);
                    var sourceId = pendingId ?? await db.Profiles.AsNoTracking().Where(p => p.Id == u.ProfileId).Select(p => p.ApprovedRevisionId).SingleOrDefaultAsync(ct)
                        ?? await db.ProfileRevisions.AsNoTracking().Where(r => r.ProfileId == u.ProfileId).OrderByDescending(r => r.SubmittedAt).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct);
                    if (sourceId is { } id)
                    {
                        var src = await db.ProfileRevisions.AsNoTracking().Where(r => r.Id == id).Select(r => new { r.Photo, r.PhotoContentType }).SingleAsync(ct);
                        if (src.Photo is { Length: > 0 } && src.PhotoContentType is not null) { photo = src.Photo; photoType = src.PhotoContentType; }
                    }
                    break;
            }

            // AD-2 / AD-14: the previous pending revision (if any) becomes superseded in the same transaction.
            await db.ProfileRevisions
                .Where(r => r.ProfileId == u.ProfileId && r.Status == RevisionStatus.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RevisionStatus.Superseded), ct);

            // A changed email or phone is only recorded as a request; the private row is not touched (AD-18, story 2.3 applies it).
            // A value equal to the current one means "no change" (it cancels an earlier request); a value equal to the pending
            // one keeps that request; any other value replaces it. All three fall out of comparing with the current value only.
            var pendingEmail = u.Email != current.Email ? u.Email : null;
            var pendingPhone = u.Phone != current.Phone ? u.Phone : null;
            var extras = new ProfileExtras(u.Description, u.Role, u.Workplace, u.IntroText, u.IntroAnswer1, u.IntroAnswer2, u.IntroAnswer3,
                photo is null ? u.AvatarId : ProfileAvatars.Default, photo, photoType, u.Cv);
            db.ProfileRevisions.Add(ProfileRevision.CreatePending(u.ProfileId, u.Name, u.ProductName, u.ProductUrl, nowUtc, extras, pendingEmail, pendingPhone));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    public async Task<OwnProfile?> GetOwnAsync(Guid profileId, CancellationToken ct)
    {
        var profile = await (from p in db.Profiles.AsNoTracking()
                             where p.Id == profileId && p.Visibility == ProfileVisibility.Active
                             join c in db.Cohorts.AsNoTracking() on p.CohortId equals c.Id
                             join pp in db.ParticipantPrivates.AsNoTracking() on p.Id equals pp.ProfileId
                             select new { p.ApprovedRevisionId, CohortName = c.Name, pp.Email, pp.Phone }).SingleOrDefaultAsync(ct);
        if (profile is null) return null;

        var pending = await db.ProfileRevisions.AsNoTracking()
            .Where(r => r.ProfileId == profileId && r.Status == RevisionStatus.Pending)
            .Select(r => new { r.Id, r.SubmittedAt, r.PendingEmail, r.PendingPhone }).FirstOrDefaultAsync(ct);

        var sourceId = pending?.Id ?? profile.ApprovedRevisionId
            ?? await db.ProfileRevisions.AsNoTracking().Where(r => r.ProfileId == profileId).OrderByDescending(r => r.SubmittedAt).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct);
        if (sourceId is null) return null;

        var rev = await db.ProfileRevisions.AsNoTracking().Where(r => r.Id == sourceId)
            .Select(r => new
            {
                r.Name, r.ProductName, r.ProductUrl, r.Description, r.Role, r.Workplace, r.IntroText,
                r.IntroAnswer1, r.IntroAnswer2, r.IntroAnswer3, r.AvatarId, r.Cv, HasPhoto = r.Photo != null && r.Photo.Length > 0,
            }).SingleAsync(ct);

        return new OwnProfile(rev.Name, rev.ProductName, rev.ProductUrl, profile.Email, profile.Phone, rev.Description, rev.Role, rev.Workplace, rev.IntroText,
            rev.IntroAnswer1, rev.IntroAnswer2, rev.IntroAnswer3, rev.AvatarId, rev.Cv, rev.HasPhoto, profile.CohortName,
            profile.ApprovedRevisionId != null, pending?.SubmittedAt, pending?.PendingEmail, pending?.PendingPhone);
    }
}
