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
    private const int MaxCodeLength = 64;
    private const int RecoveryCodeCount = 10;
    private const string TotpIssuer = "resume-app";

    public sealed record LoginRequest(string? Username, string? Password, string? Code = null, string? RecoveryCode = null);
    public sealed record EnableTotpRequest(string? Code);
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
            if (string.IsNullOrWhiteSpace(body.Username) || string.IsNullOrEmpty(body.Password) || body.Password.Length > MaxPasswordLength || body.Username.Length > MaxPasswordLength
                || (body.Code?.Length ?? 0) > MaxCodeLength || (body.RecoveryCode?.Length ?? 0) > MaxCodeLength)
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

            if (await users.IsLockedOutAsync(user))
            {
                hasher.HashPassword(new AppUser(), body.Password); // same cost as a real check
                return InvalidCredentials();
            }

            // Checked manually (not CheckPasswordSignInAsync): a correct password must not reset the failure
            // counter before the second factor is verified, or wrong codes would never lock the account.
            if (!await users.CheckPasswordAsync(user, body.Password))
            {
                await users.AccessFailedAsync(user);
                return InvalidCredentials();
            }

            if (user.TwoFactorEnabled)
            {
                var secondFactorOk = false;
                if (!string.IsNullOrWhiteSpace(body.Code))
                {
                    var code = body.Code.Replace(" ", string.Empty).Replace("-", string.Empty);
                    secondFactorOk = await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, code);
                }
                else if (!string.IsNullOrWhiteSpace(body.RecoveryCode))
                {
                    var recovery = body.RecoveryCode.Replace(" ", string.Empty);
                    secondFactorOk = (await users.RedeemTwoFactorRecoveryCodeAsync(user, recovery)).Succeeded;
                }

                if (!secondFactorOk)
                {
                    await users.AccessFailedAsync(user);
                    return InvalidCredentials();
                }
            }

            await users.ResetAccessFailedCountAsync(user);
            await signIn.SignInAsync(user, isPersistent: false);
            return Results.Ok(new
            {
                username = user.UserName,
                mustChangePassword = user.MustChangePassword,
                totpSetupRequired = !user.TwoFactorEnabled,
            });
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

        group.MapPost("/totp/setup", async (ClaimsPrincipal principal, UserManager<AppUser> users, SignInManager<AppUser> signIn) =>
        {
            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();
            if (user.TwoFactorEnabled) return Conflict(Strings.TotpAlreadyEnabled);
            if (await users.IsLockedOutAsync(user)) return InvalidCredentials();

            var key = await users.GetAuthenticatorKeyAsync(user);
            if (string.IsNullOrEmpty(key))
            {
                var reset = await users.ResetAuthenticatorKeyAsync(user);
                if (!reset.Succeeded) return Results.Problem(statusCode: StatusCodes.Status500InternalServerError);
                key = await users.GetAuthenticatorKeyAsync(user);
                await signIn.RefreshSignInAsync(user); // the key reset rotates the security stamp
            }

            var label = Uri.EscapeDataString(user.UserName ?? string.Empty);
            var uri = $"otpauth://totp/{TotpIssuer}:{label}?secret={key}&issuer={TotpIssuer}&digits=6";
            return Results.Ok(new { key, uri });
        });

        group.MapPost("/totp/enable", async (EnableTotpRequest body, ClaimsPrincipal principal,
            UserManager<AppUser> users, SignInManager<AppUser> signIn) =>
        {
            if (string.IsNullOrWhiteSpace(body.Code) || body.Code.Length > MaxCodeLength)
            {
                return Validation(new() { ["code"] = [Strings.FieldRequired] });
            }

            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();
            if (user.TwoFactorEnabled) return Conflict(Strings.TotpAlreadyEnabled);
            if (await users.IsLockedOutAsync(user)) return InvalidCredentials();

            if (string.IsNullOrEmpty(await users.GetAuthenticatorKeyAsync(user)))
            {
                return Validation(new() { ["code"] = [Strings.TotpNotStarted] });
            }

            var code = body.Code.Replace(" ", string.Empty).Replace("-", string.Empty);
            if (!await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, code))
            {
                await users.AccessFailedAsync(user); // counts toward lockout
                if (await users.IsLockedOutAsync(user)) return InvalidCredentials();
                return Validation(new() { ["code"] = [Strings.TotpCodeInvalid] });
            }

            // Recovery codes first: TOTP must never end up enabled without codes having been issued.
            var recoveryCodes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount))?.ToArray();
            if (recoveryCodes is null || recoveryCodes.Length != RecoveryCodeCount)
            {
                return Results.Problem(statusCode: StatusCodes.Status500InternalServerError);
            }

            var enabled = await users.SetTwoFactorEnabledAsync(user, true);
            if (!enabled.Succeeded)
            {
                await users.SetTwoFactorEnabledAsync(user, false);
                return Results.Problem(statusCode: StatusCodes.Status500InternalServerError);
            }

            await users.ResetAccessFailedCountAsync(user);
            await signIn.RefreshSignInAsync(user); // new cookie without the TOTP-setup claim
            return Results.Ok(new { recoveryCodes }); // shown once, never stored in clear
        });

        group.MapGet("/me", async (ClaimsPrincipal principal, UserManager<AppUser> users) =>
        {
            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();
            var roles = await users.GetRolesAsync(user);
            return Results.Ok(new { username = user.UserName, roles, mustChangePassword = user.MustChangePassword, totpSetupRequired = !user.TwoFactorEnabled });
        });

        return app;
    }

    private static IResult InvalidCredentials()
        => Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: Strings.InvalidCredentialsTitle);

    private static IResult Conflict(string title)
        => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: title);

    private static IResult Validation(Dictionary<string, string[]> errors)
        => Results.ValidationProblem(errors, title: Strings.ValidationTitle);
}
