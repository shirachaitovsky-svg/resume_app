using System.Globalization;
using ResumeApp.Api.Resources;
using ResumeApp.Api.Security;
using ResumeApp.Features.Profiles;
using ResumeApp.Features.PublicForms;

namespace ResumeApp.Api.Endpoints;

/// <summary>Public gallery (story 1.12). Everything goes through <see cref="PublicProfileQuery"/> and the Public* DTOs below.</summary>
public static class GalleryEndpoints
{
    public sealed record PublicCohortRef(Guid Id, string Name);

    public sealed record PublicProfileCard(
        Guid Id, string Name, string ProductName, string ProductUrl, string Description, string AvatarId, string? PhotoUrl, PublicCohortRef Cohort);

    public sealed record PublicProfilePage(IReadOnlyList<PublicProfileCard> Items, int Total);

    public sealed record PublicCohortList(IReadOnlyList<PublicCohortRef> Items, int Total);

    public static IEndpointRouteBuilder MapGallery(this IEndpointRouteBuilder app)
    {
        app.MapGet("/profiles", async (HttpContext ctx, PublicProfileQuery query, PublicFormGuard guard, CancellationToken ct) =>
            {
                ctx.Response.Headers.XContentTypeOptions = "nosniff";
                if (await guard.CheckIpAsync(ctx, RateLimitScopes.GalleryIp, guard.Options.GalleryPerIp, ct) is { } blocked)
                    return NoStore(ctx, blocked);

                var q = ctx.Request.Query;
                Guid? cohort = null;
                if (q.TryGetValue("cohort", out var cohortRaw))
                {
                    if (cohortRaw.Count != 1 || !Guid.TryParseExact(cohortRaw.ToString(), "D", out var parsed))
                        return NoStore(ctx, Validation("cohort", Strings.CohortFilterInvalid));
                    cohort = parsed;
                }

                var limit = PublicProfileQuery.DefaultLimit;
                if (q.TryGetValue("limit", out var limitRaw))
                {
                    if (limitRaw.Count != 1 || !int.TryParse(limitRaw.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit < 1)
                        return NoStore(ctx, Validation("limit", Strings.PagingInvalid));
                    limit = Math.Min(limit, PublicProfileQuery.MaxLimit);
                }

                var offset = 0;
                if (q.TryGetValue("offset", out var offsetRaw)
                    && (offsetRaw.Count != 1 || !int.TryParse(offsetRaw.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out offset) || offset < 0))
                    return NoStore(ctx, Validation("offset", Strings.PagingInvalid));

                var page = await query.GetGalleryAsync(cohort, limit, offset, ct);
                ctx.Response.Headers.CacheControl = "public, max-age=30";
                return Results.Ok(new PublicProfilePage(
                    page.Items.Select(r => new PublicProfileCard(r.ProfileId, r.Name, r.ProductName, r.ProductUrl, r.Description ?? string.Empty,
                        r.AvatarId, r.HasPhoto ? $"/profiles/{r.ProfileId}/photo" : null, new PublicCohortRef(r.CohortId, r.CohortName))).ToList(),
                    page.Total));
            })
            .AllowAnonymous();

        app.MapGet("/cohorts", async (HttpContext ctx, PublicProfileQuery query, PublicFormGuard guard, CancellationToken ct) =>
            {
                ctx.Response.Headers.XContentTypeOptions = "nosniff";
                if (await guard.CheckIpAsync(ctx, RateLimitScopes.GalleryIp, guard.Options.GalleryPerIp, ct) is { } blocked)
                    return NoStore(ctx, blocked);

                var items = (await query.GetCohortsAsync(ct)).Select(c => new PublicCohortRef(c.Id, c.Name)).ToList();
                ctx.Response.Headers.CacheControl = "public, max-age=30";
                return Results.Ok(new PublicCohortList(items, items.Count));
            })
            .AllowAnonymous();

        return app;
    }

    private static IResult NoStore(HttpContext ctx, IResult result)
    {
        ctx.Response.Headers.CacheControl = "no-store";
        return result;
    }

    private static IResult Validation(string field, string message)
        => Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] }, title: Strings.ValidationTitle);
}
