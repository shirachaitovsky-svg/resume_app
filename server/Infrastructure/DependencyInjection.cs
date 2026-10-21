using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ResumeApp.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Arbitrary constant key identifying the migration advisory lock.</summary>
    private const long MigrationLockKey = 7_261_021_001;

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connection = ConnectionStringResolver.Resolve(configuration);
        // Always registered (Identity and Data Protection depend on it); without a connection it is never opened
        // because migrations fail fast and only /healthz-like endpoints work.
        var effective = string.IsNullOrWhiteSpace(connection) ? "Host=localhost;Database=unconfigured" : connection;
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(effective));

        services.AddSingleton(new InfrastructureOptions(
            HasDatabase: !string.IsNullOrWhiteSpace(connection),
            MigrateOnStartup: !string.Equals(configuration["MIGRATE_ON_STARTUP"], "false", StringComparison.OrdinalIgnoreCase)));
        return services;
    }

    /// <summary>
    /// Runs pending EF Core migrations once, under a Postgres advisory lock so two instances never migrate in parallel.
    /// Fails startup with a clear message (no connection details) when the database is unreachable.
    /// </summary>
    public static async Task MigrateDatabaseAsync(this IHost app, CancellationToken ct = default)
    {
        var options = app.Services.GetRequiredService<InfrastructureOptions>();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Migrations");

        if (!options.MigrateOnStartup)
        {
            logger.LogInformation("Migrations skipped (MIGRATE_ON_STARTUP=false).");
            return;
        }

        if (!options.HasDatabase)
        {
            throw new InvalidOperationException(
                "No database configured. Set DATABASE_URL or ConnectionStrings__Default (or MIGRATE_ON_STARTUP=false).");
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        try
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            await connection.OpenAsync(ct);
            try
            {
                await using (var lockCmd = new NpgsqlCommand("SELECT pg_advisory_lock(@k)", connection))
                {
                    lockCmd.Parameters.AddWithValue("k", MigrationLockKey);
                    await lockCmd.ExecuteNonQueryAsync(ct);
                }

                try
                {
                    await db.Database.MigrateAsync(ct);
                    logger.LogInformation("Database migrations applied.");
                }
                finally
                {
                    try
                    {
                        await using var unlockCmd = new NpgsqlCommand("SELECT pg_advisory_unlock(@k)", connection);
                        unlockCmd.Parameters.AddWithValue("k", MigrationLockKey);
                        await unlockCmd.ExecuteNonQueryAsync(CancellationToken.None);
                    }
                    catch
                    {
                        // Lock is released when the session closes; never mask the original exception.
                    }
                }
            }
            finally
            {
                await connection.CloseAsync();
            }
        }
        catch (Exception ex) when (ex is NpgsqlException and not PostgresException or System.Net.Sockets.SocketException or TimeoutException)
        {
            // Message only: never log the connection string or credentials.
            throw new InvalidOperationException(
                "Database migration failed at startup: could not connect to or use Postgres. Check DATABASE_URL / ConnectionStrings__Default.",
                ex);
        }
    }
}

public sealed record InfrastructureOptions(bool HasDatabase, bool MigrateOnStartup);



