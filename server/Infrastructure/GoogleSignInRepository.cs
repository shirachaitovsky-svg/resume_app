using Microsoft.EntityFrameworkCore;
using Npgsql;
using ResumeApp.Features.Auth;

namespace ResumeApp.Infrastructure;

public sealed class GoogleSignInRepository(AppDbContext db) : IGoogleSignInRepository
{
    public async Task<Guid?> FindProfileIdAsync(string sub, CancellationToken ct)
        => await db.Profiles.AsNoTracking().Where(p => p.GoogleSub == sub).Select(p => (Guid?)p.Id).SingleOrDefaultAsync(ct);

    public async Task<GoogleSignInResult> SignInAsync(string sub, string normalizedEmail, CancellationToken ct)
    {
        var profile = await db.Profiles.AsNoTracking().Where(p => p.GoogleSub == sub).Select(p => new { p.Id, p.CohortId }).SingleOrDefaultAsync(ct);
        if (profile is null) return new(GoogleSignInStatus.Ok, null);

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var stored = await db.ParticipantPrivates.AsNoTracking().Where(p => p.ProfileId == profile.Id).Select(p => p.Email).SingleOrDefaultAsync(ct);
            if (stored is null || stored == normalizedEmail)
            {
                await tx.RollbackAsync(ct);
                return new GoogleSignInResult(GoogleSignInStatus.Ok, profile.Id);
            }

            try
            {
                var taken = await db.ParticipantPrivates.AsNoTracking().AnyAsync(p => p.CohortId == profile.CohortId && p.Email == normalizedEmail && p.ProfileId != profile.Id, ct);
                if (taken)
                {
                    await tx.RollbackAsync(ct);
                    return new GoogleSignInResult(GoogleSignInStatus.EmailTaken, null);
                }

                // Conditional: a concurrent callback that already applied this email updates no row, so ContactVersion advances once.
                await db.ParticipantPrivates.Where(p => p.ProfileId == profile.Id && p.Email != normalizedEmail)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(p => p.Email, normalizedEmail)
                        .SetProperty(p => p.ContactVersion, p => p.ContactVersion + 1), ct);
                await tx.CommitAsync(ct);
                return new GoogleSignInResult(GoogleSignInStatus.Ok, profile.Id);
            }
            catch (Exception ex) when (ex is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } or DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } })
            {
                await tx.RollbackAsync(ct);
                return new GoogleSignInResult(GoogleSignInStatus.EmailTaken, null);
            }
        });
    }
}
