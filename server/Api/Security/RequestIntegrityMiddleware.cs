using Microsoft.AspNetCore.Antiforgery;
using ResumeApp.Api.Resources;

namespace ResumeApp.Api.Security;

/// <summary>
/// State-changing requests need a same-origin Origin header (AD-20) and a valid anti-forgery token.
/// The expected origin is the (forwarded) request host, plus any ALLOWED_ORIGINS entries.
/// </summary>
public sealed class RequestIntegrityMiddleware(RequestDelegate next, IAntiforgery antiforgery, IConfiguration configuration)
{
    private readonly HashSet<string> _extraOrigins = (configuration["ALLOWED_ORIGINS"] ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(o => o.TrimEnd('/').ToLowerInvariant())
        .ToHashSet();

    public async Task InvokeAsync(HttpContext context)
    {
        var method = context.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method))
        {
            await next(context);
            return;
        }

        if (!OriginAllowed(context))
        {
            await Reject(context);
            return;
        }

        // Participant requests (story 2.2) must not depend on which cookies the browser holds: an admin cookie would make
        // context.User the admin, and the participant policy replaces it with the participant. Their tokens come from
        // GET /profiles/csrf, which issues a token for the anonymous principal, so validate against that same identity.
        var user = context.User;
        if (UsesAnonymousToken(context.Request.Path)) context.User = Anonymous();
        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (Exception ex) when (ex is AntiforgeryValidationException or System.Security.Cryptography.CryptographicException or InvalidDataException)
        {
            await Reject(context);
            return;
        }
        finally
        {
            context.User = user;
        }

        await next(context);
    }

    /// <summary>The paths whose anti-forgery token is bound to the anonymous principal (tokens from <c>GET /profiles/csrf</c>).</summary>
    public static bool UsesAnonymousToken(PathString path)
        => path.StartsWithSegments("/me") || path.StartsWithSegments("/auth/google") || path.StartsWithSegments("/profiles/session") || path.StartsWithSegments("/contact-decisions") || IsContactRequestPath(path);

    /// <summary>Exactly <c>/contact-requests/verify</c> and <c>/profiles/{id}/contact-requests</c> (one id segment, no trailing slash).</summary>
    public static bool IsContactRequestPath(PathString path)
    {
        var parts = (path.Value ?? "").Split('/');
        return (parts.Length == 3 && parts[0].Length == 0 && parts[1].Equals("contact-requests", StringComparison.OrdinalIgnoreCase) && parts[2].Equals("verify", StringComparison.OrdinalIgnoreCase))
            || (parts.Length == 4 && parts[0].Length == 0 && parts[1].Equals("profiles", StringComparison.OrdinalIgnoreCase) && parts[2].Length > 0
                && parts[3].Equals("contact-requests", StringComparison.OrdinalIgnoreCase));
    }

    public static System.Security.Claims.ClaimsPrincipal Anonymous() => new(new System.Security.Claims.ClaimsIdentity());

    private bool OriginAllowed(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin) || !Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (_extraOrigins.Contains(origin.TrimEnd('/').ToLowerInvariant()))
        {
            return true;
        }

        var host = context.Request.Host;
        var sameHost = string.Equals(uri.Host, host.Host, StringComparison.OrdinalIgnoreCase)
            && (host.Port.HasValue ? uri.Port == host.Port.Value : uri.IsDefaultPort);
        return sameHost && string.Equals(uri.Scheme, context.Request.Scheme, StringComparison.OrdinalIgnoreCase);
    }

    private static Task Reject(HttpContext context)
        => Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: Strings.CsrfRejected).ExecuteAsync(context);
}
