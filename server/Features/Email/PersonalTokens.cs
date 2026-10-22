using System.Security.Cryptography;
using System.Text;

namespace ResumeApp.Features.Email;

public static class PersonalTokens
{
    /// <summary>32 random bytes (256 bits) from the OS CSPRNG, base64url without padding.</summary>
    public static string Generate()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Exactly what <see cref="Generate"/> produces: 43 base64url characters. Anything else cannot be a token.</summary>
    public static bool HasValidShape(string? token)
        => token is { Length: 43 } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>SHA-256 of the token as lowercase hex: the only form that is ever stored.</summary>
    public static string Hash(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}

public interface IPersonalTokenIssuer
{
    /// <summary>
    /// Story 3.1. Issues a verify-contact-email token for one contact request, revoking its earlier unused ones, in one transaction that
    /// first re-checks under lock that the request still awaits verification. Null when it does not (then nothing is issued).
    /// </summary>
    Task<string?> IssueContactVerificationTokenAsync(Guid requestId, Guid profileId, DateTime nowUtc, CancellationToken ct);

    /// <summary>
    /// Story 3.3. Issues a contact-decision token for one request (14 days), revoking its earlier unused ones, in one transaction that first
    /// re-checks under lock (profile Active, request still sent_to_participant). Null when it does not hold (then nothing is issued).
    /// </summary>
    Task<string?> IssueContactDecisionTokenAsync(Guid requestId, Guid profileId, DateTime nowUtc, CancellationToken ct);
}
