using ResumeApp.Api.Security;

namespace ResumeApp.Api.Endpoints;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdmin(this IEndpointRouteBuilder app)
    {
        // Protected probe: 401 anonymous, 403 without the Admin role.
        app.MapGet("/admin/ping", () => Results.Ok(new { ok = true }))
            .RequireAuthorization(IdentityServiceExtensions.AdminPolicy);
        return app;
    }
}
