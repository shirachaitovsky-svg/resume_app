using ResumeApp.Api.Resources;

namespace ResumeApp.Api.Security;

public static class AuthPipelineExtensions
{
    private static readonly string[] AllowedWhileMustChange =
        ["/auth/change-password", "/auth/logout", "/auth/me", "/auth/csrf", "/auth/login", "/healthz"];

    private static readonly string[] AllowedWhileTotpSetup =
        [.. AllowedWhileMustChange, "/auth/totp/setup", "/auth/totp/enable"];

    /// <summary>Authenticated and auth/admin responses are never cached (AD-9).</summary>
    public static IApplicationBuilder UseNoStoreForAuth(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        context.Response.OnStarting(() =>
        {
            var path = context.Request.Path;
            if (context.User.Identity?.IsAuthenticated == true
                || path.StartsWithSegments("/auth") || path.StartsWithSegments("/admin"))
            {
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers.Pragma = "no-cache";
            }
            return Task.CompletedTask;
        });
        await next();
    });

    /// <summary>A one-time password must be replaced before any other action.</summary>
    public static IApplicationBuilder UseMustChangePasswordGate(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        var path = context.Request.Path;
        if (context.User.HasClaim(c => c.Type == AppUserClaimsPrincipalFactory.MustChangePasswordClaim)
            && !AllowedWhileMustChange.Any(p => path.Equals(p, StringComparison.OrdinalIgnoreCase)))
        {
            await Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: Strings.PasswordChangeRequired,
                extensions: new Dictionary<string, object?> { ["code"] = "password-change-required" }).ExecuteAsync(context);
            return;
        }

        // Until TOTP enrolment is complete only the enrolment endpoints (and the always-open ones) work (FR-17).
        if (context.User.HasClaim(c => c.Type == AppUserClaimsPrincipalFactory.TotpSetupRequiredClaim)
            && !AllowedWhileTotpSetup.Any(p => path.Equals(p, StringComparison.OrdinalIgnoreCase)))
        {
            await Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: Strings.TotpSetupRequired,
                extensions: new Dictionary<string, object?> { ["code"] = "totp-setup-required" }).ExecuteAsync(context);
            return;
        }
        await next();
    });
}
