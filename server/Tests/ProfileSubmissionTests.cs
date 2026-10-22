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

/// <summary>Story 1.7 validation and request-protection coverage, carried over by story 6.4 to the Google profile creation (POST /me/profile).</summary>
[Collection("env")]
public class ProfileSubmissionTests(PostgresFixture pg) : IDisposable
{
    private const string NewPassword = "Brand-New-Pass-42#Lk";
    private readonly List<AuthApiFactory> _factories = [];

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
    }

    private sealed record Setup(AuthApiFactory Factory, string Token, string CohortId);

    /// <summary>Token and CohortId are both the id of an active cohort: since story 6.4 a profile is created with POST /me/profile.</summary>
    private async Task<Setup> NewSetupAsync()
    {
        var factory = await AuthApiFactory.CreateAsync(pg);
        _factories.Add(factory);
        var cohort = (await ParticipantSeeding.SeedCohortAsync(factory)).Id.ToString("D");
        return new(factory, cohort, cohort);
    }

    private static object Valid(string phone = "050-123 4567") => new
    {
        name = "דנה כהן",
        productName = "מוצר מגניב",
        productUrl = "https://example.com/app",
        phone,
    };

    /// <summary>Signs a new client in as <paramref name="googleEmail"/> (a new account by default) and creates a profile.</summary>
    private static Task<HttpResponseMessage> Submit(ApiClient client, string cohortId, object body, string? googleEmail = null)
        => ParticipantSeeding.CreateProfileAsync(client.Factory, cohortId, body, googleEmail);

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
    public async Task Valid_creation_is_201_pending_and_creates_three_rows()
    {
        var s = await NewSetupAsync();
        var response = await Submit(s.Factory.NewClient(), s.Token, Valid("+972 50-123-4567"), "Dana@Example.COM");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        var body = JsonDocument.Parse(text).RootElement;
        Assert.Equal(["profileId", "status"], body.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal("pending", body.GetProperty("status").GetString());
        Assert.DoesNotContain("dana", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("972", text);

        var profile = Assert.Single(await Db(s.Factory, d => d.Profiles.AsNoTracking().ToListAsync()));
        Assert.Equal(profile.Id, body.GetProperty("profileId").GetGuid());
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
    [InlineData("phone")]
    public async Task Missing_or_blank_required_field_is_400_for_that_field(string field)
    {
        var s = await NewSetupAsync();
        foreach (var value in new[] { "", "   ", null })
        {
            var body = new Dictionary<string, object?>
            {
                ["name"] = "דנה", ["productName"] = "מוצר", ["productUrl"] = "https://example.com",
                ["phone"] = "0501234567",
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
        // The email is not an input any more: it comes from the Google session.
        Assert.Equal(["name", "phone", "productName", "productUrl"],
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
            new { name = "ד", productName = "מ", productUrl = url, phone = "0501234567" });
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
            name = new string('א', 101), productName = new string('ב', 101), productUrl = tooLongUrl, phone = "0501234567",
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.Equal(["name", "productName", "productUrl"], errors.EnumerateObject().Select(p => p.Name).Order().ToArray());
        await AssertNothingStored(s.Factory);

        var maxUrl = "https://example.com/" + new string('a', 2048 - 20);
        var ok = await Submit(client, s.Token, new
        {
            name = new string('א', 100), productName = new string('ב', 100), productUrl = maxUrl, phone = "0501234567",
        });
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
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
        Assert.Equal(HttpStatusCode.Created, (await Submit(s.Factory.NewClient(), s.Token, Valid(phone: phone))).StatusCode);
        Assert.Equal(stored, (await Db(s.Factory, d => d.ParticipantPrivates.AsNoTracking().SingleAsync())).Phone);
    }

    private static Task<HttpResponseMessage> SendRaw(ApiClient client, HttpContent content)
        => ParticipantSeeding.CreateProfileRawAsync(client.Factory, content);

    private static StringContent Raw(string text, string type) => new(text, System.Text.Encoding.UTF8, type);

    [Fact]
    public async Task Malformed_json_is_400_problem_json()
    {
        var s = await NewSetupAsync();
        var r = await SendRaw(s.Factory.NewClient(), Raw("{not json", "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        await AssertNothingStored(s.Factory);
    }

    [Fact]
    public async Task Wrong_content_type_is_415_problem_json()
    {
        var s = await NewSetupAsync();
        var r = await SendRaw(s.Factory.NewClient(), Raw("name=x", "text/plain"));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, r.StatusCode);
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Oversized_body_is_rejected_with_a_4xx_problem()
    {
        var s = await NewSetupAsync();
        var json = JsonSerializer.Serialize(new { name = new string('a', 161 * 1024), productName = "x", productUrl = "https://a.co", phone = "0501234567" });
        var r = await SendRaw(s.Factory.NewClient(), Raw(json, "application/json"));
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
            var r = await Submit(client, s.Token, new { name = bad, productName = bad, productUrl = "https://a.co", phone = "0501234567" });
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
            name = "", productName = new string('ב', 101), productUrl = "ftp://x", phone = new string('1', 41),
        });
        var errors = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        string Msg(string f) => errors.GetProperty(f)[0].GetString()!;
        Assert.Equal(ResumeApp.Api.Resources.Strings.FieldRequired, Msg("name"));
        Assert.Equal(ResumeApp.Api.Resources.Strings.ProductNameTooLong, Msg("productName"));
        Assert.Equal(ResumeApp.Api.Resources.Strings.ProductUrlInvalid, Msg("productUrl"));
        Assert.Equal(ResumeApp.Api.Resources.Strings.PhoneTooLong, Msg("phone"));
    }

    [Fact]
    public async Task Duplicate_and_parallel_creations_do_not_leak_email_or_phone_to_logs()
    {
        var original = Console.Out;
        var captured = new StringWriter();
        var synced = TextWriter.Synchronized(captured);
        Console.SetOut(synced);
        try
        {
            var s = await NewSetupAsync(); // built after SetOut so the Serilog console sink writes to it
            var client = s.Factory.NewClient();
            await Submit(client, s.Token, Valid("0587654321"), "leak.check@example.com");
            await Submit(client, s.Token, Valid("0587654321"), "leak.check@example.com"); // the same account again: its session already has a profile
            await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Submit(client, s.Token, Valid("0587654321"), "leak.parallel@example.com")));
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
    public async Task Responses_never_contain_email_or_phone()
    {
        var s = await NewSetupAsync();
        var client = s.Factory.NewClient();
        const string email = "secret.person@example.com";
        const string phone = "0541112233";

        var ok = await Submit(client, s.Token, Valid(phone), email);
        var dup = await Submit(client, s.Token, Valid(phone), email);
        var bad = await Submit(client, s.Token, new { name = "ד", productName = "מ", productUrl = "ftp://x", phone }, "other.person@example.com");

        foreach (var r in new[] { ok, dup, bad })
        {
            var text = await r.Content.ReadAsStringAsync();
            Assert.DoesNotContain("secret.person", text);
            Assert.DoesNotContain("other.person", text);
            Assert.DoesNotContain("1112233", text);
            Assert.Equal("no-store", r.Headers.CacheControl?.ToString());
            Assert.Equal("no-referrer", r.Headers.GetValues("Referrer-Policy").Single());
        }
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, dup.StatusCode); // signing in again links the session to the profile, so a second creation has no profile-less session
    }

    [Fact]
    public async Task Pending_revision_is_unique_per_profile_in_the_database()
    {
        var s = await NewSetupAsync();
        Assert.Equal(HttpStatusCode.Created, (await Submit(s.Factory.NewClient(), s.Token, Valid())).StatusCode);
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
    public void Created_receipt_has_only_profile_id_and_status_and_private_data_is_not_in_any_public_dto()
    {
        var props = typeof(ProfileEndpoints.CreatedProfileReceipt).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).ToArray();
        Assert.Equal(["ProfileId", "Status"], props);
        Assert.Empty(typeof(CohortEndpoints.PublicInvite).GetProperties().Where(p => p.Name is "Email" or "Phone"));
    }
}
