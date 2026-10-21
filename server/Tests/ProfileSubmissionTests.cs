using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResumeApp.Api.Endpoints;
using ResumeApp.Domain.Profiles;
using ResumeApp.Infrastructure;

namespace ResumeApp.Tests;

[Collection("env")]
public class ProfileSubmissionTests(PostgresFixture pg) : IDisposable
{
    private const string NewPassword = "Brand-New-Pass-42#Lk";
    private readonly List<AuthApiFactory> _factories = [];

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
    }

    private sealed record Setup(AuthApiFactory Factory, ApiClient Admin, string Token, string CohortId);

    private async Task<Setup> NewSetupAsync()
    {
        var factory = await AuthApiFactory.CreateAsync(pg);
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

    private static object Valid(string email = "dana@example.com", string phone = "050-123 4567") => new
    {
        name = "דנה כהן",
        productName = "מוצר מגניב",
        productUrl = "https://example.com/app",
        email,
        phone,
    };

    private static Task<HttpResponseMessage> Submit(ApiClient client, string token, object body)
        => client.PostWithCsrfAsync($"/invites/{token}/profiles", body);

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

    [Fact]
    public async Task Valid_submission_is_202_pending_and_creates_three_rows()
    {
        var s = await NewSetupAsync();
        var response = await Submit(s.Factory.NewClient(), s.Token, Valid(" Dana@Example.COM ", "+972 50-123-4567"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        var body = JsonDocument.Parse(text).RootElement;
        Assert.Equal(["status"], body.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("pending", body.GetProperty("status").GetString());
        Assert.DoesNotContain("dana", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("972", text);

        var profile = Assert.Single(await Db(s.Factory, d => d.Profiles.AsNoTracking().ToListAsync()));
        Assert.Equal(Guid.Parse(s.CohortId), profile.CohortId);
        Assert.Equal(4, profile.Id.ToString()[14] - '0');
        Assert.Equal(ProfileVisibility.Active, profile.Visibility);
        Assert.Null(profile.ApprovedRevisionId);

        var revision = Assert.Single(await Db(s.Factory, d => d.ProfileRevisions.AsNoTracking().ToListAsync()));
        Assert.Equal(profile.Id, revision.ProfileId);
        Assert.Equal(RevisionStatus.Pending, revision.Status);
        Assert.Equal("דנה כהן", revision.Name);
        Assert.Equal("מוצר מגניב", revision.ProductName);
        Assert.Equal("https://example.com/app", revision.ProductUrl);

        var priv = Assert.Single(await Db(s.Factory, d => d.ParticipantPrivates.AsNoTracking().ToListAsync()));
        Assert.Equal(profile.Id, priv.ProfileId);
        Assert.Equal("dana@example.com", priv.Email);
        Assert.Equal("+972501234567", priv.Phone);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("productName")]
    [InlineData("productUrl")]
    [InlineData("email")]
    [InlineData("phone")]
    public async Task Missing_or_blank_required_field_is_400_for_that_field(string field)
    {
        var s = await NewSetupAsync();
        foreach (var value in new[] { "", "   ", null })
        {
            var body = new Dictionary<string, object?>
            {
                ["name"] = "דנה", ["productName"] = "מוצר", ["productUrl"] = "https://example.com",
                ["email"] = "a@b.co", ["phone"] = "0501234567",
            };
            body[field] = value;
            var response = await Submit(s.Factory.NewClient(), s.Token, body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
            Assert.Equal([field], errors.EnumerateObject().Select(p => p.Name).ToArray());
        }
        await AssertNothingStored(s.Factory);
    }

    [Fact]
    public async Task All_blank_returns_an_error_per_field()
    {
        var s = await NewSetupAsync();
        var response = await Submit(s.Factory.NewClient(), s.Token, new { });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.Equal(["email", "name", "phone", "productName", "productUrl"],
            errors.EnumerateObject().Select(p => p.Name).Order().ToArray());
        await AssertNothingStored(s.Factory);
    }

    [Theory]
    [InlineData("ftp://x.example/a")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/relative/path")]
    [InlineData("example.com")]
    [InlineData("https://exa mple.com")]
    [InlineData("data:text/html,hi")]
    [InlineData("http:///x")]
    [InlineData("http://user:pass@example.com")]
    public async Task Invalid_product_url_is_400_on_that_field(string url)
    {
        var s = await NewSetupAsync();
        var response = await Submit(s.Factory.NewClient(), s.Token,
            new { name = "ד", productName = "מ", productUrl = url, email = "a@b.co", phone = "0501234567" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.Equal(["productUrl"], errors.EnumerateObject().Select(p => p.Name).ToArray());
        await AssertNothingStored(s.Factory);
    }

    [Fact]
    public async Task Length_limits_are_enforced_and_boundaries_accepted()
    {
        var s = await NewSetupAsync();
        var client = s.Factory.NewClient();
        var tooLongUrl = "https://example.com/" + new string('a', 2048 - 20 + 1);
        var response = await Submit(client, s.Token, new
        {
            name = new string('א', 101), productName = new string('ב', 101), productUrl = tooLongUrl,
            email = new string('a', 250) + "@b.co", phone = "0501234567",
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.Equal(["email", "name", "productName", "productUrl"], errors.EnumerateObject().Select(p => p.Name).Order().ToArray());
        await AssertNothingStored(s.Factory);

        var maxUrl = "https://example.com/" + new string('a', 2048 - 20);
        var maxEmail = new string('a', 254 - 6) + "@b.com"; // exactly 254
        Assert.Equal(254, maxEmail.Length);
        var ok = await Submit(client, s.Token, new
        {
            name = new string('א', 100), productName = new string('ב', 100), productUrl = maxUrl, email = maxEmail, phone = "0501234567",
        });
        Assert.Equal(HttpStatusCode.Accepted, ok.StatusCode);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("a@b")]
    [InlineData("@b.co")]
    [InlineData("a@@b.co")]
    [InlineData("a b@c.co")]
    [InlineData("a@.co")]
    [InlineData("a@b.")]
    [InlineData("a@b..co")]
    [InlineData("a,b@c.co")]
    [InlineData("a<b@c.co")]
    public async Task Invalid_email_is_400(string email)
    {
        var s = await NewSetupAsync();
        var response = await Submit(s.Factory.NewClient(), s.Token, Valid(email));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.Equal(["email"], errors.EnumerateObject().Select(p => p.Name).ToArray());
        await AssertNothingStored(s.Factory);
    }

    [Theory]
    [InlineData("12345678")]            // 8 digits
    [InlineData("1234567890123456")]    // 16 digits
    [InlineData("050-abc-4567")]
    [InlineData("050+1234567")]
    [InlineData("(050) 1234567")]
    public async Task Invalid_phone_is_400(string phone)
    {
        var s = await NewSetupAsync();
        var response = await Submit(s.Factory.NewClient(), s.Token, Valid(phone: phone));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.Equal(["phone"], errors.EnumerateObject().Select(p => p.Name).ToArray());
        await AssertNothingStored(s.Factory);
    }

    [Theory]
    [InlineData("123456789", "123456789")]
    [InlineData("+123456789012345", "+123456789012345")]
    public async Task Phone_boundaries_are_accepted(string phone, string stored)
    {
        var s = await NewSetupAsync();
        Assert.Equal(HttpStatusCode.Accepted, (await Submit(s.Factory.NewClient(), s.Token, Valid(phone: phone))).StatusCode);
        Assert.Equal(stored, (await Db(s.Factory, d => d.ParticipantPrivates.AsNoTracking().SingleAsync())).Phone);
    }

    [Fact]
    public async Task Duplicate_email_in_same_cohort_is_identical_202_without_new_profile()
    {
        var s = await NewSetupAsync();
        var client = s.Factory.NewClient();
        var first = await Submit(client, s.Token, Valid("dana@example.com"));
        var second = await Submit(client, s.Token, Valid("DANA@Example.com"));

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(first.StatusCode, second.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
        Assert.Equal(first.Content.Headers.ContentType, second.Content.Headers.ContentType);
        Assert.Equal(1, await Db(s.Factory, d => d.Profiles.CountAsync()));
        Assert.Equal(1, await Db(s.Factory, d => d.ProfileRevisions.CountAsync()));
        Assert.Equal(1, await Db(s.Factory, d => d.ParticipantPrivates.CountAsync()));
    }

    [Fact]
    public async Task Same_email_in_another_cohort_creates_a_separate_profile()
    {
        var s = await NewSetupAsync();
        var (otherToken, otherId) = await CreateCohortAsync(s.Admin, "מחזור ב");
        var client = s.Factory.NewClient();

        Assert.Equal(HttpStatusCode.Accepted, (await Submit(client, s.Token, Valid())).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await Submit(client, otherToken, Valid())).StatusCode);

        var profiles = await Db(s.Factory, d => d.Profiles.AsNoTracking().ToListAsync());
        Assert.Equal(2, profiles.Count);
        Assert.Equal(new[] { s.CohortId, otherId }.Select(Guid.Parse).Order(), profiles.Select(p => p.CohortId).Order());
    }

    [Fact]
    public async Task Five_parallel_identical_submissions_create_one_profile_and_all_return_202()
    {
        var s = await NewSetupAsync();
        var client = s.Factory.NewClient();
        var csrf = await client.GetCsrfAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => client.PostAsync($"/invites/{s.Token}/profiles", Valid(), csrf)));

        Assert.All(results, r => Assert.Equal(HttpStatusCode.Accepted, r.StatusCode));
        Assert.Equal(1, await Db(s.Factory, d => d.Profiles.CountAsync()));
        Assert.Equal(1, await Db(s.Factory, d => d.ProfileRevisions.CountAsync()));
        Assert.Equal(1, await Db(s.Factory, d => d.ParticipantPrivates.CountAsync()));
    }

    [Fact]
    public async Task Revoked_missing_and_malformed_tokens_get_identical_404_and_store_nothing()
    {
        var s = await NewSetupAsync();
        var (revokedToken, revokedId) = await CreateCohortAsync(s.Admin, "יבוטל");
        Assert.Equal(HttpStatusCode.NoContent, (await s.Admin.PostWithCsrfAsync($"/admin/cohorts/{revokedId}/revoke", null)).StatusCode);

        var anon = s.Factory.NewClient();
        var tokens = new[] { revokedToken, ResumeApp.Domain.Cohorts.Cohort.GenerateToken(), new string('a', 129), new string('a', 42) + "+" };
        var shapes = new List<string>();
        foreach (var token in tokens)
        {
            var r = await Submit(anon, token, Valid());
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
            Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
            Assert.Equal("no-store", r.Headers.CacheControl?.ToString());
            Assert.Equal("no-referrer", r.Headers.GetValues("Referrer-Policy").Single());
            shapes.Add(ProblemShape(JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement));
        }
        Assert.Single(shapes.Distinct());
        await AssertNothingStored(s.Factory);

        // The same 404 as the check endpoint (full type, title, status).
        var check = await anon.Http.GetAsync($"/invites/{revokedToken}");
        Assert.Equal(shapes[0], ProblemShape(JsonDocument.Parse(await check.Content.ReadAsStringAsync()).RootElement));
    }

    private static string ProblemShape(JsonElement json)
        => $"{json.GetProperty("type")}|{json.GetProperty("title")}|{json.GetProperty("status")}";

    private static Task<HttpResponseMessage> SendRaw(ApiClient client, string token, string csrf, HttpContent content)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/invites/{token}/profiles") { Content = content };
        request.Headers.Add("Origin", ApiClient.Origin);
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return client.Http.SendAsync(request);
    }

    private static StringContent Raw(string text, string type) => new(text, System.Text.Encoding.UTF8, type);

    [Fact]
    public async Task Bad_body_or_content_type_with_bad_token_gets_the_same_404()
    {
        var s = await NewSetupAsync();
        var (revokedToken, revokedId) = await CreateCohortAsync(s.Admin, "יבוטל");
        Assert.Equal(HttpStatusCode.NoContent, (await s.Admin.PostWithCsrfAsync($"/admin/cohorts/{revokedId}/revoke", null)).StatusCode);
        var anon = s.Factory.NewClient();
        var csrf = await anon.GetCsrfAsync();

        var expected = ProblemShape(JsonDocument.Parse(await (await Submit(anon, revokedToken, Valid())).Content.ReadAsStringAsync()).RootElement);
        foreach (var token in new[] { revokedToken, ResumeApp.Domain.Cohorts.Cohort.GenerateToken() })
        {
            foreach (var content in new HttpContent[] { Raw("{not json", "application/json"), Raw("name=x", "text/plain"), Raw(new string('x', 20_000), "application/json") })
            {
                var r = await SendRaw(anon, token, csrf, content);
                Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
                Assert.Equal(expected, ProblemShape(JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement));
            }
        }
    }

    [Fact]
    public async Task Malformed_json_with_valid_token_is_400_problem_json()
    {
        var s = await NewSetupAsync();
        var anon = s.Factory.NewClient();
        var r = await SendRaw(anon, s.Token, await anon.GetCsrfAsync(), Raw("{not json", "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        await AssertNothingStored(s.Factory);
    }

    [Fact]
    public async Task Wrong_content_type_with_valid_token_is_415_problem_json()
    {
        var s = await NewSetupAsync();
        var anon = s.Factory.NewClient();
        var r = await SendRaw(anon, s.Token, await anon.GetCsrfAsync(), Raw("name=x", "text/plain"));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, r.StatusCode);
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Oversized_body_is_rejected_with_a_4xx_problem()
    {
        var s = await NewSetupAsync();
        var anon = s.Factory.NewClient();
        var json = JsonSerializer.Serialize(new { name = new string('a', 161 * 1024), productName = "x", productUrl = "https://a.co", email = "a@b.co", phone = "0501234567" });
        var r = await SendRaw(anon, s.Token, await anon.GetCsrfAsync(), Raw(json, "application/json"));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, r.StatusCode);
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        await AssertNothingStored(s.Factory);
    }

    [Fact]
    public async Task Control_characters_in_text_fields_are_400_not_500()
    {
        var s = await NewSetupAsync();
        var client = s.Factory.NewClient();
        foreach (var bad in new[] { "ד\u0000נה", "ד\nנה", "ד\u0007" })
        {
            var r = await Submit(client, s.Token, new { name = bad, productName = bad, productUrl = "https://a.co", email = "a@b.co", phone = "0501234567" });
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            var errors = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
            Assert.Equal(["name", "productName"], errors.EnumerateObject().Select(p => p.Name).Order().ToArray());
        }
        await AssertNothingStored(s.Factory);
    }

    [Fact]
    public async Task Error_messages_match_the_failure_kind_per_field()
    {
        var s = await NewSetupAsync();
        var r = await Submit(s.Factory.NewClient(), s.Token, new
        {
            name = "", productName = new string('ב', 101), productUrl = "ftp://x", email = "nope", phone = new string('1', 41),
        });
        var errors = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        string Msg(string f) => errors.GetProperty(f)[0].GetString()!;
        Assert.Equal(ResumeApp.Api.Resources.Strings.FieldRequired, Msg("name"));
        Assert.Equal(ResumeApp.Api.Resources.Strings.ProductNameTooLong, Msg("productName"));
        Assert.Equal(ResumeApp.Api.Resources.Strings.ProductUrlInvalid, Msg("productUrl"));
        Assert.Equal(ResumeApp.Api.Resources.Strings.EmailInvalid, Msg("email"));
        Assert.Equal(ResumeApp.Api.Resources.Strings.PhoneTooLong, Msg("phone"));
    }

    [Fact]
    public async Task Duplicate_and_parallel_submissions_do_not_leak_email_or_phone_to_logs()
    {
        var original = Console.Out;
        var captured = new StringWriter();
        var synced = TextWriter.Synchronized(captured);
        Console.SetOut(synced);
        try
        {
            var s = await NewSetupAsync(); // built after SetOut so the Serilog console sink writes to it
            var client = s.Factory.NewClient();
            var csrf = await client.GetCsrfAsync();
            var body = Valid("leak.check@example.com", "0587654321");
            await Submit(client, s.Token, body);
            await Submit(client, s.Token, body);
            await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
                client.PostAsync($"/invites/{s.Token}/profiles", Valid("leak.parallel@example.com", "0587654321"), csrf)));
            string log;
            lock (synced) { log = captured.ToString(); } // Serilog may still be writing on another thread
            Assert.DoesNotContain("leak.check", log);
            Assert.DoesNotContain("leak.parallel", log);
            Assert.DoesNotContain("587654321", log);
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Fact]
    public async Task Missing_antiforgery_token_or_foreign_origin_is_rejected_and_stores_nothing()
    {
        var s = await NewSetupAsync();
        var client = s.Factory.NewClient();

        var noToken = await client.PostAsync($"/invites/{s.Token}/profiles", Valid(), csrf: null);
        Assert.True((int)noToken.StatusCode is 400 or 403);

        var foreign = await client.PostAsync($"/invites/{s.Token}/profiles", Valid(), await client.GetCsrfAsync(), origin: "https://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);

        await AssertNothingStored(s.Factory);
    }

    [Fact]
    public async Task Responses_never_contain_email_phone_or_profile_id()
    {
        var s = await NewSetupAsync();
        var client = s.Factory.NewClient();
        const string email = "secret.person@example.com";
        const string phone = "0541112233";

        var ok = await Submit(client, s.Token, Valid(email, phone));
        var dup = await Submit(client, s.Token, Valid(email, phone));
        var bad = await Submit(client, s.Token, new { name = "ד", productName = "מ", productUrl = "ftp://x", email, phone });
        var profileId = (await Db(s.Factory, d => d.Profiles.AsNoTracking().SingleAsync())).Id.ToString();

        foreach (var r in new[] { ok, dup, bad })
        {
            var text = await r.Content.ReadAsStringAsync();
            Assert.DoesNotContain("secret.person", text);
            Assert.DoesNotContain("1112233", text);
            Assert.DoesNotContain(profileId, text, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("no-store", r.Headers.CacheControl?.ToString());
            Assert.Equal("no-referrer", r.Headers.GetValues("Referrer-Policy").Single());
        }
    }

    [Fact]
    public async Task Pending_revision_is_unique_per_profile_in_the_database()
    {
        var s = await NewSetupAsync();
        Assert.Equal(HttpStatusCode.Accepted, (await Submit(s.Factory.NewClient(), s.Token, Valid())).StatusCode);
        var profile = await Db(s.Factory, d => d.Profiles.AsNoTracking().SingleAsync());

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => Db(s.Factory, async d =>
        {
            d.ProfileRevisions.Add(ProfileRevision.CreatePending(profile.Id, "x", "y", "https://z.example", DateTime.UtcNow));
            await d.SaveChangesAsync();
            return 0;
        }));
        Assert.Contains("ux_profile_revisions_one_pending", ex.InnerException?.Message);
    }

    [Fact]
    public void Submission_receipt_has_only_status_and_private_data_is_not_in_any_public_dto()
    {
        var props = typeof(ProfileEndpoints.SubmissionReceipt).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).ToArray();
        Assert.Equal(["Status"], props);
        Assert.Empty(typeof(CohortEndpoints.PublicInvite).GetProperties().Where(p => p.Name is "Email" or "Phone"));
    }
}
