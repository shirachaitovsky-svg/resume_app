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

        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (Exception ex) when (ex is AntiforgeryValidationException or System.Security.Cryptography.CryptographicException or InvalidDataException)
        {
            await Reject(context);
            return;
        }

        await next(context);
    }

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
