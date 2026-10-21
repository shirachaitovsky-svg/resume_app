using ResumeApp.Domain.Cohorts;

namespace ResumeApp.Features.Cohorts;

public interface ICohortRepository
{
    Task AddAsync(Cohort cohort, CancellationToken ct);
    Task<IReadOnlyList<Cohort>> ListAsync(CancellationToken ct);
    Task<Cohort?> FindByTokenAsync(string token, CancellationToken ct);
    /// <summary>Conditional update (WHERE revoked_at IS NULL). Returns true when a row changed.</summary>
    Task<bool> TryRevokeAsync(Guid id, DateTime nowUtc, CancellationToken ct);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct);
}

public enum RevokeResult { Revoked, NotFound, AlreadyRevoked }

public sealed class CohortService(ICohortRepository repository, TimeProvider clock)
{
    public async Task<Cohort> CreateAsync(string name, CancellationToken ct)
    {
        var cohort = Cohort.Create(name.Trim(), clock.GetUtcNow().UtcDateTime);
        await repository.AddAsync(cohort, ct);
        return cohort;
    }

    public Task<IReadOnlyList<Cohort>> ListAsync(CancellationToken ct) => repository.ListAsync(ct);

    public async Task<RevokeResult> RevokeAsync(Guid id, CancellationToken ct)
    {
        if (await repository.TryRevokeAsync(id, clock.GetUtcNow().UtcDateTime, ct)) return RevokeResult.Revoked;
        return await repository.ExistsAsync(id, ct) ? RevokeResult.AlreadyRevoked : RevokeResult.NotFound;
    }

    /// <summary>Returns the cohort for an active invite, otherwise null (missing, malformed and revoked look the same).</summary>
    public async Task<Cohort?> ResolveActiveAsync(string token, CancellationToken ct)
    {
        if (!Cohort.IsWellFormedToken(token)) return null;
        var cohort = await repository.FindByTokenAsync(token, ct);
        return cohort is { RevokedAt: null } ? cohort : null;
    }

    /// <summary>Returns the cohort name for an active invite, otherwise null (missing, malformed and revoked look the same).</summary>
    public async Task<string?> ResolveInviteAsync(string token, CancellationToken ct)
    {
        if (!Cohort.IsWellFormedToken(token)) return null;
        var cohort = await repository.FindByTokenAsync(token, ct);
        return cohort is { RevokedAt: null } ? cohort.Name : null;
    }
}
