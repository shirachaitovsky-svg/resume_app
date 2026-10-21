using System.Text.Json;
using ResumeApp.Api.Resources;
using ResumeApp.Features.Cohorts;
using ResumeApp.Features.Profiles;

namespace ResumeApp.Api.Endpoints;

public static class ProfileEndpoints
{
    public const int MaxBodyBytes = 16 * 1024;

    public sealed record SubmitProfileRequest(string? Name, string? ProductName, string? ProductUrl, string? Email, string? Phone);

    /// <summary>The only shape the submission endpoint returns: no profile id, no private data.</summary>
    public sealed record SubmissionReceipt(string Status);

    public static IEndpointRouteBuilder MapProfiles(this IEndpointRouteBuilder app)
    {
        // The body is read manually, after the token check, so a bad token always gets the uniform 404.
        app.MapPost("/invites/{token}/profiles", async (string token, HttpContext ctx, CohortService cohorts, ProfileService service, CancellationToken ct) =>
            {
                ctx.Response.Headers.CacheControl = "no-store";
                ctx.Response.Headers["Referrer-Policy"] = "no-referrer";

                if (await cohorts.ResolveActiveAsync(token, ct) is null)
                    return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: Strings.InviteNotFound);

                if (ctx.Request.ContentLength > MaxBodyBytes)
                    return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: Strings.BodyTooLarge);
                if (!ctx.Request.HasJsonContentType())
                    return Results.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType, title: Strings.UnsupportedMediaType);

                var bytes = await ReadLimitedAsync(ctx.Request.Body, ct);
                if (bytes is null)
                    return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: Strings.BodyTooLarge);

                SubmitProfileRequest? body;
                try
                {
                    body = JsonSerializer.Deserialize<SubmitProfileRequest>(bytes, JsonSerializerOptions.Web);
                }
                catch (JsonException)
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: Strings.BodyMalformed);
                }

                var result = await service.SubmitAsync(token,
                    new SubmitProfileCommand(body?.Name, body?.ProductName, body?.ProductUrl, body?.Email, body?.Phone), ct);

                return result.Outcome switch
                {
                    SubmitOutcome.Accepted => Results.Json(new SubmissionReceipt("pending"), statusCode: StatusCodes.Status202Accepted),
                    SubmitOutcome.Invalid => Results.ValidationProblem(
                        result.Errors!.ToDictionary(e => e.Key, e => new[] { Message(e.Key, e.Value) }),
                        title: Strings.ValidationTitle),
                    _ => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: Strings.InviteNotFound),
                };
            })
            .AllowAnonymous();

        return app;
    }

    /// <summary>Reads at most <see cref="MaxBodyBytes"/>; returns null when the body is larger.</summary>
    private static async Task<byte[]?> ReadLimitedAsync(Stream body, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[4096];
        int read;
        while ((read = await body.ReadAsync(buffer, ct)) > 0)
        {
            if (ms.Length + read > MaxBodyBytes) return null;
            ms.Write(buffer, 0, read);
        }
        return ms.ToArray();
    }

    private static string Message(string field, FieldError error) => (field, error) switch
    {
        ("name" or "productName" or "productUrl" or "email" or "phone", FieldError.Required) => Strings.FieldRequired,
        ("name", FieldError.TooLong) => Strings.NameTooLong,
        ("name", FieldError.Invalid) => Strings.FieldInvalid,
        ("productName", FieldError.TooLong) => Strings.ProductNameTooLong,
        ("productName", FieldError.Invalid) => Strings.FieldInvalid,
        ("productUrl", FieldError.TooLong) => Strings.ProductUrlTooLong,
        ("productUrl", FieldError.Invalid) => Strings.ProductUrlInvalid,
        ("email", FieldError.TooLong) => Strings.EmailTooLong,
        ("email", FieldError.Invalid) => Strings.EmailInvalid,
        ("phone", FieldError.TooLong) => Strings.PhoneTooLong,
        ("phone", FieldError.Invalid) => Strings.PhoneInvalid,
        _ => Strings.FieldInvalid,
    };
}
