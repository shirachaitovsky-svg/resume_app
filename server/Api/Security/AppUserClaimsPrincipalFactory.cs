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
    public const string TotpSetupRequiredClaim = "totp_setup_required";

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (user.MustChangePassword)
        {
            identity.AddClaim(new Claim(MustChangePasswordClaim, "true"));
        }
        if (!user.TwoFactorEnabled)
        {
            identity.AddClaim(new Claim(TotpSetupRequiredClaim, "true"));
        }
        return identity;
    }
}
