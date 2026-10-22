using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using ResumeApp.Features.Auth;

namespace ResumeApp.Infrastructure;

/// <summary>
/// Development stand-in for Google. No network and no signed token: the "code" carries the chosen identity, the nonce and the PKCE challenge,
/// and the exchange checks the verifier and the nonce. Token validation is covered by tests of <see cref="GoogleOidcProvider"/>.
/// </summary>
public sealed class FakeOidcProvider(GoogleAuthOptions options) : IOidcProvider
{
    public const string DefaultEmail = "participant@example.test";
    public const string AuthorizePath = "/api/dev-idp/authorize";

    public sealed record FakeCode(string Email, bool Verified, string Nonce, string Challenge);

    public Task<Uri> BuildAuthorizeUrlAsync(OidcAuthorizeRequest request, CancellationToken ct)
    {
        var url = QueryHelpers.AddQueryString(options.PublicBaseUrl + AuthorizePath, new Dictionary<string, string?>
        {
            ["state"] = request.State,
            ["nonce"] = request.Nonce,
            ["code_challenge"] = request.CodeChallenge,
            ["redirect_uri"] = request.RedirectUri,
        });
        return Task.FromResult(new Uri(url));
    }

    public Task<OidcIdentity?> ExchangeCodeAsync(string code, string codeVerifier, string redirectUri, string expectedNonce, CancellationToken ct)
    {
        try
        {
            var data = JsonSerializer.Deserialize<FakeCode>(WebEncoders.Base64UrlDecode(code), JsonSerializerOptions.Web);
            if (data is null || data.Nonce != expectedNonce || Challenge(codeVerifier) != data.Challenge)
                return Task.FromResult<OidcIdentity?>(null);
            return Task.FromResult<OidcIdentity?>(new OidcIdentity(SubFor(data.Email), data.Email, data.Verified));
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return Task.FromResult<OidcIdentity?>(null);
        }
    }

    public static string EncodeCode(FakeCode data) => WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(data, JsonSerializerOptions.Web));

    /// <summary>Deterministic from the email, so the same fake identity is the same account every time.</summary>
    public static string SubFor(string email)
        => "fake-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant()))).ToLowerInvariant()[..32];

    public static string Challenge(string verifier) => WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
}
