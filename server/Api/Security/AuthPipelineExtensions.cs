using ResumeApp.Api.Resources;

namespace ResumeApp.Api.Security;

public static class AuthPipelineExtensions
{
    private static readonly string[] AllowedWhileMustChange =
        ["/auth/change-password", "/auth/logout", "/auth/me", "/auth/csrf", "/auth/login", "/healthz"];

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
        if (context.User.HasClaim(c => c.Type == AppUserClaimsPrincipalFactory.MustChangePasswordClaim)
            && !AllowedWhileMustChange.Any(p => context.Request.Path.Equals(p, StringComparison.OrdinalIgnoreCase)))
        {
            await Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: Strings.PasswordChangeRequired,
                extensions: new Dictionary<string, object?> { ["code"] = "password-change-required" }).ExecuteAsync(context);
            return;
        }
        await next();
    });
}
