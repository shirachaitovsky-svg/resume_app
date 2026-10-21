using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace ResumeApp.Tests;

[Collection("env")]
public class AdminAuthTests(PostgresFixture pg) : IDisposable
{
    private readonly List<AuthApiFactory> _factories = [];

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
    }

    private async Task<AuthApiFactory> CreateFactoryAsync(string adminUser = AuthApiFactory.AdminUser, string? allowedOrigins = null)
    {
        var factory = await AuthApiFactory.CreateAsync(pg, adminUser, allowedOrigins);
        _factories.Add(factory);
        return factory;
    }

    private const string NewPassword = "Brand-New-Pass-42#Lk";

    private async Task<(AuthApiFactory Factory, ApiClient Client)> StartAsync()
    {
        var factory = await CreateFactoryAsync();
        return (factory, factory.NewClient());
    }

    private static async Task ChangeInitialPasswordAsync(ApiClient client, string newPassword = NewPassword)
    {
        var response = await client.PostWithCsrfAsync("/auth/change-password",
            new { currentPassword = AuthApiFactory.InitialPassword, newPassword });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<string> TotpKeyAsync(AuthApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ResumeApp.Infrastructure.Identity.AppUser>>();
        var user = (await users.FindByNameAsync(AuthApiFactory.AdminUser))!;
        return (await users.GetAuthenticatorKeyAsync(user))!;
    }

    private static async Task EnrollTotpAsync(ApiClient client)
    {
        var key = (await (await client.PostWithCsrfAsync("/auth/totp/setup", null)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("key").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await client.PostWithCsrfAsync("/auth/totp/enable", new { code = Totp.Code(key) })).StatusCode);
    }

    [Fact]
    public async Task Seed_creates_one_admin_with_must_change_and_is_idempotent()
    {
        var (factory, client) = await StartAsync();
        Assert.Equal(1, await factory.CountAdminsAsync());

        var login = await client.LoginAsync(AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword);
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("mustChangePassword").GetBoolean());

        // A second start on the same database with a different ADMIN_USERNAME must not add another admin.
        using var again = new AuthApiFactory(factory.DatabaseUrl, adminUser: "someone-else");
        Assert.Equal(1, await again.CountAdminsAsync());
    }

    [Fact]
    public async Task Anonymous_ping_is_401()
    {
        var (_, client) = await StartAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.Http.GetAsync("/admin/ping")).StatusCode);
    }

    [Fact]
    public async Task Unmatched_path_returns_401_for_anonymous()
    {
        var (_, client) = await StartAsync();
        // The fallback policy also covers unmatched paths, so anonymous callers learn nothing about what exists.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.Http.GetAsync("/does-not-exist")).StatusCode);
    }

    [Fact]
    public async Task Endpoint_without_AllowAnonymous_is_closed_by_fallback_policy()
    {
        var (_, client) = await StartAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.Http.GetAsync("/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Healthz_stays_anonymous()
    {
        var (_, client) = await StartAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.Http.GetAsync("/healthz")).StatusCode);
    }

    [Fact]
    public async Task Login_sets_hardened_cookie_and_no_store()
    {
        var (_, client) = await StartAsync();

        var response = await client.LoginAsync(AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = ApiClient.SessionCookieHeader(response);
        Assert.NotNull(cookie);
        var lower = cookie!.ToLowerInvariant();
        Assert.StartsWith("__host-", lower);
        Assert.Contains("httponly", lower);
        Assert.Contains("secure", lower);
        Assert.Contains("samesite=strict", lower);
        Assert.Contains("path=/", lower);
        Assert.DoesNotContain("expires", lower);
        Assert.DoesNotContain("max-age", lower);
        Assert.DoesNotContain("domain", lower);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task One_time_password_blocks_everything_until_changed_then_admin_gets_200()
    {
        var (_, client) = await StartAsync();
        await client.LoginAsync(AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword);

        var blocked = await client.Http.GetAsync("/admin/ping");
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.Http.GetAsync("/auth/me")).StatusCode);

        await ChangeInitialPasswordAsync(client);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.Http.GetAsync("/admin/ping")).StatusCode); // TOTP still missing
        await EnrollTotpAsync(client);

        var ok = await client.Http.GetAsync("/admin/ping");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Contains("no-store", ok.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task Changed_password_is_what_logs_in()
    {
        var (factory, client) = await StartAsync();
        await client.LoginAsync(AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword);
        await ChangeInitialPasswordAsync(client);
        await EnrollTotpAsync(client);
        await client.PostWithCsrfAsync("/auth/logout", null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.Http.GetAsync("/admin/ping")).StatusCode);

        var fresh = factory.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await fresh.LoginAsync(AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword)).StatusCode);
        var key = await TotpKeyAsync(factory);
        Assert.Equal(HttpStatusCode.OK, (await fresh.LoginAsync(AuthApiFactory.AdminUser, NewPassword, code: Totp.Code(key))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fresh.Http.GetAsync("/admin/ping")).StatusCode);
    }

    [Fact]
    public async Task Logged_in_user_without_admin_role_gets_403()
    {
        var (factory, client) = await StartAsync();
        await factory.AddUserAsync("viewer", "Zebra-Pass-77!Zxcv", admin: false);

        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync("viewer", "Zebra-Pass-77!Zxcv")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.Http.GetAsync("/admin/ping")).StatusCode);
    }

    [Theory]
    [InlineData("Short1!")]
    [InlineData("passwordpassword")]
    [InlineData("Password123456")]
    [InlineData("xxadminxxxxxxxxx")]
    public async Task Weak_passwords_are_rejected_with_problem_json(string weak)
    {
        var (_, client) = await StartAsync();
        await client.LoginAsync(AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword);

        var response = await client.PostWithCsrfAsync("/auth/change-password",
            new { currentPassword = AuthApiFactory.InitialPassword, newPassword = weak });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("errors").TryGetProperty("newPassword", out _));
    }

    [Fact]
    public async Task Lockout_after_five_failures_is_indistinguishable_from_unknown_user()
    {
        var (_, client) = await StartAsync();

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.LoginAsync(AuthApiFactory.AdminUser, "wrong-password-" + i)).StatusCode);
        }

        // Correct password is refused while locked.
        var locked = await client.LoginAsync(AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword);
        var unknown = await client.LoginAsync("no-such-user", "whatever-password-1");

        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Null(ApiClient.SessionCookieHeader(locked));
        Assert.Equal(await Shape(unknown), await Shape(locked));
    }

    private static async Task<string> Shape(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return $"{(int)response.StatusCode}|{response.Content.Headers.ContentType}|{json.GetProperty("title")}|{json.GetProperty("status")}";
    }

    [Fact]
    public async Task Mutating_request_without_antiforgery_token_is_rejected()
    {
        var (_, client) = await StartAsync();

        var response = await client.PostAsync("/auth/login",
            new { username = AuthApiFactory.AdminUser, password = AuthApiFactory.InitialPassword }, csrf: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(ApiClient.SessionCookieHeader(response));
    }

    [Fact]
    public async Task Mutating_request_with_foreign_origin_is_rejected()
    {
        var (_, client) = await StartAsync();
        var csrf = await client.GetCsrfAsync();

        var response = await client.PostAsync("/auth/login",
            new { username = AuthApiFactory.AdminUser, password = AuthApiFactory.InitialPassword }, csrf, origin: "https://evil.example");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Mutating_request_without_origin_is_rejected()
    {
        var (_, client) = await StartAsync();
        var csrf = await client.GetCsrfAsync();

        var response = await client.PostAsync("/auth/login",
            new { username = AuthApiFactory.AdminUser, password = AuthApiFactory.InitialPassword }, csrf, origin: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Session_cookie_survives_server_restart()
    {
        var (factory, client) = await StartAsync();
        var login = await client.LoginAsync(AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword);
        await ChangeInitialPasswordAsync(client);
        await EnrollTotpAsync(client);
        var refreshed = await client.PostWithCsrfAsync("/auth/logout", null); // sanity: endpoint reachable
        Assert.Equal(HttpStatusCode.NoContent, refreshed.StatusCode);

        // Log in again with the new password to get a fully valid cookie, then "restart" on the same database.
        var second = await client.LoginAsync(AuthApiFactory.AdminUser, NewPassword, code: Totp.Code(await TotpKeyAsync(factory)));
        var setCookie = ApiClient.SessionCookieHeader(second)!;
        var cookieValue = setCookie.Split(';')[0];
        var databaseUrl = factory.DatabaseUrl;
        Assert.NotNull(login);
        factory.Dispose();

        using var restarted = new AuthApiFactory(databaseUrl);
        var fresh = restarted.NewClient(handleCookies: false);
        var request = new HttpRequestMessage(HttpMethod.Get, "/admin/ping");
        request.Headers.Add("Cookie", cookieValue);
        var response = await fresh.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Repeated_wrong_current_password_locks_the_account()
    {
        var (_, client) = await StartAsync();
        await client.LoginAsync(AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword);

        for (var i = 0; i < 5; i++)
        {
            await client.PostWithCsrfAsync("/auth/change-password",
                new { currentPassword = "wrong-current-" + i, newPassword = NewPassword });
        }

        // Even the correct current password is refused uniformly while locked.
        var response = await client.PostWithCsrfAsync("/auth/change-password",
            new { currentPassword = AuthApiFactory.InitialPassword, newPassword = NewPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var fresh = (await CreateFactoryAsync()).NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await fresh.LoginAsync("no-such-user", "x")).StatusCode);
    }

    [Fact]
    public async Task Overlong_passwords_are_rejected_before_hashing()
    {
        var (_, client) = await StartAsync();
        var huge = new string('a', 257);

        var login = await client.LoginAsync(AuthApiFactory.AdminUser, huge);
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);

        await client.LoginAsync(AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword);
        var change = await client.PostWithCsrfAsync("/auth/change-password",
            new { currentPassword = AuthApiFactory.InitialPassword, newPassword = huge });
        Assert.Equal(HttpStatusCode.BadRequest, change.StatusCode);
    }

    [Fact]
    public async Task Allowed_origins_are_honoured_case_insensitively_and_others_rejected()
    {
        var factory = await CreateFactoryAsync(allowedOrigins: "https://app.example.com/");
        var client = factory.NewClient();
        var csrf = await client.GetCsrfAsync();
        var body = new { username = AuthApiFactory.AdminUser, password = AuthApiFactory.InitialPassword };

        var allowed = await client.PostAsync("/auth/login", body, csrf, origin: "https://APP.example.com");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);

        var other = factory.NewClient();
        var otherCsrf = await other.GetCsrfAsync();
        var rejected = await other.PostAsync("/auth/login", body, otherCsrf, origin: "https://other.example.com");
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
    }
}
