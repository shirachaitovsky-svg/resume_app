using ResumeApp.Domain.Profiles;
using ResumeApp.Features.Cohorts;
using ResumeApp.Features.Markdown;
using ResumeApp.Features.PublicForms;

namespace ResumeApp.Features.Profiles;

public interface IProfileRepository
{
    /// <summary>
    /// Inserts the profile, its pending revision and the private row in one transaction.
    /// Returns false (and stores nothing) when (cohort, email) already exists.
    /// </summary>
    Task<bool> TryAddAsync(Profile profile, ProfileRevision revision, ParticipantPrivate priv, CancellationToken ct);

    /// <summary>
    /// Story 2.2. In one transaction (profile row locked): supersedes the pending revision if any and inserts a new pending one.
    /// The approved revision, the pointer, visibility and the private row are untouched. Returns false when the profile is missing or removed.
    /// </summary>
    Task<bool> AddPendingRevisionAsync(ProfileRevisionUpdate update, DateTime nowUtc, CancellationToken ct);

    /// <summary>The profile as its owner sees it (editing source: latest pending, else the latest returned if newer than the approved one, else approved); null when missing or removed.</summary>
    Task<OwnProfile?> GetOwnAsync(Guid profileId, CancellationToken ct);
}

public enum PhotoChange { Keep, Remove, Replace }

/// <summary>A validated edit, ready to be stored as a new pending revision. Email and phone are normalised.</summary>
public sealed record ProfileRevisionUpdate(
    Guid ProfileId, string Name, string ProductName, string ProductUrl, string Email, string Phone,
    string? Description, string? Role, string? Workplace, string? IntroText,
    string? IntroAnswer1, string? IntroAnswer2, string? IntroAnswer3, string AvatarId, string? Cv,
    PhotoChange PhotoChange, byte[]? NewPhoto, string? NewPhotoContentType);

/// <summary>The owner's view of their own profile (private to them: includes email and phone).</summary>
public sealed record OwnProfile(
    string Name, string ProductName, string ProductUrl, string CurrentEmail, string CurrentPhone,
    string? Description, string? Role, string? Workplace, string? IntroText,
    string? IntroAnswer1, string? IntroAnswer2, string? IntroAnswer3, string AvatarId, string? Cv,
    bool HasPhoto, string CohortName, bool HasPublished, DateTime? PendingSubmittedAt, string? PendingEmail, string? PendingPhone,
    string? ReturnedNote = null, DateTime? ReturnedAt = null);

public sealed record UpdateProfileCommand(SubmitProfileCommand Fields, bool RemovePhoto);

public enum UpdateOutcome { Accepted, NotFound, Invalid, Busy }

public sealed record UpdateResult(UpdateOutcome Outcome, IReadOnlyDictionary<string, FieldError>? Errors = null);

/// <summary>The optional fields (story 1.8) default to absent, so a submission with only the required fields still works.</summary>
public sealed record SubmitProfileCommand(
    string? Name, string? ProductName, string? ProductUrl, string? Email, string? Phone,
    string? Description = null, string? Role = null, string? Workplace = null, string? IntroText = null,
    string? IntroAnswer1 = null, string? IntroAnswer2 = null, string? IntroAnswer3 = null,
    string? AvatarId = null, byte[]? Photo = null, string? Cv = null);

/// <summary>An approved revision's photo, as served by the public photo endpoint.</summary>
public sealed record PublicPhoto(byte[] Bytes, string ContentType);

public enum SubmitOutcome { Accepted, InviteNotFound, Invalid, Busy }

public enum FieldError { Required, TooLong, Invalid }

public sealed record SubmitResult(SubmitOutcome Outcome, IReadOnlyDictionary<string, FieldError>? Errors = null);

public sealed class ProfileService(IProfileRepository repository, CohortService cohorts, IPhotoProcessor photos, TimeProvider clock)
{
    public async Task<SubmitResult> SubmitAsync(string token, SubmitProfileCommand command, CancellationToken ct)
    {
        var cohort = await cohorts.ResolveActiveAsync(token, ct);
        if (cohort is null) return new(SubmitOutcome.InviteNotFound);

        var (valid, failure) = await ValidateAsync(command, ct);
        if (valid is null) return failure!;

        var now = clock.GetUtcNow().UtcDateTime;
        var profile = Profile.Create(cohort.Id, now);
        var extras = new ProfileExtras(valid.Description, valid.Role, valid.Workplace, valid.IntroText, valid.IntroAnswer1, valid.IntroAnswer2, valid.IntroAnswer3,
            valid.Photo is null ? valid.AvatarId : ProfileAvatars.Default, valid.Photo, valid.PhotoType, valid.Cv); // an uploaded photo replaces the avatar
        var revision = ProfileRevision.CreatePending(profile.Id, valid.Name, valid.ProductName, valid.ProductUrl, now, extras);
        var priv = ParticipantPrivate.Create(profile.Id, cohort.Id, valid.Email, valid.Phone);

        // A duplicate (same cohort and email) is indistinguishable from success to the caller.
        await repository.TryAddAsync(profile, revision, priv, ct);
        return new(SubmitOutcome.Accepted);
    }

    /// <summary>Story 2.2: the same validation as a first submission, then a new pending revision for the session's profile.</summary>
    public async Task<UpdateResult> UpdateAsync(Guid profileId, UpdateProfileCommand command, CancellationToken ct)
    {
        var (valid, failure) = await ValidateAsync(command.Fields, ct);
        if (valid is null)
            return new(failure!.Outcome == SubmitOutcome.Busy ? UpdateOutcome.Busy : UpdateOutcome.Invalid, failure.Errors);

        var change = valid.Photo is not null ? PhotoChange.Replace : command.RemovePhoto ? PhotoChange.Remove : PhotoChange.Keep;
        var update = new ProfileRevisionUpdate(profileId, valid.Name, valid.ProductName, valid.ProductUrl, valid.Email, valid.Phone,
            valid.Description, valid.Role, valid.Workplace, valid.IntroText, valid.IntroAnswer1, valid.IntroAnswer2, valid.IntroAnswer3,
            valid.AvatarId, valid.Cv, change, valid.Photo, valid.PhotoType);
        var stored = await repository.AddPendingRevisionAsync(update, clock.GetUtcNow().UtcDateTime, ct);
        return new(stored ? UpdateOutcome.Accepted : UpdateOutcome.NotFound);
    }

    public Task<OwnProfile?> GetOwnAsync(Guid profileId, CancellationToken ct) => repository.GetOwnAsync(profileId, ct);

    private sealed record Validated(
        string Name, string ProductName, string ProductUrl, string Email, string Phone,
        string? Description, string? Role, string? Workplace, string? IntroText,
        string? IntroAnswer1, string? IntroAnswer2, string? IntroAnswer3, string AvatarId, string? Cv, byte[]? Photo, string? PhotoType);

    /// <summary>The single validation and cleaning path for a submission and an edit.</summary>
    private async Task<(Validated? Valid, SubmitResult? Failure)> ValidateAsync(SubmitProfileCommand command, CancellationToken ct)
    {
        var errors = new Dictionary<string, FieldError>();
        var name = Text(command.Name, ProfileRevision.NameMaxLength, "name", errors);
        var productName = Text(command.ProductName, ProfileRevision.ProductNameMaxLength, "productName", errors);
        var productUrl = Url(command.ProductUrl, errors);
        var email = Email(command.Email, errors);
        var phone = Phone(command.Phone, errors);

        var description = Optional(command.Description, ProfileRevision.DescriptionMaxLength, "description", false, errors);
        var role = Optional(command.Role, ProfileRevision.RoleMaxLength, "role", false, errors);
        var workplace = Optional(command.Workplace, ProfileRevision.WorkplaceMaxLength, "workplace", false, errors);
        var introText = Optional(command.IntroText, ProfileRevision.IntroMaxLength, "introText", true, errors);
        var answer1 = Optional(command.IntroAnswer1, ProfileRevision.IntroMaxLength, "introAnswer1", true, errors);
        var answer2 = Optional(command.IntroAnswer2, ProfileRevision.IntroMaxLength, "introAnswer2", true, errors);
        var answer3 = Optional(command.IntroAnswer3, ProfileRevision.IntroMaxLength, "introAnswer3", true, errors);

        var cvResult = SafeMarkdown.Clean(command.Cv, ProfileRevision.CvMaxLength);
        if (cvResult.Outcome == MarkdownOutcome.TooLong) errors["cv"] = FieldError.TooLong;
        else if (cvResult.Outcome == MarkdownOutcome.Invalid) errors["cv"] = FieldError.Invalid;

        var avatarId = command.AvatarId?.Trim();
        if (string.IsNullOrEmpty(avatarId) || avatarId == ProfileAvatars.Default) avatarId = ProfileAvatars.Default;
        else if (!ProfileAvatars.IsSelectable(avatarId)) errors["avatarId"] = FieldError.Invalid;

        byte[]? photo = null;
        string? photoType = null;
        if (command.Photo is { Length: > 0 } upload)
        {
            var processed = await photos.ProcessAsync(upload, ct);
            switch (processed.Outcome)
            {
                case PhotoOutcome.Ok: photo = processed.Bytes; photoType = processed.ContentType; break;
                case PhotoOutcome.Busy: return (null, new(SubmitOutcome.Busy));
                case PhotoOutcome.TooLarge: errors["photo"] = FieldError.TooLong; break;
                default: errors["photo"] = FieldError.Invalid; break;
            }
        }
        else if (command.Photo is not null)
        {
            errors["photo"] = FieldError.Invalid; // an empty file part
        }

        if (errors.Count > 0) return (null, new(SubmitOutcome.Invalid, errors));

        return (new Validated(name!, productName!, productUrl!, email!, phone!, description, role, workplace, introText, answer1, answer2, answer3,
            avatarId, cvResult.Text, photo, photoType), null);
    }

    private static string? Text(string? value, int max, string field, Dictionary<string, FieldError> errors)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) { errors[field] = FieldError.Required; return null; }
        if (v.Length > max) { errors[field] = FieldError.TooLong; return null; }
        if (v.Any(char.IsControl)) { errors[field] = FieldError.Invalid; return null; }
        return v;
    }

    /// <summary>
    /// An optional plain-text field: blank means absent. Control characters are rejected as for the required fields,
    /// except that the long free-text fields may contain line breaks (tab and LF allowed; CR normalised to LF).
    /// </summary>
    private static string? Optional(string? value, int max, string field, bool multiline, Dictionary<string, FieldError> errors)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        if (multiline) v = v.Replace("\r\n", "\n").Replace('\r', '\n');
        if (v.Length > max) { errors[field] = FieldError.TooLong; return null; }
        if (v.Any(c => UnsafeCharacters.IsUnsafe(c) && !(multiline && c is '\n' or '\t'))) { errors[field] = FieldError.Invalid; return null; }
        return v;
    }

    private static string? Url(string? value, Dictionary<string, FieldError> errors)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) { errors["productUrl"] = FieldError.Required; return null; }
        if (v.Length > ProfileRevision.ProductUrlMaxLength) { errors["productUrl"] = FieldError.TooLong; return null; }
        if (v.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))
            || !Uri.TryCreate(v, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            errors["productUrl"] = FieldError.Invalid;
            return null;
        }
        return v;
    }

    /// <summary>The single email normalisation: used for storage and for the per-email rate-limit key.</summary>
    public static string? NormalizeEmail(string? value) => value?.Trim().ToLowerInvariant();

    /// <summary>The same email validation as submission; returns the normalised address, or null with the reason.</summary>
    public static string? ValidateEmail(string? value, out FieldError? error)
    {
        var errors = new Dictionary<string, FieldError>();
        var email = Email(value, errors);
        error = errors.TryGetValue("email", out var e) ? e : null;
        return email;
    }

    /// <summary>The same phone validation as submission; returns the normalised number, or null when invalid.</summary>
    public static string? NormalizePhone(string? value) => Phone(value, []);

    private static string? Email(string? value, Dictionary<string, FieldError> errors)
    {
        var v = NormalizeEmail(value);
        if (string.IsNullOrEmpty(v)) { errors["email"] = FieldError.Required; return null; }
        if (v.Length > ParticipantPrivate.EmailMaxLength) { errors["email"] = FieldError.TooLong; return null; }
        var at = v.IndexOf('@');
        var domain = at > 0 ? v[(at + 1)..] : "";
        var valid = at > 0 && at == v.LastIndexOf('@')
            && !v.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is ',' or ';' or '<' or '>' or '(' or ')' or '"')
            && domain.Contains('.') && !domain.StartsWith('.') && !domain.EndsWith('.') && !domain.Contains("..");
        if (!valid) { errors["email"] = FieldError.Invalid; return null; }
        return v;
    }

    private static string? Phone(string? value, Dictionary<string, FieldError> errors)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) { errors["phone"] = FieldError.Required; return null; }
        if (v.Length > 40) { errors["phone"] = FieldError.TooLong; return null; }
        var plus = v.StartsWith('+');
        var body = plus ? v[1..] : v;
        var digits = body.Count(char.IsAsciiDigit);
        if (body.Any(c => !(char.IsAsciiDigit(c) || c is ' ' or '-')) || digits is < 9 or > 15)
        {
            errors["phone"] = FieldError.Invalid;
            return null;
        }
        return (plus ? "+" : "") + new string(body.Where(char.IsAsciiDigit).ToArray());
    }
}
