using ResumeApp.Api.Security;
using ResumeApp.Features.Email;

namespace ResumeApp.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealth(this IEndpointRouteBuilder app)
    {
        app.MapGet("/healthz", () => Results.Text("ok")).AllowAnonymous();

        // Story 4.2: the outbox state for a signed-in admin. Counts only, never a row, address or text.
        app.MapGet("/healthz/details", async (HttpContext ctx, IOutboxMonitor monitor, TimeProvider clock, CancellationToken ct) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            var health = await monitor.GetHealthAsync(clock.GetUtcNow().UtcDateTime, ct);
            var degraded = health.Dead > 0 || health.StuckPending > 0;
            return Results.Ok(new
            {
                status = degraded ? "degraded" : "ok",
                outbox = new { dead = health.Dead, stuckPending = health.StuckPending, oldestAgeSeconds = health.OldestAgeSeconds },
            });
        }).RequireAuthorization(IdentityServiceExtensions.AdminPolicy);
        return app;
    }
}
