using System.Security.Claims;
using ResumeApp.Api.Resources;
using ResumeApp.Api.Security;
using ResumeApp.Features.Email;

namespace ResumeApp.Api.Endpoints;

/// <summary>Admin view of the email outbox (AD-4, AD-5): counts per status and the dead rows. No address, text or token.</summary>
public static class EmailOutboxEndpoints
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;
    /// <summary>Story 4.2: the stuck list rejects a larger page (400); the audit log trims it to this.</summary>
    public const int StuckMaxLimit = 100;

    public sealed record OutboxCounts(int Pending, int Sent, int Dead);

    public sealed record DeadEmailItem(Guid Id, string TemplateKey, int Attempts, string? LastErrorCategory, DateTime CreatedAt);

    public static IEndpointRouteBuilder MapEmailOutbox(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin/email-outbox").RequireAuthorization(IdentityServiceExtensions.AdminPolicy);

        admin.MapGet("/", async (int? limit, int? offset, IEmailOutbox outbox, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (limit is < 1 or > MaxLimit) errors["limit"] = [Strings.FieldInvalid];
            if (offset is < 0) errors["offset"] = [Strings.FieldInvalid];
            if (errors.Count > 0) return Results.ValidationProblem(errors, title: Strings.ValidationTitle);

            var overview = await outbox.GetOverviewAsync(limit ?? DefaultLimit, offset ?? 0, ct);
            var items = overview.DeadItems.Select(d => new DeadEmailItem(d.Id, d.TemplateKey, d.Attempts, d.LastErrorCategory, d.CreatedAt)).ToList();
            return Results.Ok(new { counts = new OutboxCounts(overview.Pending, overview.Sent, overview.Dead), items, total = overview.Dead });
        });

        // Story 4.2: dead rows and pending rows that stayed due too long. Ids, state and counters only.
        admin.MapGet("/stuck", async (string? limit, string? offset, HttpContext ctx, IOutboxMonitor monitor, TimeProvider clock, CancellationToken ct) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            var errors = new Dictionary<string, string[]>();
            var lim = ParseInt(limit, errors, "limit", 1, StuckMaxLimit);
            var off = ParseInt(offset, errors, "offset", 0, int.MaxValue);
            if (errors.Count > 0) return Results.ValidationProblem(errors, title: Strings.ValidationTitle);

            var page = await monitor.ListStuckAsync(lim ?? DefaultLimit, off ?? 0, clock.GetUtcNow().UtcDateTime, ct);
            return Results.Ok(new { items = page.Items, total = page.Total });
        });

        // Anti-forgery and Origin checks apply to every POST.
        admin.MapPost("/{id}/retry", async (string id, ClaimsPrincipal user, HttpContext ctx, IOutboxMonitor monitor, TimeProvider clock, CancellationToken ct) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            if (!Guid.TryParseExact(id, "D", out var rowId)) return NotFound();
            if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: Strings.Forbidden);
            return await monitor.RetryAsync(rowId, actor, clock.GetUtcNow().UtcDateTime, ct) switch
            {
                RetryStatus.Retried => Results.Ok(new { id = rowId, status = "pending" }),
                RetryStatus.NotDead => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: Strings.EmailNotDead,
                    extensions: new Dictionary<string, object?> { ["code"] = "email-not-dead" }),
                _ => NotFound(),
            };
        });

        return app;
    }

    /// <summary>Absent is fine (null); a value that is not an integer inside the range is a validation error, never a 500.</summary>
    private static int? ParseInt(string? raw, Dictionary<string, string[]> errors, string name, int min, int max)
    {
        if (raw is null) return null;
        if (int.TryParse(raw, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value) && value >= min && value <= max) return value;
        errors[name] = [Strings.FieldInvalid];
        return null;
    }

    private static IResult NotFound() => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: Strings.NotFound);

    /// <summary>Story 4.2: the audit log is read-only (no write verb is mapped). Identifiers and the actor's user name only.</summary>
    public static IEndpointRouteBuilder MapAuditLog(this IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/audit-log", async (string? limit, string? offset, HttpContext ctx, IAuditLogReader reader, CancellationToken ct) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            var errors = new Dictionary<string, string[]>();
            var lim = ParseInt(limit, errors, "limit", 1, int.MaxValue);
            var off = ParseInt(offset, errors, "offset", 0, int.MaxValue);
            if (errors.Count > 0) return Results.ValidationProblem(errors, title: Strings.ValidationTitle);

            var page = await reader.ListAsync(Math.Min(lim ?? DefaultLimit, StuckMaxLimit), off ?? 0, ct);
            return Results.Ok(new { items = page.Items, total = page.Total });
        }).RequireAuthorization(IdentityServiceExtensions.AdminPolicy);
        return app;
    }
}
