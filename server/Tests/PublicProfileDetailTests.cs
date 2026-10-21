using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResumeApp.Domain.Cohorts;
using ResumeApp.Domain.Profiles;
using ResumeApp.Infrastructure;

namespace ResumeApp.Tests;

[Collection("env")]
public class PublicProfileDetailTests(PostgresFixture pg) : IDisposable
{
    private readonly List<AuthApiFactory> _factories = [];
    private static readonly byte[] PhotoBytes = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
    }

    private async Task<AuthApiFactory> NewFactoryAsync(IReadOnlyDictionary<string, string>? limits = null)
    {
        var f = await AuthApiFactory.CreateAsync(pg, limits: limits);
        _factories.Add(f);
        return f;
    }

    private static async Task<Cohort> SeedCohortAsync(AuthApiFactory f, string name)
    {
        await using var scope = f.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cohort = Cohort.Create(name, T0);
        db.Cohorts.Add(cohort);
        await db.SaveChangesAsync();
        return cohort;
    }

    private static async Task<(Guid ProfileId, Guid RevisionId)> SeedAsync(AuthApiFactory f, Guid cohortId, RevisionStatus status, string name,
        Guid? profileId = null, bool removed = false, bool approvedPointer = true, bool photo = false,
        string? a1 = "תשובה-1", string? a2 = "תשובה-2", string? a3 = "תשובה-3", string? cv = "## ניסיון\n\n- [אתר](https://example.com)",
        string email = "detail-private@example.com", bool blankOptional = false, bool emptyPhoto = false)
    {
        await using var scope = f.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Profile profile;
        if (profileId is null)
        {
            profile = Profile.Create(cohortId, T0);
            db.Profiles.Add(profile);
            await db.SaveChangesAsync();
            db.ParticipantPrivates.Add(ParticipantPrivate.Create(profile.Id, cohortId, email, "0509998877"));
        }
        else
        {
            profile = await db.Profiles.SingleAsync(p => p.Id == profileId);
        }

        var extras = blankOptional
            ? new ProfileExtras(null, null, null, null, a1, a2, a3, "girl-2", null, null, cv)
            : new ProfileExtras("תיאור " + name, "תפקיד " + name, "עבודה " + name, "היכרות " + name, a1, a2, a3, "girl-2",
                photo ? PhotoBytes : emptyPhoto ? [] : null, photo || emptyPhoto ? "image/jpeg" : null, cv);
        var rev = ProfileRevision.CreatePending(profile.Id, name, "מוצר " + name, "https://example.com/p", T0, extras);
        db.ProfileRevisions.Add(rev);
        db.Entry(rev).Property(r => r.Status).CurrentValue = status;
        await db.SaveChangesAsync();
        if (approvedPointer && status == RevisionStatus.Approved)
        {
            db.Entry(profile).Property(p => p.ApprovedRevisionId).CurrentValue = rev.Id;
            if (removed) db.Entry(profile).Property(p => p.Visibility).CurrentValue = ProfileVisibility.Removed;
            await db.SaveChangesAsync();
        }
        return (profile.Id, rev.Id);
    }

    private static async Task SetPointerAsync(AuthApiFactory f, Guid profileId, Guid revisionId)
    {
        await using var scope = f.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var profile = await db.Profiles.SingleAsync(p => p.Id == profileId);
        db.Entry(profile).Property(p => p.ApprovedRevisionId).CurrentValue = revisionId;
        await db.SaveChangesAsync();
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    private sealed record Snapshot(HttpStatusCode Status, string? CacheControl, string? ContentType, string Body);

    // The trace id is per request by design; everything else must be identical.
    private static async Task<Snapshot> SnapAsync(HttpClient http, string path)
    {
        var r = await http.GetAsync(path);
        var body = await r.Content.ReadAsStringAsync();
        var node = System.Text.Json.Nodes.JsonNode.Parse(body)!.AsObject();
        node.Remove("traceId");
        return new(r.StatusCode, r.Headers.CacheControl?.ToString(), r.Content.Headers.ContentType?.ToString(), node.ToJsonString());
    }

    private static async Task AssertUniform404Async(HttpClient http, string id)
    {
        foreach (var suffix in new[] { "", "/photo" })
        {
            var baseline = await SnapAsync(http, $"/profiles/{Guid.NewGuid():D}{suffix}");
            Assert.Equal(HttpStatusCode.NotFound, baseline.Status);
            Assert.Equal("no-store", baseline.CacheControl);
            Assert.Equal("application/problem+json", baseline.ContentType?.Split(';')[0]);
            Assert.Equal(baseline, await SnapAsync(http, $"/profiles/{id}{suffix}"));
        }
    }

    [Fact]
    public async Task Approved_profile_returns_the_full_detail()
    {
        var f = await NewFactoryAsync();
        var c = await SeedCohortAsync(f, "מחזור א");
        var s = await SeedAsync(f, c.Id, RevisionStatus.Approved, "דנה", photo: true);
        var http = f.NewClient().Http;

        var response = await http.GetAsync($"/profiles/{s.ProfileId:D}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public, max-age=30", response.Headers.CacheControl?.ToString());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        var json = await Json(response);
        Assert.Equal(s.ProfileId, json.GetProperty("id").GetGuid());
        Assert.Equal("דנה", json.GetProperty("name").GetString());
        Assert.Equal("מוצר דנה", json.GetProperty("productName").GetString());
        Assert.Equal("https://example.com/p", json.GetProperty("productUrl").GetString());
        Assert.Equal("תיאור דנה", json.GetProperty("description").GetString());
        Assert.Equal("תפקיד דנה", json.GetProperty("role").GetString());
        Assert.Equal("עבודה דנה", json.GetProperty("workplace").GetString());
        Assert.Equal("היכרות דנה", json.GetProperty("introText").GetString());
        Assert.Equal("girl-2", json.GetProperty("avatarId").GetString());
        Assert.Equal($"/profiles/{s.ProfileId}/photo", json.GetProperty("photoUrl").GetString());
        Assert.Equal(c.Id, json.GetProperty("cohort").GetProperty("id").GetGuid());
        Assert.Equal("מחזור א", json.GetProperty("cohort").GetProperty("name").GetString());
        Assert.Equal("## ניסיון\n\n- [אתר](https://example.com)", json.GetProperty("cv").GetString());
        var answers = json.GetProperty("introAnswers").EnumerateArray().ToList();
        Assert.Equal([1, 2, 3], answers.Select(a => a.GetProperty("number").GetInt32()));
        Assert.Equal(["תשובה-1", "תשובה-2", "תשובה-3"], answers.Select(a => a.GetProperty("text").GetString()));

        var photo = await http.GetAsync(json.GetProperty("photoUrl").GetString());
        Assert.Equal(HttpStatusCode.OK, photo.StatusCode);
        Assert.Equal(PhotoBytes, await photo.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Only_answered_questions_appear_in_order_and_none_gives_an_empty_array()
    {
        var f = await NewFactoryAsync();
        var c = await SeedCohortAsync(f, "מחזור א");
        var onlyTwo = await SeedAsync(f, c.Id, RevisionStatus.Approved, "א", a1: null, a2: "רק-שתיים", a3: "   ", email: "a@example.com");
        var none = await SeedAsync(f, c.Id, RevisionStatus.Approved, "ב", a1: null, a2: " ", a3: null, cv: null, email: "b@example.com");
        var http = f.NewClient().Http;

        var json = await Json(await http.GetAsync($"/profiles/{onlyTwo.ProfileId}"));
        var answers = json.GetProperty("introAnswers").EnumerateArray().ToList();
        Assert.Single(answers);
        Assert.Equal(2, answers[0].GetProperty("number").GetInt32());
        Assert.Equal("רק-שתיים", answers[0].GetProperty("text").GetString());

        var empty = await Json(await http.GetAsync($"/profiles/{none.ProfileId}"));
        Assert.Equal(0, empty.GetProperty("introAnswers").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("cv").ValueKind);
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("photoUrl").ValueKind);
    }

    [Theory]
    [InlineData("## ניסיון\n\n- [אתר](https://example.com)", "## ניסיון\n\n- [אתר](https://example.com)")]
    [InlineData("שלום <script>alert(1)</script> עולם", "שלום alert(1) עולם")]
    [InlineData("[לחץ](javascript:alert(1))", null)]
    [InlineData("<img src=x onerror=alert(1)>", null)]
    [InlineData("```\ncode\n```", null)]
    public async Task Hostile_cv_written_directly_is_cleaned_or_null(string cv, string? expected)
    {
        var f = await NewFactoryAsync();
        var c = await SeedCohortAsync(f, "מחזור א");
        var s = await SeedAsync(f, c.Id, RevisionStatus.Approved, "דנה", cv: cv);
        var response = await f.NewClient().Http.GetAsync($"/profiles/{s.ProfileId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var value = (await Json(response)).GetProperty("cv");
        if (expected is null) Assert.Equal(JsonValueKind.Null, value.ValueKind);
        else Assert.Equal(expected, value.GetString());
    }

    [Fact]
    public async Task Missing_optional_fields_serialize_as_null_and_description_as_empty_string()
    {
        var f = await NewFactoryAsync();
        var c = await SeedCohortAsync(f, "מחזור א");
        var s = await SeedAsync(f, c.Id, RevisionStatus.Approved, "דנה", blankOptional: true);
        var json = await Json(await f.NewClient().Http.GetAsync($"/profiles/{s.ProfileId}"));
        Assert.Equal("", json.GetProperty("description").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("role").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("workplace").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("introText").ValueKind);
    }

    [Fact]
    public async Task Zero_length_photo_gives_null_photo_url_and_404_photo()
    {
        var f = await NewFactoryAsync();
        var c = await SeedCohortAsync(f, "מחזור א");
        var s = await SeedAsync(f, c.Id, RevisionStatus.Approved, "דנה", emptyPhoto: true);
        var http = f.NewClient().Http;
        var json = await Json(await http.GetAsync($"/profiles/{s.ProfileId}"));
        Assert.Equal(JsonValueKind.Null, json.GetProperty("photoUrl").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"/profiles/{s.ProfileId}/photo")).StatusCode);
    }

    [Fact]
    public async Task Intro_answers_are_trimmed_and_line_endings_normalised()
    {
        var f = await NewFactoryAsync();
        var c = await SeedCohortAsync(f, "מחזור א");
        var s = await SeedAsync(f, c.Id, RevisionStatus.Approved, "דנה", a1: "  שורה א\r\nשורה ב\rשורה ג \n ", a2: "\r\n  ", a3: "\tסוף\t");
        var json = await Json(await f.NewClient().Http.GetAsync($"/profiles/{s.ProfileId}"));
        var answers = json.GetProperty("introAnswers").EnumerateArray().ToList();
        Assert.Equal([1, 3], answers.Select(a => a.GetProperty("number").GetInt32()));
        Assert.Equal(["שורה א\nשורה ב\nשורה ג", "סוף"], answers.Select(a => a.GetProperty("text").GetString()));
    }

    [Fact]
    public async Task Gallery_queries_do_not_select_the_heavy_detail_columns()
    {
        var f = await NewFactoryAsync();
        var c = await SeedCohortAsync(f, "מחזור א");
        await SeedAsync(f, c.Id, RevisionStatus.Approved, "דנה");
        var log = new SqlLog();
        await using var scope = f.Services.CreateAsyncScope();
        var options = new DbContextOptionsBuilder<AppDbContext>(scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>())
            .AddInterceptors(log).Options;
        await using var db = new AppDbContext(options);
        var page = await new PublicProfileReader(db).GetPageAsync(null, 10, 0, CancellationToken.None);
        Assert.Single(page.Items);
        Assert.NotEmpty(log.Commands);
        foreach (var sql in log.Commands)
            foreach (var column in new[] { "cv", "intro_answer_", "intro_text", "role", "workplace", "photo," })
                Assert.DoesNotContain(column, sql.Replace("\"", ""), StringComparison.OrdinalIgnoreCase);
    }

    private sealed class SqlLog : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> ReaderExecuting(
            System.Data.Common.DbCommand command, Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result)
        {
            Commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command, Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task Unavailable_profiles_all_give_the_same_404_for_profile_and_photo()
    {
        var f = await NewFactoryAsync();
        var c = await SeedCohortAsync(f, "מחזור א");
        var pendingOnly = await SeedAsync(f, c.Id, RevisionStatus.Pending, "ממתינה", photo: true, email: "p1@example.com");
        var returned = await SeedAsync(f, c.Id, RevisionStatus.Returned, "הוחזרה", photo: true, email: "p2@example.com");
        var superseded = await SeedAsync(f, c.Id, RevisionStatus.Superseded, "הוחלפה", photo: true, email: "p3@example.com");
        var removed = await SeedAsync(f, c.Id, RevisionStatus.Approved, "הוסרה", removed: true, photo: true, email: "p4@example.com");
        var visible = await SeedAsync(f, c.Id, RevisionStatus.Approved, "גלויה", photo: true, email: "p5@example.com");
        var http = f.NewClient().Http;

        foreach (var id in new[] { pendingOnly.ProfileId, returned.ProfileId, superseded.ProfileId, removed.ProfileId, Guid.NewGuid() })
            await AssertUniform404Async(http, id.ToString("D"));
        foreach (var bad in new[] { "abc", visible.ProfileId.ToString("N"), visible.ProfileId.ToString("B"), visible.ProfileId.ToString("P"), "00000000-0000-0000-0000-000000000000" })
            await AssertUniform404Async(http, Uri.EscapeDataString(bad));

        // The uniform body does not depend on why the profile is unavailable.
        var a = await (await http.GetAsync($"/profiles/{pendingOnly.ProfileId}")).Content.ReadAsStringAsync();
        var b = await (await http.GetAsync($"/profiles/{Guid.NewGuid()}")).Content.ReadAsStringAsync();
        using var da = JsonDocument.Parse(a);
        using var db = JsonDocument.Parse(b);
        Assert.Equal(da.RootElement.GetProperty("title").GetString(), db.RootElement.GetProperty("title").GetString());
        Assert.Equal(da.RootElement.GetProperty("status").GetInt32(), db.RootElement.GetProperty("status").GetInt32());

        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync($"/profiles/{visible.ProfileId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync($"/profiles/{visible.ProfileId}/photo")).StatusCode);
    }

    [Fact]
    public async Task Pending_update_does_not_change_what_is_shown()
    {
        var f = await NewFactoryAsync();
        var c = await SeedCohortAsync(f, "מחזור א");
        var s = await SeedAsync(f, c.Id, RevisionStatus.Approved, "מאושרת");
        await SeedAsync(f, c.Id, RevisionStatus.Pending, "עדכון-ממתין", profileId: s.ProfileId, photo: true, cv: "קורות-חיים-חדשים");
        var http = f.NewClient().Http;

        var json = await Json(await http.GetAsync($"/profiles/{s.ProfileId}"));
        Assert.Equal("מאושרת", json.GetProperty("name").GetString());
        Assert.Equal("תפקיד מאושרת", json.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("photoUrl").ValueKind);
        Assert.DoesNotContain("קורות-חיים-חדשים", json.GetProperty("cv").GetString());
        // The pending revision's photo is never served.
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"/profiles/{s.ProfileId}/photo")).StatusCode);
    }

    [Theory]
    [InlineData(RevisionStatus.Pending)]
    [InlineData(RevisionStatus.Returned)]
    [InlineData(RevisionStatus.Superseded)]
    public async Task Pointer_to_a_non_approved_revision_is_not_available(RevisionStatus status)
    {
        var f = await NewFactoryAsync();
        var c = await SeedCohortAsync(f, "מחזור א");
        var s = await SeedAsync(f, c.Id, status, "לא-מאושרת", approvedPointer: false, photo: true);
        await SetPointerAsync(f, s.ProfileId, s.RevisionId);
        await AssertUniform404Async(f.NewClient().Http, s.ProfileId.ToString("D"));
    }

    [Fact]
    public async Task Pointer_to_another_profiles_revision_is_not_available()
    {
        var f = await NewFactoryAsync();
        var c = await SeedCohortAsync(f, "מחזור א");
        var other = await SeedAsync(f, c.Id, RevisionStatus.Approved, "תקינה", photo: true, email: "o@example.com");
        var victim = await SeedAsync(f, c.Id, RevisionStatus.Pending, "פגומה", approvedPointer: false, email: "v@example.com");
        await SetPointerAsync(f, victim.ProfileId, other.RevisionId);
        await AssertUniform404Async(f.NewClient().Http, victim.ProfileId.ToString("D"));
    }

    [Fact]
    public async Task Response_has_no_private_data_or_admin_fields()
    {
        var f = await NewFactoryAsync();
        var c = await SeedCohortAsync(f, "מחזור א");
        var s = await SeedAsync(f, c.Id, RevisionStatus.Approved, "דנה", email: "dana-private@example.com");
        await SeedAsync(f, c.Id, RevisionStatus.Pending, "עדכון", profileId: s.ProfileId);
        var body = await (await f.NewClient().Http.GetAsync($"/profiles/{s.ProfileId}")).Content.ReadAsStringAsync();

        foreach (var secret in new[] { "dana-private@example.com", "0509998877", c.InviteToken, s.RevisionId.ToString(), "approvedRevisionId", "inviteToken", "email", "phone", "status", "visibility" })
            Assert.DoesNotContain(secret, body, StringComparison.OrdinalIgnoreCase);

        using var doc = JsonDocument.Parse(body);
        var names = doc.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray();
        Assert.Equal(new[] { "avatarId", "cohort", "cv", "description", "id", "introAnswers", "introText", "name", "photoUrl", "productName", "productUrl", "role", "workplace" }.Order(), names);
    }

    [Fact]
    public async Task Rate_limit_returns_429_with_retry_after_and_code()
    {
        var f = await NewFactoryAsync(new Dictionary<string, string> { ["RATE_LIMIT_GALLERY_IP"] = "1" });
        var http = f.NewClient().Http;
        await http.GetAsync($"/profiles/{Guid.NewGuid()}");
        var blocked = await http.GetAsync($"/profiles/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.True(blocked.Headers.Contains("Retry-After"));
        Assert.Equal("rate-limited", (await Json(blocked)).GetProperty("code").GetString());
        Assert.Equal("no-store", blocked.Headers.CacheControl?.ToString());
    }
}
