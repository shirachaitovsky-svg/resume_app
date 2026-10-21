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
}
