using ResumeApp.Domain.Profiles;
using ResumeApp.Features.Cohorts;

namespace ResumeApp.Features.Profiles;

public interface IProfileRepository
{
    /// <summary>
    /// Inserts the profile, its pending revision and the private row in one transaction.
    /// Returns false (and stores nothing) when (cohort, email) already exists.
    /// </summary>
    Task<bool> TryAddAsync(Profile profile, ProfileRevision revision, ParticipantPrivate priv, CancellationToken ct);
}

public sealed record SubmitProfileCommand(string? Name, string? ProductName, string? ProductUrl, string? Email, string? Phone);

public enum SubmitOutcome { Accepted, InviteNotFound, Invalid }

public enum FieldError { Required, TooLong, Invalid }

public sealed record SubmitResult(SubmitOutcome Outcome, IReadOnlyDictionary<string, FieldError>? Errors = null);

public sealed class ProfileService(IProfileRepository repository, CohortService cohorts, TimeProvider clock)
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
        if (errors.Count > 0) return new(SubmitOutcome.Invalid, errors);

        var now = clock.GetUtcNow().UtcDateTime;
        var profile = Profile.Create(cohort.Id, now);
        var revision = ProfileRevision.CreatePending(profile.Id, name!, productName!, productUrl!, now);
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

    private static string? Email(string? value, Dictionary<string, FieldError> errors)
    {
        var v = value?.Trim().ToLowerInvariant();
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
