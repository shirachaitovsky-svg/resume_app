using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ResumeApp.Infrastructure;

namespace ResumeApp.Tests;

/// <summary>
/// Story 4.1: the real <c>server/db/roles.sql</c> runs on a real Postgres. Migrations run as <c>migrator</c> (DATABASE_URL_MIGRATOR),
/// the application runs as <c>app</c> (DATABASE_URL), and the database itself refuses schema changes and any rewrite of the audit log.
/// </summary>
[Collection("env")]
public class DatabaseRolesTests(PostgresFixture pg) : IDisposable
{
    private const string NewPassword = "Brand-New-Pass-42#Lk";
    private readonly List<AuthApiFactory> _factories = [];

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
    }

    private static string ScriptPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "server", "db", "roles.sql");
            if (File.Exists(candidate)) return candidate;
            candidate = Path.Combine(dir.FullName, "db", "roles.sql");
            if (File.Exists(candidate) && dir.Name == "server") return candidate;
        }
        throw new FileNotFoundException("server/db/roles.sql was not found above " + AppContext.BaseDirectory);
    }

    private sealed record Roles(string Database, string Migrator, string App, string MigratorUrl, string AppUrl, Func<Task> RunScript);

    private async Task<Roles> NewRolesAsync()
    {
        var adminUrl = await pg.CreateDatabaseAsync();
        var database = new Uri(adminUrl).AbsolutePath.TrimStart('/');
        var id = Guid.NewGuid().ToString("N")[..10];
        string migrator = "mig_" + id, app = "app_" + id, migratorPassword = "Mp" + id, appPassword = "Ap" + id;
        var script = await File.ReadAllTextAsync(ScriptPath());
        var variables = new Dictionary<string, string>
        {
            ["migrator_name"] = migrator, ["app_name"] = app, ["migrator_password"] = migratorPassword, ["app_password"] = appPassword,
        };
        return new(database, migrator, app, pg.UrlFor(database, migrator, migratorPassword), pg.UrlFor(database, app, appPassword),
            () => pg.RunPsqlScriptAsync(database, script, variables));
    }

    private static NpgsqlConnection Connect(string url) => new(ConnectionStringResolver.FromUri(url));

    private static async Task<int> ExecAsync(string url, string sql)
    {
        await using var conn = Connect(url);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<T?> ScalarAsync<T>(string url, string sql)
    {
        await using var conn = Connect(url);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return (T?)await cmd.ExecuteScalarAsync();
    }

    private static async Task AssertDeniedAsync(string url, string sql)
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(url, sql));
        Assert.Equal("42501", ex.SqlState); // insufficient_privilege
    }

    [Fact]
    public async Task Migrations_run_as_migrator_and_the_app_runs_with_minimal_rights_and_cannot_change_the_audit_log()
    {
        var roles = await NewRolesAsync();
        await roles.RunScript(); // before the first migration: roles, schema ownership and default privileges
        var factory = new AuthApiFactory(roles.AppUrl, migratorDatabaseUrl: roles.MigratorUrl);
        _factories.Add(factory);
        await roles.RunScript(); // after the first migration: the audit_log restriction (the script is safe to repeat)

        // The migrator owns what the migrations created; the app owns nothing.
        Assert.Equal(roles.Migrator, await ScalarAsync<string>(roles.AppUrl, "SELECT tableowner FROM pg_tables WHERE tablename = 'audit_log'"));
        Assert.Equal(roles.Migrator, await ScalarAsync<string>(roles.AppUrl, "SELECT tableowner FROM pg_tables WHERE tablename = '__EFMigrationsHistory'"));
        Assert.Equal(0L, await ScalarAsync<long>(roles.AppUrl, $"SELECT count(*) FROM pg_tables WHERE tableowner = '{roles.App}'"));

        // The application works: the first admin was seeded under the app role, sign-in, a profile through fake Google, an approval with an audit row.
        var admin = factory.NewClient();
        await admin.EnrollAsync(AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword, NewPassword);
        var cohort = await (await admin.PostWithCsrfAsync("/admin/cohorts", new { name = "מחזור הרשאות" })).Content.ReadFromJsonAsync<JsonElement>();
        var created = await ParticipantSeeding.CreateProfileAsync(factory, cohort.GetProperty("id").GetString()!,
            new { name = "דנה", productName = "מוצר", productUrl = "https://example.com/app", phone = "0501234567" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var revisionId = await ParticipantSeeding.DbAsync(factory, d => d.ProfileRevisions.AsNoTracking().Select(r => r.Id).SingleAsync());
        Assert.Equal(HttpStatusCode.OK, (await admin.PostWithCsrfAsync($"/admin/submissions/{revisionId:D}/approve", null)).StatusCode);
        Assert.True(await ScalarAsync<long>(roles.AppUrl, "SELECT count(*) FROM audit_log") >= 1);

        // The app cannot change the schema.
        await AssertDeniedAsync(roles.AppUrl, "CREATE TABLE intruder (id int)");
        await AssertDeniedAsync(roles.AppUrl, "ALTER TABLE profiles ADD COLUMN intruder int");
        await AssertDeniedAsync(roles.AppUrl, "DROP TABLE rate_limit_counters");
        await AssertDeniedAsync(roles.AppUrl, "INSERT INTO \"__EFMigrationsHistory\" VALUES ('x', 'y')");

        // The audit log is append-only for the app.
        await AssertDeniedAsync(roles.AppUrl, "UPDATE audit_log SET action = 'tampered'");
        await AssertDeniedAsync(roles.AppUrl, "DELETE FROM audit_log");
        await AssertDeniedAsync(roles.AppUrl, "TRUNCATE audit_log");
        Assert.Equal(1, await ExecAsync(roles.AppUrl,
            "INSERT INTO audit_log (id, actor_id, at, action, entity_type, entity_id) VALUES (gen_random_uuid(), gen_random_uuid(), now(), 'probe', 'probe', gen_random_uuid())"));
        Assert.True(await ScalarAsync<long>(roles.AppUrl, "SELECT count(*) FROM audit_log WHERE action = 'probe'") == 1);

        // The grants the first-admin seeder rollback needs, and nothing beyond the documented deletes.
        await ExecAsync(roles.AppUrl, "DELETE FROM users WHERE false");
        await ExecAsync(roles.AppUrl, "DELETE FROM user_roles WHERE false");
        await AssertDeniedAsync(roles.AppUrl, "DELETE FROM cohorts");

        // Everything else the code does still works: updates, and deletes only where the code deletes.
        Assert.Equal(1, await ExecAsync(roles.AppUrl, "UPDATE profiles SET google_sub = google_sub"));
        await ExecAsync(roles.AppUrl, "DELETE FROM rate_limit_counters");
        await AssertDeniedAsync(roles.AppUrl, "DELETE FROM profiles");
        await AssertDeniedAsync(roles.AppUrl, "TRUNCATE rate_limit_counters");

        // A table the migrator creates later gets the default rights (and nothing more).
        await ExecAsync(roles.MigratorUrl, "CREATE TABLE later_table (id int primary key)");
        Assert.Equal(1, await ExecAsync(roles.AppUrl, "INSERT INTO later_table VALUES (1)"));
        await AssertDeniedAsync(roles.AppUrl, "DELETE FROM later_table");
    }

    [Fact]
    public async Task Without_the_migrator_url_the_single_development_user_still_migrates_and_runs()
    {
        var factory = await AuthApiFactory.CreateAsync(pg);
        _factories.Add(factory);

        var admin = factory.NewClient();
        await admin.EnrollAsync(AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword, NewPassword);

        Assert.Equal(1, await factory.CountAdminsAsync());
    }

    [Fact]
    public async Task A_migrator_connection_that_cannot_log_in_stops_startup()
    {
        var roles = await NewRolesAsync();
        await roles.RunScript();
        var wrongPassword = pg.UrlFor(roles.Database, roles.Migrator, "not-the-password");

        using var env = new EnvScope().Set("DATABASE_URL_MIGRATOR", null).Set("DATABASE_URL", null); // a failed construction is never disposed: restore the process environment
        var ex = Assert.Throws<InvalidOperationException>(() => new AuthApiFactory(roles.AppUrl, migratorDatabaseUrl: wrongPassword));
        Assert.Contains("DATABASE_URL_MIGRATOR", ex.Message);
        Assert.DoesNotContain("not-the-password", ex.Message);
        Assert.DoesNotContain(wrongPassword, ex.ToString());

        Assert.Equal(0L, await ScalarAsync<long>(roles.AppUrl, "SELECT count(*) FROM pg_tables WHERE tablename = 'audit_log'"));
    }
}
