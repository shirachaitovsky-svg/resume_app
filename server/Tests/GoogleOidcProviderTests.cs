using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ResumeApp.Features.Auth;
using ResumeApp.Infrastructure;

namespace ResumeApp.Tests;

/// <summary>Story 6.1: the real provider against a mocked authority (discovery, JWKS, token endpoint) with a generated RSA key. No network.</summary>
public class GoogleOidcProviderTests
{
    private const string Authority = "https://idp.test";
    private const string ClientId = "client-123";
    private const string Kid = "key-1";
    private const string Nonce = "nonce-abc";
    private const string Redirect = "https://app.test/api/auth/google/callback";

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Url, string Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add((request.Method, request.RequestUri!.ToString(), request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct)));
            return respond(request);
        }
    }

    private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Rig
    {
        public RSA SigningKey { get; } = RSA.Create(2048);
        public RSA SecondKey { get; } = RSA.Create(2048);
        /// <summary>When true the JWKS also lists <see cref="SecondKey"/> as "key-2" (a key rotation).</summary>
        public bool Rotated { get; set; }
        public string DiscoveryIssuer { get; set; } = Authority;
        public FakeTime Clock { get; } = new(DateTimeOffset.UtcNow);
        public string? IdToken { get; set; }
        public HttpStatusCode TokenStatus { get; set; } = HttpStatusCode.OK;
        public Handler Http { get; }
        public OidcMetadataStore Store { get; } = new();
        public GoogleOidcProvider Provider { get; }

        public Rig()
        {
            Http = new Handler(r => r.RequestUri!.AbsolutePath switch
            {
                "/.well-known/openid-configuration" => Json(new
                {
                    issuer = DiscoveryIssuer,
                    authorization_endpoint = Authority + "/authorize",
                    token_endpoint = Authority + "/token",
                    jwks_uri = Authority + "/jwks",
                }),
                "/jwks" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Rotated ? Jwks((SigningKey, Kid), (SecondKey, "key-2")) : Jwks((SigningKey, Kid)), Encoding.UTF8, "application/json") },
                "/token" => TokenStatus == HttpStatusCode.OK ? Json(new { id_token = IdToken, access_token = "a" }) : Json(new { error = "invalid_grant" }, TokenStatus),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            });
            var options = new GoogleAuthOptions(GoogleProvider.Google, ClientId, "secret", Authority, Redirect, "https://app.test");
            Provider = new GoogleOidcProvider(new HttpClient(Http), options, Store, Clock, NullLogger<GoogleOidcProvider>.Instance);
        }

        public Task<OidcIdentity?> ExchangeAsync(string nonce = Nonce) => Provider.ExchangeCodeAsync("code-1", "verifier-1", Redirect, nonce, CancellationToken.None);
    }

    private static string Jwks(params (RSA Key, string Kid)[] keys)
        => JsonSerializer.Serialize(new
        {
            keys = keys.Select(k =>
            {
                var p = k.Key.ExportParameters(false);
                return new { kty = "RSA", use = "sig", alg = "RS256", kid = k.Kid, n = Base64UrlEncoder.Encode(p.Modulus), e = Base64UrlEncoder.Encode(p.Exponent) };
            }).ToArray(),
        });

    private static string Token(RSA key, string issuer = Authority, string audience = ClientId, DateTime? expires = null, string nonce = Nonce,
        string sub = "sub-1", string email = "dana@example.com", object? verified = null, string kid = Kid)
    {
        var claims = new Dictionary<string, object>
        {
            ["sub"] = sub, ["email"] = email, ["email_verified"] = verified ?? true, ["nonce"] = nonce,
        };
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = now.AddMinutes(-1),
            NotBefore = now.AddMinutes(-1),
            Expires = expires ?? now.AddMinutes(10),
            Claims = claims,
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(key) { KeyId = kid }, SecurityAlgorithms.RsaSha256),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    [Fact]
    public async Task A_valid_token_yields_the_identity_and_the_code_is_redeemed_with_the_verifier_and_secret()
    {
        var rig = new Rig();
        rig.IdToken = Token(rig.SigningKey);
        var identity = await rig.ExchangeAsync();

        Assert.NotNull(identity);
        Assert.Equal("sub-1", identity.Subject);
        Assert.Equal("dana@example.com", identity.Email);
        Assert.True(identity.EmailVerified);

        var post = Assert.Single(rig.Http.Calls, c => c.Method == HttpMethod.Post);
        Assert.Equal(Authority + "/token", post.Url);
        Assert.Contains("code_verifier=verifier-1", post.Body);
        Assert.Contains("client_secret=secret", post.Body);
        Assert.Contains("grant_type=authorization_code", post.Body);
    }

    [Fact]
    public async Task Email_verified_false_or_as_a_string_is_reported_faithfully()
    {
        var rig = new Rig();
        rig.IdToken = Token(rig.SigningKey, verified: false);
        Assert.False((await rig.ExchangeAsync())!.EmailVerified);
        rig.IdToken = Token(rig.SigningKey, verified: "true");
        Assert.True((await rig.ExchangeAsync())!.EmailVerified);
    }

    [Fact]
    public async Task Discovery_and_keys_are_cached_across_exchanges()
    {
        var rig = new Rig();
        rig.IdToken = Token(rig.SigningKey);
        await rig.ExchangeAsync();
        await rig.ExchangeAsync();
        Assert.Single(rig.Http.Calls, c => c.Url.EndsWith("/.well-known/openid-configuration"));
        Assert.Single(rig.Http.Calls, c => c.Url.EndsWith("/jwks"));
    }

    [Fact]
    public async Task A_token_signed_by_another_key_is_rejected()
    {
        var rig = new Rig();
        using var other = RSA.Create(2048);
        rig.IdToken = Token(other); // same kid, different key
        Assert.Null(await rig.ExchangeAsync());
    }

    [Fact]
    public async Task A_wrong_issuer_audience_expiry_or_nonce_is_rejected()
    {
        var rig = new Rig();
        rig.IdToken = Token(rig.SigningKey, issuer: "https://evil.test");
        Assert.Null(await rig.ExchangeAsync());
        rig.IdToken = Token(rig.SigningKey, audience: "someone-else");
        Assert.Null(await rig.ExchangeAsync());
        rig.IdToken = Token(rig.SigningKey, expires: DateTime.UtcNow.AddMinutes(-30));
        Assert.Null(await rig.ExchangeAsync());
        rig.IdToken = Token(rig.SigningKey, nonce: "different");
        Assert.Null(await rig.ExchangeAsync());
        rig.IdToken = Token(rig.SigningKey);
        Assert.Null(await rig.ExchangeAsync(nonce: "not-the-flow-nonce"));
    }

    [Fact]
    public async Task An_unsigned_or_garbage_token_is_rejected()
    {
        var rig = new Rig();
        rig.IdToken = "not.a.jwt";
        Assert.Null(await rig.ExchangeAsync());
        var unsigned = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor { Issuer = Authority, Audience = ClientId, Claims = new Dictionary<string, object> { ["sub"] = "s", ["nonce"] = Nonce } });
        rig.IdToken = unsigned;
        Assert.Null(await rig.ExchangeAsync());
    }

    [Fact]
    public async Task A_failed_code_exchange_yields_no_identity()
    {
        var rig = new Rig { TokenStatus = HttpStatusCode.BadRequest };
        Assert.Null(await rig.ExchangeAsync());
    }

    [Fact]
    public async Task An_unreachable_authority_yields_no_identity_on_exchange_and_throws_on_authorize()
    {
        var options = new GoogleAuthOptions(GoogleProvider.Google, ClientId, "secret", Authority, Redirect, "https://app.test");
        var http = new Handler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var provider = new GoogleOidcProvider(new HttpClient(http), options, new OidcMetadataStore(), TimeProvider.System, NullLogger<GoogleOidcProvider>.Instance);
        Assert.Null(await provider.ExchangeCodeAsync("c", "v", Redirect, Nonce, CancellationToken.None));
        await Assert.ThrowsAsync<HttpRequestException>(() => provider.BuildAuthorizeUrlAsync(new OidcAuthorizeRequest("s", "n", "c", Redirect), CancellationToken.None));
    }

    [Fact]
    public async Task The_authorize_url_carries_pkce_state_nonce_and_the_redirect_uri()
    {
        var rig = new Rig();
        var url = await rig.Provider.BuildAuthorizeUrlAsync(new OidcAuthorizeRequest("st", "no", "ch", Redirect), CancellationToken.None);
        Assert.StartsWith(Authority + "/authorize?", url.AbsoluteUri);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(url.Query);
        Assert.Equal(ClientId, query["client_id"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("st", query["state"]);
        Assert.Equal("no", query["nonce"]);
        Assert.Equal("ch", query["code_challenge"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal(Redirect, query["redirect_uri"]);
        Assert.Contains("openid", query["scope"].ToString());
    }

    [Fact]
    public async Task A_discovery_issuer_that_differs_from_the_authority_is_a_failure()
    {
        var rig = new Rig { DiscoveryIssuer = "https://other.test" };
        rig.IdToken = Token(rig.SigningKey);
        Assert.Null(await rig.ExchangeAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Provider.BuildAuthorizeUrlAsync(new OidcAuthorizeRequest("s", "n", "c", Redirect), CancellationToken.None));
    }

    [Fact]
    public async Task An_unknown_kid_refetches_the_jwks_once_then_is_accepted_and_a_second_unknown_kid_inside_the_throttle_window_does_not_refetch()
    {
        var rig = new Rig();
        rig.IdToken = Token(rig.SigningKey);
        Assert.NotNull(await rig.ExchangeAsync());
        Assert.Single(rig.Http.Calls, c => c.Url.EndsWith("/jwks"));

        rig.Rotated = true;
        rig.Clock.Now += TimeSpan.FromMinutes(2); // past the minimum refresh interval
        rig.IdToken = Token(rig.SecondKey, kid: "key-2");
        Assert.NotNull(await rig.ExchangeAsync());
        Assert.Equal(2, rig.Http.Calls.Count(c => c.Url.EndsWith("/jwks")));

        rig.Clock.Now += TimeSpan.FromSeconds(10); // inside the throttle window
        using var stranger = RSA.Create(2048);
        rig.IdToken = Token(stranger, kid: "key-3");
        Assert.Null(await rig.ExchangeAsync());
        Assert.Equal(2, rig.Http.Calls.Count(c => c.Url.EndsWith("/jwks")));
    }
}
