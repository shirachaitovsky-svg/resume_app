using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using ResumeApp.Api.Resources;
using ResumeApp.Api.Security;
using ResumeApp.Features.Profiles;
using ResumeApp.Features.PublicForms;

namespace ResumeApp.Api.Endpoints;

public static class ProfileEndpoints
{
    public const int MaxBodyBytes = 160 * 1024;
    public const int MaxMultipartBytes = 512 * 1024;

    public sealed record SubmitProfileRequest(
        string? Name, string? ProductName, string? ProductUrl, string? Email, string? Phone,
        string? Description = null, string? Role = null, string? Workplace = null, string? IntroText = null,
        string? IntroAnswer1 = null, string? IntroAnswer2 = null, string? IntroAnswer3 = null, string? AvatarId = null, JsonElement? Cv = null,
        bool RemovePhoto = false, string? CohortId = null);

    /// <summary>Story 6.5: the receipt of a profile created by a signed-in Google account.</summary>
    public sealed record CreatedProfileReceipt(Guid ProfileId, string Status);

    public static IEndpointRouteBuilder MapProfiles(this IEndpointRouteBuilder app)
    {
        // Story 6.5: a signed-in Google account creates its profile (the only way to create one since story 6.4). Not in the /me group (that policy needs profile_id,
        // which is exactly what is missing here), so the session is checked by hand. Anti-forgery and Origin come from RequestIntegrityMiddleware.
        app.MapPost("/me/profile", async (HttpContext ctx, ProfileService service, PublicFormGuard guard, TimeProvider clock, CancellationToken ct) =>
            {
                ctx.Response.Headers.CacheControl = "no-store";
                ctx.Response.Headers["Referrer-Policy"] = "no-referrer";

                // Authenticate first: calls without a usable session must not consume the IP budget.
                if (await GoogleAuthEndpoints.GoogleSessionAsync(ctx) is not { HasProfileClaim: false } session)
                    return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: Strings.SessionInvalid);

                if (await guard.CheckIpAsync(ctx, RateLimitScopes.ProfileSubmitIp, guard.Options.ProfileSubmitPerIp, ct) is { } blocked)
                    return blocked;

                var (body, photo, readError) = await ReadProfileBodyAsync(ctx.Request, ct);
                if (readError is not null) return readError;
                var (cv, cvError) = CvOf(body);
                if (cvError is not null) return cvError;

                var result = await service.CreateForGoogleAsync(session.Sub, session.Email, body?.CohortId, ToCommand(body, photo, cv), ct);
                switch (result.Outcome)
                {
                    case CreateOutcome.Created:
                        await GoogleAuthEndpoints.SignInParticipantAsync(ctx, session.Sub, session.Email, result.ProfileId, clock.GetUtcNow());
                        return Results.Json(new CreatedProfileReceipt(result.ProfileId!.Value, "pending"), statusCode: StatusCodes.Status201Created);
                    case CreateOutcome.Busy:
                        return Busy(ctx);
                    case CreateOutcome.Invalid:
                        return Invalid(result.Errors!);
                    case CreateOutcome.ProfileExists:
                        return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: Strings.ProfileAlreadyExists,
                            extensions: new Dictionary<string, object?> { ["code"] = "profile-exists" });
                    default:
                        return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: Strings.EmailAlreadyTaken,
                            extensions: new Dictionary<string, object?> { ["code"] = "email-taken" });
                }
            })
            .AllowAnonymous();

        // Public photo: only the approved revision of an Active profile; every other case is the same 404.
        app.MapGet("/profiles/{profileId}/photo", async (string profileId, HttpContext ctx, PublicProfileQuery query, PublicFormGuard guard, CancellationToken ct) =>
            {
                ctx.Response.Headers.XContentTypeOptions = "nosniff";
                if (await guard.CheckIpAsync(ctx, RateLimitScopes.PhotoIp, guard.Options.PhotoPerIp, ct) is { } blocked)
                {
                    ctx.Response.Headers.CacheControl = "no-store";
                    return blocked;
                }

                var photo = Guid.TryParseExact(profileId, "D", out var id) ? await query.GetPhotoAsync(id, ct) : null;
                if (photo is null)
                {
                    ctx.Response.Headers.CacheControl = "no-store";
                    return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: Strings.NotFound);
                }

                // Moderate public caching: a newly approved revision may replace the photo under the same id.
                ctx.Response.Headers.CacheControl = "public, max-age=300";
                return Results.File(photo.Bytes, photo.ContentType);
            })
            .AllowAnonymous();

        return app;
    }

    /// <summary>
    /// Reads and parses a profile body (JSON or multipart) with the submission limits. The error, when set, is the response to return.
    /// </summary>
    internal static async Task<(SubmitProfileRequest? Body, byte[]? Photo, IResult? Error)> ReadProfileBodyAsync(HttpRequest request, CancellationToken ct)
    {
        var isMultipart = IsMultipart(request, out var boundary);
        var limit = isMultipart ? MaxMultipartBytes : MaxBodyBytes;
        if (request.ContentLength > limit)
            return (null, null, Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: Strings.BodyTooLarge));
        if (!isMultipart && !request.HasJsonContentType())
            return (null, null, Results.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType, title: Strings.UnsupportedMediaType));

        var bytes = await ReadLimitedAsync(request.Body, limit, ct);
        if (bytes is null)
            return (null, null, Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: Strings.BodyTooLarge));

        try
        {
            if (isMultipart)
            {
                var (parsed, photo) = await ParseMultipartAsync(bytes, boundary!, ct);
                return (parsed, photo, null);
            }
            return (JsonSerializer.Deserialize<SubmitProfileRequest>(bytes, JsonSerializerOptions.Web), null, null);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or ArgumentException or InvalidOperationException)
        {
            return (null, null, Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: Strings.BodyMalformed));
        }
    }

    /// <summary>cv must be a string (or absent/null); any other JSON type is a validation error on cv.</summary>
    internal static (string? Cv, IResult? Error) CvOf(SubmitProfileRequest? body)
    {
        if (body?.Cv is { } cvElement)
        {
            if (cvElement.ValueKind == JsonValueKind.String) return (cvElement.GetString(), null);
            if (cvElement.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                return (null, Results.ValidationProblem(new Dictionary<string, string[]> { ["cv"] = [Strings.CvInvalid] }, title: Strings.ValidationTitle));
        }
        return (null, null);
    }

    internal static SubmitProfileCommand ToCommand(SubmitProfileRequest? body, byte[]? photo, string? cv)
        => new(body?.Name, body?.ProductName, body?.ProductUrl, body?.Email, body?.Phone,
            body?.Description, body?.Role, body?.Workplace, body?.IntroText,
            body?.IntroAnswer1, body?.IntroAnswer2, body?.IntroAnswer3, body?.AvatarId, photo, cv);

    internal static IResult Invalid(IReadOnlyDictionary<string, FieldError> errors)
        => Results.ValidationProblem(errors.ToDictionary(e => e.Key, e => new[] { Message(e.Key, e.Value) }), title: Strings.ValidationTitle);

    internal static IResult Busy(HttpContext ctx)
    {
        ctx.Response.Headers.RetryAfter = "5";
        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: Strings.RateLimitUnavailable,
            extensions: new Dictionary<string, object?> { ["code"] = "photo-processing-busy" });
    }

    internal static bool IsMultipart(HttpRequest request, out string? boundary)
    {
        boundary = null;
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var type)
            || !type.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase))
            return false;
        boundary = HeaderUtilities.RemoveQuotes(type.Boundary).Value;
        return !string.IsNullOrEmpty(boundary) && boundary.Length <= 128;
    }

    /// <summary>Text parts by field name (case-insensitive, unknown names ignored) and the raw bytes of <c>photo</c>.</summary>
    internal static async Task<(SubmitProfileRequest, byte[]?)> ParseMultipartAsync(byte[] bytes, string boundary, CancellationToken ct)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        byte[]? photo = null;
        var seenPhoto = false;
        var reader = new MultipartReader(boundary, new MemoryStream(bytes)) { HeadersCountLimit = 16, HeadersLengthLimit = 8 * 1024 };
        var sections = 0;
        while (await reader.ReadNextSectionAsync(ct) is { } section)
        {
            if (++sections > 30) throw new InvalidDataException("Too many parts.");
            if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)) continue;
            var name = HeaderUtilities.RemoveQuotes(disposition.Name).Value;
            if (string.IsNullOrEmpty(name)) continue;

            if (name.Equals("photo", StringComparison.OrdinalIgnoreCase))
            {
                using var ms = new MemoryStream();
                await section.Body.CopyToAsync(ms, ct);
                if (photo is not null || seenPhoto) throw new InvalidDataException("Duplicate photo part.");
                seenPhoto = true;
                if (ms.Length > 0) photo = ms.ToArray(); // an empty part means no file was chosen
            }
            else
            {
                using var text = new StreamReader(section.Body, System.Text.Encoding.UTF8, true, 1024, leaveOpen: true);
                if (!fields.TryAdd(name, await text.ReadToEndAsync(ct))) throw new InvalidDataException("Duplicate field.");
            }
        }

        string? F(string key) => fields.GetValueOrDefault(key);
        return (new SubmitProfileRequest(F("name"), F("productName"), F("productUrl"), F("email"), F("phone"),
            F("description"), F("role"), F("workplace"), F("introText"),
            F("introAnswer1"), F("introAnswer2"), F("introAnswer3"), F("avatarId"), F("cv") is { } cvText ? JsonSerializer.SerializeToElement(cvText) : null,
            F("removePhoto") is { } rp && (rp.Equals("true", StringComparison.OrdinalIgnoreCase) || rp == "1"), F("cohortId")), photo);
    }

    /// <summary>Reads at most <paramref name="max"/> bytes; returns null when the body is larger.</summary>
    internal static async Task<byte[]?> ReadLimitedAsync(Stream body, int max, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[4096];
        int read;
        while ((read = await body.ReadAsync(buffer, ct)) > 0)
        {
            if (ms.Length + read > max) return null;
            ms.Write(buffer, 0, read);
        }
        return ms.ToArray();
    }

    internal static string Message(string field, FieldError error) => (field, error) switch
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
        ("description", FieldError.TooLong) => Strings.DescriptionTooLong,
        ("role", FieldError.TooLong) => Strings.RoleTooLong,
        ("workplace", FieldError.TooLong) => Strings.WorkplaceTooLong,
        ("introText" or "introAnswer1" or "introAnswer2" or "introAnswer3", FieldError.TooLong) => Strings.IntroTooLong,
        ("cv", FieldError.TooLong) => Strings.CvTooLong,
        ("cv", _) => Strings.CvInvalid,
        ("avatarId", _) => Strings.AvatarInvalid,
        ("photo", FieldError.TooLong) => Strings.PhotoTooLarge,
        ("photo", _) => Strings.PhotoInvalid,
        _ => Strings.FieldInvalid,
    };
}
