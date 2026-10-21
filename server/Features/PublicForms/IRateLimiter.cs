namespace ResumeApp.Features.PublicForms;

/// <param name="Allowed">False when this hit pushed the counter over the limit.</param>
/// <param name="Count">The counter value after this hit.</param>
/// <param name="RetryAfter">Time until the current fixed window ends.</param>
public readonly record struct RateLimitResult(bool Allowed, int Count, TimeSpan RetryAfter);

public interface IRateLimiter
{
    /// <summary>
    /// Counts one attempt for (scope, subject) in the current fixed window and reports whether it is within the limit.
    /// Counters live in the database; the subject (IP, email) is only ever stored as a keyed hash.
    /// </summary>
    Task<RateLimitResult> HitAsync(string scope, string subject, int limit, TimeSpan window, CancellationToken ct);
}

public static class RateLimitScopes
{
    public const string ProfileSubmitIp = "profile-submit-ip";
    public const string ProfileSubmitEmail = "profile-submit-email";
    public const string InviteCheckIp = "invite-check-ip";
    public const string PhotoIp = "photo-ip";
    public const string GalleryIp = "gallery-ip";
    public const string EditLinkIp = "edit-link-ip";
    public const string EditLinkEmail = "edit-link-email";
    public const string EditSessionIp = "edit-session-ip";
}

/// <summary>Thresholds (per window) and the window length. Defaults per AD-12.</summary>
public sealed record RateLimitOptions(int ProfileSubmitPerIp, int ProfileSubmitPerEmail, int InviteCheckPerIp, int PhotoPerIp, int GalleryPerIp, TimeSpan Window, int EditLinkPerIp = 20, int EditLinkPerEmail = 5, int EditSessionPerIp = 30)
{
    public static RateLimitOptions Defaults { get; } = new(20, 5, 120, 1200, 600, TimeSpan.FromHours(1));
}
