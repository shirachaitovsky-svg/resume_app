using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using ResumeApp.Api.Resources;
using ResumeApp.Api.Security;
using ResumeApp.Infrastructure.Identity;

namespace ResumeApp.Api.Endpoints;

public static class AuthEndpoints
{
    private const int MaxPasswordLength = 256;

    public sealed record LoginRequest(string? Username, string? Password);
    public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

    public static IEndpointRouteBuilder MapAuth(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth");

        group.MapGet("/csrf", (HttpContext ctx, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(ctx);
            return Results.Ok(new { token = tokens.RequestToken, headerName = IdentityServiceExtensions.CsrfHeaderName });
        }).AllowAnonymous();

        group.MapPost("/login", async (LoginRequest body, UserManager<AppUser> users, SignInManager<AppUser> signIn,
            IPasswordHasher<AppUser> hasher) =>
        {
            if (string.IsNullOrWhiteSpace(body.Username) || string.IsNullOrEmpty(body.Password) || body.Password.Length > MaxPasswordLength || body.Username.Length > MaxPasswordLength)
            {
                return InvalidCredentials();
            }

            var user = await users.FindByNameAsync(body.Username.Trim());
            if (user is null)
            {
                // Spend comparable time as a real check so existence is not revealed by timing.
                hasher.HashPassword(new AppUser(), body.Password);
                return InvalidCredentials();
            }

            var result = await signIn.CheckPasswordSignInAsync(user, body.Password, lockoutOnFailure: true);
            if (!result.Succeeded)
            {
                if (result.IsLockedOut)
                {
                    hasher.HashPassword(new AppUser(), body.Password); // same cost as a real check
                }

                // Wrong password, locked out, or not allowed: all look identical.
                return InvalidCredentials();
            }

            await signIn.SignInAsync(user, isPersistent: false);
            return Results.Ok(new { username = user.UserName, mustChangePassword = user.MustChangePassword });
        }).AllowAnonymous();

        group.MapPost("/logout", async (SignInManager<AppUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.NoContent();
        });

        group.MapPost("/change-password", async (ChangePasswordRequest body, ClaimsPrincipal principal,
            UserManager<AppUser> users, SignInManager<AppUser> signIn) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrEmpty(body.CurrentPassword)) errors["currentPassword"] = [Strings.FieldRequired];
            if (string.IsNullOrEmpty(body.NewPassword)) errors["newPassword"] = [Strings.FieldRequired];
            if (errors.Count > 0) return Validation(errors);

            if (body.CurrentPassword!.Length > MaxPasswordLength) errors["currentPassword"] = [Strings.PasswordInvalid];
            if (body.NewPassword!.Length > MaxPasswordLength) errors["newPassword"] = [Strings.PasswordInvalid];
            if (errors.Count > 0) return Validation(errors);

            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();
            if (await users.IsLockedOutAsync(user)) return InvalidCredentials();

            if (body.CurrentPassword == body.NewPassword)
            {
                return Validation(new() { ["newPassword"] = [Strings.PasswordSameAsCurrent] });
            }

            var result = await users.ChangePasswordAsync(user, body.CurrentPassword!, body.NewPassword!);
            if (!result.Succeeded)
            {
                if (result.Errors.Any(e => e.Code == "PasswordMismatch"))
                {
                    await users.AccessFailedAsync(user); // counts toward lockout
                    if (await users.IsLockedOutAsync(user)) return InvalidCredentials();
                }

                var mapped = new Dictionary<string, List<string>>();
                foreach (var e in result.Errors)
                {
                    var (field, message) = e.Code switch
                    {
                        "PasswordMismatch" => ("currentPassword", Strings.CurrentPasswordWrong),
                        "PasswordTooShort" => ("newPassword", Strings.PasswordTooShort),
                        CommonPasswordValidator.CommonCode => ("newPassword", Strings.PasswordCommon),
                        CommonPasswordValidator.ContainsUsernameCode => ("newPassword", Strings.PasswordContainsUsername),
                        _ => ("newPassword", Strings.PasswordInvalid),
                    };
                    if (!mapped.TryGetValue(field, out var list)) mapped[field] = list = [];
                    if (!list.Contains(message)) list.Add(message);
                }
                return Validation(mapped.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()));
            }

            user.MustChangePassword = false;
            await users.ResetAccessFailedCountAsync(user);
            var update = await users.UpdateAsync(user);
            if (!update.Succeeded)
            {
                return Results.Problem(statusCode: StatusCodes.Status500InternalServerError);
            }

            await signIn.RefreshSignInAsync(user); // new cookie without the must-change claim
            return Results.NoContent();
        });

        group.MapGet("/me", async (ClaimsPrincipal principal, UserManager<AppUser> users) =>
        {
            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();
            var roles = await users.GetRolesAsync(user);
            return Results.Ok(new { username = user.UserName, roles, mustChangePassword = user.MustChangePassword });
        });

        return app;
    }

    private static IResult InvalidCredentials()
        => Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: Strings.InvalidCredentialsTitle);

    private static IResult Validation(Dictionary<string, string[]> errors)
        => Results.ValidationProblem(errors, title: Strings.ValidationTitle);
}
