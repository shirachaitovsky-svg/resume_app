using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using MimeKit;

namespace ResumeApp.Infrastructure;

public enum EmailProvider { Smtp, Resend }

public sealed record SmtpSettings(string Host, int Port, string? User, string? Password, bool Secure);

/// <summary>
/// Email configuration from environment variables only. In Development the default is SMTP to the local catcher
/// (<c>localhost:1025</c>); anywhere else the provider must be configured or startup fails with a clear message.
/// </summary>
public sealed record EmailOptions(EmailProvider Provider, string From, string PublicBaseUrl, SmtpSettings? Smtp, string? ResendApiKey)
{
    public static EmailOptions Resolve(IConfiguration configuration, IHostEnvironment environment)
    {
        var dev = environment.IsDevelopment();
        string? Get(string key) => configuration[key] is { } v && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
        string Require(string key) => Get(key) ?? throw new InvalidOperationException($"Missing required environment variable {key}.");

        var raw = Get("EMAIL_PROVIDER")?.ToLowerInvariant();
        if (raw is null)
        {
            if (!dev) throw new InvalidOperationException("Missing required environment variable EMAIL_PROVIDER (smtp or resend).");
            raw = "smtp";
        }
        var provider = raw switch
        {
            "smtp" => EmailProvider.Smtp,
            "resend" => EmailProvider.Resend,
            _ => throw new InvalidOperationException("EMAIL_PROVIDER must be 'smtp' or 'resend'."),
        };

        var from = Get("EMAIL_FROM") ?? (dev ? "no-reply@resume-app.localhost" : Require("EMAIL_FROM"));
        // Fail at startup, not at the first send.
        if (!MailboxAddress.TryParse(from, out _))
            throw new InvalidOperationException("EMAIL_FROM must be a valid email address (optionally with a display name).");

        var baseUrl = Get("PUBLIC_BASE_URL") ?? (dev ? "http://localhost:4200" : Require("PUBLIC_BASE_URL"));
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException("PUBLIC_BASE_URL must be an absolute http or https URL.");
        if (!dev && (uri.Scheme != "https" || baseUrl.Contains('?') || baseUrl.Contains('#')))
            throw new InvalidOperationException("PUBLIC_BASE_URL must be an https URL without a query or fragment outside Development.");
        baseUrl = baseUrl.TrimEnd('/');

        if (provider == EmailProvider.Resend)
            return new(provider, from, baseUrl, null, Require("RESEND_API_KEY"));

        var host = Get("SMTP_HOST") ?? (dev ? "localhost" : Require("SMTP_HOST"));
        var portRaw = Get("SMTP_PORT");
        var port = dev ? 1025 : 587;
        if (portRaw is not null && (!int.TryParse(portRaw, out port) || port is < 1 or > 65535))
            throw new InvalidOperationException("SMTP_PORT must be a valid port number.");
        var secureRaw = Get("SMTP_SECURE");
        var secure = !dev;
        if (secureRaw is not null && !bool.TryParse(secureRaw, out secure))
            throw new InvalidOperationException("SMTP_SECURE must be true or false.");
        var user = Get("SMTP_USER");
        if (!dev && !secure && user is not null)
            throw new InvalidOperationException("SMTP_SECURE=false is not allowed with SMTP_USER set outside Development (credentials would be sent in clear text).");
        return new(provider, from, baseUrl, new SmtpSettings(host, port, user, Get("SMTP_PASSWORD"), secure), null);
    }

    /// <summary>EMAIL_WORKER: true/false/1/0/yes/no/on/off (any case). Absent means on; anything else fails startup.</summary>
    public static bool ParseWorkerFlag(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return true;
        return raw.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "yes" or "on" => true,
            "false" or "0" or "no" or "off" => false,
            _ => throw new InvalidOperationException("EMAIL_WORKER must be true, false, 1, 0, yes, no, on or off."),
        };
    }
}
