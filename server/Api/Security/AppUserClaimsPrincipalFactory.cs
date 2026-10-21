using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using ResumeApp.Infrastructure.Identity;

namespace ResumeApp.Api.Security;

public sealed class AppUserClaimsPrincipalFactory(
    UserManager<AppUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<AppUser, IdentityRole<Guid>>(userManager, roleManager, options)
{
    public const string MustChangePasswordClaim = "must_change_password";

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (user.MustChangePassword)
        {
            identity.AddClaim(new Claim(MustChangePasswordClaim, "true"));
        }
        return identity;
    }
}
