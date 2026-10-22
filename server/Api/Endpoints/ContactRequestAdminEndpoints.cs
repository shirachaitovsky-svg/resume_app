using System.Security.Claims;
using ResumeApp.Api.Resources;
using ResumeApp.Domain.Contacts;
using ResumeApp.Api.Security;
using ResumeApp.Features.Contacts;

namespace ResumeApp.Api.Endpoints;

/// <summary>Admin queue of verified contact requests (story 3.2). Read only; every route requires the admin policy (AD-5).</summary>
public static class ContactRequestAdminEndpoints
{
    public static IEndpointRouteBuilder MapContactRequestAdmin(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin/contact-requests").RequireAuthorization(IdentityServiceExtensions.AdminPolicy);

        admin.MapGet("/", async (HttpContext ctx, ContactRequestQueueService service, CancellationToken ct) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            // Raw query values, parsed here, so every malformed input (non-integer, repeated key, odd status) yields the same 400 shape.
            var query = ctx.Request.Query;
            var errors = new Dictionary<string, string[]>();
            IReadOnlyCollection<ResumeApp.Domain.Contacts.ContactRequestStatus> statuses = [];
            if (query["status"].Count > 1 || !ContactRequestQueueService.TryParseStatusFilter(query.ContainsKey("status") ? query["status"].ToString() : null, out statuses))
                errors["status"] = [Strings.FieldInvalid];
            var limit = ParseInt(query, "limit", 1, errors);
            var offset = ParseInt(query, "offset", 0, errors);
            if (errors.Count > 0) return Results.ValidationProblem(errors, title: Strings.ValidationTitle);

            var page = await service.ListAsync(statuses, limit, offset, ct);
            return Results.Ok(new { items = page.Items, total = page.Total });
        });

        admin.MapGet("/{requestId}", async (string requestId, HttpContext ctx, ContactRequestQueueService service, CancellationToken ct) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            var item = await service.GetAsync(requestId, ct);
            return item is null ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: Strings.NotFound) : Results.Ok(item);
        });

        // Anti-forgery and Origin checks apply to every POST. The worker issues the personal link and sends the mail (AD-4).
        admin.MapPost("/{requestId}/send-to-participant", async (string requestId, ClaimsPrincipal user, HttpContext ctx, ContactRequestService service, CancellationToken ct) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor))
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: Strings.Forbidden);
            var status = await service.SendToParticipantAsync(requestId, actor, ct);
            return status switch
            {
                SendToParticipantStatus.Sent => Results.Ok(new { requestId = Guid.ParseExact(requestId, "D"), status = ContactRequestStatusNames.SentToParticipant }),
                SendToParticipantStatus.NotSubmitted => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: Strings.RequestNotSubmitted,
                    extensions: new Dictionary<string, object?> { ["code"] = "request-not-submitted" }),
                _ => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: Strings.NotFound),
            };
        });

        admin.MapPost("/{requestId}/approve-delivery", async (string requestId, ClaimsPrincipal user, HttpContext ctx, ContactRequestService service, CancellationToken ct) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor))
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: Strings.Forbidden);
            var status = await service.ApproveDeliveryAsync(requestId, actor, ct);
            return status switch
            {
                ApproveDeliveryStatus.Approved => Results.Ok(new { requestId = Guid.ParseExact(requestId, "D"), status = ContactRequestStatusNames.ParticipantApproved, delivery = "pending" }),
                ApproveDeliveryStatus.NotApproved => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: Strings.RequestNotApproved,
                    extensions: new Dictionary<string, object?> { ["code"] = "request-not-approved" }),
                _ => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: Strings.NotFound),
            };
        });

        return app;
    }

    /// <summary>An optional non-negative integer query value (at least <paramref name="min"/>); absent gives null; anything else records an error.</summary>
    private static int? ParseInt(IQueryCollection query, string name, int min, Dictionary<string, string[]> errors)
    {
        if (!query.TryGetValue(name, out var values)) return null;
        if (values.Count == 1 && int.TryParse(values[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) && n >= min) return n;
        // NumberStyles.None rejects signs, so a negative or decorated value lands here too; a value too large for an int is invalid as well.
        errors[name] = [Strings.FieldInvalid];
        return null;
    }
}
