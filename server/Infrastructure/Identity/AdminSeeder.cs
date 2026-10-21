using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ResumeApp.Infrastructure.Identity;

public static class AdminSeeder
{
    /// <summary>Advisory lock key for seeding (distinct from the migration key).</summary>
    private const long SeedLockKey = 7_261_021_002;

    /// <summary>
    /// Creates the first admin from ADMIN_USERNAME / ADMIN_INITIAL_PASSWORD, only when no admin exists (AD-20),
    /// under a Postgres advisory lock. The password is one-time: the user must change it at first sign-in.
    /// Never logs the password.
    /// </summary>
    public static async Task SeedAdminAsync(this IHost app, CancellationToken ct = default)
    {
        var options = app.Services.GetRequiredService<InfrastructureOptions>();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("AdminSeed");
        if (!options.HasDatabase)
        {
            logger.LogInformation("Admin seed skipped: no database configured.");
            return;
        }

        var configuration = app.Services.GetRequiredService<IConfiguration>();
        var username = configuration["ADMIN_USERNAME"];
        var password = configuration["ADMIN_INITIAL_PASSWORD"];
        var username2 = configuration["ADMIN_USERNAME_2"];
        var password2 = configuration["ADMIN_INITIAL_PASSWORD_2"];

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await connection.OpenAsync(ct);
        try
        {
            await using (var lockCmd = new NpgsqlCommand("SELECT pg_advisory_lock(@k)", connection))
            {
                lockCmd.Parameters.AddWithValue("k", SeedLockKey);
                await lockCmd.ExecuteNonQueryAsync(ct);
            }

            try
            {
                await SeedLockedAsync(scope.ServiceProvider, logger, username, password, username2, password2);
            }
            finally
            {
                try
                {
                    await using var unlockCmd = new NpgsqlCommand("SELECT pg_advisory_unlock(@k)", connection);
                    unlockCmd.Parameters.AddWithValue("k", SeedLockKey);
                    await unlockCmd.ExecuteNonQueryAsync(CancellationToken.None);
                }
                catch
                {
                    // Released when the session closes; never mask the original exception.
                }
            }
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    private static async Task SeedLockedAsync(IServiceProvider services, ILogger logger,
        string? username, string? password, string? username2, string? password2)
    {
        var users = services.GetRequiredService<UserManager<AppUser>>();
        var roles = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        if (!await roles.RoleExistsAsync(Roles.Admin))
        {
            var role = await roles.CreateAsync(new IdentityRole<Guid>(Roles.Admin));
            if (!role.Succeeded && !await roles.RoleExistsAsync(Roles.Admin)) // tolerate a concurrent create
            {
                logger.LogError("Admin role creation failed: {Errors}", Codes(role));
                return;
            }
        }

        // First admin: only when no admin exists at all (AD-20).
        if ((await users.GetUsersInRoleAsync(Roles.Admin)).Count == 0)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            {
                logger.LogWarning("No admin exists and ADMIN_USERNAME / ADMIN_INITIAL_PASSWORD are not both set; skipping admin seed.");
            }
            else
            {
                await CreateAdminAsync(users, logger, username.Trim(), password, "Initial admin");
            }
        }

        // Second admin: created once, by username, so locking one admin out does not lock everyone out.
        if (!string.IsNullOrWhiteSpace(username2) && !string.IsNullOrEmpty(password2)
            && await users.FindByNameAsync(username2.Trim()) is null)
        {
            await CreateAdminAsync(users, logger, username2.Trim(), password2, "Second admin");
        }

        var count = (await users.GetUsersInRoleAsync(Roles.Admin)).Count;
        if (count < 2)
        {
            logger.LogWarning("Only {Count} admin(s) exist; at least two are required (set ADMIN_USERNAME_2 / ADMIN_INITIAL_PASSWORD_2).", count);
        }
    }

    private static async Task CreateAdminAsync(UserManager<AppUser> users, ILogger logger, string username, string password, string label)
    {
        var user = new AppUser { Id = Guid.NewGuid(), UserName = username, MustChangePassword = true, LockoutEnabled = true };
        var create = await users.CreateAsync(user, password);
        if (!create.Succeeded)
        {
            // Unique violation (a racing instance) means the user may exist now: re-check before warning.
            if (await users.FindByNameAsync(username) is not null)
            {
                return;
            }

            // Codes only: descriptions could echo input.
            logger.LogWarning("{Label} seed failed: {Errors}", label, Codes(create));
            return;
        }

        var add = await users.AddToRoleAsync(user, Roles.Admin);
        if (!add.Succeeded)
        {
            await users.DeleteAsync(user); // never leave a role-less user behind
            logger.LogError("{Label} seed failed adding role, user removed: {Errors}", label, Codes(add));
            return;
        }

        logger.LogInformation("{Label} created; password change required at first sign-in.", label);
    }

    private static string Codes(IdentityResult result) => string.Join(",", result.Errors.Select(e => e.Code));
}
