namespace ResumeApp.Features.PublicForms;

/// <summary>Anti-bot verification (Cloudflare Turnstile). Must fail closed: any doubt returns false.</summary>
public interface ITurnstileVerifier
{
    Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken ct);
}
