using Microsoft.AspNetCore.Identity;

namespace ResumeApp.Infrastructure.Identity;

public class AppUser : IdentityUser<Guid>
{
    /// <summary>True for a seeded one-time password; blocks every action except changing it.</summary>
    public bool MustChangePassword { get; set; }
}

public static class Roles
{
    public const string Admin = "Admin";
}
