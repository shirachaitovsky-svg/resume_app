namespace ResumeApp.Features.Profiles;

public enum VisibilityFilter { Active, Removed, All }

/// <summary>The one place that maps visibility values to and from their wire names ("active", "removed", "all").</summary>
public static class VisibilityNames
{
    public static string Wire(Enum value) => value.ToString().ToLowerInvariant();

    public static bool TryParseFilter(string? text, out VisibilityFilter filter)
    {
        foreach (var candidate in Enum.GetValues<VisibilityFilter>())
            if (text == Wire(candidate)) { filter = candidate; return true; }
        filter = default;
        return false;
    }
}

/// <summary>An admin list row: identifiers and public-facing fields only, never email, phone or cv.</summary>
public sealed record AdminProfileRow(Guid ProfileId, string Name, string ProductName, string CohortName, string Visibility, bool HasApproved, DateTime CreatedAt);

public sealed record AdminProfilePage(IReadOnlyList<AdminProfileRow> Items, int Total);

public enum RemoveStatus { Removed, NotFound, NotActive }

public sealed record RemoveOutcome(RemoveStatus Status);

/// <summary>Admin profile lifecycle (story 2.5): list and remove. Removal never deletes data (AD-13).</summary>
public interface IProfileAdminRepository
{
    Task<AdminProfilePage> ListAsync(VisibilityFilter visibility, Guid? cohortId, int limit, int offset, CancellationToken ct);

    /// <summary>One transaction under the profile row lock: conditional active to removed, revoke unredeemed edit tokens, audit row.</summary>
    Task<RemoveOutcome> RemoveAsync(Guid profileId, Guid actorId, DateTime nowUtc, CancellationToken ct);
}

public sealed class ProfileAdminService(IProfileAdminRepository repository, TimeProvider clock)
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 100;

    public Task<AdminProfilePage> ListAsync(VisibilityFilter visibility, Guid? cohortId, int limit, int offset, CancellationToken ct)
        => repository.ListAsync(visibility, cohortId, Math.Min(limit, MaxLimit), offset, ct);

    public Task<RemoveOutcome> RemoveAsync(Guid profileId, Guid actorId, CancellationToken ct)
        => repository.RemoveAsync(profileId, actorId, clock.GetUtcNow().UtcDateTime, ct);
}
