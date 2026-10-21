using System.Security.Cryptography;

namespace ResumeApp.Domain.Cohorts;

public sealed class Cohort
{
    public const int NameMaxLength = 100;
    public const int TokenBytes = 32;
    /// <summary>Length of a base64url string encoding 32 bytes (no padding).</summary>
    public const int TokenLength = 43;

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string InviteToken { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }

    private Cohort() { }

    public static Cohort Create(string name, DateTime nowUtc) => new()
    {
        Id = Guid.NewGuid(), // v4 (random)
        Name = name,
        InviteToken = GenerateToken(),
        CreatedAt = nowUtc,
    };

    /// <summary>256 random bits from the OS CSPRNG, base64url without padding.</summary>
    public static string GenerateToken()
        => Base64Url(RandomNumberGenerator.GetBytes(TokenBytes));

    public static bool IsWellFormedToken(string? token)
        => token is { Length: TokenLength } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
