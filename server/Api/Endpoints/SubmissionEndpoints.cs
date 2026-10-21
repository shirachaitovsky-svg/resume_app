using ResumeApp.Api.Resources;
using ResumeApp.Api.Security;
using ResumeApp.Features.Profiles;

namespace ResumeApp.Api.Endpoints;

/// <summary>Admin review queue (story 1.10). Read-only; every route requires the admin policy (AD-5).</summary>
public static class SubmissionEndpoints
{
    public sealed record AdminSubmissionItem(Guid RevisionId, string Name, string ProductName, string CohortName, string Kind, DateTime SubmittedAt);

    public sealed record AdminSubmissionDetail(
        Guid RevisionId, string Kind, string Name, string ProductName, string ProductUrl, string CohortName, DateTime SubmittedAt,
        string Email, string Phone, string? Description, string? Role, string? Workplace, string? IntroText,
        string? IntroAnswer1, string? IntroAnswer2, string? IntroAnswer3, string AvatarId, bool HasPhoto, string? Cv, bool CvInvalid);

    public static IEndpointRouteBuilder MapSubmissions(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin/submissions").RequireAuthorization(IdentityServiceExtensions.AdminPolicy);

        admin.MapGet("/", async (int? limit, int? offset, SubmissionReviewService service, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (limit is < 1) errors["limit"] = [Strings.FieldInvalid];
            if (offset is < 0) errors["offset"] = [Strings.FieldInvalid];
            if (errors.Count > 0) return Results.ValidationProblem(errors, title: Strings.ValidationTitle);

            var page = await service.ListAsync(limit ?? SubmissionReviewService.DefaultLimit, offset ?? 0, ct);
            var items = page.Items.Select(r => new AdminSubmissionItem(r.RevisionId, r.Name, r.ProductName, r.CohortName, Kind(r.HasApproved), r.SubmittedAt)).ToList();
            return Results.Ok(new { items, total = page.Total });
        });

        admin.MapGet("/{revisionId}", async (string revisionId, SubmissionReviewService service, CancellationToken ct) =>
        {
            if (!Guid.TryParseExact(revisionId, "D", out var id) || await service.GetAsync(id, ct) is not { } d) return NotFound();
            var r = d;
            
            return Results.Ok(new AdminSubmissionDetail(r.RevisionId, Kind(r.HasApproved), r.Name, r.ProductName, r.ProductUrl, r.CohortName, r.SubmittedAt,
                r.Email, r.Phone, r.Description, r.Role, r.Workplace, r.IntroText, r.IntroAnswer1, r.IntroAnswer2, r.IntroAnswer3,
                r.AvatarId, r.HasPhoto, r.Cv, r.CvInvalid));
        });

        admin.MapGet("/{revisionId}/photo", async (string revisionId, HttpContext ctx, SubmissionReviewService service, CancellationToken ct) =>
        {
            ctx.Response.Headers.XContentTypeOptions = "nosniff";
            ctx.Response.Headers.CacheControl = "no-store";
            var photo = Guid.TryParseExact(revisionId, "D", out var id) ? await service.GetPhotoAsync(id, ct) : null;
            return photo is null ? NotFound() : Results.File(photo.Bytes, photo.ContentType);
        });

        return app;
    }

    private static string Kind(bool hasApproved) => hasApproved ? "update" : "new";

    private static IResult NotFound() => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: Strings.NotFound);
}
