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
    public const string InviteCheckIp = "invite-check-ip";
    public const string PhotoIp = "photo-ip";
    public const string GalleryIp = "gallery-ip";
    public const string ContactRequestIp = "contact-request-ip";
    public const string ContactRequestEmail = "contact-request-email";
    public const string ContactRequestProfile = "contact-request-profile";
    public const string ContactVerifyIp = "contact-verify-ip";
    public const string ContactDecisionIp = "contact-decision-ip";
    public const string GoogleAuthIp = "google-auth-ip";
}

/// <summary>Thresholds (per window) and the window length. Defaults per AD-12.</summary>
public sealed record RateLimitOptions(int ProfileSubmitPerIp, int InviteCheckPerIp, int PhotoPerIp, int GalleryPerIp, TimeSpan Window, int ContactRequestPerIp = 10, int ContactRequestPerEmail = 3, int ContactRequestPerProfile = 10, int ContactVerifyPerIp = 30, int ContactDecisionPerIp = 30, int GoogleAuthPerIp = 200)
{
    public static RateLimitOptions Defaults { get; } = new(20, 120, 1200, 600, TimeSpan.FromHours(1));
}
