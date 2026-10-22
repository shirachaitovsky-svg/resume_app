using System.Security.Claims;
using ResumeApp.Api.Resources;
using ResumeApp.Api.Security;
using ResumeApp.Features.Profiles;

namespace ResumeApp.Api.Endpoints;

/// <summary>Admin review queue (story 1.10). Read-only; every route requires the admin policy (AD-5).</summary>
public static class SubmissionEndpoints
{
    public sealed record AdminSubmissionItem(Guid RevisionId, string Name, string ProductName, string CohortName, string Kind, DateTime SubmittedAt, bool HasContactChange);

    public sealed record AdminSubmissionDetail(
        Guid RevisionId, string Kind, string Name, string ProductName, string ProductUrl, string CohortName, DateTime SubmittedAt,
        string Email, string Phone, string? Description, string? Role, string? Workplace, string? IntroText,
        string? IntroAnswer1, string? IntroAnswer2, string? IntroAnswer3, string AvatarId, bool HasPhoto, string? Cv, bool CvInvalid,
        PublishedSnapshot? Published, IReadOnlyList<string> Changes, PendingContactChange? ContactChange);

    public sealed record ContactChangeBody(string? PendingEmail, string? PendingPhone);

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
            var items = page.Items.Select(r => new AdminSubmissionItem(r.RevisionId, r.Name, r.ProductName, r.CohortName, Kind(r.HasApproved), r.SubmittedAt, r.HasContactChange)).ToList();
            return Results.Ok(new { items, total = page.Total });
        });

        // Unresolved email/phone changes, including those on revisions already published (no longer in the pending queue).
        app.MapGet("/admin/contact-changes", async (int? limit, int? offset, HttpContext ctx, SubmissionReviewService service, CancellationToken ct) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            var errors = new Dictionary<string, string[]>();
            if (limit is < 1) errors["limit"] = [Strings.FieldInvalid];
            if (offset is < 0) errors["offset"] = [Strings.FieldInvalid];
            if (errors.Count > 0) return Results.ValidationProblem(errors, title: Strings.ValidationTitle);

            var page = await service.ListContactChangesAsync(limit ?? SubmissionReviewService.DefaultLimit, offset ?? 0, ct);
            var items = page.Items.Select(r => new { r.RevisionId, r.ProfileId, r.Name, r.ProductName, r.CohortName, r.Status, r.Email, r.Phone, r.PendingEmail, r.PendingPhone, r.SubmittedAt });
            return Results.Ok(new { items, total = page.Total });
        }).RequireAuthorization(IdentityServiceExtensions.AdminPolicy);

        admin.MapGet("/{revisionId}", async (string revisionId, SubmissionReviewService service, CancellationToken ct) =>
        {
            if (!Guid.TryParseExact(revisionId, "D", out var id) || await service.GetAsync(id, ct) is not { } d) return NotFound();
            var r = d;
            
            return Results.Ok(new AdminSubmissionDetail(r.RevisionId, Kind(r.HasApproved), r.Name, r.ProductName, r.ProductUrl, r.CohortName, r.SubmittedAt,
                r.Email, r.Phone, r.Description, r.Role, r.Workplace, r.IntroText, r.IntroAnswer1, r.IntroAnswer2, r.IntroAnswer3,
                r.AvatarId, r.HasPhoto, r.Cv, r.CvInvalid, r.Published, r.Changes, r.ContactChange));
        });

        admin.MapGet("/{revisionId}/photo", async (string revisionId, HttpContext ctx, SubmissionReviewService service, CancellationToken ct) =>
        {
            ctx.Response.Headers.XContentTypeOptions = "nosniff";
            ctx.Response.Headers.CacheControl = "no-store";
            var photo = Guid.TryParseExact(revisionId, "D", out var id) ? await service.GetPhotoAsync(id, ct) : null;
            return photo is null ? NotFound() : Results.File(photo.Bytes, photo.ContentType);
        });

        // Story 1.11: approve exactly the revision the admin saw (AD-2); anti-forgery and Origin checks apply to every POST.
        admin.MapPost("/{revisionId}/approve", async (string revisionId, ClaimsPrincipal user, SubmissionReviewService service, CancellationToken ct) =>
        {
            if (!Guid.TryParseExact(revisionId, "D", out var id)) return NotFound();
            if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: Strings.Forbidden);
            var outcome = await service.ApproveAsync(id, actor, ct);
            return outcome.Status switch
            {
                ApproveStatus.Approved => Results.Ok(new { revisionId = id, profileId = outcome.ProfileId, status = "approved" }),
                ApproveStatus.NotPending => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: Strings.RevisionNotPending,
                    extensions: new Dictionary<string, object?> { ["code"] = "revision-not-pending", ["latestPendingRevisionId"] = outcome.LatestPendingRevisionId }),
                ApproveStatus.Inconsistent => Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: Strings.ApproveInconsistent),
                _ => NotFound(),
            };
        });

        admin.MapPost("/{revisionId}/approve-contact-change", (string revisionId, ContactChangeBody body, ClaimsPrincipal user, SubmissionReviewService service, CancellationToken ct)
            => ResolveContactChange(revisionId, body, user, service, approve: true, ct));

        admin.MapPost("/{revisionId}/reject-contact-change", (string revisionId, ContactChangeBody body, ClaimsPrincipal user, SubmissionReviewService service, CancellationToken ct)
            => ResolveContactChange(revisionId, body, user, service, approve: false, ct));

        return app;
    }

    private static async Task<IResult> ResolveContactChange(string revisionId, ContactChangeBody body, ClaimsPrincipal user, SubmissionReviewService service, bool approve, CancellationToken ct)
    {
        if (!Guid.TryParseExact(revisionId, "D", out var id)) return NotFound();
        if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: Strings.Forbidden);
        var outcome = await service.ResolveContactChangeAsync(id, actor, approve, body.PendingEmail, body.PendingPhone, ct);
        if (outcome is null)
        {
            var errors = new Dictionary<string, string[]>();
            if (body.PendingEmail is null && body.PendingPhone is null) errors["pendingEmail"] = [Strings.FieldRequired];
            else
            {
                if (body.PendingEmail is not null && ProfileService.ValidateEmail(body.PendingEmail, out _) is null) errors["pendingEmail"] = [Strings.EmailInvalid];
                if (body.PendingPhone is not null && ProfileService.NormalizePhone(body.PendingPhone) is null) errors["pendingPhone"] = [Strings.FieldInvalid];
            }
            return Results.ValidationProblem(errors, title: Strings.ValidationTitle);
        }
        return outcome.Status switch
        {
            ContactChangeStatus.Done => Results.Ok(new { revisionId = id, status = approve ? "contact-change-approved" : "contact-change-rejected" }),
            ContactChangeStatus.NoChange => Conflict(Strings.NoContactChange, "no-contact-change"),
            ContactChangeStatus.Stale => Conflict(Strings.ContactChangeStale, "contact-change-stale"),
            ContactChangeStatus.NotEligible => Conflict(Strings.RevisionNotEligible, "revision-not-eligible"),
            ContactChangeStatus.EmailTaken => Conflict(Strings.EmailTaken, "email-taken"),
            _ => NotFound(),
        };
    }

    private static IResult Conflict(string title, string code)
        => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });
    private static string Kind(bool hasApproved) => hasApproved ? "update" : "new";

    private static IResult NotFound() => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: Strings.NotFound);
}
