using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ResumeApp.Infrastructure;

public enum GoogleProvider { Google, Fake }

/// <summary>
/// Google sign-in configuration from environment variables only. Outside Development the real provider with a client id and secret is
/// mandatory (startup fails otherwise); the fake provider is allowed in Development only.
/// </summary>
public sealed record GoogleAuthOptions(GoogleProvider Provider, string? ClientId, string? ClientSecret, string Authority, string RedirectUri, string PublicBaseUrl)
{
    public const string DefaultAuthority = "https://accounts.google.com";
    public const string CallbackPath = "/api/auth/google/callback";

    public static GoogleAuthOptions Resolve(IConfiguration configuration, IHostEnvironment environment, string publicBaseUrl)
    {
        var dev = environment.IsDevelopment();
        string? Get(string key) => configuration[key] is { } v && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

        var raw = Get("GOOGLE_PROVIDER")?.ToLowerInvariant();
        var provider = raw switch
        {
            null => dev ? GoogleProvider.Fake : throw new InvalidOperationException("Missing required environment variable GOOGLE_PROVIDER (google)."),
            "google" => GoogleProvider.Google,
            "fake" => GoogleProvider.Fake,
            _ => throw new InvalidOperationException("GOOGLE_PROVIDER must be 'google' or 'fake'."),
        };
        if (provider == GoogleProvider.Fake && !dev)
            throw new InvalidOperationException("GOOGLE_PROVIDER=fake is allowed in Development only.");

        var authority = (Get("GOOGLE_AUTHORITY") ?? DefaultAuthority).TrimEnd('/');
        if (!Uri.TryCreate(authority, UriKind.Absolute, out var authorityUri) || authorityUri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("GOOGLE_AUTHORITY must be an absolute http or https URL.");
        if (!dev && authorityUri.Scheme != "https")
            throw new InvalidOperationException("GOOGLE_AUTHORITY must be an https URL outside Development.");

        string? clientId = Get("GOOGLE_CLIENT_ID"), secret = Get("GOOGLE_CLIENT_SECRET");
        if (provider == GoogleProvider.Google)
        {
            if (clientId is null) throw new InvalidOperationException("Missing required environment variable GOOGLE_CLIENT_ID.");
            if (secret is null) throw new InvalidOperationException("Missing required environment variable GOOGLE_CLIENT_SECRET.");
        }

        return new(provider, clientId, secret, authority, publicBaseUrl.TrimEnd('/') + CallbackPath, publicBaseUrl.TrimEnd('/'));
    }
}
