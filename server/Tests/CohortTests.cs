using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using ResumeApp.Api.Endpoints;

namespace ResumeApp.Tests;

[Collection("env")]
public class CohortTests(PostgresFixture pg) : IDisposable
{
    private const string NewPassword = "Brand-New-Pass-42#Lk";
    private readonly List<AuthApiFactory> _factories = [];

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
    }

    private async Task<(AuthApiFactory Factory, ApiClient Admin)> EnrolledAdminAsync()
    {
        var factory = await AuthApiFactory.CreateAsync(pg);
        _factories.Add(factory);
        var admin = factory.NewClient();
        await admin.EnrollAsync(AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword, NewPassword);
        return (factory, admin);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<JsonElement> CreateAsync(ApiClient admin, string name = "מחזור א")
    {
        var response = await admin.PostWithCsrfAsync("/admin/cohorts", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await Json(response);
    }

    [Fact]
    public async Task Create_returns_201_with_token_and_url()
    {
        var (_, admin) = await EnrolledAdminAsync();
        var created = await CreateAsync(admin);

        var token = created.GetProperty("inviteToken").GetString()!;
        Assert.Equal(43, token.Length); // 32 bytes, base64url, no padding
        Assert.Matches("^[A-Za-z0-9_-]{43}$", token);
        Assert.Equal(32, Convert.FromBase64String(token.Replace('-', '+').Replace('_', '/') + "=").Length);
        Assert.Equal($"http://localhost:4200/join/{token}", created.GetProperty("inviteUrl").GetString());
        Assert.Equal(4, Guid.Parse(created.GetProperty("id").GetString()!).ToString()[14] - '0');
        Assert.Equal("מחזור א", created.GetProperty("name").GetString());
        Assert.True(created.TryGetProperty("createdAt", out _));
    }

    [Fact]
    public async Task Invite_url_uses_PUBLIC_BASE_URL()
    {
        var original = Environment.GetEnvironmentVariable("PUBLIC_BASE_URL");
        Environment.SetEnvironmentVariable("PUBLIC_BASE_URL", "https://app.example.com/");
        try
        {
            var (_, admin) = await EnrolledAdminAsync();
            var created = await CreateAsync(admin);
            Assert.Equal($"https://app.example.com/join/{created.GetProperty("inviteToken").GetString()}",
                created.GetProperty("inviteUrl").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("PUBLIC_BASE_URL", original);
        }
    }

    [Fact]
    public async Task Invalid_PUBLIC_BASE_URL_fails_with_clear_error()
    {
        var original = Environment.GetEnvironmentVariable("PUBLIC_BASE_URL");
        Environment.SetEnvironmentVariable("PUBLIC_BASE_URL", "app.example.com");
        try
        {
            var ex = await Assert.ThrowsAnyAsync<Exception>(() => AuthApiFactory.CreateAsync(pg));
            var root = ex;
            while (root.InnerException is not null && root is not InvalidOperationException) root = root.InnerException;
            Assert.IsType<InvalidOperationException>(root);
            Assert.Contains("PUBLIC_BASE_URL", root.Message);
            Assert.DoesNotContain("app.example.com", root.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PUBLIC_BASE_URL", original);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Invalid_name_is_400_problem_json(string name)
    {
        var (_, admin) = await EnrolledAdminAsync();
        var response = await admin.PostWithCsrfAsync("/admin/cohorts", new { name });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True((await Json(response)).GetProperty("errors").TryGetProperty("name", out _));
    }

    [Fact]
    public async Task Too_long_name_is_400()
    {
        var (_, admin) = await EnrolledAdminAsync();
        var response = await admin.PostWithCsrfAsync("/admin/cohorts", new { name = new string('א', 101) });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostWithCsrfAsync("/admin/cohorts", new { name = new string('א', 100) })).StatusCode);
    }

    [Fact]
    public async Task List_is_newest_first_with_revocation_status()
    {
        var (_, admin) = await EnrolledAdminAsync();
        var first = await CreateAsync(admin, "ראשון");
        var second = await CreateAsync(admin, "שני");
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PostWithCsrfAsync($"/admin/cohorts/{first.GetProperty("id").GetString()}/revoke", null)).StatusCode);

        // Expected order is derived from the created values (createdAt desc, then id desc), so it is deterministic.
        var expected = new[] { first, second }
            .OrderByDescending(c => c.GetProperty("createdAt").GetDateTime())
            .ThenByDescending(c => Guid.Parse(c.GetProperty("id").GetString()!))
            .ToArray();

        var list = await Json(await admin.Http.GetAsync("/admin/cohorts"));
        var items = list.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, items.Length);
        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(expected[i].GetProperty("id").GetString(), items[i].GetProperty("id").GetString());
            Assert.Equal(expected[i].GetProperty("inviteToken").GetString(), items[i].GetProperty("inviteToken").GetString());
            Assert.Equal(expected[i].GetProperty("inviteUrl").GetString(), items[i].GetProperty("inviteUrl").GetString());
            var isFirst = items[i].GetProperty("id").GetString() == first.GetProperty("id").GetString();
            Assert.Equal(isFirst, items[i].GetProperty("revoked").GetBoolean());
            Assert.Equal(isFirst ? JsonValueKind.String : JsonValueKind.Null, items[i].GetProperty("revokedAt").ValueKind);
        }
    }

    [Fact]
    public async Task Valid_invite_returns_only_cohort_name_to_anonymous()
    {
        var (factory, admin) = await EnrolledAdminAsync();
        var created = await CreateAsync(admin, "מחזור ציבורי");
        var token = created.GetProperty("inviteToken").GetString();

        var response = await factory.NewClient().Http.GetAsync($"/invites/{token}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Json(response);
        Assert.Equal(["cohortName"], body.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("מחזור ציבורי", body.GetProperty("cohortName").GetString());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
    }

    [Fact]
    public async Task Revoked_missing_and_malformed_invites_get_identical_404()
    {
        var (factory, admin) = await EnrolledAdminAsync();
        var created = await CreateAsync(admin, "יבוטל");
        var token = created.GetProperty("inviteToken").GetString();
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PostWithCsrfAsync($"/admin/cohorts/{created.GetProperty("id").GetString()}/revoke", null)).StatusCode);

        var anon = factory.NewClient();
        var revoked = await anon.Http.GetAsync($"/invites/{token}");
        var missing = await anon.Http.GetAsync($"/invites/{ResumeApp.Domain.Cohorts.Cohort.GenerateToken()}");
        var malformed = await anon.Http.GetAsync($"/invites/{new string('a', 129)}");
        var badChar = await anon.Http.GetAsync($"/invites/{new string('a', 42)}+");

        var shapes = new List<string>();
        foreach (var r in new[] { revoked, missing, malformed, badChar })
        {
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
            Assert.Equal("no-store", r.Headers.CacheControl?.ToString());
            Assert.Equal("no-referrer", r.Headers.GetValues("Referrer-Policy").Single());
            Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
            var text = await r.Content.ReadAsStringAsync();
            Assert.DoesNotContain("יבוטל", text);
            var json = JsonDocument.Parse(text).RootElement;
            shapes.Add($"{json.GetProperty("title")}|{json.GetProperty("status")}|{json.GetProperty("type")}|" +
                       string.Join(";", r.Headers.Select(h => h.Key).Where(k => k != "Date").OrderBy(k => k)));
        }
        Assert.Single(shapes.Distinct());
    }

    [Fact]
    public async Task Revoke_is_conditional_second_revoke_409_unknown_404()
    {
        var (_, admin) = await EnrolledAdminAsync();
        var id = (await CreateAsync(admin)).GetProperty("id").GetString();

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostWithCsrfAsync($"/admin/cohorts/{id}/revoke", null)).StatusCode);
        var again = await admin.PostWithCsrfAsync($"/admin/cohorts/{id}/revoke", null);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("application/problem+json", again.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostWithCsrfAsync($"/admin/cohorts/{Guid.NewGuid()}/revoke", null)).StatusCode);
    }

    [Fact]
    public async Task Concurrent_revokes_yield_exactly_one_success()
    {
        var (_, admin) = await EnrolledAdminAsync();
        var id = (await CreateAsync(admin)).GetProperty("id").GetString();
        var csrf = await admin.GetCsrfAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => admin.PostAsync($"/admin/cohorts/{id}/revoke", null, csrf)));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.NoContent));
        Assert.Equal(4, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task Admin_endpoints_reject_anonymous_with_401_and_non_admin_with_403()
    {
        var (factory, _) = await EnrolledAdminAsync();
        var anon = factory.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.Http.GetAsync("/admin/cohorts")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostWithCsrfAsync("/admin/cohorts", new { name = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anon.PostWithCsrfAsync($"/admin/cohorts/{Guid.NewGuid()}/revoke", null)).StatusCode);

        await factory.AddUserAsync("viewer1", "Quiet-Horse-Pass-77!Qx", admin: false);
        var plain = factory.NewClient();
        Assert.Equal(HttpStatusCode.OK, (await plain.LoginAsync("viewer1", "Quiet-Horse-Pass-77!Qx")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.Http.GetAsync("/admin/cohorts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.PostWithCsrfAsync("/admin/cohorts", new { name = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await plain.PostWithCsrfAsync($"/admin/cohorts/{Guid.NewGuid()}/revoke", null)).StatusCode);
    }

    [Fact]
    public async Task Mutations_without_csrf_token_or_with_foreign_origin_are_rejected()
    {
        var (_, admin) = await EnrolledAdminAsync();
        var noToken = await admin.PostAsync("/admin/cohorts", new { name = "x" }, csrf: null);
        Assert.NotEqual(HttpStatusCode.Created, noToken.StatusCode);
        Assert.True((int)noToken.StatusCode is 400 or 403);

        var foreign = await admin.PostAsync("/admin/cohorts", new { name = "x" }, await admin.GetCsrfAsync(), origin: "https://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);

        Assert.Equal(0, (await Json(await admin.Http.GetAsync("/admin/cohorts"))).GetProperty("total").GetInt32());
    }

    [Fact]
    public void Public_invite_dto_has_no_private_fields()
    {
        var props = typeof(CohortEndpoints.PublicInvite).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).ToArray();
        Assert.Equal(["CohortName"], props);
    }
}
