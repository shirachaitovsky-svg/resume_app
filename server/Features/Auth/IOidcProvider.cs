namespace ResumeApp.Features.Auth;

/// <summary>What the authorize redirect carries (OIDC authorization code flow with PKCE, epic 6).</summary>
public sealed record OidcAuthorizeRequest(string State, string Nonce, string CodeChallenge, string RedirectUri);

/// <summary>A verified identity token's relevant claims. The provider has already checked signature, issuer, audience, expiry and nonce.</summary>
public sealed record OidcIdentity(string Subject, string? Email, bool EmailVerified);

/// <summary>The single swap point between real Google and the development fake (AD-5, AD-9).</summary>
public interface IOidcProvider
{
    /// <summary>The URL to send the browser to. Throws when the provider is unreachable (the caller treats that as a failed sign-in).</summary>
    Task<Uri> BuildAuthorizeUrlAsync(OidcAuthorizeRequest request, CancellationToken ct);

    /// <summary>Exchanges the code and validates the id_token. Null for any failure (exchange, signature, iss, aud, expiry, nonce).</summary>
    Task<OidcIdentity?> ExchangeCodeAsync(string code, string codeVerifier, string redirectUri, string expectedNonce, CancellationToken ct);
}

public enum GoogleSignInStatus { Ok, EmailTaken }

/// <param name="ProfileId">The profile linked to the Google account, or null when there is none yet.</param>
public sealed record GoogleSignInResult(GoogleSignInStatus Status, Guid? ProfileId);

public interface IGoogleSignInRepository
{
    /// <summary>
    /// Finds the profile by <c>sub</c> only. For a linked profile whose stored email differs from <paramref name="normalizedEmail"/>, updates it
    /// and advances ContactVersion in one transaction; a (cohort, email) clash changes nothing and reports <see cref="GoogleSignInStatus.EmailTaken"/>.
    /// </summary>
    Task<GoogleSignInResult> SignInAsync(string sub, string normalizedEmail, CancellationToken ct);

    /// <summary>The profile linked to the Google account right now (a removed profile still counts), or null.</summary>
    Task<Guid?> FindProfileIdAsync(string sub, CancellationToken ct);
}
