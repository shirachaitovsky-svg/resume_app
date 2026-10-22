using Microsoft.EntityFrameworkCore;
using Npgsql;
using ResumeApp.Domain.Profiles;
using ResumeApp.Features.Profiles;

namespace ResumeApp.Infrastructure;

public sealed class ProfileRepository(AppDbContext db) : IProfileRepository
{
    public async Task<AddProfileResult> TryAddForGoogleAsync(Profile profile, ProfileRevision revision, ParticipantPrivate priv, CancellationToken ct)
    {
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // The unique google_sub index decides first: a concurrent twin waits here and then fails with a unique violation.
            db.Profiles.Add(profile);
            await db.SaveChangesAsync(ct);

            var inserted = await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO participant_private (profile_id, cohort_id, email, phone)
                VALUES ({priv.ProfileId}, {priv.CohortId}, {priv.Email}, {priv.Phone})
                ON CONFLICT (cohort_id, email) DO NOTHING", ct);
            if (inserted == 0)
            {
                await tx.RollbackAsync(ct);
                db.ChangeTracker.Clear();
                return AddProfileResult.EmailTaken;
            }

            db.ProfileRevisions.Add(revision);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return AddProfileResult.Created;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_profiles_google_sub" })
        {
            db.ChangeTracker.Clear();
            return AddProfileResult.ProfileExists;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ix_participant_private_cohort_email" })
        {
            db.ChangeTracker.Clear();
            return AddProfileResult.EmailTaken;
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
                    var sourceId = (await ResolveEditSourceAsync(u.ProfileId, ct))?.Id;
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

    private sealed record EditSource(Guid Id, RevisionStatus Status, DateTime SubmittedAt);

    /// <summary>
    /// The revision the owner edits: the pending one if any; else the latest returned one when it is newer than the approved one
    /// (so what was returned is what gets corrected); else the approved one; else the latest revision of any kind.
    /// </summary>
    private async Task<EditSource?> ResolveEditSourceAsync(Guid profileId, CancellationToken ct)
    {
        var pending = await db.ProfileRevisions.AsNoTracking()
            .Where(r => r.ProfileId == profileId && r.Status == RevisionStatus.Pending)
            .Select(r => new EditSource(r.Id, r.Status, r.SubmittedAt)).FirstOrDefaultAsync(ct);
        if (pending is not null) return pending;

        var approved = await (from p in db.Profiles.AsNoTracking()
                              where p.Id == profileId && p.ApprovedRevisionId != null
                              join r in db.ProfileRevisions.AsNoTracking() on p.ApprovedRevisionId equals r.Id
                              select new EditSource(r.Id, r.Status, r.SubmittedAt)).FirstOrDefaultAsync(ct);
        var returned = await db.ProfileRevisions.AsNoTracking()
            .Where(r => r.ProfileId == profileId && r.Status == RevisionStatus.Returned)
            .OrderByDescending(r => r.SubmittedAt).ThenByDescending(r => r.ReturnedAt)
            .Select(r => new EditSource(r.Id, r.Status, r.SubmittedAt)).FirstOrDefaultAsync(ct);
        if (returned is not null && (approved is null || returned.SubmittedAt > approved.SubmittedAt)) return returned;
        if (approved is not null) return approved;
        return await db.ProfileRevisions.AsNoTracking().Where(r => r.ProfileId == profileId)
            .OrderByDescending(r => r.SubmittedAt).Select(r => new EditSource(r.Id, r.Status, r.SubmittedAt)).FirstOrDefaultAsync(ct);
    }

    public async Task<OwnProfile?> GetOwnAsync(Guid profileId, CancellationToken ct)
    {
        var profile = await (from p in db.Profiles.AsNoTracking()
                             where p.Id == profileId && p.Visibility == ProfileVisibility.Active
                             join c in db.Cohorts.AsNoTracking() on p.CohortId equals c.Id
                             join pp in db.ParticipantPrivates.AsNoTracking() on p.Id equals pp.ProfileId
                             select new { p.ApprovedRevisionId, CohortName = c.Name, pp.Email, pp.Phone }).SingleOrDefaultAsync(ct);
        if (profile is null) return null;

        var source = await ResolveEditSourceAsync(profileId, ct);
        if (source is null) return null;
        var sourceId = source.Id;

        var rev = await db.ProfileRevisions.AsNoTracking().Where(r => r.Id == sourceId)
            .Select(r => new
            {
                r.PendingEmail, r.PendingPhone, r.ReturnNote, r.ReturnedAt,
                r.Name, r.ProductName, r.ProductUrl, r.Description, r.Role, r.Workplace, r.IntroText,
                r.IntroAnswer1, r.IntroAnswer2, r.IntroAnswer3, r.AvatarId, r.Cv, HasPhoto = r.Photo != null && r.Photo.Length > 0,
            }).SingleAsync(ct);

        return new OwnProfile(rev.Name, rev.ProductName, rev.ProductUrl, profile.Email, profile.Phone, rev.Description, rev.Role, rev.Workplace, rev.IntroText,
            rev.IntroAnswer1, rev.IntroAnswer2, rev.IntroAnswer3, rev.AvatarId, rev.Cv, rev.HasPhoto, profile.CohortName,
            profile.ApprovedRevisionId != null, source.Status == RevisionStatus.Pending ? source.SubmittedAt : null,
            // A requested email or phone change rides on a pending or returned revision and must keep prefilling the form.
            source.Status is RevisionStatus.Pending or RevisionStatus.Returned ? rev.PendingEmail : null,
            source.Status is RevisionStatus.Pending or RevisionStatus.Returned ? rev.PendingPhone : null,
            // The returned note is shown only while the returned revision is the edit source: no newer pending one, and newer than the approved one.
            source.Status == RevisionStatus.Returned ? rev.ReturnNote : null, source.Status == RevisionStatus.Returned ? rev.ReturnedAt : null);
    }
}
