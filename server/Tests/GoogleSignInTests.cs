using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ResumeApp.Domain.Cohorts;
using ResumeApp.Domain.Profiles;
using ResumeApp.Infrastructure;
using Serilog.Core;
using Serilog.Events;

namespace ResumeApp.Tests;

/// <summary>Story 6.1: sign in with Google on the server (fake provider), the participant session, and the options.</summary>
[Collection("env")]
public class GoogleSignInTests(PostgresFixture pg) : IDisposable
{
    private const string OidcCookie = "__Host-resume-oidc";
    private const string SessionCookie = "__Host-resume-participant";
    private readonly List<AuthApiFactory> _factories = [];
    private readonly CapturingSink _log = new();

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
    }

    private async Task<AuthApiFactory> NewFactoryAsync(IReadOnlyDictionary<string, string>? limits = null)
    {
        var factory = await AuthApiFactory.CreateAsync(pg, limits: limits, configureServices: s => s.AddSingleton<ILogEventSink>(_log));
        _factories.Add(factory);
        return factory;
    }

    private static Task<T> Db<T>(AuthApiFactory f, Func<AppDbContext, Task<T>> work) => Run(f, work);

    private static async Task<T> Run<T>(AuthApiFactory f, Func<AppDbContext, Task<T>> work)
    {
        await using var scope = f.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>A profile linked to the fake Google account of <paramref name="googleEmail"/> (or unlinked when null), stored with <paramref name="storedEmail"/>.</summary>
    private static async Task<Guid> SeedProfileAsync(AuthApiFactory f, string storedEmail, string? googleEmail, string name = "דנה כהן", Guid? cohortId = null)
        => await Db(f, async d =>
        {
            var now = DateTime.UtcNow;
            var cohort = cohortId is null ? Cohort.Create("מחזור " + Guid.NewGuid().ToString("N")[..6], now) : null;
            if (cohort is not null) d.Cohorts.Add(cohort);
            var cid = cohortId ?? cohort!.Id;
            var profile = Profile.Create(cid, now);
            d.Profiles.Add(profile);
            d.ParticipantPrivates.Add(ParticipantPrivate.Create(profile.Id, cid, storedEmail, "0501234567"));
            d.ProfileRevisions.Add(ProfileRevision.CreatePending(profile.Id, name, "מוצר", "https://example.com", now));
            await d.SaveChangesAsync();
            if (googleEmail is not null)
            {
                var sub = FakeOidcProvider.SubFor(googleEmail);
                await d.Database.ExecuteSqlInterpolatedAsync($"UPDATE profiles SET google_sub = {sub} WHERE id = {profile.Id}");
            }
            return profile.Id;
        });

    private static string PathAndQuery(Uri uri) => uri.IsAbsoluteUri ? uri.PathAndQuery : uri.OriginalString;

    /// <summary>The server route for a browser URL: the Vercel rewrite strips the /api prefix.</summary>
    private static string ServerPath(string pathAndQuery) => pathAndQuery.StartsWith("/api/", StringComparison.Ordinal) ? pathAndQuery[4..] : pathAndQuery;

    private sealed record Flow(HttpResponseMessage Start, HttpResponseMessage? Authorize, HttpResponseMessage? Callback);

    /// <summary>Runs start, the fake authorize (with the given fake_* choices) and the callback with a client that holds cookies.</summary>
    private static async Task<Flow> SignInAsync(ApiClient c, string? returnUrl = null, string? email = null, bool? verified = null, string? error = null,
        Func<string, string>? tamperCallback = null)
    {
        var startPath = "/auth/google/start" + (returnUrl is null ? "" : "?returnUrl=" + Uri.EscapeDataString(returnUrl));
        var start = await c.Http.GetAsync(startPath);
        if (start.StatusCode != HttpStatusCode.Redirect) return new(start, null, null);

        var authorizeUrl = PathAndQuery(start.Headers.Location!);
        var extra = new Dictionary<string, string?>();
        if (email is not null) extra["fake_email"] = email;
        if (verified == false) extra["fake_verified"] = "false";
        if (error is not null) extra["fake_error"] = error;
        var authorize = await c.Http.GetAsync(ServerPath(QueryHelpers.AddQueryString(authorizeUrl, extra)));
        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);

        var callbackUrl = ServerPath(PathAndQuery(authorize.Headers.Location!));
        if (tamperCallback is not null) callbackUrl = tamperCallback(callbackUrl);
        var callback = await c.Http.GetAsync(callbackUrl);
        return new(start, authorize, callback);
    }

    private static string SetCookie(HttpResponseMessage r, string name)
        => r.Headers.TryGetValues("Set-Cookie", out var values) ? values.FirstOrDefault(v => v.StartsWith(name + "=", StringComparison.Ordinal)) ?? "" : "";

    private static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string> Csrf(ApiClient c)
    {
        var response = await c.Http.GetAsync("/profiles/csrf");
        response.EnsureSuccessStatusCode();
        return (await Json(response)).GetProperty("token").GetString()!;
    }

    private static void AssertNoSession(HttpResponseMessage callback)
        => Assert.DoesNotContain(callback.Headers.TryGetValues("Set-Cookie", out var v) ? v : [], c => c.StartsWith(SessionCookie + "=") && !c.Contains("expires=Thu, 01 Jan 1970"));

    [Fact]
    public async Task Start_redirects_to_authorize_with_state_nonce_challenge_and_redirect_uri_and_sets_a_protected_cookie()
    {
        var f = await NewFactoryAsync();
        var c = f.NewClient();
        var start = await c.Http.GetAsync("/auth/google/start");

        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        var query = QueryHelpers.ParseQuery(start.Headers.Location!.Query);
        Assert.True(query["state"].ToString().Length >= 32);
        Assert.True(query["nonce"].ToString().Length >= 32);
        Assert.True(query["code_challenge"].ToString().Length >= 32);
        Assert.Equal(AuthApiFactory.PublicBaseUrl + "/api/auth/google/callback", query["redirect_uri"].ToString());

        var cookie = SetCookie(start, OidcCookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(query["state"].ToString(), cookie); // protected, not readable
        Assert.Equal("no-store", start.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Sign_in_without_a_profile_issues_a_session_without_profile_id_and_me_says_so()
    {
        var f = await NewFactoryAsync();
        var c = f.NewClient();
        var flow = await SignInAsync(c, returnUrl: "/me?x=1", email: "Nobody@Example.com");

        Assert.Equal(HttpStatusCode.Redirect, flow.Callback!.StatusCode);
        Assert.Equal("/me?x=1", flow.Callback.Headers.Location!.OriginalString);
        var session = SetCookie(flow.Callback, SessionCookie);
        Assert.Contains("samesite=strict", session, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", session, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", session, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(OidcCookie + "=;", SetCookie(flow.Callback, OidcCookie)); // cleared

        var me = await c.Http.GetAsync("/auth/google/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal("no-store", me.Headers.CacheControl?.ToString());
        var json = await Json(me);
        Assert.Equal("nobody@example.com", json.GetProperty("email").GetString());
        Assert.False(json.GetProperty("hasProfile").GetBoolean());
        Assert.False(json.TryGetProperty("profileId", out var pid) && pid.ValueKind != JsonValueKind.Null);

        // A session without profile_id does not pass the participant policy.
        Assert.Equal(HttpStatusCode.Forbidden, (await c.Http.GetAsync("/me/profile")).StatusCode);
    }

    [Fact]
    public async Task Sign_in_with_a_linked_profile_opens_the_existing_me_endpoints_for_that_profile_only()
    {
        var f = await NewFactoryAsync();
        var mine = await SeedProfileAsync(f, "dana@example.com", "dana@example.com", name: "דנה כהן");
        await SeedProfileAsync(f, "other@example.com", "other@example.com", name: "אחרת");

        var c = f.NewClient();
        Assert.Equal(HttpStatusCode.Redirect, (await SignInAsync(c, email: "dana@example.com")).Callback!.StatusCode);

        var me = await Json(await c.Http.GetAsync("/auth/google/me"));
        Assert.True(me.GetProperty("hasProfile").GetBoolean());
        Assert.Equal(mine, me.GetProperty("profileId").GetGuid());

        var own = await c.Http.GetAsync("/me/profile");
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal("דנה כהן", (await Json(own)).GetProperty("name").GetString());

        // The other participant sees only her own profile; there is no way to name another one.
        var c2 = f.NewClient();
        await SignInAsync(c2, email: "other@example.com");
        Assert.Equal("אחרת", (await Json(await c2.Http.GetAsync("/me/profile"))).GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_profile_is_never_linked_by_email_alone()
    {
        var f = await NewFactoryAsync();
        await SeedProfileAsync(f, "dana@example.com", googleEmail: null);
        var c = f.NewClient();
        await SignInAsync(c, email: "dana@example.com");

        Assert.False((await Json(await c.Http.GetAsync("/auth/google/me"))).GetProperty("hasProfile").GetBoolean());
        Assert.Null(await Db(f, d => d.Profiles.Where(p => p.GoogleSub != null).Select(p => p.GoogleSub).FirstOrDefaultAsync()));
    }

    [Fact]
    public async Task A_changed_google_email_updates_the_stored_email_and_advances_contact_version_once()
    {
        var f = await NewFactoryAsync();
        // The stored email differs from the Google one (the sub is that of the Google email).
        var id = await SeedProfileAsync(f, "old@example.com", googleEmail: "new@example.com");
        var c = f.NewClient();

        await SignInAsync(c, email: "new@example.com");
        var after = await Db(f, d => d.ParticipantPrivates.AsNoTracking().SingleAsync(p => p.ProfileId == id));
        Assert.Equal("new@example.com", after.Email);
        Assert.Equal(2, after.ContactVersion);

        await SignInAsync(f.NewClient(), email: "new@example.com"); // unchanged email: no bump
        Assert.Equal(2, (await Db(f, d => d.ParticipantPrivates.AsNoTracking().SingleAsync(p => p.ProfileId == id))).ContactVersion);
    }

    [Fact]
    public async Task An_email_that_clashes_in_the_cohort_rejects_the_sign_in_and_changes_nothing()
    {
        var f = await NewFactoryAsync();
        var first = await SeedProfileAsync(f, "taken@example.com", googleEmail: null);
        var cohortId = await Db(f, d => d.Profiles.AsNoTracking().Where(p => p.Id == first).Select(p => p.CohortId).SingleAsync());
        var second = await SeedProfileAsync(f, "old@example.com", googleEmail: "taken@example.com", cohortId: cohortId);

        var c = f.NewClient();
        var flow = await SignInAsync(c, email: "taken@example.com");

        Assert.Equal(AuthApiFactory.PublicBaseUrl + "/me?signin=failed", flow.Callback!.Headers.Location!.ToString());
        AssertNoSession(flow.Callback);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.Http.GetAsync("/auth/google/me")).StatusCode);
        var row = await Db(f, d => d.ParticipantPrivates.AsNoTracking().SingleAsync(p => p.ProfileId == second));
        Assert.Equal("old@example.com", row.Email);
        Assert.Equal(1, row.ContactVersion);
    }

    [Fact]
    public async Task A_cancelled_sign_in_redirects_with_cancelled_and_no_session()
    {
        var f = await NewFactoryAsync();
        var c = f.NewClient();
        var flow = await SignInAsync(c, error: "access_denied");
        Assert.Equal(AuthApiFactory.PublicBaseUrl + "/me?signin=cancelled", flow.Callback!.Headers.Location!.ToString());
        AssertNoSession(flow.Callback);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.Http.GetAsync("/auth/google/me")).StatusCode);
    }

    [Fact]
    public async Task Another_provider_error_is_a_failure()
    {
        var f = await NewFactoryAsync();
        var flow = await SignInAsync(f.NewClient(), error: "server_error");
        Assert.Equal(AuthApiFactory.PublicBaseUrl + "/me?signin=failed", flow.Callback!.Headers.Location!.ToString());
    }

    [Fact]
    public async Task An_unverified_email_redirects_with_unverified_and_no_session()
    {
        var f = await NewFactoryAsync();
        var c = f.NewClient();
        var flow = await SignInAsync(c, email: "dana@example.com", verified: false);
        Assert.Equal(AuthApiFactory.PublicBaseUrl + "/me?signin=unverified", flow.Callback!.Headers.Location!.ToString());
        AssertNoSession(flow.Callback);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.Http.GetAsync("/auth/google/me")).StatusCode);
    }

    [Fact]
    public async Task A_state_mismatch_or_a_missing_flow_cookie_fails_and_clears_the_cookie()
    {
        var f = await NewFactoryAsync();
        var c = f.NewClient();
        var tampered = await SignInAsync(c, tamperCallback: url => url.Replace("state=", "state=x"));
        Assert.Equal(AuthApiFactory.PublicBaseUrl + "/me?signin=failed", tampered.Callback!.Headers.Location!.ToString());
        AssertNoSession(tampered.Callback);
        Assert.Contains(OidcCookie + "=;", SetCookie(tampered.Callback, OidcCookie));

        // A forged callback from a browser that never started the flow.
        var forged = await f.NewClient().Http.GetAsync("/auth/google/callback?code=abc&state=abc");
        Assert.Equal(AuthApiFactory.PublicBaseUrl + "/me?signin=failed", forged.Headers.Location!.ToString());

        // Even a forged "cancelled" without the state is just a failure.
        var cancelled = await f.NewClient().Http.GetAsync("/auth/google/callback?error=access_denied");
        Assert.Equal(AuthApiFactory.PublicBaseUrl + "/me?signin=failed", cancelled.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Replaying_a_callback_fails_because_the_flow_cookie_is_single_use()
    {
        var f = await NewFactoryAsync();
        var c = f.NewClient();
        string? url = null;
        var first = await SignInAsync(c, tamperCallback: u => url = u);
        Assert.Equal(HttpStatusCode.Redirect, first.Callback!.StatusCode);
        Assert.DoesNotContain("signin=", first.Callback.Headers.Location!.OriginalString);

        var replay = await c.Http.GetAsync(url!);
        Assert.Equal(AuthApiFactory.PublicBaseUrl + "/me?signin=failed", replay.Headers.Location!.ToString());
    }

    [Fact]
    public async Task A_code_without_a_matching_nonce_or_verifier_is_rejected()
    {
        var f = await NewFactoryAsync();
        var c = f.NewClient();
        // The fake authorize is given a nonce that is not the one in the flow cookie.
        var start = await c.Http.GetAsync("/auth/google/start");
        var query = QueryHelpers.ParseQuery(start.Headers.Location!.Query);
        var authorize = await c.Http.GetAsync("/dev-idp/authorize?state=" + Uri.EscapeDataString(query["state"].ToString())
            + "&nonce=other&code_challenge=" + Uri.EscapeDataString(query["code_challenge"].ToString()));
        var callback = await c.Http.GetAsync(ServerPath(PathAndQuery(authorize.Headers.Location!)));
        Assert.Equal(AuthApiFactory.PublicBaseUrl + "/me?signin=failed", callback.Headers.Location!.ToString());
        AssertNoSession(callback);
    }

    [Theory]
    [InlineData("https://evil.example/x")]
    [InlineData("//evil.example/x")]
    [InlineData("/\\evil.example")]
    [InlineData("javascript:alert(1)")]
    [InlineData("me")]
    [InlineData("/redirect?to=http://evil.example")]
    public async Task An_unsafe_return_url_falls_back_to_me(string returnUrl)
    {
        var f = await NewFactoryAsync();
        var flow = await SignInAsync(f.NewClient(), returnUrl: returnUrl);
        Assert.Equal("/me", flow.Callback!.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Me_without_a_session_is_401_and_a_retired_personal_link_session_is_not_a_google_session()
    {
        var f = await NewFactoryAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.NewClient().Http.GetAsync("/auth/google/me")).StatusCode);

        var profileId = await SeedProfileAsync(f, "dana@example.com", googleEmail: null);
        // A cookie with the purpose of the removed personal link (story 6.4) is refused everywhere.
        var c = f.ClientWithForgedSession(profileId, RetiredPersonalLink.Purpose);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.Http.GetAsync("/me/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.Http.GetAsync("/auth/google/me")).StatusCode);
    }

    [Fact]
    public async Task Me_reflects_a_profile_linked_or_removed_after_sign_in_without_signing_in_again()
    {
        var f = await NewFactoryAsync();
        var c = f.NewClient();
        await SignInAsync(c, email: "late@example.com");
        Assert.False((await Json(await c.Http.GetAsync("/auth/google/me"))).GetProperty("hasProfile").GetBoolean());

        var id = await SeedProfileAsync(f, "late@example.com", googleEmail: "late@example.com");
        var linked = await Json(await c.Http.GetAsync("/auth/google/me"));
        Assert.True(linked.GetProperty("hasProfile").GetBoolean());
        Assert.Equal(id, linked.GetProperty("profileId").GetGuid());

        await Db(f, d => d.Database.ExecuteSqlInterpolatedAsync($"UPDATE profiles SET visibility = 'Removed' WHERE id = {id}"));
        var removed = await Json(await c.Http.GetAsync("/auth/google/me"));
        Assert.True(removed.GetProperty("hasProfile").GetBoolean());
        Assert.Equal(id, removed.GetProperty("profileId").GetGuid());
    }

    /// <summary>Puts the state in the authorize URL (so a test can echo it back) and fails the code exchange with an exception.</summary>
    private sealed class ThrowingProvider : ResumeApp.Features.Auth.IOidcProvider
    {
        public Task<Uri> BuildAuthorizeUrlAsync(ResumeApp.Features.Auth.OidcAuthorizeRequest request, CancellationToken ct)
            => Task.FromResult(new Uri("https://idp.test/authorize?state=" + Uri.EscapeDataString(request.State)));

        public Task<ResumeApp.Features.Auth.OidcIdentity?> ExchangeCodeAsync(string code, string codeVerifier, string redirectUri, string expectedNonce, CancellationToken ct)
            => throw new InvalidOperationException("boom");
    }

    [Fact]
    public async Task A_provider_that_throws_during_the_exchange_is_a_failed_sign_in_not_a_500()
    {
        var factory = await AuthApiFactory.CreateAsync(pg, configureServices: s =>
        {
            s.RemoveAll<ResumeApp.Features.Auth.IOidcProvider>();
            s.AddSingleton<ResumeApp.Features.Auth.IOidcProvider, ThrowingProvider>();
        });
        _factories.Add(factory);
        var c = factory.NewClient();
        var start = await c.Http.GetAsync("/auth/google/start");
        var state = QueryHelpers.ParseQuery(start.Headers.Location!.Query)["state"].ToString();

        var callback = await c.Http.GetAsync("/auth/google/callback?code=x&state=" + Uri.EscapeDataString(state));
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal(AuthApiFactory.PublicBaseUrl + "/me?signin=failed", callback.Headers.Location!.ToString());
        AssertNoSession(callback);
    }

    [Fact]
    public async Task A_google_host_resolves_the_real_provider_and_has_no_dev_idp()
    {
        var limits = new Dictionary<string, string>
        {
            ["GOOGLE_PROVIDER"] = "google", ["GOOGLE_CLIENT_ID"] = "id", ["GOOGLE_CLIENT_SECRET"] = "secret", ["GOOGLE_AUTHORITY"] = "https://idp.test",
        };
        var factory = await AuthApiFactory.CreateAsync(pg, limits: limits, configureServices: s =>
            s.AddHttpClient<ResumeApp.Features.Auth.IOidcProvider, GoogleOidcProvider>().ConfigurePrimaryHttpMessageHandler(() => new StubHandler()));
        _factories.Add(factory);

        await using (var scope = factory.Services.CreateAsyncScope())
            Assert.IsType<GoogleOidcProvider>(scope.ServiceProvider.GetRequiredService<ResumeApp.Features.Auth.IOidcProvider>());
        // Not mapped: it answers exactly like any unknown path (the closed-by-default fallback policy turns that into 401), never a redirect.
        var c = factory.NewClient();
        var devIdp = await c.Http.GetAsync("/dev-idp/authorize?state=x");
        Assert.Null(devIdp.Headers.Location);
        Assert.Equal((await c.Http.GetAsync("/no-such-endpoint")).StatusCode, devIdp.StatusCode);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }

    [Fact]
    public async Task Logout_needs_anti_forgery_and_clears_the_session()
    {
        var f = await NewFactoryAsync();
        var c = f.NewClient();
        await SignInAsync(c);
        Assert.Equal(HttpStatusCode.OK, (await c.Http.GetAsync("/auth/google/me")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync("/auth/google/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.Http.GetAsync("/auth/google/me")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync("/auth/google/logout", null, await Csrf(c))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.Http.GetAsync("/auth/google/me")).StatusCode);
    }

    [Fact]
    public async Task Logout_rejects_a_foreign_origin()
    {
        var f = await NewFactoryAsync();
        var c = f.NewClient();
        await SignInAsync(c);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync("/auth/google/logout", null, await Csrf(c), origin: "https://evil.example")).StatusCode);
    }

    [Fact]
    public async Task Start_and_callback_are_rate_limited_per_ip()
    {
        var f = await NewFactoryAsync(new Dictionary<string, string> { ["RATE_LIMIT_GOOGLE_AUTH_IP"] = "2" });
        var c = f.NewClient();
        Assert.DoesNotContain("signin=", (await c.Http.GetAsync("/auth/google/start")).Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.Redirect, (await c.Http.GetAsync("/auth/google/callback")).StatusCode);
        // Blocked: a redirect back to the app (a browser navigation), never raw problem JSON.
        foreach (var path in new[] { "/auth/google/start", "/auth/google/callback" })
        {
            var blocked = await c.Http.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, blocked.StatusCode);
            Assert.Equal(AuthApiFactory.PublicBaseUrl + "/me?signin=failed", blocked.Headers.Location!.ToString());
        }
    }

    [Fact]
    public async Task Neither_responses_nor_logs_carry_the_code_state_or_email()
    {
        var f = await NewFactoryAsync();
        await SeedProfileAsync(f, "dana@example.com", "dana@example.com");
        var c = f.NewClient();

        var start = await c.Http.GetAsync("/auth/google/start");
        var query = QueryHelpers.ParseQuery(start.Headers.Location!.Query);
        var state = query["state"].ToString();
        var authorize = await c.Http.GetAsync(ServerPath(QueryHelpers.AddQueryString(PathAndQuery(start.Headers.Location!),
            new Dictionary<string, string?> { ["fake_email"] = "dana@example.com" })));
        var callbackUrl = ServerPath(PathAndQuery(authorize.Headers.Location!));
        var code = QueryHelpers.ParseQuery(new Uri("http://x" + callbackUrl).Query)["code"].ToString();
        var callback = await c.Http.GetAsync(callbackUrl);
        var me = await c.Http.GetAsync("/auth/google/me");
        var logout = await c.PostAsync("/auth/google/logout", null, await Csrf(c));

        foreach (var response in new[] { callback, me, logout })
        {
            var text = response + await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(code, text);
            Assert.DoesNotContain(state, text);
            Assert.DoesNotContain("id_token", text);
        }

        Assert.NotEmpty(_log.Lines);
        foreach (var line in _log.Lines)
        {
            Assert.DoesNotContain(code, line);
            Assert.DoesNotContain(state, line);
            Assert.DoesNotContain("id_token", line);
            Assert.DoesNotContain("dana@example.com", line);
        }
    }

    [Fact]
    public async Task The_dev_identity_provider_defaults_to_a_fixed_participant()
    {
        var f = await NewFactoryAsync();
        var c = f.NewClient();
        await SignInAsync(c);
        Assert.Equal(FakeOidcProvider.DefaultEmail, (await Json(await c.Http.GetAsync("/auth/google/me"))).GetProperty("email").GetString());
    }

    // ---- options -------------------------------------------------------------------------------------------------------------------

    private static IConfiguration Config(params (string, string?)[] values)
        => new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Item1, v.Item2))).Build();

    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "t";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private const string Base = "https://app.example.com";

    [Fact]
    public void Development_defaults_to_the_fake_provider_and_builds_the_callback_url()
    {
        var o = GoogleAuthOptions.Resolve(Config(), new Env("Development"), "http://localhost:4200/");
        Assert.Equal(GoogleProvider.Fake, o.Provider);
        Assert.Equal("http://localhost:4200/api/auth/google/callback", o.RedirectUri);
        Assert.Equal("https://accounts.google.com", o.Authority);
    }

    [Fact]
    public void Outside_development_the_real_provider_with_id_and_secret_is_required()
    {
        var production = new Env("Production");
        Assert.Throws<InvalidOperationException>(() => GoogleAuthOptions.Resolve(Config(), production, Base));
        Assert.Throws<InvalidOperationException>(() => GoogleAuthOptions.Resolve(Config(("GOOGLE_PROVIDER", "fake")), production, Base));
        Assert.Throws<InvalidOperationException>(() => GoogleAuthOptions.Resolve(Config(("GOOGLE_PROVIDER", "google")), production, Base));
        Assert.Throws<InvalidOperationException>(() => GoogleAuthOptions.Resolve(Config(("GOOGLE_PROVIDER", "google"), ("GOOGLE_CLIENT_ID", "id")), production, Base));
        Assert.Throws<InvalidOperationException>(() => GoogleAuthOptions.Resolve(Config(("GOOGLE_PROVIDER", "google"), ("GOOGLE_CLIENT_SECRET", "s")), production, Base));
        Assert.Throws<InvalidOperationException>(() => GoogleAuthOptions.Resolve(Config(("GOOGLE_PROVIDER", "nope")), new Env("Development"), Base));

        var ok = GoogleAuthOptions.Resolve(Config(("GOOGLE_PROVIDER", "google"), ("GOOGLE_CLIENT_ID", "id"), ("GOOGLE_CLIENT_SECRET", "s")), production, Base);
        Assert.Equal(GoogleProvider.Google, ok.Provider);
        Assert.Equal(Base + "/api/auth/google/callback", ok.RedirectUri);
    }

    [Fact]
    public void A_selected_google_provider_needs_credentials_even_in_development()
        => Assert.Throws<InvalidOperationException>(() => GoogleAuthOptions.Resolve(Config(("GOOGLE_PROVIDER", "google")), new Env("Development"), Base));
}
