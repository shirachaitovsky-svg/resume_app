using ResumeApp.Domain.Profiles;
using ResumeApp.Features.Markdown;
using ResumeApp.Features.PublicForms;

namespace ResumeApp.Features.Profiles;

public sealed record SubmissionQueueRow(Guid RevisionId, string Name, string ProductName, string CohortName, bool HasApproved, DateTime SubmittedAt, bool HasContactChange = false);

/// <summary>The currently approved revision's visible fields, as stored; <see cref="PhotoSame"/> is computed by the database (byte equality with the proposed photo).</summary>
public sealed record PublishedRow(
    string Name, string ProductName, string ProductUrl, string? Description, string? Role, string? Workplace, string? IntroText,
    string? IntroAnswer1, string? IntroAnswer2, string? IntroAnswer3, string AvatarId, bool HasPhoto, string? Cv, bool PhotoSame);

public sealed record SubmissionDetailRow(
    Guid RevisionId, Guid ProfileId, string Name, string ProductName, string ProductUrl, string CohortName, bool HasApproved,
    DateTime SubmittedAt, string? Description, string? Role, string? Workplace, string? IntroText,
    string? IntroAnswer1, string? IntroAnswer2, string? IntroAnswer3, string AvatarId, bool HasPhoto, string? Cv,
    string Email, string Phone, string? PendingEmail = null, string? PendingPhone = null, PublishedRow? Published = null);

public sealed record SubmissionQueuePage(IReadOnlyList<SubmissionQueueRow> Items, int Total);

/// <summary>The visible fields of the published revision, with the cv already cleaned.</summary>
public sealed record PublishedSnapshot(
    string Name, string ProductName, string ProductUrl, string? Description, string? Role, string? Workplace, string? IntroText,
    string? IntroAnswer1, string? IntroAnswer2, string? IntroAnswer3, string AvatarId, bool HasPhoto, string? Cv, bool CvInvalid);

public sealed record PendingContactChange(string? PendingEmail, string? PendingPhone);

/// <summary>What the endpoint may serialise: the raw stored cv is deliberately absent.</summary>
public sealed record SubmissionDetail(
    Guid RevisionId, bool HasApproved, string Name, string ProductName, string ProductUrl, string CohortName, DateTime SubmittedAt,
    string Email, string Phone, string? Description, string? Role, string? Workplace, string? IntroText,
    string? IntroAnswer1, string? IntroAnswer2, string? IntroAnswer3, string AvatarId, bool HasPhoto, string? Cv, bool CvInvalid,
    PublishedSnapshot? Published, IReadOnlyList<string> Changes, PendingContactChange? ContactChange);

/// <summary>Read-only admin queries over pending revisions (story 1.10). Projections never load the photo bytes except in <see cref="GetPendingPhotoAsync"/>.</summary>
public interface ISubmissionReviewRepository
{
    Task<SubmissionQueuePage> ListPendingAsync(int limit, int offset, CancellationToken ct);
    Task<SubmissionDetailRow?> GetPendingAsync(Guid revisionId, CancellationToken ct);
    Task<PublicPhoto?> GetPendingPhotoAsync(Guid revisionId, CancellationToken ct);

    /// <summary>One transaction: pending to approved, supersede the previous approved revision, move the pointer, set the profile active, write the audit row.</summary>
    Task<ApproveOutcome> ApproveAsync(Guid revisionId, Guid actorId, DateTime nowUtc, CancellationToken ct);

    /// <summary>One transaction under the profile row lock: pending to returned with the note, audit row, and the outbox row for the email.</summary>
    Task<ReturnOutcome> ReturnAsync(Guid revisionId, Guid actorId, string note, DateTime nowUtc, CancellationToken ct);

    /// <summary>Revisions (pending, or approved with no newer pending one) that carry an unresolved email or phone change, oldest first.</summary>
    Task<ContactChangePage> ListContactChangesAsync(int limit, int offset, CancellationToken ct);

    /// <summary>One transaction under the profile row lock: apply (or drop) the revision's pending email or phone, bump the contact version, clear the pending fields, write the audit row.</summary>
    Task<ContactChangeOutcome> ResolveContactChangeAsync(Guid revisionId, Guid actorId, bool approve, string? seenEmail, string? seenPhone, DateTime nowUtc, CancellationToken ct);
}

public sealed record ContactChangeRow(
    Guid RevisionId, Guid ProfileId, string Name, string ProductName, string CohortName, string Status,
    string Email, string Phone, string? PendingEmail, string? PendingPhone, DateTime SubmittedAt);

public sealed record ContactChangePage(IReadOnlyList<ContactChangeRow> Items, int Total);

public enum ApproveStatus{ Approved, NotFound, NotPending, Inconsistent }

/// <summary><see cref="LatestPendingRevisionId"/> is set on <see cref="ApproveStatus.NotPending"/>: the profile's current pending revision, if any.</summary>
public sealed record ApproveOutcome(ApproveStatus Status, Guid ProfileId = default, Guid? LatestPendingRevisionId = null);

public enum ReturnStatus { Returned, NotFound, NotPending }

public sealed record ReturnOutcome(ReturnStatus Status, Guid? LatestPendingRevisionId = null);

public enum ContactChangeStatus { Done, NotFound, NoChange, NotEligible, Stale, EmailTaken }

public sealed record ContactChangeOutcome(ContactChangeStatus Status);

public sealed class SubmissionReviewService(ISubmissionReviewRepository repository, TimeProvider clock)
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 100;

    public Task<SubmissionQueuePage> ListAsync(int limit, int offset, CancellationToken ct)
        => repository.ListPendingAsync(Math.Min(limit, MaxLimit), offset, ct);

    public Task<ContactChangePage> ListContactChangesAsync(int limit, int offset, CancellationToken ct)
        => repository.ListContactChangesAsync(Math.Min(limit, MaxLimit), offset, ct);

    public async Task<SubmissionDetail?> GetAsync(Guid revisionId, CancellationToken ct)
    {
        var row = await repository.GetPendingAsync(revisionId, ct);
        if (row is null) return null;
        // Same shared function as the public path; a value it rejects is returned empty and flagged.
        var (cv, invalid) = CleanCv(row.Cv);

        PublishedSnapshot? published = null;
        var changes = new List<string>();
        if (row.Published is { } p)
        {
            var (pubCv, pubInvalid) = CleanCv(p.Cv);
            published = new(p.Name, p.ProductName, p.ProductUrl, p.Description, p.Role, p.Workplace, p.IntroText,
                p.IntroAnswer1, p.IntroAnswer2, p.IntroAnswer3, p.AvatarId, p.HasPhoto, pubCv, pubInvalid);
            void Diff(string field, string? before, string? after) { if (!string.Equals(before, after, StringComparison.Ordinal)) changes.Add(field); }
            Diff("name", p.Name, row.Name);
            Diff("productName", p.ProductName, row.ProductName);
            Diff("productUrl", p.ProductUrl, row.ProductUrl);
            Diff("description", p.Description, row.Description);
            Diff("role", p.Role, row.Role);
            Diff("workplace", p.Workplace, row.Workplace);
            Diff("introText", p.IntroText, row.IntroText);
            Diff("introAnswer1", p.IntroAnswer1, row.IntroAnswer1);
            Diff("introAnswer2", p.IntroAnswer2, row.IntroAnswer2);
            Diff("introAnswer3", p.IntroAnswer3, row.IntroAnswer3);
            Diff("avatarId", p.AvatarId, row.AvatarId);
            Diff("cv", pubCv, cv);
            if (p.HasPhoto != row.HasPhoto || (row.HasPhoto && !p.PhotoSame)) changes.Add("photo");
        }

        var contact = row.PendingEmail is null && row.PendingPhone is null ? null : new PendingContactChange(row.PendingEmail, row.PendingPhone);
        return new(row.RevisionId, row.HasApproved, row.Name, row.ProductName, row.ProductUrl, row.CohortName, row.SubmittedAt,
            row.Email, row.Phone, row.Description, row.Role, row.Workplace, row.IntroText, row.IntroAnswer1, row.IntroAnswer2, row.IntroAnswer3,
            row.AvatarId, row.HasPhoto, cv, invalid, published, changes, contact);
    }

    private static (string? Text, bool Invalid) CleanCv(string? raw)
    {
        if (raw is null) return (null, false);
        var cleaned = SafeMarkdown.Clean(raw, ProfileRevision.CvMaxLength);
        return cleaned.Outcome != MarkdownOutcome.Ok ? (null, true) : (cleaned.Text, false);
    }

    public Task<PublicPhoto?> GetPhotoAsync(Guid revisionId, CancellationToken ct) => repository.GetPendingPhotoAsync(revisionId, ct);

    public Task<ApproveOutcome> ApproveAsync(Guid revisionId, Guid actorId, CancellationToken ct)
        => repository.ApproveAsync(revisionId, actorId, clock.GetUtcNow().UtcDateTime, ct);

    /// <summary>Validates the note (1 to 1000 characters after trim, plain text) and returns the revision to the participant. A null result means a field error, see <paramref name="error"/>.</summary>
    public async Task<ReturnOutcome?> ReturnAsync(Guid revisionId, Guid actorId, string? note, CancellationToken ct)
    {
        var clean = CleanReturnNote(note, out _);
        if (clean is null) return null;
        return await repository.ReturnAsync(revisionId, actorId, clean, clock.GetUtcNow().UtcDateTime, ct);
    }

    /// <summary>Trims, normalises line breaks to LF, and rejects empty, too long (over 1000) or unsafe text (control characters other than LF and tab, invisible characters).</summary>
    public static string? CleanReturnNote(string? note, out FieldError? error)
    {
        error = null;
        var v = note?.Trim();
        if (string.IsNullOrEmpty(v)) { error = FieldError.Required; return null; }
        v = v.Replace("\r\n", "\n").Replace('\r', '\n');
        if (v.Length > ProfileRevision.ReturnNoteMaxLength) { error = FieldError.TooLong; return null; }
        if (v.Any(c => UnsafeCharacters.IsUnsafe(c) && c is not ('\n' or '\t'))) { error = FieldError.Invalid; return null; }
        return v;
    }

    /// <summary>Approves or rejects the pending contact change the admin saw. Returns null when a seen value is not a valid email or phone, or none was given.</summary>
    public async Task<ContactChangeOutcome?> ResolveContactChangeAsync(Guid revisionId, Guid actorId, bool approve, string? seenEmail, string? seenPhone, CancellationToken ct)
    {
        string? email = null, phone = null;
        if (seenEmail is not null)
        {
            email = ProfileService.ValidateEmail(seenEmail, out _);
            if (email is null) return null;
        }
        if (seenPhone is not null)
        {
            phone = ProfileService.NormalizePhone(seenPhone);
            if (phone is null) return null;
        }
        if (email is null && phone is null) return null;
        return await repository.ResolveContactChangeAsync(revisionId, actorId, approve, email, phone, clock.GetUtcNow().UtcDateTime, ct);
    }
}
