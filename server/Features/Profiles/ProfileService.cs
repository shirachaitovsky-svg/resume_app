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
}

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
                case PhotoOutcome.Busy: return new(SubmitOutcome.Busy);
                case PhotoOutcome.TooLarge: errors["photo"] = FieldError.TooLong; break;
                default: errors["photo"] = FieldError.Invalid; break;
            }
        }
        else if (command.Photo is not null)
        {
            errors["photo"] = FieldError.Invalid; // an empty file part
        }

        if (errors.Count > 0) return new(SubmitOutcome.Invalid, errors);

        var now = clock.GetUtcNow().UtcDateTime;
        var profile = Profile.Create(cohort.Id, now);
        var extras = new ProfileExtras(description, role, workplace, introText, answer1, answer2, answer3,
            photo is null ? avatarId : ProfileAvatars.Default, photo, photoType, cvResult.Text); // an uploaded photo replaces the avatar
        var revision = ProfileRevision.CreatePending(profile.Id, name!, productName!, productUrl!, now, extras);
        var priv = ParticipantPrivate.Create(profile.Id, cohort.Id, email!, phone!);

        // A duplicate (same cohort and email) is indistinguishable from success to the caller.
        await repository.TryAddAsync(profile, revision, priv, ct);
        return new(SubmitOutcome.Accepted);
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
