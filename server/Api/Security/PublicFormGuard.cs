using System.Net;
using System.Net.Sockets;
using ResumeApp.Api.Resources;
using ResumeApp.Features.Profiles;
using ResumeApp.Features.PublicForms;

namespace ResumeApp.Api.Security;

/// <summary>
/// Shared protections for anonymous forms (AD-12). Order: IP limit, Turnstile, (caller reads body), email limit.
/// Every attempt is counted, even one that later fails verification. Returns null when the request may proceed.
/// Failures carry a machine-readable <c>code</c> extension in the problem+json body.
/// </summary>
public sealed class PublicFormGuard(IRateLimiter limiter, ITurnstileVerifier turnstile, RateLimitOptions options, ILogger<PublicFormGuard> logger)
{
    public const string TurnstileHeader = "X-Turnstile-Token";

    public const string CodeBotCheckFailed = "bot-check-failed";
    public const string CodeRateLimited = "rate-limited";
    public const string CodeRateLimitUnavailable = "rate-limit-unavailable";
    public const string CodeClientAddressUnknown = "client-address-unknown";

    public RateLimitOptions Options => options;

    /// <summary>
    /// The rate-limit key for a client address. IPv4 is used as is; IPv6 is keyed by its /64 prefix, so a client
    /// cannot rotate addresses inside its own subnet. IPv4-mapped IPv6 addresses count as IPv4.
    /// </summary>
    public static string IpKey(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily != AddressFamily.InterNetworkV6) return ip.ToString();
        var prefix = ip.GetAddressBytes().AsSpan(0, 8);
        return "v6/64:" + Convert.ToHexString(prefix).ToLowerInvariant();
    }

    public async Task<IResult?> CheckIpAndBotAsync(HttpContext ctx, string ipScope, int ipLimit, CancellationToken ct)
    {
        // Valid only after UseForwardedHeaders (which runs behind the origin-secret check). No address: fail closed.
        var remote = ctx.Connection.RemoteIpAddress;
        if (remote is null)
        {
            logger.LogWarning("Public form request rejected: the client address is unknown.");
            return Problem(StatusCodes.Status403Forbidden, Strings.BotCheckFailed, CodeClientAddressUnknown);
        }

        var (hit, failure) = await HitAsync(ipScope, IpKey(remote), ipLimit, ct);
        if (failure is not null) return failure;
        if (!hit.Allowed) return TooMany(ctx, hit);

        var token = ctx.Request.Headers[TurnstileHeader].ToString();
        if (!await turnstile.VerifyAsync(token, remote.ToString(), ct))
            return Problem(StatusCodes.Status403Forbidden, Strings.BotCheckFailed, CodeBotCheckFailed);
        return null;
    }

    /// <summary>IP limit only (no Turnstile), for public reads such as the photo endpoint.</summary>
    public async Task<IResult?> CheckIpAsync(HttpContext ctx, string ipScope, int ipLimit, CancellationToken ct)
    {
        var remote = ctx.Connection.RemoteIpAddress;
        if (remote is null)
        {
            logger.LogWarning("Public request rejected: the client address is unknown.");
            return Problem(StatusCodes.Status403Forbidden, Strings.BotCheckFailed, CodeClientAddressUnknown);
        }
        var (hit, failure) = await HitAsync(ipScope, IpKey(remote), ipLimit, ct);
        if (failure is not null) return failure;
        return hit.Allowed ? null : TooMany(ctx, hit);
    }

    /// <summary>Per (cohort, email) limit for profile submission. The email is normalised exactly as at storage.</summary>
    public async Task<IResult?> CheckSubmissionEmailAsync(HttpContext ctx, Guid cohortId, string? email, CancellationToken ct)
    {
        var normalised = ProfileService.NormalizeEmail(email);
        if (string.IsNullOrEmpty(normalised)) return null; // validation rejects it; nothing to key on
        var (hit, failure) = await HitAsync(RateLimitScopes.ProfileSubmitEmail, $"{cohortId:N}:{normalised}", options.ProfileSubmitPerEmail, ct);
        if (failure is not null) return failure;
        return hit.Allowed ? null : TooMany(ctx, hit);
    }

    /// <summary>Calls the limiter; any store failure fails closed with a deliberate 503 (no PII in the log).</summary>
    private async Task<(RateLimitResult Hit, IResult? Failure)> HitAsync(string scope, string subject, int limit, CancellationToken ct)
    {
        try
        {
            return (await limiter.HitAsync(scope, subject, limit, options.Window, ct), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Rate limiter unavailable for scope {Scope}: {ErrorType}", scope, ex.GetType().Name);
            return (default, Problem(StatusCodes.Status503ServiceUnavailable, Strings.RateLimitUnavailable, CodeRateLimitUnavailable));
        }
    }

    private static IResult TooMany(HttpContext ctx, RateLimitResult hit)
    {
        ctx.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(hit.RetryAfter.TotalSeconds)).ToString();
        return Problem(StatusCodes.Status429TooManyRequests, Strings.TooManyRequests, CodeRateLimited);
    }

    private static IResult Problem(int status, string title, string code)
        => Results.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });
}
