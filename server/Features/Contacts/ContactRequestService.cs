using System.Text.RegularExpressions;
using ResumeApp.Domain.Contacts;

using ResumeApp.Features.Email;
using ResumeApp.Features.Profiles;
using ResumeApp.Features.PublicForms;

namespace ResumeApp.Features.Contacts;

/// <summary>Validated, normalised input of a contact request.</summary>
public sealed record ValidContactRequest(string Name, string Email, string Reason);

public interface IContactRequestRepository
{
    /// <summary>
    /// One transaction: if the profile is Active (row-share lock, so removal is serialised with it) and no open request exists for
    /// (profile, address), writes the request and its outbox row. Returns whether a request was created. Never reveals why not.
    /// </summary>
    Task<bool> TryCreateAsync(Guid profileId, ValidContactRequest request, DateTime nowUtc, CancellationToken ct);

    /// <summary>
    /// Story 3.1. Atomically consumes a verify-contact-email token and moves its request to submitted (conditional updates, AD-14).
    /// False for every kind of failure; a valid token whose request ran out or whose profile is no longer Active also expires the request.
    /// </summary>
    Task<bool> VerifyAsync(string tokenHash, DateTime nowUtc, CancellationToken ct);

    /// <summary>Moves awaiting requests past their expiry to expired and revokes their open verification tokens. Returns how many.</summary>
    Task<int> ExpireStaleAsync(DateTime nowUtc, CancellationToken ct);
}

/// <summary>Visitor contact requests (FR-13, AD-3, AD-12), up to email verification.</summary>
public sealed partial class ContactRequestService(IContactRequestRepository repository, TimeProvider clock)
{
    [GeneratedRegex(@"(?:https?\s*:\s*//|www\s*\.|\b(?:javascript|data|vbscript|file|ftp|mailto):\S)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LinkPattern();

    /// <summary>Best-effort link filter on the NFKC form (fullwidth colons and the like fold to ASCII). Scheme-like tokens only: bare domains pass.</summary>
    private static bool HasLink(string text) => LinkPattern().IsMatch(text.Normalize(System.Text.NormalizationForm.FormKC));

    /// <summary>Field-by-field validation, run before anything about the profile is looked up. Returns null (with errors) when invalid.</summary>
    public static ValidContactRequest? Validate(string? name, string? email, string? reason, out IReadOnlyDictionary<string, FieldError> errors)
    {
        var found = new Dictionary<string, FieldError>();
        errors = found;

        var cleanName = name?.Trim();
        if (string.IsNullOrEmpty(cleanName)) found["name"] = FieldError.Required;
        else if (cleanName.Length > ContactRequest.NameMaxLength) found["name"] = FieldError.TooLong;
        else if (cleanName.Any(UnsafeCharacters.IsUnsafe) || HasLink(cleanName)) found["name"] = FieldError.Invalid; // the name is echoed in the verification mail

        var cleanEmail = ProfileService.ValidateEmail(email, out var emailError);
        if (cleanEmail is null) found["email"] = emailError ?? FieldError.Invalid;

        // Plain text: line breaks are normalised to LF; only LF and tab survive as control characters.
        var cleanReason = reason?.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (string.IsNullOrEmpty(cleanReason)) found["reason"] = FieldError.Required;
        else if (cleanReason.Length > ContactRequest.ReasonMaxLength) found["reason"] = FieldError.TooLong;
        else if (cleanReason.Any(c => c is not ('\n' or '\t') && UnsafeCharacters.IsUnsafe(c)) || HasLink(cleanReason))
            found["reason"] = FieldError.Invalid;

        return found.Count == 0 ? new ValidContactRequest(cleanName!, cleanEmail!, cleanReason!) : null;
    }

    /// <summary>
    /// Light work only. The caller answers identically whatever happens here: an unknown, malformed or non-Active profile
    /// and a duplicate open request all end silently.
    /// </summary>
    public async Task SubmitAsync(string? profileId, ValidContactRequest request, CancellationToken ct)
    {
        if (!Guid.TryParseExact(profileId, "D", out var id)) return;
        await repository.TryCreateAsync(id, request, clock.GetUtcNow().UtcDateTime, ct);
    }

    public Task<bool> VerifyAsync(string? token, CancellationToken ct)
        => PersonalTokens.HasValidShape(token)
            ? repository.VerifyAsync(PersonalTokens.Hash(token!), clock.GetUtcNow().UtcDateTime, ct)
            : Task.FromResult(false);

    public Task<int> ExpireStaleAsync(CancellationToken ct) => repository.ExpireStaleAsync(clock.GetUtcNow().UtcDateTime, ct);
}
