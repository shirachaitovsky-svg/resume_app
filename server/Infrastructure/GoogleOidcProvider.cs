using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ResumeApp.Features.Auth;

namespace ResumeApp.Infrastructure;

/// <summary>Discovery document and signing keys, cached and shared by every (transient) provider instance.</summary>
public sealed class OidcMetadataStore
{
    public sealed record Discovery(string Issuer, string AuthorizationEndpoint, string TokenEndpoint, string JwksUri);

    public static readonly TimeSpan DiscoveryTtl = TimeSpan.FromHours(6);
    public static readonly TimeSpan KeysTtl = TimeSpan.FromHours(1);
    /// <summary>An unknown key id triggers one refresh, but never more often than this (no refresh storms from forged tokens).</summary>
    public static readonly TimeSpan MinKeysRefresh = TimeSpan.FromMinutes(1);

    internal readonly SemaphoreSlim Gate = new(1, 1);
    internal Discovery? Document;
    internal DateTime DocumentAt;
    internal IReadOnlyList<SecurityKey> Keys = [];
    internal DateTime KeysAt;
}

/// <summary>
/// Real Google (or any OIDC authority): authorization code flow with PKCE, id_token validated against the authority's JWKS.
/// Nothing sensitive (code, state, tokens, email) is ever logged.
/// </summary>
public sealed class GoogleOidcProvider(HttpClient http, GoogleAuthOptions options, OidcMetadataStore store, TimeProvider clock, ILogger<GoogleOidcProvider> logger) : IOidcProvider
{
    public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(2);

    public async Task<Uri> BuildAuthorizeUrlAsync(OidcAuthorizeRequest request, CancellationToken ct)
    {
        var doc = await GetDiscoveryAsync(ct);
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = options.ClientId,
            ["response_type"] = "code",
            ["scope"] = "openid email",
            ["redirect_uri"] = request.RedirectUri,
            ["state"] = request.State,
            ["nonce"] = request.Nonce,
            ["code_challenge"] = request.CodeChallenge,
            ["code_challenge_method"] = "S256",
            ["prompt"] = "select_account",
        };
        var separator = doc.AuthorizationEndpoint.Contains('?') ? "&" : "?";
        return new Uri(doc.AuthorizationEndpoint + separator
            + string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value ?? "")}")));
    }

    public async Task<OidcIdentity?> ExchangeCodeAsync(string code, string codeVerifier, string redirectUri, string expectedNonce, CancellationToken ct)
    {
        try
        {
            var doc = await GetDiscoveryAsync(ct);
            var idToken = await RedeemAsync(doc, code, codeVerifier, redirectUri, ct);
            if (idToken is null) return null;
            return await ValidateAsync(doc, idToken, expectedNonce, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Google sign-in failed: {ErrorType}", ex.GetType().Name);
            return null;
        }
    }

    private async Task<string?> RedeemAsync(OidcMetadataStore.Discovery doc, string code, string verifier, string redirectUri, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, doc.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
                ["client_id"] = options.ClientId ?? "",
                ["client_secret"] = options.ClientSecret ?? "",
                ["code_verifier"] = verifier,
            }),
        };
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Google code exchange was rejected with status {Status}.", (int)response.StatusCode);
            return null;
        }
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return json.RootElement.TryGetProperty("id_token", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
    }

    private async Task<OidcIdentity?> ValidateAsync(OidcMetadataStore.Discovery doc, string idToken, string expectedNonce, CancellationToken ct)
    {
        var handler = new JsonWebTokenHandler();
        if (!handler.CanReadToken(idToken)) return null;
        var kid = handler.ReadJsonWebToken(idToken).Kid;

        var keys = await GetKeysAsync(doc, kid, ct);
        var issuers = new List<string> { doc.Issuer };
        if (doc.Issuer == GoogleAuthOptions.DefaultAuthority) issuers.Add("accounts.google.com"); // Google documents both forms
        var parameters = new TokenValidationParameters
        {
            ValidIssuers = issuers,
            ValidAudience = options.ClientId,
            IssuerSigningKeys = keys,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ClockSkew = ClockSkew,
            LifetimeValidator = (notBefore, expires, _, p) =>
            {
                var now = clock.GetUtcNow().UtcDateTime;
                return expires is not null && expires.Value.ToUniversalTime() + p.ClockSkew > now
                    && (notBefore is null || notBefore.Value.ToUniversalTime() - p.ClockSkew <= now);
            },
        };
        var result = await handler.ValidateTokenAsync(idToken, parameters);
        if (!result.IsValid) return null;

        var claims = result.ClaimsIdentity.Claims.ToList();
        string? Get(string type) => claims.FirstOrDefault(c => c.Type == type)?.Value;
        if (!string.Equals(Get("nonce"), expectedNonce, StringComparison.Ordinal)) return null;
        var sub = Get("sub");
        if (string.IsNullOrEmpty(sub) || sub.Length > ResumeApp.Domain.Profiles.Profile.GoogleSubMaxLength) return null;
        var verified = string.Equals(Get("email_verified"), "true", StringComparison.OrdinalIgnoreCase);
        return new OidcIdentity(sub, Get("email"), verified);
    }

    private async Task<OidcMetadataStore.Discovery> GetDiscoveryAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (store.Document is { } cached && now - store.DocumentAt < OidcMetadataStore.DiscoveryTtl) return cached;

        await store.Gate.WaitAsync(ct);
        try
        {
            now = clock.GetUtcNow().UtcDateTime;
            if (store.Document is { } again && now - store.DocumentAt < OidcMetadataStore.DiscoveryTtl) return again;
            using var response = await http.GetAsync(options.Authority + "/.well-known/openid-configuration", ct);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = json.RootElement;
            string Need(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
                ? v.GetString()! : throw new InvalidOperationException("The discovery document is incomplete.");
            // OIDC discovery: the issuer must be the authority we asked.
            if (Need("issuer").TrimEnd('/') != options.Authority.TrimEnd('/'))
                throw new InvalidOperationException("The discovery issuer does not match the configured authority.");
            var doc = new OidcMetadataStore.Discovery(Need("issuer"), Need("authorization_endpoint"), Need("token_endpoint"), Need("jwks_uri"));
            store.Document = doc;
            store.DocumentAt = now;
            return doc;
        }
        finally
        {
            store.Gate.Release();
        }
    }

    private async Task<IReadOnlyList<SecurityKey>> GetKeysAsync(OidcMetadataStore.Discovery doc, string? kid, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        bool Fresh() => store.Keys.Count > 0 && now - store.KeysAt < OidcMetadataStore.KeysTtl;
        bool Has() => kid is null || store.Keys.Any(k => k.KeyId == kid);
        if (Fresh() && Has()) return store.Keys;

        await store.Gate.WaitAsync(ct);
        try
        {
            now = clock.GetUtcNow().UtcDateTime;
            var refreshAllowed = store.Keys.Count == 0 || now - store.KeysAt >= OidcMetadataStore.MinKeysRefresh;
            if ((Fresh() && Has()) || !refreshAllowed) return store.Keys;
            using var response = await http.GetAsync(doc.JwksUri, ct);
            response.EnsureSuccessStatusCode();
            var set = new JsonWebKeySet(await response.Content.ReadAsStringAsync(ct));
            store.Keys = set.GetSigningKeys().ToList();
            store.KeysAt = now;
            return store.Keys;
        }
        finally
        {
            store.Gate.Release();
        }
    }
}
