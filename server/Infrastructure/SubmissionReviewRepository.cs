using Microsoft.EntityFrameworkCore;
using Npgsql;
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
                           select new SubmissionQueueRow(r.Id, r.Name, r.ProductName, c.Name, p.ApprovedRevisionId != null, r.SubmittedAt, r.PendingEmail != null || r.PendingPhone != null))
            .Skip(offset).Take(limit).ToListAsync(ct);
        return new(items, total);
    }

    public async Task<ContactChangePage> ListContactChangesAsync(int limit, int offset, CancellationToken ct)
    {
        var query = from r in db.ProfileRevisions.AsNoTracking()
                    where (r.PendingEmail != null || r.PendingPhone != null)
                          && (r.Status == RevisionStatus.Pending
                              || (r.Status == RevisionStatus.Approved
                                  && !db.ProfileRevisions.Any(n => n.ProfileId == r.ProfileId && n.Status == RevisionStatus.Pending)))
                    join p in db.Profiles.AsNoTracking() on r.ProfileId equals p.Id
                    join c in db.Cohorts.AsNoTracking() on p.CohortId equals c.Id
                    join pp in db.ParticipantPrivates.AsNoTracking() on p.Id equals pp.ProfileId
                    select new { r, c, pp };
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.r.SubmittedAt).ThenBy(x => x.r.Id).Skip(offset).Take(limit)
            .Select(x => new { x.r.Id, x.r.ProfileId, x.r.Name, x.r.ProductName, CohortName = x.c.Name, x.r.Status, x.pp.Email, x.pp.Phone, x.r.PendingEmail, x.r.PendingPhone, x.r.SubmittedAt })
            .ToListAsync(ct);
        var items = rows.Select(x => new ContactChangeRow(x.Id, x.ProfileId, x.Name, x.ProductName, x.CohortName,
            x.Status == RevisionStatus.Pending ? "pending" : "approved", x.Email, x.Phone, x.PendingEmail, x.PendingPhone, x.SubmittedAt)).ToList();
        return new(items, total);
    }

    public async Task<SubmissionDetailRow?> GetPendingAsync(Guid revisionId, CancellationToken ct)
    {
        var row = await (from r in db.ProfileRevisions.AsNoTracking()
                         where r.Id == revisionId && r.Status == RevisionStatus.Pending
                         join p in db.Profiles.AsNoTracking() on r.ProfileId equals p.Id
                         join c in db.Cohorts.AsNoTracking() on p.CohortId equals c.Id
                         join pp in db.ParticipantPrivates.AsNoTracking() on p.Id equals pp.ProfileId
                         select new { Row = new SubmissionDetailRow(r.Id, p.Id, r.Name, r.ProductName, r.ProductUrl, c.Name, p.ApprovedRevisionId != null,
                             r.SubmittedAt, r.Description, r.Role, r.Workplace, r.IntroText, r.IntroAnswer1, r.IntroAnswer2, r.IntroAnswer3,
                             r.AvatarId, r.Photo != null, r.Cv, pp.Email, pp.Phone, r.PendingEmail, r.PendingPhone, null), p.ApprovedRevisionId })
            .FirstOrDefaultAsync(ct);
        if (row is null) return null;
        if (row.ApprovedRevisionId is not { } approvedId) return row.Row;

        // The published side: the profile's current approved revision; photo equality is decided by the database so the bytes are not loaded.
        var published = await (from a in db.ProfileRevisions.AsNoTracking()
                               where a.Id == approvedId && a.Status == RevisionStatus.Approved
                               select new PublishedRow(a.Name, a.ProductName, a.ProductUrl, a.Description, a.Role, a.Workplace, a.IntroText,
                                   a.IntroAnswer1, a.IntroAnswer2, a.IntroAnswer3, a.AvatarId, a.Photo != null, a.Cv,
                                   db.ProfileRevisions.Any(x => x.Id == revisionId && x.Photo != null && a.Photo != null && x.Photo == a.Photo)))
            .FirstOrDefaultAsync(ct);
        return row.Row with { Published = published };
    }

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
            // Still under the profile lock: the pending revision named here is the current one for this profile.
            var latest = await db.ProfileRevisions.AsNoTracking()
                .Where(r => r.ProfileId == owner.Value && r.Status == RevisionStatus.Pending).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct);
            await tx.RollbackAsync(ct);
            var exists = await db.ProfileRevisions.AsNoTracking().AnyAsync(r => r.Id == revisionId, ct);
            return new(exists ? ApproveStatus.NotPending : ApproveStatus.NotFound, LatestPendingRevisionId: latest);
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

    public async Task<ContactChangeOutcome> ResolveContactChangeAsync(Guid revisionId, Guid actorId, bool approve, string? seenEmail, string? seenPhone, DateTime nowUtc, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var owner = await db.ProfileRevisions.AsNoTracking().Where(r => r.Id == revisionId).Select(r => (Guid?)r.ProfileId).SingleOrDefaultAsync(ct);
        if (owner is null)
        {
            await tx.RollbackAsync(ct);
            return new(ContactChangeStatus.NotFound);
        }
        // Serialise with approvals and other contact changes of this profile.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM profiles WHERE id = {owner.Value} FOR UPDATE", ct);

        var rev = await db.ProfileRevisions.AsNoTracking().Where(r => r.Id == revisionId)
            .Select(r => new { r.Status, r.PendingEmail, r.PendingPhone }).SingleAsync(ct);
        // An approved revision is eligible only while no newer pending revision exists (that one carries the replacing request).
        if (rev.Status is not (RevisionStatus.Pending or RevisionStatus.Approved)
            || (rev.Status == RevisionStatus.Approved
                && await db.ProfileRevisions.AsNoTracking().AnyAsync(r => r.ProfileId == owner.Value && r.Status == RevisionStatus.Pending, ct)))
        {
            await tx.RollbackAsync(ct);
            return new(ContactChangeStatus.NotEligible);
        }
        if (rev.PendingEmail is null && rev.PendingPhone is null)
        {
            await tx.RollbackAsync(ct);
            return new(ContactChangeStatus.NoChange);
        }
        // The admin must have seen exactly what is pending (stored values are already normalised).
        if (!string.Equals(rev.PendingEmail, seenEmail, StringComparison.Ordinal) || !string.Equals(rev.PendingPhone, seenPhone, StringComparison.Ordinal))
        {
            await tx.RollbackAsync(ct);
            return new(ContactChangeStatus.Stale);
        }

        // Conditional clear is the arbiter (AD-14).
        var cleared = await db.ProfileRevisions
            .Where(r => r.Id == revisionId && (r.Status == RevisionStatus.Pending || r.Status == RevisionStatus.Approved)
                        && r.PendingEmail == rev.PendingEmail && r.PendingPhone == rev.PendingPhone)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.PendingEmail, (string?)null).SetProperty(r => r.PendingPhone, (string?)null), ct);
        if (cleared != 1)
        {
            await tx.RollbackAsync(ct);
            return new(ContactChangeStatus.Stale);
        }

        if (approve)
        {
            var profileId = owner.Value;
            var current = await db.ParticipantPrivates.AsNoTracking().Where(p => p.ProfileId == profileId)
                .Select(p => new { p.Email, p.Phone }).SingleAsync(ct);
            var emailChanges = rev.PendingEmail is not null && rev.PendingEmail != current.Email;
            var phoneChanges = rev.PendingPhone is not null && rev.PendingPhone != current.Phone;
            if (emailChanges && rev.PendingEmail is { } newEmail)
            {
                var cohortId = await db.Profiles.AsNoTracking().Where(p => p.Id == profileId).Select(p => p.CohortId).SingleAsync(ct);
                var taken = await db.ParticipantPrivates.AsNoTracking().AnyAsync(p => p.CohortId == cohortId && p.Email == newEmail && p.ProfileId != profileId, ct);
                if (taken)
                {
                    await tx.RollbackAsync(ct);
                    return new(ContactChangeStatus.EmailTaken);
                }
            }

            if (emailChanges || phoneChanges) try
            {
                var email = emailChanges ? rev.PendingEmail : null;
                var phone = phoneChanges ? rev.PendingPhone : null;
                var updated = await db.ParticipantPrivates.Where(p => p.ProfileId == profileId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(p => p.Email, p => email ?? p.Email)
                        .SetProperty(p => p.Phone, p => phone ?? p.Phone)
                        .SetProperty(p => p.ContactVersion, p => p.ContactVersion + 1), ct);
                if (updated != 1) throw new InvalidOperationException("Participant private row missing for profile.");
            }
            catch (Exception ex) when (ex is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } or DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } })
            {
                await tx.RollbackAsync(ct);
                return new(ContactChangeStatus.EmailTaken);
            }
        }

        db.AuditLog.Add(AuditLogEntry.Create(actorId, nowUtc, approve ? AuditLogEntry.ApproveContactChange : AuditLogEntry.RejectContactChange, AuditLogEntry.RevisionEntity, revisionId));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(ContactChangeStatus.Done);
    }
}
