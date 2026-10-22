using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using ResumeApp.Domain.Cohorts;
using ResumeApp.Domain.Profiles;
using ResumeApp.Infrastructure;

namespace ResumeApp.Tests;

/// <summary>Story 6.5: a signed-in Google account creates its profile on the server without an invite.</summary>
[Collection("env")]
public class MyProfileCreationTests(PostgresFixture pg) : IDisposable
{
    private const string SessionCookie = "__Host-resume-participant";
    private const string Phone = "0501234567";
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

    private static async Task<T> Db<T>(AuthApiFactory f, Func<AppDbContext, Task<T>> work)
    {
        await using var scope = f.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static async Task<Guid> SeedCohortAsync(AuthApiFactory f, bool revoked = false)
    {
        var cohort = Cohort.Create("מחזור " + Guid.NewGuid().ToString("N")[..6], DateTime.UtcNow);
        await Db(f, async d =>
        {
            d.Cohorts.Add(cohort);
            await d.SaveChangesAsync();
            if (revoked) await d.Database.ExecuteSqlInterpolatedAsync($"UPDATE cohorts SET revoked_at = {DateTime.UtcNow} WHERE id = {cohort.Id}");
            return 0;
        });
        return cohort.Id;
    }

    private static string PathAndQuery(Uri uri) => uri.IsAbsoluteUri ? uri.PathAndQuery : uri.OriginalString;

    private static string ServerPath(string pathAndQuery) => pathAndQuery.StartsWith("/api/", StringComparison.Ordinal) ? pathAndQuery[4..] : pathAndQuery;

    private static async Task SignInAsync(ApiClient c, string email)
    {
        var start = await c.Http.GetAsync("/auth/google/start");
        var authorizeUrl = PathAndQuery(start.Headers.Location!);
        var authorize = await c.Http.GetAsync(ServerPath(QueryHelpers.AddQueryString(authorizeUrl, new Dictionary<string, string?> { ["fake_email"] = email })));
        var callback = await c.Http.GetAsync(ServerPath(PathAndQuery(authorize.Headers.Location!)));
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
    }

    private static async Task<ApiClient> SignedInAsync(AuthApiFactory f, string email = "dana@example.com")
    {
        var c = f.NewClient();
        await SignInAsync(c, email);
        return c;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    private static Dictionary<string, object?> Body(Guid? cohortId, string? email = null, string phone = Phone) => new()
    {
        ["cohortId"] = cohortId?.ToString("D"),
        ["name"] = "דנה כהן",
        ["productName"] = "מוצר מגניב",
        ["productUrl"] = "https://example.com/app",
        ["phone"] = phone,
        ["email"] = email,
    };

    private static Task<HttpResponseMessage> Create(ApiClient c, object body) => c.PostWithCsrfAsync("/me/profile", body);

    private static async Task SeedLinkedProfileAsync(AuthApiFactory f, string googleEmail, Guid cohortId)
        => await Db(f, async d =>
        {
            var now = DateTime.UtcNow;
            var sub = FakeOidcProvider.SubFor(googleEmail);
            var profile = Profile.Create(cohortId, now, sub);
            d.Profiles.Add(profile);
            d.ParticipantPrivates.Add(ParticipantPrivate.Create(profile.Id, cohortId, googleEmail, "0509999999"));
            d.ProfileRevisions.Add(ProfileRevision.CreatePending(profile.Id, "קיימת", "מוצר", "https://example.com", now));
            await d.SaveChangesAsync();
            return 0;
        });

    [Fact]
    public async Task Creating_a_profile_stores_pending_revision_with_google_sub_and_session_email_and_reissues_the_cookie()
    {
        var f = await NewFactoryAsync();
        var cohort = await SeedCohortAsync(f);
        var c = await SignedInAsync(f, "Dana@Example.com");

        var response = await Create(c, Body(cohort));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var json = await Json(response);
        var id = json.GetProperty("profileId").GetGuid();
        Assert.Equal("pending", json.GetProperty("status").GetString());
        Assert.Contains(SessionCookie + "=", string.Join(";", response.Headers.GetValues("Set-Cookie")));

        var profile = await Db(f, d => d.Profiles.AsNoTracking().SingleAsync());
        Assert.Equal(id, profile.Id);
        Assert.Equal(cohort, profile.CohortId);
        Assert.Equal(FakeOidcProvider.SubFor("dana@example.com"), profile.GoogleSub);
        Assert.Null(profile.ApprovedRevisionId);
        var revision = await Db(f, d => d.ProfileRevisions.AsNoTracking().SingleAsync());
        Assert.Equal(RevisionStatus.Pending, revision.Status);
        var priv = await Db(f, d => d.ParticipantPrivates.AsNoTracking().SingleAsync());
        Assert.Equal("dana@example.com", priv.Email);
        Assert.Equal(Phone, priv.Phone);

        // The reissued cookie works at once.
        var me = await Json(await c.Http.GetAsync("/auth/google/me"));
        Assert.True(me.GetProperty("hasProfile").GetBoolean());
        Assert.Equal(id, me.GetProperty("profileId").GetGuid());
        var own = await c.Http.GetAsync("/me/profile");
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal("דנה כהן", (await Json(own)).GetProperty("name").GetString());

        // Not public until approved.
        Assert.Equal(0, (await Json(await f.NewClient().Http.GetAsync("/cohorts"))).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Without_a_google_session_or_with_an_edit_link_session_it_is_401()
    {
        var f = await NewFactoryAsync();
        var cohort = await SeedCohortAsync(f);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Create(f.NewClient(), Body(cohort))).StatusCode);

        // A personal-link session carries a profile_id and the wrong purpose.
        await SeedLinkedProfileAsync(f, "other@example.com", cohort);
        var profileId = await Db(f, d => d.Profiles.AsNoTracking().Select(p => p.Id).SingleAsync());
        string token;
        await using (var scope = f.Services.CreateAsyncScope())
            token = await scope.ServiceProvider.GetRequiredService<ResumeApp.Features.Email.IPersonalTokenIssuer>().IssueEditTokenAsync(profileId, DateTime.UtcNow, CancellationToken.None);
        var link = f.NewClient();
        Assert.Equal(HttpStatusCode.OK, (await link.PostWithCsrfAsync("/profiles/session", new { token })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Create(link, Body(cohort))).StatusCode);
        Assert.Equal(1, await Db(f, d => d.Profiles.CountAsync()));
    }

    [Fact]
    public async Task A_session_that_already_has_a_profile_cannot_create_another()
    {
        var f = await NewFactoryAsync();
        var cohort = await SeedCohortAsync(f);
        var c = await SignedInAsync(f);
        Assert.Equal(HttpStatusCode.Created, (await Create(c, Body(cohort))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Create(c, Body(cohort))).StatusCode);
        Assert.Equal(1, await Db(f, d => d.Profiles.CountAsync()));
    }

    [Fact]
    public async Task Post_needs_anti_forgery_and_a_same_origin_request()
    {
        var f = await NewFactoryAsync();
        var cohort = await SeedCohortAsync(f);
        var c = await SignedInAsync(f);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync("/me/profile", Body(cohort))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync("/me/profile", Body(cohort), await c.GetCsrfAsync(), origin: "https://evil.example")).StatusCode);
        Assert.Equal(0, await Db(f, d => d.Profiles.CountAsync()));
    }

    [Fact]
    public async Task An_account_that_already_has_a_profile_gets_409_profile_exists_and_nothing_changes()
    {
        var f = await NewFactoryAsync();
        var cohort = await SeedCohortAsync(f);
        var c = await SignedInAsync(f, "dana@example.com"); // signed in before the profile exists, so the session has no profile_id
        await SeedLinkedProfileAsync(f, "dana@example.com", cohort);

        var response = await Create(c, Body(cohort));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("profile-exists", (await Json(response)).GetProperty("code").GetString());
        Assert.Equal(1, await Db(f, d => d.Profiles.CountAsync()));
        Assert.Equal(1, await Db(f, d => d.ProfileRevisions.CountAsync()));
        Assert.Equal("קיימת", (await Db(f, d => d.ProfileRevisions.AsNoTracking().SingleAsync())).Name);
    }

    [Fact]
    public async Task Two_parallel_creations_for_the_same_account_make_exactly_one_profile()
    {
        var f = await NewFactoryAsync();
        var cohort = await SeedCohortAsync(f);
        var first = await SignedInAsync(f);
        var second = await SignedInAsync(f);

        var results = await Task.WhenAll(Create(first, Body(cohort)), Create(second, Body(cohort)));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created);
        var loser = Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("profile-exists", (await Json(loser)).GetProperty("code").GetString());
        Assert.Equal(1, await Db(f, d => d.Profiles.CountAsync()));
        Assert.Equal(1, await Db(f, d => d.ProfileRevisions.CountAsync()));
        Assert.Equal(1, await Db(f, d => d.ParticipantPrivates.CountAsync()));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("unknown")]
    [InlineData("revoked")]
    [InlineData("garbage")]
    public async Task An_invalid_cohort_is_400_on_cohort_id(string kind)
    {
        var f = await NewFactoryAsync();
        var c = await SignedInAsync(f);
        var body = Body(kind == "revoked" ? await SeedCohortAsync(f, revoked: true) : kind == "unknown" ? Guid.NewGuid() : null);
        if (kind == "empty") body["cohortId"] = "";
        if (kind == "garbage") body["cohortId"] = "not-a-guid";
        if (kind == "missing") body.Remove("cohortId");

        var response = await Create(c, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await Json(response)).GetProperty("errors").TryGetProperty("cohortId", out _));
        Assert.Equal(0, await Db(f, d => d.Profiles.CountAsync()));
    }

    [Theory]
    [InlineData("name")]
    [InlineData("productName")]
    [InlineData("productUrl")]
    [InlineData("phone")]
    public async Task A_missing_required_field_is_400_for_that_field_only(string field)
    {
        var f = await NewFactoryAsync();
        var cohort = await SeedCohortAsync(f);
        var c = await SignedInAsync(f);
        var body = Body(cohort);
        body[field] = " ";

        var response = await Create(c, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await Json(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty(field, out _));
        Assert.False(errors.TryGetProperty("email", out _));
        Assert.Equal(0, await Db(f, d => d.Profiles.CountAsync()));
    }

    [Fact]
    public async Task An_invalid_url_and_phone_are_validated_like_the_invite_submission()
    {
        var f = await NewFactoryAsync();
        var cohort = await SeedCohortAsync(f);
        var c = await SignedInAsync(f);
        var body = Body(cohort, phone: "abc");
        body["productUrl"] = "javascript:alert(1)";
        var errors = (await Json(await Create(c, body))).GetProperty("errors");
        Assert.True(errors.TryGetProperty("productUrl", out _));
        Assert.True(errors.TryGetProperty("phone", out _));
    }

    [Fact]
    public async Task An_email_in_the_body_is_ignored_and_the_session_email_is_stored()
    {
        var f = await NewFactoryAsync();
        var cohort = await SeedCohortAsync(f);
        var c = await SignedInAsync(f, "real@example.com");

        Assert.Equal(HttpStatusCode.Created, (await Create(c, Body(cohort, email: "someone.else@example.com"))).StatusCode);
        Assert.Equal("real@example.com", (await Db(f, d => d.ParticipantPrivates.AsNoTracking().SingleAsync())).Email);

        // Even an invalid email in the body does not matter.
        var c2 = await SignedInAsync(f, "second@example.com");
        Assert.Equal(HttpStatusCode.Created, (await Create(c2, Body(cohort, email: "not an email"))).StatusCode);
    }

    [Fact]
    public async Task An_email_already_used_in_the_cohort_by_another_profile_is_409_email_taken_with_nothing_stored()
    {
        var f = await NewFactoryAsync();
        var cohort = await SeedCohortAsync(f);
        // Another profile (another account) already holds this email in the cohort.
        await Db(f, async d =>
        {
            var now = DateTime.UtcNow;
            var other = Profile.Create(cohort, now);
            d.Profiles.Add(other);
            d.ParticipantPrivates.Add(ParticipantPrivate.Create(other.Id, cohort, "dana@example.com", "0509999999"));
            d.ProfileRevisions.Add(ProfileRevision.CreatePending(other.Id, "אחרת", "מוצר", "https://example.com", now));
            await d.SaveChangesAsync();
            return 0;
        });
        var c = await SignedInAsync(f, "dana@example.com");

        var response = await Create(c, Body(cohort));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("email-taken", (await Json(response)).GetProperty("code").GetString());
        Assert.Equal(1, await Db(f, d => d.Profiles.CountAsync()));
        Assert.Equal(1, await Db(f, d => d.ProfileRevisions.CountAsync()));
        Assert.Equal(1, await Db(f, d => d.ParticipantPrivates.CountAsync()));
        Assert.Equal(0, await Db(f, d => d.Profiles.CountAsync(p => p.GoogleSub != null)));

        // The same account can still create in another cohort.
        var other = await SeedCohortAsync(f);
        Assert.Equal(HttpStatusCode.Created, (await Create(c, Body(other))).StatusCode);
    }

    [Fact]
    public async Task Optional_fields_in_a_json_create_are_stored_and_returned_by_my_profile()
    {
        var f = await NewFactoryAsync();
        var cohort = await SeedCohortAsync(f);
        var c = await SignedInAsync(f);
        var body = Body(cohort);
        body["description"] = "תיאור קצר";
        body["role"] = "מפתחת";
        body["workplace"] = "חברה";
        body["introText"] = "שורה ראשונה\nשורה שנייה";
        body["introAnswer1"] = "תשובה 1";
        body["introAnswer2"] = "תשובה 2";
        body["introAnswer3"] = "תשובה 3";
        body["avatarId"] = "boy-2";
        body["cv"] = "## ניסיון\n\nעבדתי בהרבה מקומות";

        Assert.Equal(HttpStatusCode.Created, (await Create(c, body)).StatusCode);

        var rev = await Db(f, d => d.ProfileRevisions.AsNoTracking().SingleAsync());
        Assert.Equal("תיאור קצר", rev.Description);
        Assert.Equal("מפתחת", rev.Role);
        Assert.Equal("חברה", rev.Workplace);
        Assert.Equal("שורה ראשונה\nשורה שנייה", rev.IntroText);
        Assert.Equal("תשובה 1", rev.IntroAnswer1);
        Assert.Equal("תשובה 2", rev.IntroAnswer2);
        Assert.Equal("תשובה 3", rev.IntroAnswer3);
        Assert.Equal("boy-2", rev.AvatarId);
        Assert.Contains("עבדתי בהרבה מקומות", rev.Cv);

        var own = await Json(await c.Http.GetAsync("/me/profile"));
        Assert.Equal("תיאור קצר", own.GetProperty("description").GetString());
        Assert.Equal("מפתחת", own.GetProperty("role").GetString());
        Assert.Equal("חברה", own.GetProperty("workplace").GetString());
        Assert.Equal("שורה ראשונה\nשורה שנייה", own.GetProperty("introText").GetString());
        Assert.Equal("תשובה 1", own.GetProperty("introAnswer1").GetString());
        Assert.Equal("תשובה 2", own.GetProperty("introAnswer2").GetString());
        Assert.Equal("תשובה 3", own.GetProperty("introAnswer3").GetString());
        Assert.Equal("boy-2", own.GetProperty("avatarId").GetString());
        Assert.Contains("עבדתי בהרבה מקומות", own.GetProperty("cv").GetString());
    }

    [Fact]
    public async Task A_multipart_create_with_a_photo_stores_the_reencoded_photo_and_resets_the_avatar()
    {
        var f = await NewFactoryAsync();
        var cohort = await SeedCohortAsync(f);
        var c = await SignedInAsync(f);

        byte[] original;
        using (var bitmap = new SkiaSharp.SKBitmap(64, 48))
        {
            bitmap.Erase(new SkiaSharp.SKColor(30, 120, 200));
            using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
            original = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90).ToArray();
        }

        var content = new MultipartFormDataContent();
        foreach (var (k, v) in new Dictionary<string, string>
                 {
                     ["cohortId"] = cohort.ToString("D"), ["name"] = "דנה כהן", ["productName"] = "מוצר", ["productUrl"] = "https://example.com/app",
                     ["phone"] = Phone, ["avatarId"] = "boy-2", ["email"] = "ignored@example.com",
                 })
            content.Add(new StringContent(v, System.Text.Encoding.UTF8), k);
        var file = new ByteArrayContent(original);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        content.Add(file, "photo", "me.png");
        var request = new HttpRequestMessage(HttpMethod.Post, "/me/profile") { Content = content };
        request.Headers.Add("Origin", ApiClient.Origin);
        request.Headers.Add("X-CSRF-TOKEN", await c.GetCsrfAsync());

        var response = await c.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var rev = await Db(f, d => d.ProfileRevisions.AsNoTracking().SingleAsync());
        Assert.Equal("image/jpeg", rev.PhotoContentType);
        Assert.NotNull(rev.Photo);
        Assert.Equal([(byte)0xFF, (byte)0xD8], rev.Photo!.Take(2).ToArray());
        Assert.Equal("default", rev.AvatarId); // an uploaded photo replaces the avatar
        Assert.Equal("dana@example.com", (await Db(f, d => d.ParticipantPrivates.AsNoTracking().SingleAsync())).Email);
        Assert.True((await Json(await c.Http.GetAsync("/me/profile"))).GetProperty("hasPhoto").GetBoolean());
    }

    [Fact]
    public async Task Active_cohorts_list_is_empty_when_every_cohort_is_revoked()
    {
        var f = await NewFactoryAsync();
        await SeedCohortAsync(f, revoked: true);
        await SeedCohortAsync(f, revoked: true);

        var json = await Json(await f.NewClient().Http.GetAsync("/cohorts/active"));

        Assert.Equal(0, json.GetProperty("total").GetInt32());
        Assert.Empty(json.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Calls_without_a_usable_session_do_not_consume_the_ip_budget()
    {
        var f = await NewFactoryAsync(new Dictionary<string, string> { ["RATE_LIMIT_PROFILE_IP"] = "2" });
        var cohort = await SeedCohortAsync(f);
        var anonymous = f.NewClient();
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Create(anonymous, Body(cohort))).StatusCode);

        var c = await SignedInAsync(f);
        Assert.Equal(HttpStatusCode.Created, (await Create(c, Body(cohort))).StatusCode);
    }

    [Fact]
    public async Task The_creation_is_rate_limited_per_ip()
    {
        var f = await NewFactoryAsync(new Dictionary<string, string> { ["RATE_LIMIT_PROFILE_IP"] = "2" });
        var cohort = await SeedCohortAsync(f);
        var c = await SignedInAsync(f);
        var bad = Body(cohort);
        bad["name"] = "";
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(c, bad)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(c, bad)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Create(c, Body(cohort))).StatusCode);
    }

    [Fact]
    public async Task Active_cohorts_list_has_only_non_revoked_cohorts_without_invite_tokens()
    {
        var f = await NewFactoryAsync();
        var open = await SeedCohortAsync(f);
        var revoked = await SeedCohortAsync(f, revoked: true);

        var response = await f.NewClient().Http.GetAsync("/cohorts/active");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var text = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(text).RootElement;
        Assert.Equal(1, json.GetProperty("total").GetInt32());
        var item = json.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(open, item.GetProperty("id").GetGuid());
        Assert.NotEqual(revoked, item.GetProperty("id").GetGuid());
        Assert.False(item.TryGetProperty("inviteToken", out _));
        var token = await Db(f, d => d.Cohorts.AsNoTracking().Where(x => x.Id == open).Select(x => x.InviteToken).SingleAsync());
        Assert.DoesNotContain(token, text);
    }

    [Fact]
    public async Task Nothing_private_leaks_into_responses_audit_outbox_or_logs()
    {
        var f = await NewFactoryAsync();
        var cohort = await SeedCohortAsync(f);
        var c = await SignedInAsync(f, "leak.check@example.com");
        var sub = FakeOidcProvider.SubFor("leak.check@example.com");
        const string phone = "0587654321";

        var twin = await SignedInAsync(f, "leak.check@example.com"); // signed in before the profile exists
        var created = await Create(c, Body(cohort, phone: phone));
        var conflict = await Create(twin, Body(cohort, phone: phone));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var bad = Body(cohort, phone: phone);
        bad["name"] = "";
        var invalid = await Create(await SignedInAsync(f, "second@example.com"), bad);

        foreach (var response in new[] { created, conflict, invalid })
        {
            var text = response + await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(phone, text);
            Assert.DoesNotContain(sub, text);
            Assert.DoesNotContain("leak.check@example.com", await response.Content.ReadAsStringAsync());
        }

        var audit = JsonSerializer.Serialize(await Db(f, d => d.AuditLog.AsNoTracking().ToListAsync()));
        var outbox = JsonSerializer.Serialize(await Db(f, d => d.EmailOutbox.AsNoTracking().ToListAsync()));
        var revisions = JsonSerializer.Serialize(await Db(f, d => d.ProfileRevisions.AsNoTracking().ToListAsync()));
        foreach (var stored in new[] { audit, outbox, revisions })
        {
            Assert.DoesNotContain(phone, stored);
            Assert.DoesNotContain(sub, stored);
        }

        Assert.NotEmpty(_log.Lines);
        foreach (var line in _log.Lines)
        {
            Assert.DoesNotContain(phone, line);
            Assert.DoesNotContain(sub, line);
            Assert.DoesNotContain("leak.check@example.com", line);
        }
    }
}
