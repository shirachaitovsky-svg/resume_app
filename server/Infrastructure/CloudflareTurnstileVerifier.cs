using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ResumeApp.Features.PublicForms;

namespace ResumeApp.Infrastructure;

public sealed record TurnstileOptions(string Secret)
{
    /// <summary>Cloudflare test secret that always passes. Development only.</summary>
    public const string DevAlwaysPassSecret = "1x0000000000000000000000000000000AA";

    /// <summary>Cloudflare test secret that always fails (useful for manual checks).</summary>
    public const string DevAlwaysFailSecret = "2x0000000000000000000000000000000AA";

    public const string EndpointUrl = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

    /// <summary>TURNSTILE_SECRET is mandatory outside Development; there is no bypass mode in production.</summary>
    public static TurnstileOptions Resolve(IConfiguration configuration, IHostEnvironment environment)
    {
        var secret = configuration["TURNSTILE_SECRET"]?.Trim();
        if (!string.IsNullOrEmpty(secret)) return new(secret);
        if (environment.IsDevelopment()) return new(DevAlwaysPassSecret);
        throw new InvalidOperationException("Missing required environment variable TURNSTILE_SECRET.");
    }
}

/// <summary>Verifies a Turnstile token against Cloudflare siteverify. Fails closed on every error and logs the category only.</summary>
public sealed class CloudflareTurnstileVerifier(HttpClient http, TurnstileOptions options, ILogger<CloudflareTurnstileVerifier> logger) : ITurnstileVerifier
{
    public const int MaxTokenLength = 2048; // Cloudflare documented maximum
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    public async Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > MaxTokenLength) return false;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            var form = new List<KeyValuePair<string, string>>
            {
                new("secret", options.Secret),
                new("response", token),
            };
            if (!string.IsNullOrEmpty(remoteIp)) form.Add(new("remoteip", remoteIp));

            using var response = await http.PostAsync(TurnstileOptions.EndpointUrl, new FormUrlEncodedContent(form), timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Turnstile verification failed closed: non-success HTTP status {Status}.", (int)response.StatusCode);
                return false;
            }

            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var doc = await JsonDocument.ParseAsync(body, cancellationToken: timeout.Token);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("success", out var success))
            {
                if (success.ValueKind == JsonValueKind.True) return true;
                if (success.ValueKind == JsonValueKind.False)
                {
                    // Cloudflare error codes (for example invalid-input-secret) are fixed identifiers, safe to log.
                    logger.LogWarning("Turnstile verification rejected (success=false). Error codes: {ErrorCodes}.", ErrorCodes(root));
                    return false;
                }
            }
            logger.LogWarning("Turnstile verification failed closed: unexpected response shape.");
            return false;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // the caller went away; nothing to decide
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Turnstile verification failed closed: timeout.");
            return false;
        }
        catch (HttpRequestException)
        {
            logger.LogWarning("Turnstile verification failed closed: network error.");
            return false;
        }
        catch (JsonException)
        {
            logger.LogWarning("Turnstile verification failed closed: bad JSON.");
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Turnstile verification failed closed: {ErrorType}.", ex.GetType().Name);
            return false;
        }
    }

    private static string ErrorCodes(JsonElement root)
    {
        if (!root.TryGetProperty("error-codes", out var codes) || codes.ValueKind != JsonValueKind.Array) return "none";
        var list = codes.EnumerateArray()
            .Where(c => c.ValueKind == JsonValueKind.String)
            .Select(c => c.GetString()!)
            .Where(c => c.Length <= 64 && c.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-'))
            .Take(10);
        var joined = string.Join(",", list);
        return joined.Length == 0 ? "none" : joined;
    }
}
