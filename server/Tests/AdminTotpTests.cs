using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ResumeApp.Tests;

[Collection("env")]
public class AdminTotpTests(PostgresFixture pg) : IDisposable
{
    private const string NewPassword = "Brand-New-Pass-42#Lk";
    private readonly List<AuthApiFactory> _factories = [];

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
    }

    private async Task<AuthApiFactory> CreateFactoryAsync(string? adminUser2 = null)
    {
        var factory = await AuthApiFactory.CreateAsync(pg, adminUser2: adminUser2);
        _factories.Add(factory);
        return factory;
    }

    private async Task<(AuthApiFactory Factory, string Key, string[] Codes)> EnrolledAsync()
    {
        var factory = await CreateFactoryAsync();
        var (key, codes) = await factory.NewClient().EnrollAsync(AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword, NewPassword);
        return (factory, key, codes);
    }

    private static async Task<string> Shape(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var headers = string.Join(";", response.Headers.Select(h => h.Key).Where(k => k != "Date").OrderBy(k => k));
        return $"{(int)response.StatusCode}|{response.Content.Headers.ContentType}|{json.GetProperty("title")}|{json.GetProperty("status")}|{headers}";
    }

    [Fact]
    public async Task Ping_is_403_until_enrolment_completes_then_200()
    {
        var factory = await CreateFactoryAsync();
        var client = factory.NewClient();
        await client.LoginAsync(AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword);
        await client.PostWithCsrfAsync("/auth/change-password",
            new { currentPassword = AuthApiFactory.InitialPassword, newPassword = NewPassword });

        var blocked = await client.Http.GetAsync("/admin/ping");
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        var problem = await blocked.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("totp-setup-required", problem.GetProperty("code").GetString());
        // Always-open endpoints stay reachable.
        Assert.Equal(HttpStatusCode.OK, (await client.Http.GetAsync("/auth/me")).StatusCode);

        var setup = await client.PostWithCsrfAsync("/auth/totp/setup", null);
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        var body = await setup.Content.ReadFromJsonAsync<JsonElement>();
        var key = body.GetProperty("key").GetString()!;
        Assert.StartsWith("otpauth://totp/", body.GetProperty("uri").GetString());
        Assert.Contains("secret=" + key, body.GetProperty("uri").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.Http.GetAsync("/admin/ping")).StatusCode);

        var enable = await client.PostWithCsrfAsync("/auth/totp/enable", new { code = Totp.Code(key) });
        Assert.Equal(HttpStatusCode.OK, enable.StatusCode);
        var codes = (await enable.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("recoveryCodes");
        Assert.Equal(10, codes.GetArrayLength());

        Assert.Equal(HttpStatusCode.OK, (await client.Http.GetAsync("/admin/ping")).StatusCode);
    }

    [Fact]
    public async Task Setup_is_blocked_while_password_change_is_pending()
    {
        var factory = await CreateFactoryAsync();
        var client = factory.NewClient();
        await client.LoginAsync(AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostWithCsrfAsync("/auth/totp/setup", null)).StatusCode);
    }

    [Fact]
    public async Task Wrong_enable_code_is_a_validation_error_and_counts_toward_lockout()
    {
        var factory = await CreateFactoryAsync();
        var client = factory.NewClient();
        await client.LoginAsync(AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword);
        await client.PostWithCsrfAsync("/auth/change-password",
            new { currentPassword = AuthApiFactory.InitialPassword, newPassword = NewPassword });
        var key = (await (await client.PostWithCsrfAsync("/auth/totp/setup", null)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("key").GetString()!;
        var bad = Totp.Code(key, stepOffset: 120);

        var first = await client.PostWithCsrfAsync("/auth/totp/enable", new { code = bad });
        Assert.Equal(HttpStatusCode.BadRequest, first.StatusCode);
        Assert.True((await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("code", out _));

        for (var i = 0; i < 4; i++) await client.PostWithCsrfAsync("/auth/totp/enable", new { code = bad });

        // Locked now: even the correct code is refused.
        var locked = await client.PostWithCsrfAsync("/auth/totp/enable", new { code = Totp.Code(key) });
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
    }

    [Fact]
    public async Task Login_with_totp_succeeds_and_sets_cookie()
    {
        var (factory, key, _) = await EnrolledAsync();
        var client = factory.NewClient();

        var response = await client.LoginAsync(AuthApiFactory.AdminUser, NewPassword, code: Totp.Code(key));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(ApiClient.SessionCookieHeader(response));
        Assert.Equal(HttpStatusCode.OK, (await client.Http.GetAsync("/admin/ping")).StatusCode);
    }

    [Fact]
    public async Task Wrong_or_missing_code_looks_identical_to_wrong_password()
    {
        var (factory, key, _) = await EnrolledAsync();
        var bad = Totp.Code(key, stepOffset: 120);

        var wrongPassword = await factory.NewClient().LoginAsync(AuthApiFactory.AdminUser, "not-the-password-1", code: Totp.Code(key));
        var wrongCode = await factory.NewClient().LoginAsync(AuthApiFactory.AdminUser, NewPassword, code: bad);
        var missingCode = await factory.NewClient().LoginAsync(AuthApiFactory.AdminUser, NewPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, wrongCode.StatusCode);
        Assert.Null(ApiClient.SessionCookieHeader(wrongCode));
        Assert.Null(ApiClient.SessionCookieHeader(missingCode));
        var expected = await Shape(wrongPassword);
        Assert.Equal(expected, await Shape(wrongCode));
        Assert.Equal(expected, await Shape(missingCode));
    }

    [Fact]
    public async Task Five_wrong_codes_lock_the_account()
    {
        var (factory, key, _) = await EnrolledAsync();
        var bad = Totp.Code(key, stepOffset: 120);

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await factory.NewClient().LoginAsync(AuthApiFactory.AdminUser, NewPassword, code: bad)).StatusCode);
        }

        var locked = await factory.NewClient().LoginAsync(AuthApiFactory.AdminUser, NewPassword, code: Totp.Code(key));
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        Assert.Null(ApiClient.SessionCookieHeader(locked));
    }

    [Fact]
    public async Task Recovery_code_works_once_only()
    {
        var (factory, _, codes) = await EnrolledAsync();

        var first = await factory.NewClient().LoginAsync(AuthApiFactory.AdminUser, NewPassword, recoveryCode: codes[0]);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var again = await factory.NewClient().LoginAsync(AuthApiFactory.AdminUser, NewPassword, recoveryCode: codes[0]);
        Assert.Equal(HttpStatusCode.Unauthorized, again.StatusCode);

        // A different unused code still works.
        var other = await factory.NewClient().LoginAsync(AuthApiFactory.AdminUser, NewPassword, recoveryCode: codes[1]);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }

    [Fact]
    public async Task Setup_and_enable_are_refused_once_enabled()
    {
        var (factory, key, _) = await EnrolledAsync();
        var client = factory.NewClient();
        await client.LoginAsync(AuthApiFactory.AdminUser, NewPassword, code: Totp.Code(key));

        Assert.Equal(HttpStatusCode.Conflict, (await client.PostWithCsrfAsync("/auth/totp/setup", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostWithCsrfAsync("/auth/totp/enable", new { code = Totp.Code(key) })).StatusCode);
    }

    [Fact]
    public async Task Second_admin_is_seeded_once_and_a_restart_adds_none()
    {
        var factory = await CreateFactoryAsync(adminUser2: "admin-two");
        Assert.Equal(2, await factory.CountAdminsAsync());

        var client = factory.NewClient();
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync("admin-two", AuthApiFactory.InitialPassword2)).StatusCode);

        using var again = new AuthApiFactory(factory.DatabaseUrl, adminUser2: "admin-two");
        Assert.Equal(2, await again.CountAdminsAsync());
    }

    [Fact]
    public async Task Second_admin_added_later_is_seeded_on_next_start()
    {
        var factory = await CreateFactoryAsync();
        Assert.Equal(1, await factory.CountAdminsAsync());

        using var later = new AuthApiFactory(factory.DatabaseUrl, adminUser2: "admin-two");
        Assert.Equal(2, await later.CountAdminsAsync());
    }

    [Fact]
    public async Task Five_wrong_recovery_codes_lock_the_account()
    {
        var (factory, key, _) = await EnrolledAsync();

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await factory.NewClient().LoginAsync(AuthApiFactory.AdminUser, NewPassword, recoveryCode: "WRONG-CODE-" + i)).StatusCode);
        }

        var locked = await factory.NewClient().LoginAsync(AuthApiFactory.AdminUser, NewPassword, code: Totp.Code(key));
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        Assert.Null(ApiClient.SessionCookieHeader(locked));
    }

    [Fact]
    public async Task TotpSetupRequired_flag_tracks_enrolment()
    {
        var factory = await CreateFactoryAsync();
        var client = factory.NewClient();

        var login = await client.LoginAsync(AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword);
        Assert.True((await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("totpSetupRequired").GetBoolean());
        var me = await client.Http.GetAsync("/auth/me");
        Assert.True((await me.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("totpSetupRequired").GetBoolean());

        var (key, _) = await factory.NewClient().EnrollAsync(AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword, NewPassword);
        var fresh = factory.NewClient();
        var totpLogin = await fresh.LoginAsync(AuthApiFactory.AdminUser, NewPassword, code: Totp.Code(key));
        Assert.False((await totpLogin.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("totpSetupRequired").GetBoolean());
        var meAfter = await fresh.Http.GetAsync("/auth/me");
        Assert.False((await meAfter.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("totpSetupRequired").GetBoolean());
    }

    [Fact]
    public async Task Enable_before_setup_is_a_validation_error()
    {
        var factory = await CreateFactoryAsync();
        var client = factory.NewClient();
        await client.LoginAsync(AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword);
        await client.PostWithCsrfAsync("/auth/change-password",
            new { currentPassword = AuthApiFactory.InitialPassword, newPassword = NewPassword });

        var response = await client.PostWithCsrfAsync("/auth/totp/enable", new { code = "123456" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("code", out _));
    }

    [Fact]
    public async Task Setup_twice_returns_the_same_key()
    {
        var factory = await CreateFactoryAsync();
        var client = factory.NewClient();
        await client.LoginAsync(AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword);
        await client.PostWithCsrfAsync("/auth/change-password",
            new { currentPassword = AuthApiFactory.InitialPassword, newPassword = NewPassword });

        var first = await (await client.PostWithCsrfAsync("/auth/totp/setup", null)).Content.ReadFromJsonAsync<JsonElement>();
        var second = await (await client.PostWithCsrfAsync("/auth/totp/setup", null)).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(first.GetProperty("key").GetString(), second.GetProperty("key").GetString());
    }

    [Fact]
    public async Task Missing_second_admin_variables_are_skipped()
    {
        var factory = await CreateFactoryAsync();
        Assert.Equal(1, await factory.CountAdminsAsync());
    }
}
