using System.Text.Json;
using ResumeApp.Api.Resources;
using ResumeApp.Api.Security;
using ResumeApp.Features.Contacts;
using ResumeApp.Features.Email;
using ResumeApp.Features.Profiles;
using ResumeApp.Features.PublicForms;

namespace ResumeApp.Api.Endpoints;

/// <summary>Visitor contact requests (story 3.1, FR-13): submission and email verification. Nothing here exposes participant details.</summary>
public static class ContactRequestEndpoints
{
    public const int MaxBodyBytes = 8 * 1024;
    public const int MaxVerifyBodyBytes = 1024;
    public const string CodeLinkInvalid = "link-invalid";

    public sealed record ContactRequestBody(string? Name, string? Email, string? Reason);

    public sealed record VerifyBody(string? Token);

    /// <summary>The only shapes these endpoints return on success.</summary>
    public sealed record StatusReceipt(string Status);

    public static IEndpointRouteBuilder MapContactRequests(this IEndpointRouteBuilder app)
    {
        app.MapPost("/profiles/{profileId}/contact-requests", async (string profileId, HttpContext ctx, ContactRequestService service, PublicFormGuard guard, CancellationToken ct) =>
            {
                NoStore(ctx);

                // AD-12 order: IP limit, Turnstile, body, validation (before any look at the profile), address limit, profile limit.
                if (await guard.CheckIpAndBotAsync(ctx, RateLimitScopes.ContactRequestIp, guard.Options.ContactRequestPerIp, ct) is { } blocked)
                    return blocked;

                if (ctx.Request.ContentLength > MaxBodyBytes)
                    return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: Strings.BodyTooLarge);
                if (!ctx.Request.HasJsonContentType())
                    return Results.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType, title: Strings.UnsupportedMediaType);

                var bytes = await ProfileEndpoints.ReadLimitedAsync(ctx.Request.Body, MaxBodyBytes, ct);
                if (bytes is null)
                    return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: Strings.BodyTooLarge);

                ContactRequestBody? body;
                try
                {
                    body = JsonSerializer.Deserialize<ContactRequestBody>(bytes, JsonSerializerOptions.Web);
                }
                catch (JsonException)
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: Strings.BodyMalformed);
                }

                var valid = ContactRequestService.Validate(body?.Name, body?.Email, body?.Reason, out var errors);
                if (valid is null)
                    return Results.ValidationProblem(errors.ToDictionary(e => e.Key, e => new[] { Message(e.Key, e.Value) }), title: Strings.ValidationTitle);

                if (await guard.CheckContactRequestEmailAsync(ctx, valid.Email, ct) is { } emailBlocked)
                    return emailBlocked;
                if (await guard.CheckContactRequestProfileAsync(ctx, profileId, ct) is { } profileBlocked)
                    return profileBlocked;

                // Active, inactive, unknown and malformed profiles, and a duplicate open request, all answer identically.
                await service.SubmitAsync(profileId, valid, ct);
                return Results.Json(new StatusReceipt("verification-sent"), statusCode: StatusCodes.Status202Accepted);
            })
            .AllowAnonymous();

        // The link in the email is a GET to a client page; consuming the token is this POST only (mail scanners fetch links).
        app.MapPost("/contact-requests/verify", async (HttpContext ctx, ContactRequestService service, PublicFormGuard guard, CancellationToken ct) =>
            {
                NoStore(ctx);

                // The token is the secret, so no Turnstile; the per-IP limit still applies.
                if (await guard.CheckIpAsync(ctx, RateLimitScopes.ContactVerifyIp, guard.Options.ContactVerifyPerIp, ct) is { } blocked)
                    return blocked;

                // An oversized or unreadable body is just another invalid token: the same 401.
                if (ctx.Request.ContentLength > MaxVerifyBodyBytes) return LinkInvalid();
                if (!ctx.Request.HasJsonContentType())
                    return Results.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType, title: Strings.UnsupportedMediaType);

                var bytes = await ProfileEndpoints.ReadLimitedAsync(ctx.Request.Body, MaxVerifyBodyBytes, ct);
                if (bytes is null) return LinkInvalid();

                string? token = null;
                try
                {
                    token = JsonSerializer.Deserialize<VerifyBody>(bytes, JsonSerializerOptions.Web)?.Token;
                }
                catch (JsonException)
                {
                    // an unreadable body carries no valid token
                }

                return await service.VerifyAsync(token, ct)
                    ? Results.Json(new StatusReceipt("submitted"))
                    : LinkInvalid();
            })
            .AllowAnonymous();

        // Story 3.3: the participant's link. Preview consumes nothing (mail scanners), the decision consumes the token. Token is the secret: no Turnstile.
        app.MapPost("/contact-decisions/preview", async (HttpContext ctx, ContactRequestService service, PublicFormGuard guard, CancellationToken ct) =>
            {
                NoStore(ctx);
                if (await guard.CheckIpAsync(ctx, RateLimitScopes.ContactDecisionIp, guard.Options.ContactDecisionPerIp, ct) is { } blocked) return blocked;
                if (!ctx.Request.HasJsonContentType())
                    return Results.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType, title: Strings.UnsupportedMediaType);
                var body = await ReadDecisionBodyAsync(ctx, ct);
                var result = await service.PreviewDecisionAsync(body?.Token, ct);
                return result.Status == DecisionLinkStatus.Ok
                    ? Results.Json(new { visitorName = result.Preview!.VisitorName, reason = result.Preview.Reason })
                    : DecisionFailure(result.Status);
            })
            .AllowAnonymous();

        app.MapPost("/contact-decisions/decide", async (HttpContext ctx, ContactRequestService service, PublicFormGuard guard, CancellationToken ct) =>
            {
                NoStore(ctx);
                if (await guard.CheckIpAsync(ctx, RateLimitScopes.ContactDecisionIp, guard.Options.ContactDecisionPerIp, ct) is { } blocked) return blocked;
                if (!ctx.Request.HasJsonContentType())
                    return Results.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType, title: Strings.UnsupportedMediaType);
                var body = await ReadDecisionBodyAsync(ctx, ct);
                if (!PersonalTokens.HasValidShape(body?.Token)) return LinkInvalid();
                if (!ContactRequestService.IsDecisionValue(body?.Decision))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["decision"] = [Strings.FieldInvalid] }, title: Strings.ValidationTitle);
                var result = await service.DecideAsync(body!.Token, body.Decision!, ct);
                return result.Status == DecisionLinkStatus.Ok ? Results.Json(new { decision = body.Decision }) : DecisionFailure(result.Status);
            })
            .AllowAnonymous();

        return app;
    }

    public sealed record DecisionBody(string? Token, string? Decision);

    /// <summary>An oversized or unreadable body carries no valid token (null): the caller answers with the same 401.</summary>
    private static async Task<DecisionBody?> ReadDecisionBodyAsync(HttpContext ctx, CancellationToken ct)
    {
        if (ctx.Request.ContentLength > MaxVerifyBodyBytes) return null;
        var bytes = await ProfileEndpoints.ReadLimitedAsync(ctx.Request.Body, MaxVerifyBodyBytes, ct);
        if (bytes is null) return null;
        try
        {
            return JsonSerializer.Deserialize<DecisionBody>(bytes, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IResult DecisionFailure(DecisionLinkStatus status) => status switch
    {
        DecisionLinkStatus.Expired => Results.Problem(statusCode: StatusCodes.Status410Gone, title: Strings.LinkExpired,
            extensions: new Dictionary<string, object?> { ["code"] = "link-expired" }),
        DecisionLinkStatus.AlreadyDecided => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: Strings.AlreadyDecided,
            extensions: new Dictionary<string, object?> { ["code"] = "already-decided" }),
        _ => LinkInvalid(),
    };

    private static void NoStore(HttpContext ctx)
    {
        ctx.Response.Headers.CacheControl = "no-store";
        ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    }

    private static IResult LinkInvalid() => Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: Strings.LinkInvalid,
        extensions: new Dictionary<string, object?> { ["code"] = CodeLinkInvalid });

    private static string Message(string field, FieldError error) => (field, error) switch
    {
        (_, FieldError.Required) => Strings.FieldRequired,
        ("name", FieldError.TooLong) => Strings.NameTooLong,
        ("name", _) => Strings.FieldInvalid,
        ("email", FieldError.TooLong) => Strings.EmailTooLong,
        ("email", _) => Strings.EmailInvalid,
        ("reason", FieldError.TooLong) => Strings.ReasonTooLong,
        _ => Strings.ReasonInvalid,
    };
}
