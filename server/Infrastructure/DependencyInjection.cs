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

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ResumeApp.Features.Cohorts.ICohortRepository, CohortRepository>();
        services.AddScoped<ResumeApp.Features.Cohorts.CohortService>();
        services.AddScoped<ResumeApp.Features.Profiles.IProfileRepository, ProfileRepository>();
        services.AddScoped<ResumeApp.Features.Profiles.ISubmissionReviewRepository, SubmissionReviewRepository>();
        services.AddScoped<ResumeApp.Features.Profiles.IProfileAdminRepository, ProfileAdminRepository>();
        services.AddScoped<ResumeApp.Features.Profiles.ProfileAdminService>();
        services.AddSingleton<ResumeApp.Features.Profiles.IPhotoProcessor, SkiaPhotoProcessor>();
        services.AddScoped<ResumeApp.Features.Profiles.IPublicProfileReader, PublicProfileReader>();
        services.AddScoped<ResumeApp.Features.Profiles.PublicProfileQuery>();
        services.AddScoped<ResumeApp.Features.Profiles.ProfileService>();
        services.AddScoped<ResumeApp.Features.Profiles.SubmissionReviewService>();
        services.AddSingleton(new InfrastructureOptions(
            HasDatabase: !string.IsNullOrWhiteSpace(connection),
            MigrateOnStartup: !string.Equals(configuration["MIGRATE_ON_STARTUP"], "false", StringComparison.OrdinalIgnoreCase)));
        return services;
    }

    /// <summary>Turnstile verification (fail closed) and database-backed rate limiting for the public forms (AD-12).</summary>
    public static IServiceCollection AddPublicFormProtection(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var turnstile = TurnstileOptions.Resolve(configuration, environment);
        services.AddSingleton(turnstile);
        services.AddHttpClient<ResumeApp.Features.PublicForms.ITurnstileVerifier, CloudflareTurnstileVerifier>(c => c.Timeout = CloudflareTurnstileVerifier.Timeout);

        var defaults = ResumeApp.Features.PublicForms.RateLimitOptions.Defaults;
        services.AddSingleton(new ResumeApp.Features.PublicForms.RateLimitOptions(
            PositiveInt(configuration, "RATE_LIMIT_PROFILE_IP", defaults.ProfileSubmitPerIp),
            PositiveInt(configuration, "RATE_LIMIT_PROFILE_EMAIL", defaults.ProfileSubmitPerEmail),
            PositiveInt(configuration, "RATE_LIMIT_INVITE_IP", defaults.InviteCheckPerIp),
            PositiveInt(configuration, "RATE_LIMIT_PHOTO_IP", defaults.PhotoPerIp),
            PositiveInt(configuration, "RATE_LIMIT_GALLERY_IP", defaults.GalleryPerIp),
            TimeSpan.FromMinutes(WindowMinutes(configuration, (int)defaults.Window.TotalMinutes)),
            PositiveInt(configuration, "RATE_LIMIT_EDIT_LINK_IP", defaults.EditLinkPerIp),
            PositiveInt(configuration, "RATE_LIMIT_EDIT_LINK_EMAIL", defaults.EditLinkPerEmail),
            PositiveInt(configuration, "RATE_LIMIT_EDIT_SESSION_IP", defaults.EditSessionPerIp),
            PositiveInt(configuration, "RATE_LIMIT_CONTACT_REQUEST_IP", defaults.ContactRequestPerIp),
            PositiveInt(configuration, "RATE_LIMIT_CONTACT_REQUEST_EMAIL", defaults.ContactRequestPerEmail),
            PositiveInt(configuration, "RATE_LIMIT_CONTACT_REQUEST_PROFILE", defaults.ContactRequestPerProfile),
            PositiveInt(configuration, "RATE_LIMIT_CONTACT_VERIFY_IP", defaults.ContactVerifyPerIp),
            PositiveInt(configuration, "RATE_LIMIT_CONTACT_DECISION_IP", defaults.ContactDecisionPerIp)));

        // The pepper keys the hash of IPs and emails; the origin secret is already a deployment secret.
        var pepper = System.Text.Encoding.UTF8.GetBytes("rate-limit|" + configuration["ORIGIN_SECRET"]);
        services.AddSingleton(new RateLimitPepper(pepper));
        services.AddScoped<ResumeApp.Features.PublicForms.IRateLimiter, RateLimiter>();
        return services;
    }

    /// <summary>
    /// Email: the provider is chosen by EMAIL_PROVIDER (the single swap point, <c>IEmailSender</c>), plus the outbox and token services
    /// and the background worker (not registered without a database, or when EMAIL_WORKER=false).
    /// </summary>
    public static IServiceCollection AddEmail(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var options = EmailOptions.Resolve(configuration, environment);
        services.AddSingleton(options);
        if (options.Provider == EmailProvider.Resend)
            services.AddHttpClient<ResumeApp.Features.Email.IEmailSender, ResendEmailSender>(c => c.Timeout = TimeSpan.FromSeconds(30));
        else
            services.AddSingleton<ResumeApp.Features.Email.IEmailSender, SmtpEmailSender>();

        services.AddSingleton(new ResumeApp.Features.Email.EmailOutboxSettings(options.PublicBaseUrl, AdminRecipients: options.AdminRecipients));
        services.AddScoped<ResumeApp.Features.Email.IEmailOutbox, EmailOutboxRepository>();
        services.AddScoped<ResumeApp.Features.Email.IPersonalTokenIssuer, PersonalTokenIssuer>();
        services.AddScoped<ResumeApp.Features.Email.EditLinkService>();
        services.AddScoped<ResumeApp.Features.Contacts.IContactRequestRepository, ContactRequestRepository>();
        services.AddScoped<ResumeApp.Features.Contacts.ContactRequestService>();
        services.AddScoped<ResumeApp.Features.Contacts.IContactRequestQueueRepository, ContactRequestQueueRepository>();
        services.AddScoped<ResumeApp.Features.Contacts.ContactRequestQueueService>();
        services.AddScoped<ResumeApp.Features.Email.EmailOutboxProcessor>();

        var hasDatabase = !string.IsNullOrWhiteSpace(ConnectionStringResolver.Resolve(configuration));
        var workerEnabled = EmailOptions.ParseWorkerFlag(configuration["EMAIL_WORKER"]); // always parsed, so a typo fails startup
        if (hasDatabase && workerEnabled) services.AddHostedService<EmailOutboxWorker>();
        return services;
    }

    /// <summary>Windows are anchored at UTC midnight, so the length must divide a day (1440 minutes) evenly.</summary>
    private static int WindowMinutes(IConfiguration configuration, int fallback)
    {
        var minutes = PositiveInt(configuration, "RATE_LIMIT_WINDOW_MINUTES", fallback);
        if (minutes > 1440 || 1440 % minutes != 0)
            throw new InvalidOperationException("RATE_LIMIT_WINDOW_MINUTES must be a positive divisor of 1440 (for example 1, 5, 15, 30, 60, 120, 1440).");
        return minutes;
    }

    private static int PositiveInt(IConfiguration configuration, string key, int fallback)
    {
        var raw = configuration[key];
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        if (!int.TryParse(raw, out var value) || value <= 0)
            throw new InvalidOperationException($"{key} must be a positive integer.");
        return value;
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



