using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using ResumeApp.Features.PublicForms;
using ResumeApp.Infrastructure;

namespace ResumeApp.Tests;

[Collection("env")]
public class PublicFormProtectionTests(PostgresFixture pg) : IDisposable
{
    private const string NewPassword = "Brand-New-Pass-42#Lk";
    private readonly List<AuthApiFactory> _factories = [];

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
    }

    private sealed record Setup(AuthApiFactory Factory, ApiClient Admin, string Token, string CohortId);

    private async Task<Setup> NewSetupAsync(Dictionary<string, string>? limits = null)
    {
        var factory = await AuthApiFactory.CreateAsync(pg, limits: limits);
        _factories.Add(factory);
        var admin = factory.NewClient();
        await admin.EnrollAsync(AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword, NewPassword);
        var (token, id) = await CreateCohortAsync(admin);
        return new(factory, admin, token, id);
    }

    private static async Task<(string Token, string Id)> CreateCohortAsync(ApiClient admin, string name = "מחזור")
    {
        var response = await admin.PostWithCsrfAsync("/admin/cohorts", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (json.GetProperty("inviteToken").GetString()!, json.GetProperty("id").GetString()!);
    }

    private static object Valid(string email = "dana@example.com") => new
    {
        name = "דנה כהן", productName = "מוצר", productUrl = "https://example.com/app", email, phone = "0501234567",
    };

    /// <summary>POST with full control over the Turnstile header and the forwarded client IP.</summary>
    private static async Task<HttpResponseMessage> Submit(ApiClient client, string token, object body, string csrf,
        string? ip = null, string? turnstile = FakeTurnstileVerifier.ValidToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/invites/{token}/profiles") { Content = JsonContent.Create(body) };
        request.Headers.Add("Origin", ApiClient.Origin);
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        if (ip is not null) request.Headers.Add("X-Forwarded-For", ip);
        request.Headers.Remove("X-Turnstile-Token");
        if (turnstile is not null) request.Headers.Add("X-Turnstile-Token", turnstile);
        // Per-request headers override the client's defaults only when the default is absent, so strip it first.
        return await client.Http.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> Check(ApiClient client, string token, string? ip = null, string? turnstile = FakeTurnstileVerifier.ValidToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/invites/{token}");
        if (ip is not null) request.Headers.Add("X-Forwarded-For", ip);
        if (turnstile is not null) request.Headers.Add("X-Turnstile-Token", turnstile);
        return await client.Http.SendAsync(request);
    }

    private static ApiClient Bare(AuthApiFactory factory)
    {
        // A client without the default Turnstile header, so each test controls it explicitly.
        var client = factory.NewClient();
        client.Http.DefaultRequestHeaders.Remove("X-Turnstile-Token");
        return client;
    }

    private static async Task<T> Db<T>(AuthApiFactory factory, Func<AppDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static async Task AssertNothingStored(AuthApiFactory factory)
    {
        Assert.Equal(0, await Db(factory, d => d.Profiles.CountAsync()));
        Assert.Equal(0, await Db(factory, d => d.ProfileRevisions.CountAsync()));
        Assert.Equal(0, await Db(factory, d => d.ParticipantPrivates.CountAsync()));
    }

    private static async Task AssertProblem(HttpResponseMessage r, HttpStatusCode status, string? code = null)
    {
        Assert.Equal(status, r.StatusCode);
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-store", r.Headers.CacheControl?.ToString());
        var json = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        if (code is not null) Assert.Equal(code, json.GetProperty("code").GetString());
    }

    // ---- Turnstile ----

    [Fact]
    public async Task Missing_invalid_or_unavailable_turnstile_is_403_and_creates_nothing()
    {
        var s = await NewSetupAsync();
        var client = Bare(s.Factory);
        var csrf = await client.GetCsrfAsync();

        await AssertProblem(await Submit(client, s.Token, Valid(), csrf, turnstile: null), HttpStatusCode.Forbidden, "bot-check-failed");
        await AssertProblem(await Submit(client, s.Token, Valid(), csrf, turnstile: "forged"), HttpStatusCode.Forbidden);
        await AssertProblem(await Check(client, s.Token, turnstile: null), HttpStatusCode.Forbidden);
        await AssertProblem(await Check(client, s.Token, turnstile: "forged"), HttpStatusCode.Forbidden);

        s.Factory.Turnstile.Unavailable = true; // Cloudflare down: fail closed even with a "valid" token
        await AssertProblem(await Submit(client, s.Token, Valid(), csrf), HttpStatusCode.Forbidden);
        await AssertProblem(await Check(client, s.Token), HttpStatusCode.Forbidden);

        await AssertNothingStored(s.Factory);
    }

    [Fact]
    public async Task Valid_turnstile_proceeds_to_normal_handling()
    {
        var s = await NewSetupAsync();
        var client = Bare(s.Factory);
        Assert.Equal(HttpStatusCode.Accepted, (await Submit(client, s.Token, Valid(), await client.GetCsrfAsync())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Check(client, s.Token)).StatusCode);
        // An unknown token still gets the uniform 404 once the bot check passed.
        Assert.Equal(HttpStatusCode.NotFound, (await Check(client, ResumeApp.Domain.Cohorts.Cohort.GenerateToken())).StatusCode);
    }

    // ---- Rate limiting ----

    [Fact]
    public async Task Ip_over_the_threshold_gets_429_with_retry_after_and_other_ips_are_unaffected()
    {
        var s = await NewSetupAsync(new() { ["RATE_LIMIT_PROFILE_IP"] = "3" });
        var client = Bare(s.Factory);
        var csrf = await client.GetCsrfAsync();
        const string ip = "203.0.113.5";

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Accepted, (await Submit(client, s.Token, Valid($"p{i}@example.com"), csrf, ip)).StatusCode);

        var callsBefore = s.Factory.Turnstile.Calls;
        var blocked = await Submit(client, s.Token, Valid("p9@example.com"), csrf, ip);
        await AssertProblem(blocked, HttpStatusCode.TooManyRequests, "rate-limited");
        var retry = int.Parse(blocked.Headers.GetValues("Retry-After").Single());
        Assert.InRange(retry, 1, 3600);
        Assert.DoesNotContain(ip, await blocked.Content.ReadAsStringAsync());
        Assert.Equal(callsBefore, s.Factory.Turnstile.Calls); // IP limit runs before Turnstile
        Assert.Equal(3, await Db(s.Factory, d => d.Profiles.CountAsync()));

        Assert.Equal(HttpStatusCode.Accepted, (await Submit(client, s.Token, Valid("other@example.com"), csrf, "203.0.113.99")).StatusCode);

        // The counter is in the database and keyed by a hash, never the raw IP.
        var rows = await Db(s.Factory, d => d.RateLimitCounters.AsNoTracking().Where(c => c.Scope == RateLimitScopes.ProfileSubmitIp).ToListAsync());
        var row = Assert.Single(rows, r => r.Count == 4);
        Assert.Equal(64, row.KeyHash.Length);
        Assert.All(rows, r => Assert.DoesNotContain("203.0.113", r.KeyHash));
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public async Task Failed_turnstile_attempts_still_count_toward_the_ip_limit()
    {
        var s = await NewSetupAsync(new() { ["RATE_LIMIT_PROFILE_IP"] = "2" });
        var client = Bare(s.Factory);
        var csrf = await client.GetCsrfAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await Submit(client, s.Token, Valid(), csrf, "198.51.100.7", turnstile: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Submit(client, s.Token, Valid(), csrf, "198.51.100.7", turnstile: "bad")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Submit(client, s.Token, Valid(), csrf, "198.51.100.7")).StatusCode);
    }

    [Fact]
    public async Task Email_over_the_threshold_gets_429_other_email_or_cohort_unaffected_and_email_is_hashed()
    {
        var s = await NewSetupAsync(new() { ["RATE_LIMIT_PROFILE_EMAIL"] = "2" });
        var (otherToken, _) = await CreateCohortAsync(s.Admin, "מחזור ב");
        var client = Bare(s.Factory);
        var csrf = await client.GetCsrfAsync();

        Assert.Equal(HttpStatusCode.Accepted, (await Submit(client, s.Token, Valid("Rate.Limited@Example.com"), csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await Submit(client, s.Token, Valid(" rate.limited@example.com"), csrf)).StatusCode);
        var blocked = await Submit(client, s.Token, Valid("RATE.limited@example.com"), csrf);
        await AssertProblem(blocked, HttpStatusCode.TooManyRequests);
        Assert.True(blocked.Headers.Contains("Retry-After"));

        Assert.Equal(HttpStatusCode.Accepted, (await Submit(client, s.Token, Valid("someone.else@example.com"), csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await Submit(client, otherToken, Valid("rate.limited@example.com"), csrf)).StatusCode);

        var rows = await Db(s.Factory, d => d.RateLimitCounters.AsNoTracking().Where(c => c.Scope == RateLimitScopes.ProfileSubmitEmail).ToListAsync());
        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.DoesNotContain("limited", r.KeyHash, StringComparison.OrdinalIgnoreCase));
        Assert.All(rows, r => Assert.Equal(64, r.KeyHash.Length));
    }

    [Fact]
    public async Task Invite_check_has_its_own_ip_limit()
    {
        var s = await NewSetupAsync(new() { ["RATE_LIMIT_INVITE_IP"] = "2" });
        var client = Bare(s.Factory);
        Assert.Equal(HttpStatusCode.OK, (await Check(client, s.Token, "192.0.2.10")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Check(client, s.Token, "192.0.2.10")).StatusCode);
        var blocked = await Check(client, s.Token, "192.0.2.10");
        await AssertProblem(blocked, HttpStatusCode.TooManyRequests);
        Assert.True(blocked.Headers.Contains("Retry-After"));
        Assert.Equal(HttpStatusCode.OK, (await Check(client, s.Token, "192.0.2.11")).StatusCode);
        // The submission limit for the same IP is a different counter.
        Assert.Equal(HttpStatusCode.Accepted, (await Submit(client, s.Token, Valid(), await client.GetCsrfAsync(), "192.0.2.10")).StatusCode);
    }

    [Fact]
    public async Task Counters_survive_a_server_restart()
    {
        var s = await NewSetupAsync(new() { ["RATE_LIMIT_INVITE_IP"] = "2" });
        var client = Bare(s.Factory);
        Assert.Equal(HttpStatusCode.OK, (await Check(client, s.Token, "192.0.2.20")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Check(client, s.Token, "192.0.2.20")).StatusCode);

        // A second host on the same database stands in for a restarted server.
        using var restarted = new AuthApiFactory(s.Factory.DatabaseUrl, limits: new Dictionary<string, string> { ["RATE_LIMIT_INVITE_IP"] = "2" });
        var again = Bare(restarted);
        await AssertProblem(await Check(again, s.Token, "192.0.2.20"), HttpStatusCode.TooManyRequests);
        Assert.Equal(HttpStatusCode.OK, (await Check(again, s.Token, "192.0.2.21")).StatusCode);
    }

    [Fact]
    public async Task Five_parallel_requests_are_counted_exactly()
    {
        var s = await NewSetupAsync(new() { ["RATE_LIMIT_INVITE_IP"] = "3" });
        var client = Bare(s.Factory);
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Check(client, s.Token, "192.0.2.30")));
        Assert.Equal(3, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(2, results.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests));
        var row = Assert.Single(await Db(s.Factory, d => d.RateLimitCounters.AsNoTracking().Where(c => c.Scope == RateLimitScopes.InviteCheckIp).ToListAsync()));
        Assert.Equal(5, row.Count);
    }

    [Fact]
    public async Task Limiter_counts_atomically_rolls_windows_and_prunes_old_ones()
    {
        var s = await NewSetupAsync();
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 21, 10, 30, 0, TimeSpan.Zero));
        var window = TimeSpan.FromHours(1);

        async Task<RateLimitResult[]> Hits(int n)
        {
            return await Task.WhenAll(Enumerable.Range(0, n).Select(async _ =>
            {
                await using var scope = s.Factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var limiter = new RateLimiter(db, clock, scope.ServiceProvider.GetRequiredService<RateLimitPepper>());
                return await limiter.HitAsync("unit-test", "subject", 3, window, CancellationToken.None);
            }));
        }

        var first = await Hits(5);
        Assert.Equal([1, 2, 3, 4, 5], first.Select(r => r.Count).Order().ToArray());
        Assert.Equal(3, first.Count(r => r.Allowed));
        Assert.All(first, r => Assert.Equal(TimeSpan.FromMinutes(30), r.RetryAfter));

        clock.Now = clock.Now.AddHours(1); // next window: the count starts over, and the old row is pruned two windows later
        var next = await Hits(1);
        Assert.Equal(1, next[0].Count);
        Assert.True(next[0].Allowed);

        clock.Now = clock.Now.AddHours(2);
        await Hits(1);
        var rows = await Db(s.Factory, d => d.RateLimitCounters.AsNoTracking().Where(c => c.Scope == "unit-test").ToListAsync());
        Assert.Single(rows); // earlier windows were deleted
    }

    [Fact]
    public async Task Spoofed_forwarded_for_without_the_secret_is_rejected_and_not_counted()
    {
        var s = await NewSetupAsync(new() { ["RATE_LIMIT_INVITE_IP"] = "1" });
        var client = Bare(s.Factory);
        client.Http.DefaultRequestHeaders.Remove("x-origin-secret");
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Forbidden, (await Check(client, s.Token, "192.0.2.40")).StatusCode);
        Assert.Equal(0, await Db(s.Factory, d => d.RateLimitCounters.CountAsync(c => c.Scope == RateLimitScopes.InviteCheckIp)));
    }

    // ---- Sanitizer ----

    [Theory]
    [InlineData("hello\r\nBcc: evil@example.com", "hello  Bcc: evil@example.com")]
    [InlineData("a\nb", "a b")]
    [InlineData("a\rb", "a b")]
    [InlineData("tab\there\u0000nul\u0007", "tab here nul")]
    [InlineData("line\u2028sep\u2029para", "line sep para")]
    [InlineData("  שלום עולם  ", "שלום עולם")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Sanitizer_removes_line_breaks_and_control_characters(string? input, string expected)
    {
        var result = EmailHeaderSanitizer.Sanitize(input);
        Assert.Equal(expected, result);
        Assert.DoesNotContain('\r', result);
        Assert.DoesNotContain('\n', result);
        Assert.DoesNotContain(result, char.IsControl);
    }

    // ---- Cloudflare verifier (no network) ----

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? LastBody { get; private set; }
        public Uri? LastUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            LastUri = request.RequestUri;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return await respond(request, ct);
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private static CloudflareTurnstileVerifier Verifier(StubHandler handler)
        => new(new HttpClient(handler), new TurnstileOptions("test-secret"),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CloudflareTurnstileVerifier>.Instance);

    [Fact]
    public async Task Verifier_accepts_only_an_explicit_success_true_and_sends_secret_response_and_ip()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Json("{\"success\":true}")));
        Assert.True(await Verifier(handler).VerifyAsync("tok", "203.0.113.1", CancellationToken.None));
        Assert.Equal(TurnstileOptions.EndpointUrl, handler.LastUri!.ToString());
        Assert.Contains("secret=test-secret", handler.LastBody);
        Assert.Contains("response=tok", handler.LastBody);
        Assert.Contains("remoteip=203.0.113.1", handler.LastBody);
    }

    [Theory]
    [InlineData("{\"success\":false}", 200)]
    [InlineData("{\"success\":\"true\"}", 200)]
    [InlineData("{\"success\":1}", 200)]
    [InlineData("{}", 200)]
    [InlineData("[]", 200)]
    [InlineData("not json", 200)]
    [InlineData("", 200)]
    [InlineData("{\"success\":true}", 500)]
    [InlineData("{\"success\":true}", 403)]
    public async Task Verifier_fails_closed_on_rejections_and_unexpected_responses(string body, int status)
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Json(body, (HttpStatusCode)status)));
        Assert.False(await Verifier(handler).VerifyAsync("tok", null, CancellationToken.None));
    }

    [Fact]
    public async Task Verifier_fails_closed_on_network_error()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException("down"));
        Assert.False(await Verifier(handler).VerifyAsync("tok", null, CancellationToken.None));
    }

    [Fact]
    public async Task Verifier_fails_closed_on_timeout()
    {
        var handler = new StubHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Json("{\"success\":true}");
        });
        var started = DateTime.UtcNow;
        Assert.False(await Verifier(handler).VerifyAsync("tok", null, CancellationToken.None));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Verifier_rejects_a_missing_token_without_calling_cloudflare(string? token)
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Json("{\"success\":true}")));
        Assert.False(await Verifier(handler).VerifyAsync(token, null, CancellationToken.None));
        Assert.False(await Verifier(handler).VerifyAsync(new string('x', 5000), null, CancellationToken.None));
        Assert.Equal(0, handler.Calls);
    }

    // ---- Configuration ----

    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static IConfiguration Config(string? secret)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(secret is null ? [] : new Dictionary<string, string?> { ["TURNSTILE_SECRET"] = secret })
            .Build();

    [Fact]
    public void Turnstile_secret_is_required_outside_development_and_defaults_to_the_test_key_in_development()
    {
        Assert.Throws<InvalidOperationException>(() => TurnstileOptions.Resolve(Config(null), new Env("Production")));
        Assert.Throws<InvalidOperationException>(() => TurnstileOptions.Resolve(Config("  "), new Env("Production")));
        Assert.Throws<InvalidOperationException>(() => TurnstileOptions.Resolve(Config(null), new Env("Staging")));
        Assert.Equal(TurnstileOptions.DevAlwaysPassSecret, TurnstileOptions.Resolve(Config(null), new Env("Development")).Secret);
        Assert.Equal("real", TurnstileOptions.Resolve(Config("real"), new Env("Production")).Secret);
    }

    // ---- Guard: codes, unknown address, IPv6 /64, limiter outage ----

    private sealed class ThrowingLimiter : IRateLimiter
    {
        public Task<RateLimitResult> HitAsync(string scope, string subject, int limit, TimeSpan window, CancellationToken ct)
            => throw new Npgsql.NpgsqlException("db down");
    }

    private sealed class PassLimiter : IRateLimiter
    {
        public Task<RateLimitResult> HitAsync(string scope, string subject, int limit, TimeSpan window, CancellationToken ct)
            => Task.FromResult(new RateLimitResult(true, 1, TimeSpan.FromMinutes(1)));
    }

    private static ResumeApp.Api.Security.PublicFormGuard Guard(IRateLimiter limiter, FakeTurnstileVerifier? turnstile = null)
        => new(limiter, turnstile ?? new FakeTurnstileVerifier(), RateLimitOptions.Defaults,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ResumeApp.Api.Security.PublicFormGuard>.Instance);

    private static async Task<(int Status, string Code)> Execute(Microsoft.AspNetCore.Http.IResult result)
    {
        var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider(),
        };
        ctx.Response.Body = new MemoryStream();
        await result.ExecuteAsync(ctx);
        ctx.Response.Body.Position = 0;
        var json = await JsonDocument.ParseAsync(ctx.Response.Body);
        return (ctx.Response.StatusCode, json.RootElement.GetProperty("code").GetString()!);
    }

    private static Microsoft.AspNetCore.Http.DefaultHttpContext HttpCtx(string? ip)
    {
        var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = ip is null ? null : System.Net.IPAddress.Parse(ip);
        ctx.Request.Headers["X-Turnstile-Token"] = FakeTurnstileVerifier.ValidToken;
        return ctx;
    }

    [Fact]
    public async Task Unknown_client_address_fails_closed_with_403()
    {
        var guard = Guard(new PassLimiter());
        var result = await guard.CheckIpAndBotAsync(HttpCtx(null), RateLimitScopes.InviteCheckIp, 10, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal((403, "client-address-unknown"), await Execute(result));
        Assert.Null(await guard.CheckIpAndBotAsync(HttpCtx("203.0.113.1"), RateLimitScopes.InviteCheckIp, 10, CancellationToken.None));
    }

    [Fact]
    public async Task Limiter_failure_fails_closed_with_503_and_code()
    {
        var turnstile = new FakeTurnstileVerifier();
        var guard = Guard(new ThrowingLimiter(), turnstile);
        var ip = await guard.CheckIpAndBotAsync(HttpCtx("203.0.113.1"), RateLimitScopes.InviteCheckIp, 10, CancellationToken.None);
        Assert.Equal((503, "rate-limit-unavailable"), await Execute(ip!));
        Assert.Equal(0, turnstile.Calls);
        var email = await guard.CheckSubmissionEmailAsync(HttpCtx("203.0.113.1"), Guid.NewGuid(), "a@b.co", CancellationToken.None);
        Assert.Equal((503, "rate-limit-unavailable"), await Execute(email!));
    }

    [Fact]
    public void Ipv6_is_keyed_by_its_64_prefix_and_ipv4_is_unchanged()
    {
        static string Key(string ip) => ResumeApp.Api.Security.PublicFormGuard.IpKey(System.Net.IPAddress.Parse(ip));
        Assert.Equal(Key("2001:db8:1:2:aaaa::1"), Key("2001:db8:1:2:bbbb:cccc:dddd:eeee"));
        Assert.NotEqual(Key("2001:db8:1:2::1"), Key("2001:db8:1:3::1"));
        Assert.Equal("203.0.113.5", Key("203.0.113.5"));
        Assert.Equal("203.0.113.5", Key("::ffff:203.0.113.5"));
    }

    [Fact]
    public async Task Ipv6_addresses_in_one_64_share_a_counter_over_http()
    {
        var s = await NewSetupAsync(new() { ["RATE_LIMIT_INVITE_IP"] = "2" });
        var client = Bare(s.Factory);
        Assert.Equal(HttpStatusCode.OK, (await Check(client, s.Token, "2001:db8:aa:bb::1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Check(client, s.Token, "2001:db8:aa:bb:1::2")).StatusCode);
        await AssertProblem(await Check(client, s.Token, "2001:db8:aa:bb:ffff::3"), HttpStatusCode.TooManyRequests, "rate-limited");
        Assert.Equal(HttpStatusCode.OK, (await Check(client, s.Token, "2001:db8:aa:cc::1")).StatusCode);
    }

    [Theory]
    [InlineData(" Dana@Example.COM ")]
    [InlineData("MIXED.case@Example.Com")]
    [InlineData("plain@example.com")]
    public async Task Stored_email_and_the_rate_limit_key_use_the_same_normalisation(string input)
    {
        var s = await NewSetupAsync();
        var client = Bare(s.Factory);
        Assert.Equal(HttpStatusCode.Accepted, (await Submit(client, s.Token, Valid(input), await client.GetCsrfAsync())).StatusCode);
        var stored = (await Db(s.Factory, d => d.ParticipantPrivates.AsNoTracking().SingleAsync())).Email;
        Assert.Equal(ResumeApp.Features.Profiles.ProfileService.NormalizeEmail(input), stored);
    }

    // ---- Startup configuration and DI ----

    private static void Register(Dictionary<string, string?> values)
        => new ServiceCollection().AddPublicFormProtection(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build(), new Env("Production"));

    [Theory]
    [InlineData("RATE_LIMIT_PROFILE_IP", "abc")]
    [InlineData("RATE_LIMIT_PROFILE_IP", "0")]
    [InlineData("RATE_LIMIT_PROFILE_EMAIL", "-3")]
    [InlineData("RATE_LIMIT_INVITE_IP", "1.5")]
    [InlineData("RATE_LIMIT_INVITE_IP", "99999999999")]
    [InlineData("RATE_LIMIT_WINDOW_MINUTES", "abc")]
    [InlineData("RATE_LIMIT_WINDOW_MINUTES", "0")]
    [InlineData("RATE_LIMIT_WINDOW_MINUTES", "-60")]
    [InlineData("RATE_LIMIT_WINDOW_MINUTES", "7")]
    [InlineData("RATE_LIMIT_WINDOW_MINUTES", "11")]
    [InlineData("RATE_LIMIT_WINDOW_MINUTES", "2880")]
    public void Invalid_rate_limit_settings_stop_startup(string key, string value)
        => Assert.Throws<InvalidOperationException>(() => Register(new() { ["TURNSTILE_SECRET"] = "s", [key] = value }));

    [Theory]
    [InlineData("1")]
    [InlineData("60")]
    [InlineData("1440")]
    public void Windows_that_divide_a_day_are_accepted(string minutes)
        => Register(new() { ["TURNSTILE_SECRET"] = "s", ["RATE_LIMIT_WINDOW_MINUTES"] = minutes });

    [Fact]
    public void Real_registration_resolves_the_cloudflare_verifier()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddPublicFormProtection(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["TURNSTILE_SECRET"] = "s" }).Build(),
            new Env("Production"));
        using var provider = services.BuildServiceProvider();
        Assert.IsType<CloudflareTurnstileVerifier>(provider.GetRequiredService<ITurnstileVerifier>());
    }

    [Theory]
    [InlineData(0x200B)] [InlineData(0x200C)] [InlineData(0x200D)] [InlineData(0x200E)] [InlineData(0x200F)]
    [InlineData(0x202A)] [InlineData(0x202B)] [InlineData(0x202C)] [InlineData(0x202D)] [InlineData(0x202E)]
    [InlineData(0x2066)] [InlineData(0x2067)] [InlineData(0x2068)] [InlineData(0x2069)] [InlineData(0xFEFF)]
    public void Sanitizer_removes_bidi_and_zero_width_characters(int codePoint)
    {
        var c = (char)codePoint;
        Assert.Equal("a b", EmailHeaderSanitizer.Sanitize($"a{c}b"));
        Assert.Equal("שלום", EmailHeaderSanitizer.Sanitize($"{c}שלום{c}"));
    }

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
