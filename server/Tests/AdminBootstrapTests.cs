using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ResumeApp.Tests;

/// <summary>
/// Story 4.4 (NFR6): two admins are created from the two pairs of environment variables, each must replace the one-time password and
/// enrol in TOTP before reaching an admin endpoint, sign-in then needs a code, and a second start never duplicates them.
/// The detailed password and TOTP flows are covered by AdminAuthTests (story 1.3) and AdminTotpTests (story 1.4).
/// Nothing in the code refuses to start in production with a single admin (it only logs a warning), so "two admins exist" is a manual
/// launch condition in docs/launch-checklist.md.
/// </summary>
[Collection("env")]
public class AdminBootstrapTests(PostgresFixture pg) : IDisposable
{
    private const string SecondUser = "admin2";
    private const string NewPassword1 = "Brand-New-Pass-42#Lk";
    private const string NewPassword2 = "Another-New-Pass-73#Qz";
    private readonly List<AuthApiFactory> _factories = [];

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
    }

    private AuthApiFactory NewFactory(string databaseUrl)
    {
        var f = new AuthApiFactory(databaseUrl, adminUser2: SecondUser);
        _factories.Add(f);
        return f;
    }

    private static async Task<string> ProblemCodeAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("code").GetString()!;
    }

    [Fact]
    public async Task Both_admins_must_change_the_password_and_enrol_in_totp_before_any_admin_endpoint()
    {
        var factory = NewFactory(await pg.CreateDatabaseAsync());
        Assert.Equal(2, await factory.CountAdminsAsync());

        foreach (var (user, initial, replacement) in new[]
                 {
                     (AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword, NewPassword1),
                     (SecondUser, AuthApiFactory.InitialPassword2, NewPassword2),
                 })
        {
            var client = factory.NewClient();
            Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(user, initial)).StatusCode);
            Assert.Equal("password-change-required", await ProblemCodeAsync(await client.Http.GetAsync("/admin/ping")));

            Assert.Equal(HttpStatusCode.NoContent, (await client.PostWithCsrfAsync("/auth/change-password", new { currentPassword = initial, newPassword = replacement })).StatusCode);
            Assert.Equal("totp-setup-required", await ProblemCodeAsync(await client.Http.GetAsync("/admin/ping")));

            var setup = await client.PostWithCsrfAsync("/auth/totp/setup", null);
            Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
            var key = (await setup.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("key").GetString()!;
            Assert.Equal(HttpStatusCode.OK, (await client.PostWithCsrfAsync("/auth/totp/enable", new { code = Totp.Code(key) })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.Http.GetAsync("/admin/ping")).StatusCode);

            // A later sign-in needs the code: the password alone never opens an admin endpoint.
            var later = factory.NewClient();
            var withoutCode = await later.LoginAsync(user, replacement);
            Assert.Equal(HttpStatusCode.Unauthorized, withoutCode.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await later.Http.GetAsync("/admin/ping")).StatusCode);

            var withCode = factory.NewClient();
            Assert.Equal(HttpStatusCode.OK, (await withCode.LoginAsync(user, replacement, code: Totp.Code(key))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await withCode.Http.GetAsync("/admin/ping")).StatusCode);
        }
    }

    [Fact]
    public async Task A_wrong_totp_code_is_rejected_for_both_admins()
    {
        var factory = NewFactory(await pg.CreateDatabaseAsync());
        foreach (var (user, initial, replacement) in new[]
                 {
                     (AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword, NewPassword1),
                     (SecondUser, AuthApiFactory.InitialPassword2, NewPassword2),
                 })
        {
            var (key, _) = await factory.NewClient().EnrollAsync(user, initial, replacement);
            var wrong = Totp.Code(key, stepOffset: 40); // far outside the accepted window, never equal to the current code

            var client = factory.NewClient();
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.LoginAsync(user, replacement, code: wrong)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.Http.GetAsync("/admin/ping")).StatusCode);
        }
    }

    [Fact]
    public async Task The_initial_password_stops_working_after_it_is_changed()
    {
        var factory = NewFactory(await pg.CreateDatabaseAsync());
        foreach (var (user, initial, replacement) in new[]
                 {
                     (AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword, NewPassword1),
                     (SecondUser, AuthApiFactory.InitialPassword2, NewPassword2),
                 })
        {
            var (key, _) = await factory.NewClient().EnrollAsync(user, initial, replacement);

            var client = factory.NewClient();
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.LoginAsync(user, initial, code: Totp.Code(key))).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.Http.GetAsync("/admin/ping")).StatusCode);
        }
    }

    [Fact]
    public async Task Starting_again_on_the_same_database_does_not_duplicate_the_admins()
    {
        var url = await pg.CreateDatabaseAsync();
        var first = NewFactory(url);
        Assert.Equal(2, await first.CountAdminsAsync());

        var second = NewFactory(url);

        Assert.Equal(2, await second.CountAdminsAsync());
        Assert.Equal(2, await first.CountAdminsAsync());
    }
}
