namespace ResumeApp.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealth(this IEndpointRouteBuilder app)
    {
        app.MapGet("/healthz", () => Results.Text("ok")).AllowAnonymous();
        return app;
    }
}
