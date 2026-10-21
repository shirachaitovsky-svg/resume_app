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
                await SeedLockedAsync(scope.ServiceProvider, logger, username, password);
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

    private static async Task SeedLockedAsync(IServiceProvider services, ILogger logger, string? username, string? password)
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

        if ((await users.GetUsersInRoleAsync(Roles.Admin)).Count > 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            logger.LogWarning("No admin exists and ADMIN_USERNAME / ADMIN_INITIAL_PASSWORD are not both set; skipping admin seed.");
            return;
        }

        var user = new AppUser { Id = Guid.NewGuid(), UserName = username.Trim(), MustChangePassword = true, LockoutEnabled = true };
        var create = await users.CreateAsync(user, password);
        if (!create.Succeeded)
        {
            // Unique violation (a racing instance) means an admin may exist now: re-check before warning.
            if ((await users.GetUsersInRoleAsync(Roles.Admin)).Count > 0)
            {
                return;
            }

            // Codes only: descriptions could echo input.
            logger.LogWarning("Admin seed failed: {Errors}", Codes(create));
            return;
        }

        var add = await users.AddToRoleAsync(user, Roles.Admin);
        if (!add.Succeeded)
        {
            await users.DeleteAsync(user); // never leave a role-less user behind
            logger.LogError("Admin seed failed adding role, user removed: {Errors}", Codes(add));
            return;
        }

        logger.LogInformation("Initial admin created; password change required at first sign-in.");
    }

    private static string Codes(IdentityResult result) => string.Join(",", result.Errors.Select(e => e.Code));
}
