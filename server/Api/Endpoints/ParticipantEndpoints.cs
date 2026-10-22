using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using ResumeApp.Api.Resources;
using ResumeApp.Api.Security;
using ResumeApp.Features.Email;
using ResumeApp.Features.Profiles;
using ResumeApp.Features.PublicForms;

namespace ResumeApp.Api.Endpoints;

/// <summary>Participant identity and editing (story 2.2, FR-11, FR-12): personal link to session, and edits as new pending revisions.</summary>
public static class ParticipantEndpoints
{
    public const int MaxSessionBodyBytes = 1024;
    public const string CodeLinkInvalid = "link-invalid";
    public const string CodeSessionInvalid = "session-invalid";

    public sealed record SessionRequest(string? Token);

    public sealed record StatusReceipt(string Status);

    /// <summary>Everything the owner may see about their own profile. Private to the session's participant only.</summary>
    public sealed record OwnProfileResponse(
        string Name, string ProductName, string ProductUrl, string Email, string Phone,
        string? Description, string? Role, string? Workplace, string? IntroText,
        string? IntroAnswer1, string? IntroAnswer2, string? IntroAnswer3, string AvatarId, string? Cv,
        bool HasPhoto, string CohortName, bool HasPublished, DateTime? Pending, bool PendingContactChange,
        string CurrentEmail, string CurrentPhone, string? PendingEmail, string? PendingPhone, ReturnedInfo? Returned);

    /// <summary>The admin's note on a returned revision (story 2.4); present only while the returned revision is the latest.</summary>
    public sealed record ReturnedInfo(string Note, DateTime ReturnedAt);

    public static IEndpointRouteBuilder MapParticipant(this IEndpointRouteBuilder app)
    {
        app.MapPost("/profiles/session", async (HttpContext ctx, IPersonalTokenIssuer tokens, PublicFormGuard guard, TimeProvider clock, CancellationToken ct) =>
            {
                NoStore(ctx);

                // The token itself is the secret, so there is no Turnstile here; the per-IP limit still applies.
                if (await guard.CheckIpAsync(ctx, RateLimitScopes.EditSessionIp, guard.Options.EditSessionPerIp, ct) is { } blocked)
                    return blocked;

                // An oversized token is just another invalid token: the same 401, never a distinguishable 413.
                if (ctx.Request.ContentLength > MaxSessionBodyBytes) return LinkInvalid();
                if (!ctx.Request.HasJsonContentType())
                    return Results.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType, title: Strings.UnsupportedMediaType);

                var bytes = await ProfileEndpoints.ReadLimitedAsync(ctx.Request.Body, MaxSessionBodyBytes, ct);
                if (bytes is null) return LinkInvalid();

                string? token = null;
                try
                {
                    token = JsonSerializer.Deserialize<SessionRequest>(bytes, JsonSerializerOptions.Web)?.Token;
                }
                catch (JsonException)
                {
                    // an unreadable body carries no valid token: same answer as any invalid link
                }

                var now = clock.GetUtcNow();
                var profileId = await tokens.TryRedeemEditTokenAsync(token, now.UtcDateTime, ct);
                if (profileId is null) return LinkInvalid();

                var identity = new ClaimsIdentity(
                    [new Claim(IdentityServiceExtensions.ProfileIdClaim, profileId.Value.ToString("D")), new Claim(IdentityServiceExtensions.PurposeClaim, ResumeApp.Domain.Email.PersonalToken.EditProfilePurpose)],
                    IdentityServiceExtensions.ParticipantScheme);
                await ctx.SignInAsync(IdentityServiceExtensions.ParticipantScheme, new ClaimsPrincipal(identity), new AuthenticationProperties
                {
                    IsPersistent = true,
                    AllowRefresh = false,
                    IssuedUtc = now,
                    ExpiresUtc = now + IdentityServiceExtensions.ParticipantSessionLifetime,
                });
                return Results.Json(new StatusReceipt("ok"));
            })
            .AllowAnonymous();

        // Anti-forgery token for /profiles/session and /me/*: always bound to the anonymous principal, whatever cookies the browser holds.
        app.MapGet("/profiles/csrf", (HttpContext ctx, IAntiforgery antiforgery) =>
            {
                NoStore(ctx);
                ctx.User = RequestIntegrityMiddleware.Anonymous();
                var tokens = antiforgery.GetAndStoreTokens(ctx);
                return Results.Ok(new { token = tokens.RequestToken, headerName = IdentityServiceExtensions.CsrfHeaderName });
            })
            .AllowAnonymous();

        app.MapPost("/profiles/session/logout", async (HttpContext ctx) =>
            {
                NoStore(ctx);
                await ctx.SignOutAsync(IdentityServiceExtensions.ParticipantScheme);
                return Results.NoContent();
            })
            .AllowAnonymous();

        var me = app.MapGroup("/me").RequireAuthorization(IdentityServiceExtensions.ParticipantPolicy);

        me.MapGet("/profile", async (HttpContext ctx, ProfileService service, CancellationToken ct) =>
        {
            NoStore(ctx);
            if (ProfileIdOf(ctx.User) is not { } profileId) return await SessionInvalidAsync(ctx);
            var own = await service.GetOwnAsync(profileId, ct);
            if (own is null) return await SessionInvalidAsync(ctx);
            // email and phone are the values to prefill the form with: the requested ones while a change is pending, else the current ones.
            return Results.Json(new OwnProfileResponse(own.Name, own.ProductName, own.ProductUrl, own.PendingEmail ?? own.CurrentEmail, own.PendingPhone ?? own.CurrentPhone,
                own.Description, own.Role, own.Workplace, own.IntroText, own.IntroAnswer1, own.IntroAnswer2, own.IntroAnswer3,
                own.AvatarId, own.Cv, own.HasPhoto, own.CohortName, own.HasPublished, own.PendingSubmittedAt, own.PendingEmail != null || own.PendingPhone != null,
                own.CurrentEmail, own.CurrentPhone, own.PendingEmail, own.PendingPhone,
                own.ReturnedNote is { } note && own.ReturnedAt is { } at ? new ReturnedInfo(note, at) : null));
        });

        me.MapPut("/profile", async (HttpContext ctx, ProfileService service, CancellationToken ct) =>
        {
            NoStore(ctx);
            if (ProfileIdOf(ctx.User) is not { } profileId) return await SessionInvalidAsync(ctx);

            var (body, photo, readError) = await ProfileEndpoints.ReadProfileBodyAsync(ctx.Request, ct);
            if (readError is not null) return readError;
            var (cv, cvError) = ProfileEndpoints.CvOf(body);
            if (cvError is not null) return cvError;

            var result = await service.UpdateAsync(profileId, new UpdateProfileCommand(ProfileEndpoints.ToCommand(body, photo, cv), body?.RemovePhoto ?? false), ct);
            return result.Outcome switch
            {
                UpdateOutcome.Accepted => Results.Json(new StatusReceipt("pending"), statusCode: StatusCodes.Status202Accepted),
                UpdateOutcome.Busy => ProfileEndpoints.Busy(ctx),
                UpdateOutcome.Invalid => ProfileEndpoints.Invalid(result.Errors!),
                _ => await SessionInvalidAsync(ctx),
            };
        });

        return app;
    }

    private static void NoStore(HttpContext ctx)
    {
        ctx.Response.Headers.CacheControl = "no-store";
        ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    }

    private static Guid? ProfileIdOf(ClaimsPrincipal user)
        => Guid.TryParseExact(user.FindFirstValue(IdentityServiceExtensions.ProfileIdClaim), "D", out var id) ? id : null;

    private static IResult LinkInvalid() => Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: Strings.LinkInvalid,
        extensions: new Dictionary<string, object?> { ["code"] = CodeLinkInvalid });

    /// <summary>The session no longer maps to an active profile: drop the dead cookie and answer 401.</summary>
    private static async Task<IResult> SessionInvalidAsync(HttpContext ctx)
    {
        await ctx.SignOutAsync(IdentityServiceExtensions.ParticipantScheme);
        return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: Strings.SessionInvalid,
            extensions: new Dictionary<string, object?> { ["code"] = CodeSessionInvalid });
    }
}
