using Microsoft.EntityFrameworkCore;
using ResumeApp.Domain.Cohorts;
using ResumeApp.Features.Cohorts;

namespace ResumeApp.Infrastructure;

public sealed class CohortRepository(AppDbContext db) : ICohortRepository
{
    public async Task AddAsync(Cohort cohort, CancellationToken ct)
    {
        db.Cohorts.Add(cohort);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<Cohort>> ListAsync(CancellationToken ct)
        => await db.Cohorts.AsNoTracking().OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id).ToListAsync(ct);

    public Task<Cohort?> FindByTokenAsync(string token, CancellationToken ct)
        => db.Cohorts.AsNoTracking().FirstOrDefaultAsync(c => c.InviteToken == token, ct);

    public async Task<bool> TryRevokeAsync(Guid id, DateTime nowUtc, CancellationToken ct)
        => await db.Cohorts.Where(c => c.Id == id && c.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.RevokedAt, nowUtc), ct) == 1;

    public Task<Cohort?> FindActiveByIdAsync(Guid id, CancellationToken ct)
        => db.Cohorts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id && c.RevokedAt == null, ct);

    public async Task<IReadOnlyList<Cohort>> ListActiveAsync(CancellationToken ct)
        => await db.Cohorts.AsNoTracking().Where(c => c.RevokedAt == null).OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id).ToListAsync(ct);

    public Task<bool> ExistsAsync(Guid id, CancellationToken ct) => db.Cohorts.AnyAsync(c => c.Id == id, ct);
}
