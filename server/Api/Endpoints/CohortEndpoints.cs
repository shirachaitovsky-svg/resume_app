using ResumeApp.Api.Resources;
using ResumeApp.Api.Security;
using ResumeApp.Domain.Cohorts;
using ResumeApp.Features.Cohorts;

namespace ResumeApp.Api.Endpoints;

public static class CohortEndpoints
{
    public sealed record CreateCohortRequest(string? Name);
    public sealed record AdminCohort(Guid Id, string Name, string InviteToken, string InviteUrl, DateTime CreatedAt, bool Revoked, DateTime? RevokedAt);
    public sealed record CreatedCohort(Guid Id, string Name, string InviteToken, string InviteUrl, DateTime CreatedAt);
    /// <summary>The only shape the anonymous invite endpoint may return.</summary>
    public sealed record PublicInvite(string CohortName);

    public static IEndpointRouteBuilder MapCohorts(this IEndpointRouteBuilder app)
    {
        _ = ResolveBaseUrl(app.ServiceProvider.GetRequiredService<IConfiguration>()); // fail fast on a bad PUBLIC_BASE_URL

        var admin = app.MapGroup("/admin/cohorts").RequireAuthorization(IdentityServiceExtensions.AdminPolicy);

        admin.MapPost("/", async (CreateCohortRequest body, CohortService service, IConfiguration config, CancellationToken ct) =>
        {
            var name = body.Name?.Trim();
            if (string.IsNullOrEmpty(name))
                return Validation(new() { ["name"] = [Strings.FieldRequired] });
            if (name.Length > Cohort.NameMaxLength)
                return Validation(new() { ["name"] = [Strings.CohortNameTooLong] });

            var c = await service.CreateAsync(name, ct);
            var url = InviteUrl(config, c.InviteToken);
            return Results.Created($"/admin/cohorts/{c.Id}", new CreatedCohort(c.Id, c.Name, c.InviteToken, url, c.CreatedAt));
        });

        admin.MapGet("/", async (CohortService service, IConfiguration config, CancellationToken ct) =>
        {
            var items = (await service.ListAsync(ct))
                .Select(c => new AdminCohort(c.Id, c.Name, c.InviteToken, InviteUrl(config, c.InviteToken), c.CreatedAt, c.RevokedAt is not null, c.RevokedAt))
                .ToList();
            return Results.Ok(new { items, total = items.Count });
        });

        admin.MapPost("/{id:guid}/revoke", async (Guid id, CohortService service, CancellationToken ct) =>
            await service.RevokeAsync(id, ct) switch
            {
                RevokeResult.Revoked => Results.NoContent(),
                RevokeResult.AlreadyRevoked => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: Strings.CohortAlreadyRevoked),
                _ => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: Strings.NotFound),
            });

        app.MapGet("/invites/{token}", async (string token, HttpContext ctx, CohortService service, CancellationToken ct) =>
            {
                ctx.Response.Headers.CacheControl = "no-store";
                ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
                return await service.ResolveInviteAsync(token, ct) is { } name
                    ? Results.Ok(new PublicInvite(name))
                    : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: Strings.InviteNotFound);
            })
            .AllowAnonymous();

        return app;
    }

    private static string ResolveBaseUrl(IConfiguration config)
    {
        var baseUrl = config["PUBLIC_BASE_URL"];
        if (string.IsNullOrWhiteSpace(baseUrl)) return "http://localhost:4200";
        baseUrl = baseUrl.Trim();
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("PUBLIC_BASE_URL must be an absolute http or https URL.");
        return baseUrl.TrimEnd('/');
    }

    private static string InviteUrl(IConfiguration config, string token)
        => $"{ResolveBaseUrl(config)}/join/{token}";

    private static IResult Validation(Dictionary<string, string[]> errors)
        => Results.ValidationProblem(errors, title: Strings.ValidationTitle);
}
