using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResumeApp.Domain.Cohorts;
using ResumeApp.Domain.Profiles;
using ResumeApp.Infrastructure;

namespace ResumeApp.Tests;

[Collection("env")]
public class PublicGalleryTests(PostgresFixture pg) : IDisposable
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
        DateTime submittedAt, bool photo = false, bool approvedPointer = true, Guid? profileId = null, bool removed = false, string email = "", bool photoWithoutType = false)
    {
        await using var scope = f.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Profile profile;
        if (profileId is null)
        {
            profile = Profile.Create(cohortId, submittedAt);
            db.Profiles.Add(profile);
            await db.SaveChangesAsync();
            db.ParticipantPrivates.Add(ParticipantPrivate.Create(profile.Id, cohortId, string.IsNullOrEmpty(email) ? $"{Guid.NewGuid():N}@secret-mail.example" : email, "0509998877"));
        }
        else
        {
            profile = await db.Profiles.SingleAsync(p => p.Id == profileId);
        }

        var extras = new ProfileExtras("תיאור " + name, "תפקיד-סודי", "מקום-סודי", "היכרות-סודית", "תשובה-סודית", null, null, "girl-2",
            photo || photoWithoutType ? PhotoBytes : null, photo ? "image/jpeg" : null, "קורות-חיים-סודיים");
        var rev = ProfileRevision.CreatePending(profile.Id, name, "מוצר " + name, "https://example.com/" + Guid.NewGuid().ToString("N"), submittedAt, extras);
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

    private static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    private static string[] Names(JsonElement page) => page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()!).ToArray();

    [Fact]
    public async Task Empty_gallery_returns_200_with_no_items()
    {
        var f = await NewFactoryAsync();
        var http = f.NewClient().Http;
        var response = await http.GetAsync("/profiles");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await Json(response);
        Assert.Equal(0, json.GetProperty("items").GetArrayLength());
        Assert.Equal(0, json.GetProperty("total").GetInt32());

        var cohorts = await Json(await http.GetAsync("/cohorts"));
        Assert.Equal(0, cohorts.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Gallery_shows_only_active_approved_profiles_newest_first_from_the_approved_revision()
    {
        var f = await NewFactoryAsync();
        var c = await SeedCohortAsync(f, "מחזור א");
        var older = await SeedAsync(f, c.Id, RevisionStatus.Approved, "ישנה", T0.AddHours(1));
        var newer = await SeedAsync(f, c.Id, RevisionStatus.Approved, "חדשה", T0.AddHours(3), photo: true);
        await SeedAsync(f, c.Id, RevisionStatus.Pending, "ממתינה", T0.AddHours(5));
        await SeedAsync(f, c.Id, RevisionStatus.Returned, "הוחזרה", T0.AddHours(5));
        await SeedAsync(f, c.Id, RevisionStatus.Superseded, "הוחלפה", T0.AddHours(5));
        await SeedAsync(f, c.Id, RevisionStatus.Approved, "הוסרה", T0.AddHours(6), removed: true);
        // An update pending on an approved profile does not change what is shown.
        await SeedAsync(f, c.Id, RevisionStatus.Pending, "עדכון-ממתין", T0.AddHours(7), profileId: older.ProfileId);

        var http = f.NewClient().Http;
        var response = await http.GetAsync("/profiles");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public, max-age=30", response.Headers.CacheControl?.ToString());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        var json = await Json(response);
        Assert.Equal(2, json.GetProperty("total").GetInt32());
        Assert.Equal(["חדשה", "ישנה"], Names(json));

        var card = json.GetProperty("items")[0];
        Assert.Equal(newer.ProfileId, card.GetProperty("id").GetGuid());
        Assert.Equal("מוצר חדשה", card.GetProperty("productName").GetString());
        Assert.StartsWith("https://example.com/", card.GetProperty("productUrl").GetString());
        Assert.Equal("תיאור חדשה", card.GetProperty("description").GetString());
        Assert.Equal("girl-2", card.GetProperty("avatarId").GetString());
        Assert.Equal($"/profiles/{newer.ProfileId}/photo", card.GetProperty("photoUrl").GetString());
        Assert.Equal(c.Id, card.GetProperty("cohort").GetProperty("id").GetGuid());
        Assert.Equal("מחזור א", card.GetProperty("cohort").GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("items")[1].GetProperty("photoUrl").ValueKind);

        // The photo URL actually serves the approved photo.
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(card.GetProperty("photoUrl").GetString())).StatusCode);
    }

    [Fact]
    public async Task Responses_never_contain_private_data_and_ids_are_random_guids()
    {
        var f = await NewFactoryAsync();
        var c = await SeedCohortAsync(f, "מחזור א");
        var s = await SeedAsync(f, c.Id, RevisionStatus.Approved, "דנה", T0, email: "dana-private@example.com");
        await SeedAsync(f, c.Id, RevisionStatus.Pending, "עדכון", T0.AddHours(1), profileId: s.ProfileId);

        var http = f.NewClient().Http;
        var gallery = await (await http.GetAsync("/profiles")).Content.ReadAsStringAsync();
        var cohorts = await (await http.GetAsync("/cohorts")).Content.ReadAsStringAsync();
        foreach (var body in new[] { gallery, cohorts })
        {
            foreach (var secret in new[] { "dana-private@example.com", "0509998877", c.InviteToken, "קורות-חיים-סודיים", "תשובה-סודית", "היכרות-סודית", "תפקיד-סודי", "מקום-סודי", s.RevisionId.ToString(), "approvedRevisionId", "inviteToken", "email", "phone", "status", "visibility" })
                Assert.DoesNotContain(secret, body, StringComparison.OrdinalIgnoreCase);
        }

        using var doc = JsonDocument.Parse(gallery);
        var id = doc.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid();
        Assert.Equal(s.ProfileId, id);
        AssertV4(id);
        AssertV4(doc.RootElement.GetProperty("items")[0].GetProperty("cohort").GetProperty("id").GetGuid());
        using var cdoc = JsonDocument.Parse(cohorts);
        AssertV4(cdoc.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid());

        // Property names of every JSON object returned are free of forbidden words.
        foreach (var name in PropertyNames(doc.RootElement).Concat(PropertyNames(cdoc.RootElement)))
            Assert.DoesNotContain(Forbidden, w => name.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertV4(Guid id)
    {
        var s = id.ToString("D");
        Assert.Equal('4', s[14]);
        Assert.Contains(s[19], "89ab");
    }

    private static IEnumerable<string> PropertyNames(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Object)
            foreach (var p in e.EnumerateObject())
            {
                yield return p.Name;
                foreach (var n in PropertyNames(p.Value)) yield return n;
            }
        else if (e.ValueKind == JsonValueKind.Array)
            foreach (var item in e.EnumerateArray())
                foreach (var n in PropertyNames(item)) yield return n;
    }

    private static readonly string[] Forbidden = ["email", "phone", "mobile", "token", "contact", "request", "private", "revision"];

    [Fact]
    public void Every_Public_type_in_the_API_has_no_private_property()
    {
        var apiAssembly = typeof(Program).Assembly;
        var assemblies = new[] { apiAssembly }
            .Concat(apiAssembly.GetReferencedAssemblies().Where(a => a.Name!.StartsWith("ResumeApp", StringComparison.Ordinal)).Select(Assembly.Load))
            .Distinct().ToList();
        var types = assemblies.SelectMany(a => a.GetTypes()).Where(t => t.Name.StartsWith("Public", StringComparison.Ordinal)).ToList();

        // The scan must really see the gallery DTOs and the older public shapes.
        foreach (var expected in new[] { "PublicProfileCard", "PublicProfilePage", "PublicCohortRef", "PublicCohortList", "PublicInvite" })
            Assert.Contains(types, t => t.Name == expected);

        foreach (var type in types)
        {
            var members = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Select(p => p.Name)
                .Concat(type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Select(f => f.Name));
            foreach (var member in members)
            {
                var hit = Forbidden.FirstOrDefault(w => member.Contains(w, StringComparison.OrdinalIgnoreCase));
                Assert.True(hit is null, $"{type.Name}.{member} contains forbidden word '{hit}'.");
            }
        }
    }

    [Fact]
    public async Task Cohort_filter_returns_only_that_cohort_and_rejects_bad_ids()
    {
        var f = await NewFactoryAsync();
        var a = await SeedCohortAsync(f, "מחזור א");
        var b = await SeedCohortAsync(f, "מחזור ב");
        await SeedAsync(f, a.Id, RevisionStatus.Approved, "א1", T0.AddHours(1));
        await SeedAsync(f, b.Id, RevisionStatus.Approved, "ב1", T0.AddHours(2));
        await SeedAsync(f, b.Id, RevisionStatus.Approved, "ב2", T0.AddHours(3));
        var http = f.NewClient().Http;

        var onlyB = await Json(await http.GetAsync($"/profiles?cohort={b.Id:D}"));
        Assert.Equal(2, onlyB.GetProperty("total").GetInt32());
        Assert.Equal(["ב2", "ב1"], Names(onlyB));

        var unknown = await Json(await http.GetAsync($"/profiles?cohort={Guid.NewGuid():D}"));
        Assert.Equal(0, unknown.GetProperty("total").GetInt32());
        Assert.Equal(0, unknown.GetProperty("items").GetArrayLength());

        foreach (var bad in new[] { "abc", "", $"{b.Id:N}", $"{{{b.Id:D}}}", b.InviteToken })
        {
            var response = await http.GetAsync($"/profiles?cohort={Uri.EscapeDataString(bad)}");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        }
    }

    [Fact]
    public async Task Paging_slices_and_validates()
    {
        var f = await NewFactoryAsync();
        var c = await SeedCohortAsync(f, "מחזור א");
        for (var i = 1; i <= 4; i++) await SeedAsync(f, c.Id, RevisionStatus.Approved, $"פ{i}", T0.AddHours(i));
        var http = f.NewClient().Http;

        var page = await Json(await http.GetAsync("/profiles?limit=2&offset=1"));
        Assert.Equal(4, page.GetProperty("total").GetInt32());
        Assert.Equal(["פ3", "פ2"], Names(page));

        var big = await http.GetAsync("/profiles?limit=100000");
        Assert.Equal(HttpStatusCode.OK, big.StatusCode);
        Assert.Equal(4, (await Json(big)).GetProperty("items").GetArrayLength());

        foreach (var bad in new[] { "limit=0", "limit=-1", "limit=x", "offset=-1", "offset=x" })
            Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync("/profiles?" + bad)).StatusCode);

        var past = await Json(await http.GetAsync("/profiles?offset=10"));
        Assert.Equal(4, past.GetProperty("total").GetInt32());
        Assert.Equal(0, past.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Default_limit_is_100_and_max_is_200()
    {
        var f = await NewFactoryAsync();
        var c = await SeedCohortAsync(f, "מחזור א");
        await using (var scope = f.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var i = 0; i < 205; i++)
            {
                var p = Profile.Create(c.Id, T0);
                db.Profiles.Add(p);
                var rev = ProfileRevision.CreatePending(p.Id, "n" + i, "p", "https://example.com", T0.AddMinutes(i));
                db.ProfileRevisions.Add(rev);
                db.Entry(rev).Property(r => r.Status).CurrentValue = RevisionStatus.Approved;
                await db.SaveChangesAsync();
                db.Entry(p).Property(x => x.ApprovedRevisionId).CurrentValue = rev.Id;
                await db.SaveChangesAsync();
            }
        }
        var http = f.NewClient().Http;
        var def = await Json(await http.GetAsync("/profiles"));
        Assert.Equal(100, def.GetProperty("items").GetArrayLength());
        Assert.Equal(205, def.GetProperty("total").GetInt32());
        Assert.Equal(200, (await Json(await http.GetAsync("/profiles?limit=5000"))).GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Cohorts_lists_only_cohorts_with_a_public_profile_by_name()
    {
        var f = await NewFactoryAsync();
        var b = await SeedCohortAsync(f, "ב מחזור");
        var a = await SeedCohortAsync(f, "א מחזור");
        var empty = await SeedCohortAsync(f, "ריק");
        var pendingOnly = await SeedCohortAsync(f, "ממתין בלבד");
        await SeedAsync(f, b.Id, RevisionStatus.Approved, "x", T0);
        await SeedAsync(f, b.Id, RevisionStatus.Approved, "y", T0.AddHours(1));
        await SeedAsync(f, a.Id, RevisionStatus.Approved, "z", T0);
        await SeedAsync(f, pendingOnly.Id, RevisionStatus.Pending, "w", T0);

        var response = await f.NewClient().Http.GetAsync("/cohorts");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public, max-age=30", response.Headers.CacheControl?.ToString());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        var json = await Json(response);
        Assert.Equal(2, json.GetProperty("total").GetInt32());
        var items = json.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal([a.Id, b.Id], items.Select(i => i.GetProperty("id").GetGuid()));
        Assert.Equal(["א מחזור", "ב מחזור"], items.Select(i => i.GetProperty("name").GetString()));
        Assert.All(items, i => Assert.Equal(2, i.EnumerateObject().Count()));
        Assert.DoesNotContain(empty.Id, items.Select(i => i.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task Removed_profile_disappears_and_its_cohort_with_it()
    {
        var f = await NewFactoryAsync();
        var c = await SeedCohortAsync(f, "מחזור א");
        await SeedAsync(f, c.Id, RevisionStatus.Approved, "הוסרה", T0, removed: true);
        var http = f.NewClient().Http;
        Assert.Equal(0, (await Json(await http.GetAsync("/profiles"))).GetProperty("total").GetInt32());
        Assert.Equal(0, (await Json(await http.GetAsync("/cohorts"))).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Rate_limit_returns_429_with_retry_after_and_code()
    {
        var f = await NewFactoryAsync(new Dictionary<string, string> { ["RATE_LIMIT_GALLERY_IP"] = "2" });
        var http = f.NewClient().Http;
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/profiles")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/cohorts")).StatusCode);
        var blocked = await http.GetAsync("/profiles");
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.True(blocked.Headers.Contains("Retry-After"));
        Assert.Equal("rate-limited", (await Json(blocked)).GetProperty("code").GetString());
        Assert.Equal("no-store", blocked.Headers.CacheControl?.ToString());
    }

    private static async Task SetPointerAsync(AuthApiFactory f, Guid profileId, Guid revisionId)
    {
        await using var scope = f.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var profile = await db.Profiles.SingleAsync(p => p.Id == profileId);
        db.Entry(profile).Property(p => p.ApprovedRevisionId).CurrentValue = revisionId;
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData(RevisionStatus.Pending)]
    [InlineData(RevisionStatus.Returned)]
    [InlineData(RevisionStatus.Superseded)]
    public async Task Pointer_to_a_non_approved_revision_is_not_public(RevisionStatus status)
    {
        var f = await NewFactoryAsync();
        var c = await SeedCohortAsync(f, "מחזור מצביע");
        var s = await SeedAsync(f, c.Id, status, "לא-מאושרת", T0, approvedPointer: false);
        await SetPointerAsync(f, s.ProfileId, s.RevisionId);
        var http = f.NewClient().Http;
        Assert.Equal(0, (await Json(await http.GetAsync("/profiles"))).GetProperty("total").GetInt32());
        Assert.Equal(0, (await Json(await http.GetAsync("/cohorts"))).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Pointer_to_another_profiles_approved_revision_is_not_public()
    {
        var f = await NewFactoryAsync();
        var good = await SeedCohortAsync(f, "מחזור תקין");
        var bad = await SeedCohortAsync(f, "מחזור פגום");
        var other = await SeedAsync(f, good.Id, RevisionStatus.Approved, "תקינה", T0);
        var victim = await SeedAsync(f, bad.Id, RevisionStatus.Pending, "פגומה", T0, approvedPointer: false);
        await SetPointerAsync(f, victim.ProfileId, other.RevisionId);
        var http = f.NewClient().Http;
        var json = await Json(await http.GetAsync("/profiles"));
        Assert.Equal(["תקינה"], Names(json));
        var cohorts = await Json(await http.GetAsync("/cohorts"));
        Assert.Equal([good.Id], cohorts.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task Equal_submission_times_are_ordered_by_id_descending()
    {
        var f = await NewFactoryAsync();
        var c = await SeedCohortAsync(f, "מחזור א");
        var a = await SeedAsync(f, c.Id, RevisionStatus.Approved, "א", T0);
        var b = await SeedAsync(f, c.Id, RevisionStatus.Approved, "ב", T0);
        var expected = new[] { a, b }.OrderByDescending(x => x.ProfileId).Select(x => x.ProfileId).ToArray();
        var json = await Json(await f.NewClient().Http.GetAsync("/profiles"));
        Assert.Equal(expected, json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task Photo_without_content_type_gives_null_photo_url()
    {
        var f = await NewFactoryAsync();
        var c = await SeedCohortAsync(f, "מחזור א");
        await SeedAsync(f, c.Id, RevisionStatus.Approved, "בלי-סוג", T0, photoWithoutType: true);
        var json = await Json(await f.NewClient().Http.GetAsync("/profiles"));
        Assert.Equal(JsonValueKind.Null, json.GetProperty("items")[0].GetProperty("photoUrl").ValueKind);
    }

    [Theory]
    [InlineData("cohort=abc", "cohort")]
    [InlineData("limit=0", "limit")]
    [InlineData("limit=x", "limit")]
    [InlineData("offset=-1", "offset")]
    public async Task Bad_parameters_return_problem_with_title_and_field_key(string query, string field)
    {
        var f = await NewFactoryAsync();
        var response = await f.NewClient().Http.GetAsync("/profiles?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await Json(response);
        Assert.Equal("הנתונים אינם תקינים", json.GetProperty("title").GetString());
        Assert.Equal(1, json.GetProperty("errors").GetProperty(field).GetArrayLength());
        Assert.Single(json.GetProperty("errors").EnumerateObject());
    }

    [Theory]
    [InlineData("limit=%205")]
    [InlineData("limit=%2B5")]
    [InlineData("offset=%2B1")]
    [InlineData("offset=%201")]
    [InlineData("offset=99999999999")]
    [InlineData("limit=99999999999")]
    [InlineData("limit=1&limit=2")]
    [InlineData("offset=0&offset=1")]
    [InlineData("cohort=00000000-0000-4000-8000-000000000000&cohort=00000000-0000-4000-8000-000000000001")]
    public async Task Ambiguous_or_non_canonical_parameters_are_rejected(string query)
    {
        var f = await NewFactoryAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await f.NewClient().Http.GetAsync("/profiles?" + query)).StatusCode);
    }

    [Fact]
    public async Task Cohort_limit_and_offset_combine()
    {
        var f = await NewFactoryAsync();
        var a = await SeedCohortAsync(f, "מחזור א");
        var b = await SeedCohortAsync(f, "מחזור ב");
        await SeedAsync(f, a.Id, RevisionStatus.Approved, "א1", T0.AddHours(9));
        for (var i = 1; i <= 4; i++) await SeedAsync(f, b.Id, RevisionStatus.Approved, $"ב{i}", T0.AddHours(i));
        var json = await Json(await f.NewClient().Http.GetAsync($"/profiles?cohort={b.Id:D}&limit=2&offset=1"));
        Assert.Equal(4, json.GetProperty("total").GetInt32());
        Assert.Equal(["ב3", "ב2"], Names(json));
    }
}
