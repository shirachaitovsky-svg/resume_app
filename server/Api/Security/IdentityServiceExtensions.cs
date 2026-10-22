using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using ResumeApp.Infrastructure;
using ResumeApp.Infrastructure.Identity;

namespace ResumeApp.Api.Security;

public static class IdentityServiceExtensions
{
    public const string AdminPolicy = "Admin";
    public const string CookieName = "__Host-resume-admin";
    public const string ParticipantPolicy = "Participant";
    public const string ParticipantScheme = "Participant";
    public const string ParticipantCookieName = "__Host-resume-participant";
    public const string GoogleSignInPurpose = "google-signin";
    public const string ProfileIdClaim = "profile_id";
    public const string PurposeClaim = "purpose";
    public static readonly TimeSpan ParticipantSessionLifetime = TimeSpan.FromMinutes(60);
    public const string CsrfCookieName = "__Host-resume-csrf";
    public const string CsrfHeaderName = "X-CSRF-TOKEN";

    public static IServiceCollection AddAdminSecurity(this IServiceCollection services)
    {
        services.AddIdentity<AppUser, IdentityRole<Guid>>(o =>
            {
                o.Password.RequiredLength = 12;
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;

                o.Lockout.AllowedForNewUsers = true;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

                o.User.RequireUniqueEmail = false;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders()
            .AddPasswordValidator<CommonPasswordValidator>()
            .AddClaimsPrincipalFactory<AppUserClaimsPrincipalFactory>();

        services.ConfigureApplicationCookie(o =>
        {
            o.Cookie.Name = CookieName;
            o.Cookie.HttpOnly = true;
            o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.Path = "/";
            o.Cookie.IsEssential = true;
            o.ExpireTimeSpan = TimeSpan.FromHours(8);
            o.SlidingExpiration = true;
            // JSON API: never redirect.
            o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
            o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
        });

        // Story 2.2: a separate scheme for participants (AD-5); since story 6.4 its only session is the Google one. It is never the default, so it never authenticates admin endpoints,
        // and the admin cookie never satisfies ParticipantPolicy (which names this scheme only).
        services.AddAuthentication().AddCookie(ParticipantScheme, o =>
        {
            o.Cookie.Name = ParticipantCookieName;
            o.Cookie.HttpOnly = true;
            o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.Path = "/";
            o.Cookie.IsEssential = true;
            o.ExpireTimeSpan = ParticipantSessionLifetime;
            o.SlidingExpiration = false; // an absolute lifetime
            // Story 6.4: only the Google sign-in session exists. A cookie issued for any other purpose (the removed personal link) is not a session at all: 401.
            o.Events.OnValidatePrincipal = async ctx =>
            {
                if (ctx.Principal?.FindFirst(PurposeClaim)?.Value != GoogleSignInPurpose)
                {
                    ctx.RejectPrincipal();
                    await ctx.HttpContext.SignOutAsync(ParticipantScheme);
                }
            };
            o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
            o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
        });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(AdminPolicy, p => p.RequireAuthenticatedUser().RequireRole(Roles.Admin))
            .AddPolicy(ParticipantPolicy, p => p.AddAuthenticationSchemes(ParticipantScheme).RequireAuthenticatedUser()
                .RequireClaim(PurposeClaim, GoogleSignInPurpose).RequireClaim(ProfileIdClaim));

        services.AddAntiforgery(o =>
        {
            o.HeaderName = CsrfHeaderName;
            o.Cookie.Name = CsrfCookieName;
            o.Cookie.HttpOnly = true;
            o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.Path = "/";
        });

        // Keys in the database so sessions survive restarts and deploys (AD-21).
        services.AddDataProtection()
            .SetApplicationName("resume-app")
            .PersistKeysToDbContext<AppDbContext>();

        return services;
    }
}
