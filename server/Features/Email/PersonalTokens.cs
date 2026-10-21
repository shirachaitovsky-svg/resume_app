using System.Security.Cryptography;
using System.Text;

namespace ResumeApp.Features.Email;

public static class PersonalTokens
{
    /// <summary>32 random bytes (256 bits) from the OS CSPRNG, base64url without padding.</summary>
    public static string Generate()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>SHA-256 of the token as lowercase hex: the only form that is ever stored.</summary>
    public static string Hash(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}

public interface IPersonalTokenIssuer
{
    /// <summary>
    /// Issues a new token for (profile, purpose) and revokes earlier unused ones in the same transaction.
    /// Returns the raw token, which exists nowhere else.
    /// </summary>
    Task<string> IssueEditTokenAsync(Guid profileId, DateTime nowUtc, CancellationToken ct);
}
