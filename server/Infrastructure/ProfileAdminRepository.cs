using Microsoft.EntityFrameworkCore;
using ResumeApp.Domain.Audit;
using ResumeApp.Domain.Contacts;
using ResumeApp.Domain.Email;
using ResumeApp.Domain.Profiles;
using ResumeApp.Features.Profiles;

namespace ResumeApp.Infrastructure;

public sealed class ProfileAdminRepository(AppDbContext db) : IProfileAdminRepository
{
    public async Task<AdminProfilePage> ListAsync(VisibilityFilter visibility, Guid? cohortId, int limit, int offset, CancellationToken ct)
    {
        var profiles = db.Profiles.AsNoTracking();
        if (visibility == VisibilityFilter.Active) profiles = profiles.Where(p => p.Visibility == ProfileVisibility.Active);
        else if (visibility == VisibilityFilter.Removed) profiles = profiles.Where(p => p.Visibility == ProfileVisibility.Removed);
        if (cohortId is { } cid) profiles = profiles.Where(p => p.CohortId == cid);

        var total = await profiles.CountAsync(ct);
        var rows = await profiles.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id).Skip(offset).Take(limit)
            .Select(p => new
            {
                p.Id,
                p.Visibility,
                p.CreatedAt,
                HasApproved = p.ApprovedRevisionId != null,
                CohortName = db.Cohorts.Where(c => c.Id == p.CohortId).Select(c => c.Name).FirstOrDefault(),
                // The latest approved revision (the pointer) when there is one, else the latest revision.
                Shown = db.ProfileRevisions
                    .Where(r => r.ProfileId == p.Id && (p.ApprovedRevisionId == null || r.Id == p.ApprovedRevisionId))
                    .OrderByDescending(r => r.SubmittedAt).ThenByDescending(r => r.Id)
                    .Select(r => new { r.Name, r.ProductName }).FirstOrDefault(),
            })
            .ToListAsync(ct);
        var items = rows.Select(x => new AdminProfileRow(x.Id, x.Shown?.Name ?? string.Empty, x.Shown?.ProductName ?? string.Empty,
            x.CohortName ?? string.Empty, VisibilityNames.Wire(x.Visibility), x.HasApproved, x.CreatedAt)).ToList();
        return new(items, total);
    }

    public async Task<RemoveOutcome> RemoveAsync(Guid profileId, Guid actorId, DateTime nowUtc, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Same lock as approval, return and token issuance: removal is serialised with them per profile.
        var locked = await db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM profiles WHERE id = {profileId} FOR UPDATE").ToListAsync(ct);
        if (locked.Count == 0)
        {
            await tx.RollbackAsync(ct);
            return new(RemoveStatus.NotFound);
        }

        // AD-14: the conditional update is the arbiter; concurrent callers block on the row lock and then match nothing.
        var updated = await db.Profiles
            .Where(p => p.Id == profileId && p.Visibility == ProfileVisibility.Active)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Visibility, ProfileVisibility.Removed), ct);
        if (updated != 1)
        {
            await tx.RollbackAsync(ct);
            return new(RemoveStatus.NotActive);
        }

        await db.PersonalTokens
            .Where(t => t.ProfileId == profileId && t.Purpose == PersonalToken.EditProfilePurpose && t.UsedAt == null && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, nowUtc), ct);

        // AD-13: open contact requests of the profile expire in the same transaction, and their verification tokens are revoked.
        // The later states of epic 3 (sent_to_participant, participant_approved) are added to this list by stories 3.3 and 3.4.
        var awaiting = ContactRequestStatus.AwaitingEmailVerification;
        var submitted = ContactRequestStatus.Submitted;
        await db.ContactRequests
            .Where(r => r.ProfileId == profileId && (r.Status == awaiting || r.Status == submitted))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ContactRequestStatus.Expired), ct);
        await db.PersonalTokens
            .Where(t => t.ProfileId == profileId && t.Purpose == PersonalToken.VerifyContactEmailPurpose && t.UsedAt == null && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, nowUtc), ct);

        db.AuditLog.Add(AuditLogEntry.Create(actorId, nowUtc, AuditLogEntry.RemoveProfile, AuditLogEntry.ProfileEntity, profileId));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(RemoveStatus.Removed);
    }
}
