using Microsoft.AspNetCore.Builder;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ResumeApp.Infrastructure.Identity;
using Testcontainers.PostgreSql;

namespace ResumeApp.Tests;

/// <summary>One real Postgres container for the whole "env" collection; each factory gets its own database.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder().WithImage("postgres:16").Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public async Task<string> CreateDatabaseAsync()
    {
        var name = "t" + Guid.NewGuid().ToString("N");
        await using var conn = new NpgsqlConnection(_container.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"CREATE DATABASE {name}", conn);
        await cmd.ExecuteNonQueryAsync();
        var builder = new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name };
        return $"postgres://{builder.Username}:{builder.Password}@{builder.Host}:{builder.Port}/{name}";
    }
}

[CollectionDefinition("env")]
public sealed class EnvCollection : ICollectionFixture<PostgresFixture>;

public sealed class AuthApiFactory : WebApplicationFactory<Program>
{
    public const string Secret = "test-secret-value";
    public const string AdminUser = "admin";
    public const string InitialPassword = "Initial-Pass-9x7!Zq";
    public const string InitialPassword2 = "Second-Pass-4k2!Wm";
    /// <summary>The Development default of PUBLIC_BASE_URL (the factory leaves the variable unset).</summary>
    public const string PublicBaseUrl = "http://localhost:4200";

    private static readonly object EnvLock = new();
    private static int _live;
    private bool _released;

    public string DatabaseUrl { get; }

    /// <summary>Generous defaults so unrelated tests never trip the limiter; tests of the limiter pass their own.</summary>
    public static readonly IReadOnlyDictionary<string, string> HighLimits = new Dictionary<string, string>
    {
        ["RATE_LIMIT_PROFILE_IP"] = "100000", ["RATE_LIMIT_PROFILE_EMAIL"] = "100000", ["RATE_LIMIT_INVITE_IP"] = "100000", ["RATE_LIMIT_PHOTO_IP"] = "100000", ["RATE_LIMIT_GALLERY_IP"] = "100000", ["RATE_LIMIT_EDIT_LINK_IP"] = "100000", ["RATE_LIMIT_EDIT_LINK_EMAIL"] = "100000",
    };

    public FakeTurnstileVerifier Turnstile { get; } = new();
    public FakeEmailSender Email { get; } = new();

    public AuthApiFactory(string databaseUrl, string adminUser = AdminUser, string? allowedOrigins = null, string? adminUser2 = null, IReadOnlyDictionary<string, string>? limits = null, bool emailWorker = false)
    {
        DatabaseUrl = databaseUrl;
        // The pristine environment was snapshotted at assembly load (see EnvSnapshot), never from whatever a test set before this call.
        lock (EnvLock) _live++;

        // Minimal-hosting reads configuration before test overrides apply, so use environment variables.
        Environment.SetEnvironmentVariable("ORIGIN_SECRET", Secret);
        Environment.SetEnvironmentVariable("MIGRATE_ON_STARTUP", "true");
        Environment.SetEnvironmentVariable("DATABASE_URL", databaseUrl);
        Environment.SetEnvironmentVariable("ADMIN_USERNAME", adminUser);
        Environment.SetEnvironmentVariable("ADMIN_INITIAL_PASSWORD", InitialPassword);
        Environment.SetEnvironmentVariable("ADMIN_USERNAME_2", adminUser2);
        Environment.SetEnvironmentVariable("ADMIN_INITIAL_PASSWORD_2", adminUser2 is null ? null : InitialPassword2);
        Environment.SetEnvironmentVariable("ALLOWED_ORIGINS", allowedOrigins);
        Environment.SetEnvironmentVariable("EMAIL_WORKER", emailWorker ? "true" : "false");
        Environment.SetEnvironmentVariable("EMAIL_PROVIDER", null);
        foreach (var (name, value) in limits ?? HighLimits) Environment.SetEnvironmentVariable(name, value);
        if (limits is not null)
            foreach (var (name, value) in HighLimits) if (!limits.ContainsKey(name)) Environment.SetEnvironmentVariable(name, value);
        if (limits is null || !limits.ContainsKey("RATE_LIMIT_WINDOW_MINUTES")) Environment.SetEnvironmentVariable("RATE_LIMIT_WINDOW_MINUTES", null);
        try
        {
            _ = Server; // build the host now, while the variables are set
        }
        catch
        {
            // A failed construction is never disposed by the caller: release the environment here.
            ReleaseEnvironment();
            throw;
        }
    }

    private void ReleaseEnvironment()
    {
        lock (EnvLock)
        {
            if (_released) return;
            _released = true;
            if (--_live == 0)
            {
                foreach (var (name, value) in EnvSnapshot.Pristine) Environment.SetEnvironmentVariable(name, value);
            }
        }
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ResumeApp.Features.PublicForms.ITurnstileVerifier>();
            services.AddSingleton<ResumeApp.Features.PublicForms.ITurnstileVerifier>(Turnstile);
            services.RemoveAll<ResumeApp.Features.Email.IEmailSender>();
            services.AddSingleton<ResumeApp.Features.Email.IEmailSender>(Email);
            services.AddTransient<Microsoft.AspNetCore.Hosting.IStartupFilter, DefaultRemoteIpFilter>();
        });

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        // Each factory has its own database and pool; release them so the shared container never hits max_connections.
        NpgsqlConnection.ClearAllPools();
        ReleaseEnvironment();
    }

    public static async Task<AuthApiFactory> CreateAsync(PostgresFixture pg, string adminUser = AdminUser, string? allowedOrigins = null, string? adminUser2 = null, IReadOnlyDictionary<string, string>? limits = null, bool emailWorker = false)
        => new(await pg.CreateDatabaseAsync(), adminUser, allowedOrigins, adminUser2, limits, emailWorker);

    public async Task AddUserAsync(string username, string password, bool admin, bool mustChange = false)
    {
        await using var scope = Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser { Id = Guid.NewGuid(), UserName = username, MustChangePassword = mustChange };
        var created = await users.CreateAsync(user, password);
        Assert.True(created.Succeeded, string.Join(",", created.Errors.Select(e => e.Code)));
        if (admin) await users.AddToRoleAsync(user, Roles.Admin);
    }

    public async Task<int> CountAdminsAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        return (await users.GetUsersInRoleAsync(Roles.Admin)).Count;
    }

    public ApiClient NewClient(bool handleCookies = true) => new(this, handleCookies);
}

/// <summary>The process environment as it was when the test assembly loaded, for the variables the factory manages.</summary>
public static class EnvSnapshot
{
    public static readonly string[] Names =
    [
        "ORIGIN_SECRET", "MIGRATE_ON_STARTUP", "DATABASE_URL", "ADMIN_USERNAME", "ADMIN_INITIAL_PASSWORD", "ADMIN_USERNAME_2", "ADMIN_INITIAL_PASSWORD_2",
        "ALLOWED_ORIGINS", "RATE_LIMIT_PROFILE_IP", "RATE_LIMIT_PROFILE_EMAIL", "RATE_LIMIT_INVITE_IP", "RATE_LIMIT_PHOTO_IP", "RATE_LIMIT_GALLERY_IP",
        "RATE_LIMIT_WINDOW_MINUTES", "RATE_LIMIT_EDIT_LINK_IP", "RATE_LIMIT_EDIT_LINK_EMAIL", "EMAIL_WORKER", "EMAIL_PROVIDER", "PUBLIC_BASE_URL",
        "EMAIL_FROM", "SMTP_HOST", "SMTP_PORT", "SMTP_USER", "SMTP_PASSWORD", "SMTP_SECURE", "RESEND_API_KEY",
    ];

    public static Dictionary<string, string?> Pristine { get; private set; } = new();

    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Capture() => Pristine = Names.ToDictionary(n => n, Environment.GetEnvironmentVariable);
}

/// <summary>Sets environment variables for the duration of a test and restores the previous values, even when the test throws.</summary>
public sealed class EnvScope : IDisposable
{
    private readonly Dictionary<string, string?> _previous = new();

    public EnvScope Set(string name, string? value)
    {
        _previous.TryAdd(name, Environment.GetEnvironmentVariable(name));
        Environment.SetEnvironmentVariable(name, value);
        return this;
    }

    public void Dispose()
    {
        foreach (var (name, value) in _previous) Environment.SetEnvironmentVariable(name, value);
    }
}

/// <summary>TestServer has no remote address; give requests one (before forwarded-header handling) unless X-Forwarded-For overrides it.</summary>
public sealed class DefaultRemoteIpFilter : Microsoft.AspNetCore.Hosting.IStartupFilter
{
    public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next)
        => app =>
        {
            app.Use((ctx, nxt) =>
            {
                ctx.Connection.RemoteIpAddress ??= IPAddress.Parse("198.51.100.250");
                return nxt();
            });
            next(app);
        };
}

/// <summary>Replaces Cloudflare in tests: only <see cref="ValidToken"/> passes, and it can simulate an outage.</summary>
public sealed class FakeTurnstileVerifier : ResumeApp.Features.PublicForms.ITurnstileVerifier
{
    public const string ValidToken = "valid-turnstile-token";
    private int _calls;

    public bool Unavailable { get; set; }
    public int Calls => _calls;

    public Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken ct)
    {
        Interlocked.Increment(ref _calls);
        return Task.FromResult(!Unavailable && token == ValidToken);
    }
}

/// <summary>HTTPS test client (the cookie is Secure) that adds the origin secret, Origin and the anti-forgery token.</summary>
public sealed class ApiClient
{
    public const string Origin = "https://localhost";

    public HttpClient Http { get; }

    public ApiClient(AuthApiFactory factory, bool handleCookies)
    {
        Http = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(Origin),
            HandleCookies = handleCookies,
            AllowAutoRedirect = false,
        });
        Http.DefaultRequestHeaders.Add("x-origin-secret", AuthApiFactory.Secret);
        Http.DefaultRequestHeaders.Add("X-Turnstile-Token", FakeTurnstileVerifier.ValidToken);
    }

    public async Task<string> GetCsrfAsync()
    {
        var response = await Http.GetAsync("/auth/csrf");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("token").GetString()!;
    }

    public async Task<HttpResponseMessage> PostAsync(string path, object? body, string? csrf = null, string? origin = Origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body ?? new { }) };
        if (origin is not null) request.Headers.Add("Origin", origin);
        if (csrf is not null) request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await Http.SendAsync(request);
    }

    /// <summary>Fetches a fresh token (they are bound to the current user), then posts.</summary>
    public async Task<HttpResponseMessage> PostWithCsrfAsync(string path, object? body)
        => await PostAsync(path, body, await GetCsrfAsync());

    public Task<HttpResponseMessage> LoginAsync(string username, string password, string? code = null, string? recoveryCode = null)
        => PostWithCsrfAsync("/auth/login", new { username, password, code, recoveryCode });

    /// <summary>
    /// Signs in with the one-time password, replaces it, and completes TOTP enrolment.
    /// Leaves the client fully authorised; returns the authenticator key and the recovery codes.
    /// </summary>
    public async Task<(string Key, string[] RecoveryCodes)> EnrollAsync(string username, string initialPassword, string newPassword)
    {
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(username, initialPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await PostWithCsrfAsync("/auth/change-password",
            new { currentPassword = initialPassword, newPassword })).StatusCode);

        var setup = await PostWithCsrfAsync("/auth/totp/setup", null);
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        var key = (await setup.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("key").GetString()!;

        var enable = await PostWithCsrfAsync("/auth/totp/enable", new { code = Totp.Code(key) });
        Assert.Equal(HttpStatusCode.OK, enable.StatusCode);
        var codes = (await enable.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("recoveryCodes")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
        return (key, codes);
    }

    public static string? SessionCookieHeader(HttpResponseMessage response)
        => response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(v => v.StartsWith("__Host-resume-admin=", StringComparison.Ordinal))
            : null;
}

/// <summary>Minimal RFC 6238 (SHA-1, 6 digits, 30 s) for tests.</summary>
public static class Totp
{
    public static string Code(string base32Key, int stepOffset = 0)
    {
        var key = Base32Decode(base32Key);
        var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30 + stepOffset;
        var message = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(message);
        var hash = System.Security.Cryptography.HMACSHA1.HashData(key, message);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = 0;
        var value = 0;
        var output = new List<byte>();
        foreach (var c in input.TrimEnd('=').ToUpperInvariant())
        {
            value = (value << 5) | alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)(value >> (bits - 8)));
                bits -= 8;
            }
        }
        return output.ToArray();
    }
}

/// <summary>Replaces the email provider in tests: records every message and can be scripted to fail or defer.</summary>
public sealed class FakeEmailSender : ResumeApp.Features.Email.IEmailSender
{
    public sealed record Sent(ResumeApp.Features.Email.EmailMessage Message, string IdempotencyKey);

    private readonly object _lock = new();
    private readonly List<Sent> _sent = [];
    private readonly Queue<ResumeApp.Features.Email.EmailSendResult> _script = new();
    private int _attempts;

    /// <summary>Messages the fake accepted (a scripted failure is not recorded here).</summary>
    public IReadOnlyList<Sent> Messages { get { lock (_lock) return _sent.ToList(); } }
    public int Attempts => Volatile.Read(ref _attempts);
    /// <summary>Optional delay per send, to make concurrent workers overlap.</summary>
    public TimeSpan Delay { get; set; }

    /// <summary>The next sends return these results in order; afterwards sends succeed.</summary>
    public void Script(params ResumeApp.Features.Email.EmailSendResult[] results)
    {
        lock (_lock) foreach (var r in results) _script.Enqueue(r);
    }

    public async Task<ResumeApp.Features.Email.EmailSendResult> SendAsync(ResumeApp.Features.Email.EmailMessage message, string idempotencyKey, CancellationToken ct)
    {
        Interlocked.Increment(ref _attempts);
        if (Delay > TimeSpan.Zero) await Task.Delay(Delay, ct);
        lock (_lock)
        {
            if (_script.Count > 0)
            {
                var scripted = _script.Dequeue();
                if (scripted.Status != ResumeApp.Features.Email.EmailSendStatus.Sent) return scripted;
            }
            _sent.Add(new Sent(message, idempotencyKey));
            return ResumeApp.Features.Email.EmailSendResult.Sent;
        }
    }
}
