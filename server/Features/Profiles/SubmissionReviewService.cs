using ResumeApp.Domain.Profiles;
using ResumeApp.Features.Markdown;

namespace ResumeApp.Features.Profiles;

public sealed record SubmissionQueueRow(Guid RevisionId, string Name, string ProductName, string CohortName, bool HasApproved, DateTime SubmittedAt);

public sealed record SubmissionDetailRow(
    Guid RevisionId, Guid ProfileId, string Name, string ProductName, string ProductUrl, string CohortName, bool HasApproved,
    DateTime SubmittedAt, string? Description, string? Role, string? Workplace, string? IntroText,
    string? IntroAnswer1, string? IntroAnswer2, string? IntroAnswer3, string AvatarId, bool HasPhoto, string? Cv,
    string Email, string Phone);

public sealed record SubmissionQueuePage(IReadOnlyList<SubmissionQueueRow> Items, int Total);

/// <summary>What the endpoint may serialise: the raw stored cv is deliberately absent.</summary>
public sealed record SubmissionDetail(
    Guid RevisionId, bool HasApproved, string Name, string ProductName, string ProductUrl, string CohortName, DateTime SubmittedAt,
    string Email, string Phone, string? Description, string? Role, string? Workplace, string? IntroText,
    string? IntroAnswer1, string? IntroAnswer2, string? IntroAnswer3, string AvatarId, bool HasPhoto, string? Cv, bool CvInvalid);

/// <summary>Read-only admin queries over pending revisions (story 1.10). Projections never load the photo bytes except in <see cref="GetPendingPhotoAsync"/>.</summary>
public interface ISubmissionReviewRepository
{
    Task<SubmissionQueuePage> ListPendingAsync(int limit, int offset, CancellationToken ct);
    Task<SubmissionDetailRow?> GetPendingAsync(Guid revisionId, CancellationToken ct);
    Task<PublicPhoto?> GetPendingPhotoAsync(Guid revisionId, CancellationToken ct);

    /// <summary>One transaction: pending to approved, supersede the previous approved revision, move the pointer, set the profile active, write the audit row.</summary>
    Task<ApproveOutcome> ApproveAsync(Guid revisionId, Guid actorId, DateTime nowUtc, CancellationToken ct);
}

public enum ApproveStatus { Approved, NotFound, NotPending, Inconsistent }

public sealed record ApproveOutcome(ApproveStatus Status, Guid ProfileId = default);

public sealed class SubmissionReviewService(ISubmissionReviewRepository repository, TimeProvider clock)
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 100;

    public Task<SubmissionQueuePage> ListAsync(int limit, int offset, CancellationToken ct)
        => repository.ListPendingAsync(Math.Min(limit, MaxLimit), offset, ct);

    public async Task<SubmissionDetail?> GetAsync(Guid revisionId, CancellationToken ct)
    {
        var row = await repository.GetPendingAsync(revisionId, ct);
        if (row is null) return null;
        // Same shared function as the public path; a value it rejects is returned empty and flagged.
        var cv = row.Cv is null ? null : SafeMarkdown.Clean(row.Cv, ProfileRevision.CvMaxLength);
        var invalid = cv is { Outcome: not MarkdownOutcome.Ok };
        return new(row.RevisionId, row.HasApproved, row.Name, row.ProductName, row.ProductUrl, row.CohortName, row.SubmittedAt,
            row.Email, row.Phone, row.Description, row.Role, row.Workplace, row.IntroText, row.IntroAnswer1, row.IntroAnswer2, row.IntroAnswer3,
            row.AvatarId, row.HasPhoto, invalid ? null : cv?.Text, invalid);
    }

    public Task<PublicPhoto?> GetPhotoAsync(Guid revisionId, CancellationToken ct) => repository.GetPendingPhotoAsync(revisionId, ct);

    public Task<ApproveOutcome> ApproveAsync(Guid revisionId, Guid actorId, CancellationToken ct)
        => repository.ApproveAsync(revisionId, actorId, clock.GetUtcNow().UtcDateTime, ct);
}
