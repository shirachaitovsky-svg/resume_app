using System.Text.Json;
using ResumeApp.Api.Resources;
using ResumeApp.Api.Security;
using ResumeApp.Features.Email;
using ResumeApp.Features.Profiles;
using ResumeApp.Features.PublicForms;

namespace ResumeApp.Api.Endpoints;

/// <summary>Personal link by email (story 2.1, FR-11): request side and the admin view of the outbox.</summary>
public static class EditLinkEndpoints
{
    public const int MaxBodyBytes = 4 * 1024;

    public sealed record EditLinkRequest(string? Email);

    /// <summary>The only shape the request returns, whether or not the address is registered.</summary>
    public sealed record EditLinkReceipt(string Status);

    public static IEndpointRouteBuilder MapEditLink(this IEndpointRouteBuilder app)
    {
        app.MapPost("/profiles/edit-link", async (HttpContext ctx, EditLinkService service, PublicFormGuard guard, CancellationToken ct) =>
            {
                ctx.Response.Headers.CacheControl = "no-store";
                ctx.Response.Headers["Referrer-Policy"] = "no-referrer";

                // AD-12 order: IP limit, Turnstile, then (after the body) the per-email limit.
                if (await guard.CheckIpAndBotAsync(ctx, RateLimitScopes.EditLinkIp, guard.Options.EditLinkPerIp, ct) is { } blocked)
                    return blocked;

                if (ctx.Request.ContentLength > MaxBodyBytes)
                    return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: Strings.BodyTooLarge);
                if (!ctx.Request.HasJsonContentType())
                    return Results.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType, title: Strings.UnsupportedMediaType);

                var bytes = await ReadLimitedAsync(ctx.Request.Body, MaxBodyBytes, ct);
                if (bytes is null)
                    return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: Strings.BodyTooLarge);

                EditLinkRequest? body;
                try
                {
                    body = JsonSerializer.Deserialize<EditLinkRequest>(bytes, JsonSerializerOptions.Web);
                }
                catch (JsonException)
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: Strings.BodyMalformed);
                }

                var email = ProfileService.ValidateEmail(body?.Email, out var error);
                if (email is null)
                {
                    var message = error switch
                    {
                        FieldError.Required => Strings.FieldRequired,
                        FieldError.TooLong => Strings.EmailTooLong,
                        _ => Strings.EmailInvalid,
                    };
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = [message] }, title: Strings.ValidationTitle);
                }

                if (await guard.CheckEditLinkEmailAsync(ctx, email, ct) is { } emailBlocked)
                    return emailBlocked;

                // Light work only (one lookup, idempotent inserts). Nothing is sent here, and the answer never depends on the result.
                await service.RequestAsync(email, ct);
                return Results.Json(new EditLinkReceipt("ok"), statusCode: StatusCodes.Status202Accepted);
            })
            .AllowAnonymous();

        return app;
    }

    private static async Task<byte[]?> ReadLimitedAsync(Stream body, int max, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[1024];
        int read;
        while ((read = await body.ReadAsync(buffer, ct)) > 0)
        {
            if (ms.Length + read > max) return null;
            ms.Write(buffer, 0, read);
        }
        return ms.ToArray();
    }
}
