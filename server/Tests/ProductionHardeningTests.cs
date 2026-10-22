using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using ResumeApp.Domain.Cohorts;
using ResumeApp.Features.Cohorts;

namespace ResumeApp.Tests;

/// <summary>Story 4.1: uniform problem+json errors (no stack trace), and the cookie and cache headers of authenticated responses, also on errors.</summary>
[Collection("env")]
public class ProductionHardeningTests(PostgresFixture pg) : IDisposable
{
    private const string NewPassword = "Brand-New-Pass-42#Lk";
    private const string Leak = "leak.me@example.com";
    private readonly List<AuthApiFactory> _factories = [];
    private readonly CapturingSink _log = new();

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
    }

    /// <summary>Stands in for the database: every call throws an exception whose message carries personal data.</summary>
    private sealed class ThrowingCohorts : ICohortRepository
    {
        private static Exception Boom() => new InvalidOperationException("boom " + Leak);
        public Task AddAsync(Cohort cohort, CancellationToken ct) => throw Boom();
        public Task<IReadOnlyList<Cohort>> ListAsync(CancellationToken ct) => throw Boom();
        public Task<Cohort?> FindByTokenAsync(string token, CancellationToken ct) => throw Boom();
        public Task<bool> TryRevokeAsync(Guid id, DateTime nowUtc, CancellationToken ct) => throw Boom();
        public Task<bool> ExistsAsync(Guid id, CancellationToken ct) => throw Boom();
        public Task<Cohort?> FindActiveByIdAsync(Guid id, CancellationToken ct) => throw Boom();
        public Task<IReadOnlyList<Cohort>> ListActiveAsync(CancellationToken ct) => throw Boom();
    }

    private async Task<AuthApiFactory> NewFactoryAsync(bool throwing = false)
    {
        var factory = await AuthApiFactory.CreateAsync(pg, configureServices: s =>
        {
            s.AddSingleton<ILogEventSink>(_log);
            if (throwing) s.AddScoped<ICohortRepository, ThrowingCohorts>();
        });
        _factories.Add(factory);
        return factory;
    }

    private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)status, json.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("title").GetString()));
        return json;
    }

    private static void AssertNoStore(HttpResponseMessage response)
        => Assert.True(response.Headers.CacheControl?.NoStore == true, "Cache-Control: " + response.Headers.CacheControl);

    // ---- errors ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_unhandled_exception_is_a_500_problem_json_without_stack_trace_or_message_and_is_logged_without_personal_data()
    {
        var f = await NewFactoryAsync(throwing: true);

        var response = await f.NewClient().Http.GetAsync("/cohorts/active");

        var json = await AssertProblemAsync(response, HttpStatusCode.InternalServerError);
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("traceId").GetString()));
        var text = json.GetRawText();
        foreach (var forbidden in new[] { "boom", Leak, "InvalidOperationException", "StackTrace", "   at ", "ResumeApp.", "exception" })
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(_log.Lines, l => l.Contains("Unhandled exception of type", StringComparison.OrdinalIgnoreCase));
        foreach (var line in _log.Lines)
        {
            Assert.DoesNotContain(Leak, line);
            Assert.DoesNotContain("boom", line);
        }
    }

    [Fact]
    public async Task An_unhandled_exception_on_an_authenticated_route_is_still_no_store()
    {
        var f = await NewFactoryAsync(throwing: true);
        var admin = f.NewClient();
        await admin.EnrollAsync(AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword, NewPassword);

        var response = await admin.Http.GetAsync("/admin/cohorts");

        var json = await AssertProblemAsync(response, HttpStatusCode.InternalServerError);
        Assert.DoesNotContain("boom", json.GetRawText());
        AssertNoStore(response);
    }

    [Fact]
    public async Task An_authenticated_admin_gets_a_real_404_problem_json_that_is_no_store()
    {
        var f = await NewFactoryAsync();
        var admin = f.NewClient();
        await admin.EnrollAsync(AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword, NewPassword);

        var response = await admin.Http.GetAsync("/nope-not-mapped");

        await AssertProblemAsync(response, HttpStatusCode.NotFound);
        AssertNoStore(response);
    }

    [Fact]
    public async Task Empty_status_responses_get_a_uniform_problem_json_body()
    {
        var f = await NewFactoryAsync();
        var c = f.NewClient();

        var get = await c.Http.GetAsync("/nope");
        Assert.True(get.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized, get.StatusCode.ToString());
        await AssertProblemAsync(get, get.StatusCode);
        var post = await c.PostWithCsrfAsync("/nope", null); // an anonymous POST to an unmapped path is refused (401) or not found (404): uniform either way
        Assert.True(post.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized, post.StatusCode.ToString());
        await AssertProblemAsync(post, post.StatusCode);
        var admin = f.NewClient(); // the 405 of a mapped path with the wrong method is only reached once authorised (anonymous gets 401 first)
        await admin.EnrollAsync(AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword, NewPassword);
        await AssertProblemAsync(await admin.PostWithCsrfAsync("/cohorts/active", null), HttpStatusCode.MethodNotAllowed);
        await AssertProblemAsync(await c.Http.GetAsync("/me/profile"), HttpStatusCode.Unauthorized);
        await AssertProblemAsync(await c.Http.GetAsync("/admin/cohorts"), HttpStatusCode.Unauthorized);

        // Without the origin secret even the 403 is uniform.
        var bare = f.CreateClient();
        await AssertProblemAsync(await bare.GetAsync("/healthz"), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Error_responses_of_authenticated_areas_are_no_store()
    {
        var f = await NewFactoryAsync();
        var admin = f.NewClient();
        await admin.EnrollAsync(AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword, NewPassword);
        var anonymous = f.NewClient();

        foreach (var response in new[]
                 {
                     await anonymous.Http.GetAsync("/me/profile"),      // 401
                     await anonymous.Http.GetAsync("/admin/cohorts"),   // 401
                     await anonymous.Http.GetAsync("/auth/nope"),       // 404
                     await admin.Http.GetAsync("/admin/nope"),          // 404
                     await admin.Http.GetAsync("/me/nope"),             // 404
                 })
        {
            Assert.True((int)response.StatusCode >= 400);
            AssertNoStore(response);
        }
    }

    // ---- cookies and headers -----------------------------------------------------------------------------------------

    private static string SetCookie(HttpResponseMessage r, string name)
    {
        Assert.True(r.Headers.TryGetValues("Set-Cookie", out var values), $"no Set-Cookie header for {name}");
        return Assert.Single(values!, v => v.StartsWith(name + "=", StringComparison.Ordinal));
    }

    /// <summary>The __Host- prefix rules (Secure, Path=/, no Domain) plus the expected HttpOnly and SameSite.</summary>
    private static void AssertHostCookie(string setCookie, string sameSite)
    {
        var parts = setCookie.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(p => p.ToLowerInvariant()).ToList();
        Assert.Contains("secure", parts);
        Assert.Contains("path=/", parts);
        Assert.DoesNotContain(parts, p => p.StartsWith("domain", StringComparison.Ordinal));
        Assert.Contains("httponly", parts);
        Assert.Contains("samesite=" + sameSite, parts);
    }

    [Fact]
    public async Task Every_host_cookie_is_secure_path_root_without_domain_and_has_the_expected_samesite()
    {
        var f = await NewFactoryAsync();

        // CSRF and admin session (Strict).
        var admin = f.NewClient();
        AssertHostCookie(SetCookie(await admin.Http.GetAsync("/auth/csrf"), "__Host-resume-csrf"), "strict");
        var login = await admin.LoginAsync(AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        AssertHostCookie(SetCookie(login, "__Host-resume-admin"), "strict");

        // Google flow: the short-lived oidc cookie is Lax (the return from Google is a cross-site navigation), the session is Strict.
        var participant = f.NewClient();
        var start = await participant.Http.GetAsync("/auth/google/start");
        AssertHostCookie(SetCookie(start, "__Host-resume-oidc"), "lax");
        var authorize = await participant.Http.GetAsync(ServerPath(QueryString(start.Headers.Location!)));
        var callback = await participant.Http.GetAsync(ServerPath(QueryString(authorize.Headers.Location!)));
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        AssertHostCookie(SetCookie(callback, "__Host-resume-participant"), "strict");
    }

    private static string QueryString(Uri uri) => uri.IsAbsoluteUri ? uri.PathAndQuery : uri.OriginalString;

    private static string ServerPath(string pathAndQuery) => pathAndQuery.StartsWith("/api/", StringComparison.Ordinal) ? pathAndQuery[4..] : pathAndQuery;

    [Fact]
    public async Task Authenticated_responses_are_no_store()
    {
        var f = await NewFactoryAsync();
        var admin = f.NewClient();
        await admin.EnrollAsync(AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword, NewPassword);
        var participant = await f.SignedInAsync(await ParticipantSeeding.SeedProfileAsync(f));

        foreach (var response in new[]
                 {
                     await admin.Http.GetAsync("/admin/cohorts"),
                     await admin.Http.GetAsync("/admin/submissions"),
                     await admin.Http.GetAsync("/auth/me"),
                     await admin.Http.GetAsync("/auth/csrf"),
                     await participant.Http.GetAsync("/me/profile"),
                     await participant.Http.GetAsync("/profiles/csrf"),
                 })
        {
            Assert.True(response.IsSuccessStatusCode, response.RequestMessage?.RequestUri + " " + response.StatusCode);
            AssertNoStore(response);
        }
    }
}
