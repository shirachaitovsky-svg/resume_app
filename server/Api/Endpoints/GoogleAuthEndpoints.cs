using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using ResumeApp.Api.Resources;
using ResumeApp.Api.Security;
using ResumeApp.Features.Auth;
using ResumeApp.Features.Profiles;
using ResumeApp.Features.PublicForms;
using ResumeApp.Infrastructure;

namespace ResumeApp.Api.Endpoints;

/// <summary>
/// Sign-in with Google (epic 6, story 6.1): OIDC authorization code flow with PKCE, state and nonce. Identity is the Google <c>sub</c>;
/// the session is the participant cookie. Neither the code, the state, the id_token nor the email is ever logged.
/// </summary>
public static class GoogleAuthEndpoints
{
    public const string OidcCookieName = "__Host-resume-oidc";
    public const string DefaultReturnUrl = "/me";
    public const string GoogleSubClaim = "google_sub";
    public const string EmailClaim = "email";
    public const int MaxReturnUrlLength = 512;
    public static readonly TimeSpan FlowLifetime = TimeSpan.FromMinutes(10);
    private const string ProtectorPurpose = "ResumeApp.GoogleOidcFlow.v1";

    public sealed record MeResponse(string Email, bool HasProfile, Guid? ProfileId);

    private sealed record FlowState(string State, string Nonce, string Verifier, string ReturnUrl, long ExpiresAt);

    public static IEndpointRouteBuilder MapGoogleAuth(this IEndpointRouteBuilder app)
    {
        app.MapGet("/auth/google/start", async (HttpContext ctx, IOidcProvider provider, GoogleAuthOptions options, PublicFormGuard guard,
                IDataProtectionProvider protection, TimeProvider clock, CancellationToken ct) =>
            {
                NoStore(ctx);
                // A top-level browser navigation: never answer with raw problem JSON.
                if (await guard.CheckIpAsync(ctx, RateLimitScopes.GoogleAuthIp, guard.Options.GoogleAuthPerIp, ct) is not null)
                    return Results.Redirect(Failure(options, "failed"));

                var flow = new FlowState(RandomToken(), RandomToken(), RandomToken(), SafeReturnUrl(ctx.Request.Query["returnUrl"].ToString()),
                    clock.GetUtcNow().Add(FlowLifetime).ToUnixTimeSeconds());

                Uri authorize;
                try
                {
                    authorize = await provider.BuildAuthorizeUrlAsync(
                        new OidcAuthorizeRequest(flow.State, flow.Nonce, FakeOidcProvider.Challenge(flow.Verifier), options.RedirectUri), ct);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or UriFormatException)
                {
                    return Results.Redirect(Failure(options, "failed"));
                }

                ctx.Response.Cookies.Append(OidcCookieName, protection.CreateProtector(ProtectorPurpose).Protect(JsonSerializer.Serialize(flow)), OidcCookie(FlowLifetime));
                return Results.Redirect(authorize.AbsoluteUri);
            })
            .AllowAnonymous();

        app.MapGet("/auth/google/callback", async (HttpContext ctx, IOidcProvider provider, GoogleAuthOptions options, IGoogleSignInRepository repository,
                PublicFormGuard guard, IDataProtectionProvider protection, TimeProvider clock, CancellationToken ct) =>
            {
                NoStore(ctx);
                if (await guard.CheckIpAsync(ctx, RateLimitScopes.GoogleAuthIp, guard.Options.GoogleAuthPerIp, ct) is not null)
                {
                    ctx.Response.Cookies.Delete(OidcCookieName, OidcCookie(null));
                    return Results.Redirect(Failure(options, "failed"));
                }

                // The flow cookie is single use: gone after the first callback, whatever the outcome.
                var flow = ReadFlow(ctx, protection, clock);
                ctx.Response.Cookies.Delete(OidcCookieName, OidcCookie(null));

                var state = ctx.Request.Query["state"].ToString();
                if (flow is null || state.Length == 0 || !FixedEquals(state, flow.State)) return Results.Redirect(Failure(options, "failed"));

                var error = ctx.Request.Query["error"].ToString();
                if (error.Length > 0) return Results.Redirect(Failure(options, error == "access_denied" ? "cancelled" : "failed"));

                var code = ctx.Request.Query["code"].ToString();
                if (code.Length == 0) return Results.Redirect(Failure(options, "failed"));

                OidcIdentity? identity;
                try
                {
                    identity = await provider.ExchangeCodeAsync(code, flow.Verifier, options.RedirectUri, flow.Nonce, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return Results.Redirect(Failure(options, "failed"));
                }
                if (identity is null) return Results.Redirect(Failure(options, "failed"));
                if (!identity.EmailVerified || string.IsNullOrWhiteSpace(identity.Email)) return Results.Redirect(Failure(options, "unverified"));
                var email = ProfileService.ValidateEmail(identity.Email, out _);
                if (email is null) return Results.Redirect(Failure(options, "failed"));

                GoogleSignInResult result;
                try
                {
                    result = await repository.SignInAsync(identity.Subject, email, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return Results.Redirect(Failure(options, "failed"));
                }
                if (result.Status != GoogleSignInStatus.Ok) return Results.Redirect(Failure(options, "failed"));

                await SignInParticipantAsync(ctx, identity.Subject, email, result.ProfileId, clock.GetUtcNow());
                return Results.Redirect(flow.ReturnUrl);
            })
            .AllowAnonymous();

        app.MapGet("/auth/google/me", async (HttpContext ctx, IGoogleSignInRepository repository, CancellationToken ct) =>
            {
                NoStore(ctx);
                if (await GoogleSessionAsync(ctx) is not { } session)
                    return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: Strings.SessionInvalid);
                var (sub, email) = (session.Sub, session.Email);

                // The database, not the session, says whether the account has a profile: it may be linked (or removed) after sign-in.
                var profileId = await repository.FindProfileIdAsync(sub, ct);
                return Results.Json(new MeResponse(email, profileId is not null, profileId));
            })
            .AllowAnonymous();

        // Anti-forgery and Origin are enforced by RequestIntegrityMiddleware (token from GET /profiles/csrf).
        app.MapPost("/auth/google/logout", async (HttpContext ctx) =>
            {
                NoStore(ctx);
                await ctx.SignOutAsync(IdentityServiceExtensions.ParticipantScheme);
                return Results.NoContent();
            })
            .AllowAnonymous();

        // Development only: the fake identity provider's authorize endpoint. Mapped only when GOOGLE_PROVIDER=fake (which needs Development).
        var google = app.ServiceProvider.GetRequiredService<GoogleAuthOptions>();
        if (google.Provider == GoogleProvider.Fake)
        {
            app.MapGet("/dev-idp/authorize", (HttpContext ctx) =>
                {
                    NoStore(ctx);
                    var q = ctx.Request.Query;
                    var state = q["state"].ToString();
                    if (state.Length == 0) return Results.BadRequest();
                    var target = google.RedirectUri;
                    if (q["fake_error"].ToString() is { Length: > 0 } fakeError)
                        return Results.Redirect(QueryHelpers.AddQueryString(target, new Dictionary<string, string?> { ["error"] = fakeError, ["state"] = state }));

                    var email = q["fake_email"].ToString() is { Length: > 0 } e ? e : FakeOidcProvider.DefaultEmail;
                    var verified = !string.Equals(q["fake_verified"].ToString(), "false", StringComparison.OrdinalIgnoreCase);
                    var code = FakeOidcProvider.EncodeCode(new FakeOidcProvider.FakeCode(email, verified, q["nonce"].ToString(), q["code_challenge"].ToString()));
                    return Results.Redirect(QueryHelpers.AddQueryString(target, new Dictionary<string, string?> { ["code"] = code, ["state"] = state }));
                })
                .AllowAnonymous();
        }

        return app;
    }

    /// <summary>A valid Google participant session: its <c>sub</c> and email, and the profile id when the session carries one.</summary>
    public sealed record GoogleSession(string Sub, string Email, bool HasProfileClaim);

    /// <summary>Authenticates the participant cookie by hand (the endpoints are AllowAnonymous). Null when it is not a Google sign-in session.</summary>
    public static async Task<GoogleSession?> GoogleSessionAsync(HttpContext ctx)
    {
        var auth = await ctx.AuthenticateAsync(IdentityServiceExtensions.ParticipantScheme);
        var user = auth.Principal;
        if (!auth.Succeeded || user is null || user.FindFirstValue(IdentityServiceExtensions.PurposeClaim) != IdentityServiceExtensions.GoogleSignInPurpose
            || user.FindFirstValue(EmailClaim) is not { Length: > 0 } email
            || user.FindFirstValue(GoogleSubClaim) is not { Length: > 0 } sub)
            return null;
        return new(sub, email, user.FindFirstValue(IdentityServiceExtensions.ProfileIdClaim) is not null);
    }

    /// <summary>Issues (or re-issues) the participant session cookie for a Google account, with <c>profile_id</c> when it has a profile.</summary>
    public static Task SignInParticipantAsync(HttpContext ctx, string sub, string email, Guid? profileId, DateTimeOffset now)
    {
        var claims = new List<Claim>
        {
            new(GoogleSubClaim, sub),
            new(EmailClaim, email),
            new(IdentityServiceExtensions.PurposeClaim, IdentityServiceExtensions.GoogleSignInPurpose),
        };
        if (profileId is { } id) claims.Add(new(IdentityServiceExtensions.ProfileIdClaim, id.ToString("D")));

        return ctx.SignInAsync(IdentityServiceExtensions.ParticipantScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, IdentityServiceExtensions.ParticipantScheme)),
            new AuthenticationProperties
            {
                IsPersistent = true,
                AllowRefresh = false,
                IssuedUtc = now,
                ExpiresUtc = now + IdentityServiceExtensions.ParticipantSessionLifetime,
            });
    }

    /// <summary>Only a same-site relative path is allowed: starts with one slash, no scheme, no backslash, no control characters.</summary>
    public static string SafeReturnUrl(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxReturnUrlLength) return DefaultReturnUrl;
        if (value[0] != '/' || (value.Length > 1 && value[1] is '/' or '\\')) return DefaultReturnUrl;
        if (value.Contains('\\') || value.Contains("://") || value.Any(char.IsControl) || value.Any(char.IsWhiteSpace)) return DefaultReturnUrl;
        return Uri.IsWellFormedUriString(value, UriKind.Relative) ? value : DefaultReturnUrl;
    }

    private static string Failure(GoogleAuthOptions options, string reason) => $"{options.PublicBaseUrl}/me?signin={reason}";

    private static FlowState? ReadFlow(HttpContext ctx, IDataProtectionProvider protection, TimeProvider clock)
    {
        if (!ctx.Request.Cookies.TryGetValue(OidcCookieName, out var raw) || string.IsNullOrEmpty(raw)) return null;
        try
        {
            var flow = JsonSerializer.Deserialize<FlowState>(protection.CreateProtector(ProtectorPurpose).Unprotect(raw));
            return flow is not null && flow.ExpiresAt > clock.GetUtcNow().ToUnixTimeSeconds() ? flow : null;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            return null;
        }
    }

    private static CookieOptions OidcCookie(TimeSpan? maxAge) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax, // the return from Google is a cross-site navigation; the session cookie itself stays Strict
        Path = "/",
        IsEssential = true,
        MaxAge = maxAge,
    };

    private static string RandomToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static bool FixedEquals(string a, string b)
        => CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(a), System.Text.Encoding.UTF8.GetBytes(b));

    private static void NoStore(HttpContext ctx)
    {
        ctx.Response.Headers.CacheControl = "no-store";
        ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    }
}
