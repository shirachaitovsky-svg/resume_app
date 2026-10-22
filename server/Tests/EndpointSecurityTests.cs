using System.Net.Http.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ResumeApp.Api.Security;
using ResumeApp.Infrastructure;

namespace ResumeApp.Tests;

/// <summary>
/// Story 4.4 (AR9): every endpoint is closed by default. The allowlist below is the one place that names the anonymous endpoints;
/// adding or removing one fails this test until the list is updated on purpose. Every other endpoint must carry the Admin or
/// Participant policy explicitly, not only the fallback policy.
/// </summary>
[Collection("env")]
public class EndpointSecurityTests(PostgresFixture pg) : IDisposable
{
    private readonly List<AuthApiFactory> _factories = [];

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
    }

    /// <summary>Method and route pattern of every endpoint that is deliberately public (AllowAnonymous).</summary>
    internal static readonly string[] AnonymousAllowlist =
    [
        "GET /healthz",
        "GET /auth/csrf",
        "POST /auth/login",
        "GET /auth/google/start",
        "GET /auth/google/callback",
        "GET /auth/google/me",
        "POST /auth/google/logout",
        "GET /dev-idp/authorize", // only mapped when GOOGLE_PROVIDER=fake (Development and tests)
        "GET /profiles/csrf",
        "GET /profiles",
        "GET /profiles/{profileId}",
        "GET /profiles/{profileId}/photo",
        "GET /cohorts",
        "GET /cohorts/active",
        "GET /invites/{token}",
        // Anonymous at the routing level on purpose: it authenticates itself with the Google sign-in session (no profile yet) and answers 401 without one,
        // so that a missed session never consumes the IP budget. The "/me" group (Participant policy) is a different set of routes. See Post_me_profile_without_a_session_is_rejected.
        "POST /me/profile",
        "POST /profiles/{profileId}/contact-requests",
        "POST /contact-requests/verify",
        "POST /contact-decisions/preview",
        "POST /contact-decisions/decide",
    ];

    private static readonly string[] ExplicitPolicies = [IdentityServiceExtensions.AdminPolicy, IdentityServiceExtensions.ParticipantPolicy];

    /// <summary>True when <paramref name="path"/> is the area itself or below it (a segment boundary, so "/me" never matches "/metrics").</summary>
    internal static bool InArea(string path, string area)
        => path.Equals(area, StringComparison.Ordinal) || path.StartsWith(area + "/", StringComparison.Ordinal);

    private static string Normalize(string pattern) => "/" + pattern.Trim('/').Replace("//", "/");

    private static string Key(RouteEndpoint e)
    {
        var methods = e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
        var method = methods is { Count: > 0 } ? string.Join("|", methods.OrderBy(m => m, StringComparer.Ordinal)) : "*";
        return $"{method} {Normalize(e.RoutePattern.RawText ?? "")}";
    }

    private static List<RouteEndpoint> RouteEndpoints(AuthApiFactory factory)
        => factory.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<RouteEndpoint>().ToList();

    [Fact]
    public async Task Anonymous_endpoints_match_the_explicit_allowlist_and_every_other_endpoint_has_an_explicit_policy()
    {
        var factory = await AuthApiFactory.CreateAsync(pg);
        _factories.Add(factory);
        var endpoints = RouteEndpoints(factory);
        Assert.NotEmpty(endpoints);

        var anonymous = endpoints.Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null).Select(Key).Distinct().OrderBy(k => k, StringComparer.Ordinal).ToList();
        var allowed = AnonymousAllowlist.OrderBy(k => k, StringComparer.Ordinal).ToList();

        var added = anonymous.Except(allowed).ToList();
        var missing = allowed.Except(anonymous).ToList();
        Assert.True(added.Count == 0 && missing.Count == 0,
            $"Anonymous endpoints changed. New anonymous (add to the allowlist on purpose, or remove AllowAnonymous): [{string.Join("; ", added)}]. "
            + $"In the allowlist but not mapped (remove from the allowlist): [{string.Join("; ", missing)}].");

        var unprotected = endpoints
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .Where(e => !e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(a => a.Policy is { } p && ExplicitPolicies.Contains(p)))
            .Select(Key).Distinct().OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.True(unprotected.Count == 0,
            $"Endpoints without an explicit Admin or Participant policy (the fallback policy is not enough): [{string.Join("; ", unprotected)}].");
    }

    [Fact]
    public async Task Admin_and_participant_routes_are_protected_by_their_own_policy()
    {
        var factory = await AuthApiFactory.CreateAsync(pg);
        _factories.Add(factory);

        foreach (var e in RouteEndpoints(factory).Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null))
        {
            var path = Normalize(e.RoutePattern.RawText ?? "");
            var policies = e.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).ToList();
            if (InArea(path, "/admin") || InArea(path, "/auth") || InArea(path, "/healthz"))
                Assert.True(policies.Contains(IdentityServiceExtensions.AdminPolicy), $"{Key(e)} must require the Admin policy");
            if (InArea(path, "/me"))
                Assert.True(policies.Contains(IdentityServiceExtensions.ParticipantPolicy), $"{Key(e)} must require the Participant policy");
        }
    }

    /// <summary>Behaviour check: an anonymous caller gets 401 on these protected endpoints. The explicit-policy requirement is proved by the metadata tests above, not here.</summary>
    [Theory]
    [InlineData("GET", "/auth/me")]
    [InlineData("POST", "/auth/logout")]
    [InlineData("POST", "/auth/change-password")]
    [InlineData("POST", "/auth/totp/setup")]
    [InlineData("POST", "/auth/totp/enable")]
    [InlineData("GET", "/admin/ping")]
    [InlineData("GET", "/me/profile")]
    public async Task Protected_endpoints_reject_an_anonymous_caller(string method, string path)
    {
        var factory = await AuthApiFactory.CreateAsync(pg);
        _factories.Add(factory);
        var client = factory.NewClient();
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST") request.Headers.Add("X-CSRF-TOKEN", await client.GetCsrfAsync());
        request.Headers.Add("Origin", ApiClient.Origin);
        if (method == "POST") request.Content = JsonContent.Create(new { });

        var response = await client.Http.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }
    [Fact]
    public async Task Post_me_profile_without_a_session_is_rejected()
    {
        var factory = await AuthApiFactory.CreateAsync(pg);
        _factories.Add(factory);
        var client = factory.NewClient();

        var response = await client.PostWithCsrfAsync("/me/profile", new { });

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static void AssertRejected(HttpResponseMessage response, string what)
        => Assert.True(response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden, $"{what} answered {(int)response.StatusCode}");

    [Fact]
    public async Task A_participant_session_cannot_reach_admin_or_auth_endpoints()
    {
        var factory = await AuthApiFactory.CreateAsync(pg);
        _factories.Add(factory);
        var seeded = await ParticipantSeeding.SeedProfileAsync(factory, status: ResumeApp.Domain.Profiles.RevisionStatus.Approved);
        var participant = await factory.SignedInAsync(seeded);
        Assert.Equal(System.Net.HttpStatusCode.OK, (await participant.Http.GetAsync("/me/profile")).StatusCode); // the session itself is valid

        foreach (var path in new[] { "/admin/ping", "/admin/submissions", "/admin/audit-log", "/healthz/details", "/auth/me" })
            AssertRejected(await participant.Http.GetAsync(path), "participant GET " + path);
        AssertRejected(await participant.PostWithCsrfAsync("/auth/logout", null), "participant POST /auth/logout");
        AssertRejected(await participant.PostWithCsrfAsync("/auth/totp/setup", null), "participant POST /auth/totp/setup");
    }

    [Fact]
    public async Task An_admin_session_cannot_reach_participant_endpoints()
    {
        var factory = await AuthApiFactory.CreateAsync(pg);
        _factories.Add(factory);
        var admin = factory.NewClient();
        await admin.EnrollAsync(AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword, "Brand-New-Pass-42#Lk");
        Assert.Equal(System.Net.HttpStatusCode.OK, (await admin.Http.GetAsync("/admin/ping")).StatusCode);

        AssertRejected(await admin.Http.GetAsync("/me/profile"), "admin GET /me/profile");
        AssertRejected(await admin.PostWithCsrfAsync("/me/profile", new { }), "admin POST /me/profile");
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
    }

    [Fact]
    public async Task Dev_idp_authorize_is_not_mapped_when_the_provider_is_google()
    {
        var limits = new Dictionary<string, string>
        {
            ["GOOGLE_PROVIDER"] = "google", ["GOOGLE_CLIENT_ID"] = "id", ["GOOGLE_CLIENT_SECRET"] = "secret", ["GOOGLE_AUTHORITY"] = "https://idp.test",
        };
        var factory = await AuthApiFactory.CreateAsync(pg, limits: limits, configureServices: s =>
            s.AddHttpClient<ResumeApp.Features.Auth.IOidcProvider, GoogleOidcProvider>().ConfigurePrimaryHttpMessageHandler(() => new StubHandler()));
        _factories.Add(factory);

        Assert.DoesNotContain(RouteEndpoints(factory), e => Normalize(e.RoutePattern.RawText ?? "") == "/dev-idp/authorize");
        var response = await factory.NewClient().Http.GetAsync("/dev-idp/authorize?state=x");
        Assert.Null(response.Headers.Location);
        // Not mapped: it answers exactly like an unknown path (this host's closed-by-default pipeline answers unknown paths with 401, not 404).
        var client = factory.NewClient();
        Assert.Equal((await client.Http.GetAsync("/no-such-endpoint")).StatusCode, response.StatusCode);
        Assert.NotEqual(System.Net.HttpStatusCode.OK, response.StatusCode);
    }
}
