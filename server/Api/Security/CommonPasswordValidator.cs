using System.Reflection;
using Microsoft.AspNetCore.Identity;
using ResumeApp.Infrastructure.Identity;

namespace ResumeApp.Api.Security;

/// <summary>
/// Rejects passwords found in the bundled common-password list (embedded resource) and passwords containing the user name.
/// </summary>
public sealed class CommonPasswordValidator : IPasswordValidator<AppUser>
{
    public const string CommonCode = "PasswordCommon";
    public const string ContainsUsernameCode = "PasswordContainsUsername";

    private static readonly Lazy<HashSet<string>> Common = new(Load);

    public Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user, string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return Task.FromResult(IdentityResult.Success);
        }

        var lower = password.ToLowerInvariant();
        var trimmed = lower.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '!', '@', '#', '$', '.', '_');
        if (Common.Value.Contains(lower) || (trimmed.Length > 0 && Common.Value.Contains(trimmed)))
        {
            return Task.FromResult(IdentityResult.Failed(new IdentityError { Code = CommonCode, Description = CommonCode }));
        }

        var name = user.UserName?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(name) && name.Length >= 3 && lower.Contains(name))
        {
            return Task.FromResult(IdentityResult.Failed(new IdentityError { Code = ContainsUsernameCode, Description = ContainsUsernameCode }));
        }

        return Task.FromResult(IdentityResult.Success);
    }

    private static HashSet<string> Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith("common-passwords.txt", StringComparison.Ordinal));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
        var set = new HashSet<string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            line = line.Trim().ToLowerInvariant();
            if (line.Length > 0)
            {
                set.Add(line);
            }
        }
        return set;
    }
}
