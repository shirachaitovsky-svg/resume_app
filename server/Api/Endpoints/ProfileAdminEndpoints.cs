using System.Security.Claims;
using ResumeApp.Api.Resources;
using ResumeApp.Domain.Profiles;
using ResumeApp.Api.Security;
using ResumeApp.Features.Profiles;

namespace ResumeApp.Api.Endpoints;

/// <summary>Admin profile list and removal (story 2.5). Every route requires the admin policy (AD-5).</summary>
public static class ProfileAdminEndpoints
{
    public static IEndpointRouteBuilder MapProfileAdmin(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin/profiles").RequireAuthorization(IdentityServiceExtensions.AdminPolicy);

        admin.MapGet("/", async (string? visibility, string? cohort, int? limit, int? offset, HttpContext ctx, ProfileAdminService service, CancellationToken ct) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            var errors = new Dictionary<string, string[]>();
            var filter = VisibilityFilter.Active;
            if (visibility is not null)
            {
                if (!VisibilityNames.TryParseFilter(visibility, out filter)) errors["visibility"] = [Strings.FieldInvalid];
            }
            Guid? cohortId = null;
            if (cohort is not null)
            {
                if (Guid.TryParseExact(cohort, "D", out var cid)) cohortId = cid;
                else errors["cohort"] = [Strings.CohortFilterInvalid];
            }
            if (limit is < 1) errors["limit"] = [Strings.FieldInvalid];
            if (offset is < 0) errors["offset"] = [Strings.FieldInvalid];
            if (errors.Count > 0) return Results.ValidationProblem(errors, title: Strings.ValidationTitle);

            var page = await service.ListAsync(filter, cohortId, limit ?? ProfileAdminService.DefaultLimit, offset ?? 0, ct);
            var items = page.Items.Select(r => new { r.ProfileId, r.Name, r.ProductName, r.CohortName, r.Visibility, r.HasApproved, r.CreatedAt });
            return Results.Ok(new { items, total = page.Total });
        });

        // Anti-forgery and Origin checks apply to every POST. Removal hides the profile everywhere public; nothing is deleted.
        admin.MapPost("/{profileId}/remove", async (string profileId, ClaimsPrincipal user, HttpContext ctx, ProfileAdminService service, CancellationToken ct) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            if (!Guid.TryParseExact(profileId, "D", out var id)) return NotFound();
            if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: Strings.Forbidden);
            var outcome = await service.RemoveAsync(id, actor, ct);
            return outcome.Status switch
            {
                RemoveStatus.Removed => Results.Ok(new { profileId = id, visibility = VisibilityNames.Wire(ProfileVisibility.Removed) }),
                RemoveStatus.NotActive => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: Strings.ProfileNotActive,
                    extensions: new Dictionary<string, object?> { ["code"] = "profile-not-active" }),
                _ => NotFound(),
            };
        });

        return app;
    }

    private static IResult NotFound() => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: Strings.NotFound);
}
