using System.Security.Cryptography;
using System.Text;

namespace ResumeApp.Api.Security;

/// <summary>
/// Rejects every request that does not carry the shared secret Vercel adds (AD-9).
/// Runs before forwarded-headers processing so spoofed headers are never trusted.
/// </summary>
public sealed class OriginSecretMiddleware
{
    public const string HeaderName = "x-origin-secret";

    private readonly RequestDelegate _next;
    private readonly byte[] _secret;

    public OriginSecretMiddleware(RequestDelegate next, OriginSecretOptions options)
    {
        _next = next;
        _secret = SHA256.HashData(Encoding.UTF8.GetBytes(options.Secret));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var provided = context.Request.Headers[HeaderName].ToString();
        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));

        if (provided.Length == 0 || !CryptographicOperations.FixedTimeEquals(providedHash, _secret))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await _next(context);
    }
}

public sealed record OriginSecretOptions(string Secret);
