using ResumeApp.Api.Resources;
using ResumeApp.Api.Security;
using ResumeApp.Features.Email;

namespace ResumeApp.Api.Endpoints;

/// <summary>Admin view of the email outbox (AD-4, AD-5): counts per status and the dead rows. No address, text or token.</summary>
public static class EmailOutboxEndpoints
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

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

        return app;
    }
}
