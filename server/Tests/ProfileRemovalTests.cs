using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResumeApp.Domain.Email;
using ResumeApp.Domain.Profiles;
using ResumeApp.Features.Email;
using ResumeApp.Features.Profiles;
using ResumeApp.Infrastructure;

namespace ResumeApp.Tests;

/// <summary>Story 2.5: admin removal of a published profile, and the admin profile list.</summary>
[Collection("env")]
public class ProfileRemovalTests(PostgresFixture pg) : IDisposable
{
    private const string NewPassword = "Brand-New-Pass-42#Lk";
    private const string CookieName = "__Host-resume-participant";
    private static readonly byte[] PhotoBytes = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];
    private readonly List<AuthApiFactory> _factories = [];

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
    }

    private async Task<(AuthApiFactory Factory, ApiClient Admin, Guid CohortId)> SetupAsync(string cohortName = "מחזור ב")
    {
        var factory = await AuthApiFactory.CreateAsync(pg);
        _factories.Add(factory);
        var admin = factory.NewClient();
        await admin.EnrollAsync(AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword, NewPassword);
        var response = await admin.PostWithCsrfAsync("/admin/cohorts", new { name = cohortName });
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (factory, admin, Guid.Parse(json.GetProperty("id").GetString()!));
    }

    private static async Task<Guid> NewCohortAsync(ApiClient admin, string name)
    {
        var json = await (await admin.PostWithCsrfAsync("/admin/cohorts", new { name })).Content.ReadFromJsonAsync<JsonElement>();
        return Guid.Parse(json.GetProperty("id").GetString()!);
    }

    /// <summary>Seeds a profile with one revision in the given state; an approved revision gets the pointer.</summary>
    private static async Task<(Guid ProfileId, Guid RevisionId)> SeedAsync(AuthApiFactory f, Guid cohortId, RevisionStatus status, string email,
        DateTime createdAt, string name = "דנה", string productName = "מוצר", bool photo = true, Guid? profileId = null)
    {
        await using var scope = f.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Profile profile;
        if (profileId is null)
        {
            profile = Profile.Create(cohortId, createdAt);
            db.Profiles.Add(profile);
            await db.SaveChangesAsync();
            db.ParticipantPrivates.Add(ParticipantPrivate.Create(profile.Id, cohortId, email, "0501234567"));
        }
        else profile = await db.Profiles.SingleAsync(p => p.Id == profileId);

        var extras = new ProfileExtras("תיאור", "תפקיד", "מקום", "היכרות", "א1", "א2", "א3", "girl-1",
            photo ? PhotoBytes : null, photo ? "image/jpeg" : null, "# קורות חיים");
        var rev = ProfileRevision.CreatePending(profile.Id, name, productName, "https://example.com/p", createdAt, extras);
        db.ProfileRevisions.Add(rev);
        db.Entry(rev).Property(r => r.Status).CurrentValue = status;
        await db.SaveChangesAsync();
        if (status == RevisionStatus.Approved)
        {
            db.Entry(profile).Property(p => p.ApprovedRevisionId).CurrentValue = rev.Id;
            await db.SaveChangesAsync();
        }
        return (profile.Id, rev.Id);
    }

    private static async Task<T> Db<T>(AuthApiFactory f, Func<AppDbContext, Task<T>> query)
    {
        await using var scope = f.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string> IssueAsync(AuthApiFactory f, Guid profileId)
    {
        await using var scope = f.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPersonalTokenIssuer>().IssueEditTokenAsync(profileId, DateTime.UtcNow, CancellationToken.None);
    }

    private static async Task<string> Csrf(ApiClient c)
    {
        var response = await c.Http.GetAsync("/profiles/csrf");
        response.EnsureSuccessStatusCode();
        return (await Json(response)).GetProperty("token").GetString()!;
    }

    private static Task<HttpResponseMessage> RemoveAsync(ApiClient admin, Guid profileId) => admin.PostWithCsrfAsync($"/admin/profiles/{profileId}/remove", null);

    private static async Task<(ProfileVisibility Visibility, Guid? Pointer)> StateAsync(AuthApiFactory f, Guid profileId)
    {
        var p = await Db(f, d => d.Profiles.AsNoTracking().SingleAsync(x => x.Id == profileId));
        return (p.Visibility, p.ApprovedRevisionId);
    }

    // ---- removal ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Remove_hides_the_profile_keeps_all_data_and_writes_one_audit_row()
    {
        var (f, admin, cohort) = await SetupAsync();
        var s = await SeedAsync(f, cohort, RevisionStatus.Approved, "priv@example.com", DateTime.UtcNow);
        var token = await IssueAsync(f, s.ProfileId);

        var response = await RemoveAsync(admin, s.ProfileId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("priv@example.com", body);
        Assert.DoesNotContain("0501234567", body);
        var json = JsonSerializer.Deserialize<JsonElement>(body);
        Assert.Equal(s.ProfileId, json.GetProperty("profileId").GetGuid());
        Assert.Equal("removed", json.GetProperty("visibility").GetString());

        Assert.Equal((ProfileVisibility.Removed, (Guid?)s.RevisionId), await StateAsync(f, s.ProfileId));
        var rev = await Db(f, d => d.ProfileRevisions.AsNoTracking().SingleAsync(r => r.ProfileId == s.ProfileId));
        Assert.Equal(RevisionStatus.Approved, rev.Status);
        Assert.Equal(PhotoBytes, rev.Photo);
        Assert.Equal("# קורות חיים", rev.Cv);
        var priv = await Db(f, d => d.ParticipantPrivates.AsNoTracking().SingleAsync(p => p.ProfileId == s.ProfileId));
        Assert.Equal("priv@example.com", priv.Email);
        Assert.Equal("0501234567", priv.Phone);

        var audit = await Db(f, d => d.AuditLog.AsNoTracking().ToListAsync());
        var row = Assert.Single(audit);
        Assert.Equal("remove-profile", row.Action);
        Assert.Equal("profile", row.EntityType);
        Assert.Equal(s.ProfileId, row.EntityId);
        await using var scope = f.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ResumeApp.Infrastructure.Identity.AppUser>>();
        Assert.Equal((await users.FindByNameAsync(AuthApiFactory.AdminUser))!.Id.ToString(), row.ActorId.ToString());

        // The unredeemed edit token was revoked in the same transaction.
        var tokens = await Db(f, d => d.PersonalTokens.AsNoTracking().Where(t => t.ProfileId == s.ProfileId).ToListAsync());
        Assert.All(tokens, t => Assert.NotNull(t.RevokedAt));
        var client = f.NewClient();
        var exchange = await client.PostAsync("/profiles/session", new { token }, await Csrf(client));
        Assert.Equal(HttpStatusCode.Unauthorized, exchange.StatusCode);
        Assert.Equal("link-invalid", (await Json(exchange)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Removing_an_already_removed_profile_is_409_and_changes_nothing()
    {
        var (f, admin, cohort) = await SetupAsync();
        var s = await SeedAsync(f, cohort, RevisionStatus.Approved, "a@example.com", DateTime.UtcNow);
        Assert.Equal(HttpStatusCode.OK, (await RemoveAsync(admin, s.ProfileId)).StatusCode);

        var again = await RemoveAsync(admin, s.ProfileId);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("profile-not-active", (await Json(again)).GetProperty("code").GetString());
        Assert.Single(await Db(f, d => d.AuditLog.AsNoTracking().ToListAsync()));
        Assert.Equal(ProfileVisibility.Removed, (await StateAsync(f, s.ProfileId)).Visibility);
    }

    [Fact]
    public async Task Unknown_or_non_canonical_ids_give_the_same_404()
    {
        var (f, admin, cohort) = await SetupAsync();
        var s = await SeedAsync(f, cohort, RevisionStatus.Approved, "a@example.com", DateTime.UtcNow);
        var bodies = new List<string>();
        foreach (var id in new[] { Guid.NewGuid().ToString(), s.ProfileId.ToString("N"), s.ProfileId.ToString("B").ToUpperInvariant(), "not-a-guid" })
        {
            var r = await admin.PostWithCsrfAsync($"/admin/profiles/{Uri.EscapeDataString(id)}/remove", null);
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
            var j = await Json(r);
            bodies.Add($"{j.GetProperty("status")}|{j.GetProperty("title")}");
        }
        Assert.Single(bodies.Distinct());
        Assert.Equal(ProfileVisibility.Active, (await StateAsync(f, s.ProfileId)).Visibility);
        Assert.Empty(await Db(f, d => d.AuditLog.AsNoTracking().ToListAsync()));
    }

    [Fact]
    public async Task Concurrent_removals_yield_exactly_one_success_and_one_audit_row()
    {
        var (f, admin, cohort) = await SetupAsync();
        var s = await SeedAsync(f, cohort, RevisionStatus.Approved, "x@example.com", DateTime.UtcNow);
        var csrfs = new List<string>();
        for (var i = 0; i < 5; i++) csrfs.Add(await admin.GetCsrfAsync());

        var results = await Task.WhenAll(csrfs.Select(c => admin.PostAsync($"/admin/profiles/{s.ProfileId}/remove", null, c)));

        var codes = results.Select(r => r.StatusCode).ToList();
        Assert.Single(codes, HttpStatusCode.OK);
        Assert.Equal(4, codes.Count(c => c == HttpStatusCode.Conflict));
        Assert.Single(await Db(f, d => d.AuditLog.AsNoTracking().ToListAsync()));
        Assert.Equal(ProfileVisibility.Removed, (await StateAsync(f, s.ProfileId)).Visibility);
    }

    [Fact]
    public async Task A_profile_with_only_a_pending_submission_can_be_removed()
    {
        var (f, admin, cohort) = await SetupAsync();
        var s = await SeedAsync(f, cohort, RevisionStatus.Pending, "p@example.com", DateTime.UtcNow);

        Assert.Equal(HttpStatusCode.OK, (await RemoveAsync(admin, s.ProfileId)).StatusCode);

        Assert.Equal((ProfileVisibility.Removed, (Guid?)null), await StateAsync(f, s.ProfileId));
        Assert.Equal(RevisionStatus.Pending, (await Db(f, d => d.ProfileRevisions.AsNoTracking().SingleAsync(r => r.Id == s.RevisionId))).Status);
    }

    [Fact]
    public async Task After_removal_the_public_site_no_longer_shows_or_reveals_the_profile()
    {
        var (f, admin, cohort) = await SetupAsync();
        var gone = await SeedAsync(f, cohort, RevisionStatus.Approved, "g@example.com", DateTime.UtcNow);
        var otherCohort = await NewCohortAsync(admin, "מחזור ג");
        var stays = await SeedAsync(f, otherCohort, RevisionStatus.Approved, "s@example.com", DateTime.UtcNow, name: "נשארת");
        var http = f.NewClient().Http;

        Assert.Equal(2, (await Json(await http.GetAsync("/profiles"))).GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync($"/profiles/{gone.ProfileId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync($"/profiles/{gone.ProfileId}/photo")).StatusCode);
        Assert.Equal(2, (await Json(await http.GetAsync("/cohorts"))).GetProperty("total").GetInt32());

        Assert.Equal(HttpStatusCode.OK, (await RemoveAsync(admin, gone.ProfileId)).StatusCode);

        var gallery = await http.GetAsync("/profiles");
        var galleryJson = await Json(gallery);
        Assert.Equal(1, galleryJson.GetProperty("total").GetInt32());
        Assert.DoesNotContain(gone.ProfileId.ToString(), await gallery.Content.ReadAsStringAsync());
        var detail = await http.GetAsync($"/profiles/{gone.ProfileId}");
        var photo = await http.GetAsync($"/profiles/{gone.ProfileId}/photo");
        var unknown = await http.GetAsync($"/profiles/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, photo.StatusCode);
        // Indistinguishable from a profile that never existed.
        var removedJson = await Json(detail);
        var unknownJson = await Json(unknown);
        Assert.Equal(unknownJson.GetProperty("title").GetString(), removedJson.GetProperty("title").GetString());
        var cohorts = await Json(await http.GetAsync("/cohorts"));
        Assert.Equal(1, cohorts.GetProperty("total").GetInt32());
        Assert.DoesNotContain("מחזור ב", cohorts.GetRawText());
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync($"/profiles/{stays.ProfileId}")).StatusCode);
    }

    [Fact]
    public async Task An_existing_participant_session_is_rejected_and_its_cookie_dropped_after_removal()
    {
        var (f, admin, cohort) = await SetupAsync();
        var s = await SeedAsync(f, cohort, RevisionStatus.Approved, "m@example.com", DateTime.UtcNow);
        var client = f.NewClient();
        var exchange = await client.PostAsync("/profiles/session", new { token = await IssueAsync(f, s.ProfileId) }, await Csrf(client));
        Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.Http.GetAsync("/me/profile")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await RemoveAsync(admin, s.ProfileId)).StatusCode);

        var get = await client.Http.GetAsync("/me/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
        Assert.Equal("session-invalid", (await Json(get)).GetProperty("code").GetString());
        var cookie = get.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith(CookieName + "=", StringComparison.Ordinal));
        Assert.StartsWith(CookieName + "=;", cookie);
    }

    [Fact]
    public async Task A_second_session_cannot_put_after_removal()
    {
        var (f, admin, cohort) = await SetupAsync();
        var s = await SeedAsync(f, cohort, RevisionStatus.Approved, "m2@example.com", DateTime.UtcNow);
        var client = f.NewClient();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/profiles/session", new { token = await IssueAsync(f, s.ProfileId) }, await Csrf(client))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RemoveAsync(admin, s.ProfileId)).StatusCode);

        var request = new HttpRequestMessage(HttpMethod.Put, "/me/profile")
        {
            Content = JsonContent.Create(new Dictionary<string, object?>
            {
                ["name"] = "שם", ["productName"] = "מוצר", ["productUrl"] = "https://example.com/app", ["email"] = "m2@example.com", ["phone"] = "0501234567",
            }),
        };
        request.Headers.Add("Origin", ApiClient.Origin);
        request.Headers.Add("X-CSRF-TOKEN", await Csrf(client));
        var put = await client.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, put.StatusCode);
        Assert.Single(await Db(f, d => d.ProfileRevisions.AsNoTracking().Where(r => r.ProfileId == s.ProfileId).ToListAsync()));
    }

    [Fact]
    public async Task Approving_a_new_revision_of_a_removed_profile_republishes_it()
    {
        var (f, admin, cohort) = await SetupAsync();
        var s = await SeedAsync(f, cohort, RevisionStatus.Approved, "r@example.com", DateTime.UtcNow.AddDays(-1));
        Assert.Equal(HttpStatusCode.OK, (await RemoveAsync(admin, s.ProfileId)).StatusCode);
        var pending = await SeedAsync(f, cohort, RevisionStatus.Pending, "", DateTime.UtcNow, profileId: s.ProfileId);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostWithCsrfAsync($"/admin/submissions/{pending.RevisionId}/approve", null)).StatusCode);

        Assert.Equal((ProfileVisibility.Active, (Guid?)pending.RevisionId), await StateAsync(f, s.ProfileId));
        Assert.Equal(HttpStatusCode.OK, (await f.NewClient().Http.GetAsync($"/profiles/{s.ProfileId}")).StatusCode);
    }

    [Fact]
    public async Task A_waiting_outbox_row_of_a_removed_profile_goes_dead_with_no_recipient()
    {
        var (f, admin, cohort) = await SetupAsync();
        var s = await SeedAsync(f, cohort, RevisionStatus.Approved, "o@example.com", DateTime.UtcNow);
        var now = DateTime.UtcNow;
        await Db(f, d => d.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO email_outbox (id, template_key, profile_id, deduplication_key, status, attempts, claim_count, next_attempt_at, created_at)
            VALUES ({Guid.NewGuid()}, {EmailOutboxEntry.EditLinkTemplate}, {s.ProfileId}, {"edit-link:" + s.ProfileId}, {OutboxStatus.Pending.ToString()}, 0, 0, {now}, {now})"));
        Assert.Equal(HttpStatusCode.OK, (await RemoveAsync(admin, s.ProfileId)).StatusCode);

        await using (var scope = f.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<EmailOutboxProcessor>().ProcessBatchAsync(CancellationToken.None);

        var row = Assert.Single(await Db(f, d => d.EmailOutbox.AsNoTracking().ToListAsync()));
        Assert.Equal(OutboxStatus.Dead, row.Status);
        Assert.Equal(EmailOutboxProcessor.CategoryNoRecipient, row.LastErrorCategory);
    }

    [Fact]
    public async Task Removal_requires_admin_and_request_protection()
    {
        var (f, admin, cohort) = await SetupAsync();
        var s = await SeedAsync(f, cohort, RevisionStatus.Approved, "p@example.com", DateTime.UtcNow);
        var path = $"/admin/profiles/{s.ProfileId}/remove";

        var anon = f.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsync(path, null, await anon.GetCsrfAsync())).StatusCode);

        await f.AddUserAsync("viewer2", "Quiet-Horse-Pass-77!Qx", admin: false);
        var plain = f.NewClient();
        Assert.Equal(HttpStatusCode.OK, (await plain.LoginAsync("viewer2", "Quiet-Horse-Pass-77!Qx")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.PostWithCsrfAsync(path, null)).StatusCode);

        Assert.False((await admin.PostAsync(path, null, csrf: null)).IsSuccessStatusCode);
        Assert.False((await admin.PostAsync(path, null, await admin.GetCsrfAsync(), origin: "https://evil.example")).IsSuccessStatusCode);
        Assert.Equal(ProfileVisibility.Active, (await StateAsync(f, s.ProfileId)).Visibility);
        Assert.Empty(await Db(f, d => d.AuditLog.AsNoTracking().ToListAsync()));

        // The list is admin-only too.
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.Http.GetAsync("/admin/profiles")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.Http.GetAsync("/admin/profiles")).StatusCode);
    }

    // ---- list -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task List_defaults_to_active_newest_first_and_projects_no_personal_data()
    {
        var (f, admin, cohort) = await SetupAsync();
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var old = await SeedAsync(f, cohort, RevisionStatus.Approved, "old@example.com", t, name: "ישנה", productName: "מוצר ישן");
        var neu = await SeedAsync(f, cohort, RevisionStatus.Pending, "new@example.com", t.AddDays(2), name: "חדשה", productName: "מוצר חדש");
        var removed = await SeedAsync(f, cohort, RevisionStatus.Approved, "rem@example.com", t.AddDays(1), name: "הוסרה");
        Assert.Equal(HttpStatusCode.OK, (await RemoveAsync(admin, removed.ProfileId)).StatusCode);

        var response = await admin.Http.GetAsync("/admin/profiles");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("@example.com", text);
        Assert.DoesNotContain("0501234567", text);
        Assert.DoesNotContain("קורות חיים", text);
        var json = JsonSerializer.Deserialize<JsonElement>(text);
        Assert.Equal(2, json.GetProperty("total").GetInt32());
        var items = json.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal([neu.ProfileId, old.ProfileId], items.Select(i => i.GetProperty("profileId").GetGuid()).ToList());
        Assert.Equal("חדשה", items[0].GetProperty("name").GetString());
        Assert.Equal("מוצר חדש", items[0].GetProperty("productName").GetString());
        Assert.Equal("מחזור ב", items[0].GetProperty("cohortName").GetString());
        Assert.Equal("active", items[0].GetProperty("visibility").GetString());
        Assert.False(items[0].GetProperty("hasApproved").GetBoolean());
        Assert.True(items[1].GetProperty("hasApproved").GetBoolean());
        Assert.True(items[1].TryGetProperty("createdAt", out _));
        Assert.Equal(7, items[0].EnumerateObject().Count());
    }

    [Fact]
    public async Task List_removed_all_and_the_product_name_comes_from_the_approved_revision()
    {
        var (f, admin, cohort) = await SetupAsync();
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var a = await SeedAsync(f, cohort, RevisionStatus.Approved, "a@example.com", t, productName: "מאושר");
        await SeedAsync(f, cohort, RevisionStatus.Pending, "", t.AddDays(1), productName: "ממתין", profileId: a.ProfileId);
        var b = await SeedAsync(f, cohort, RevisionStatus.Approved, "b@example.com", t.AddDays(1), productName: "שנייה");
        Assert.Equal(HttpStatusCode.OK, (await RemoveAsync(admin, b.ProfileId)).StatusCode);

        var removed = await Json(await admin.Http.GetAsync("/admin/profiles?visibility=removed"));
        Assert.Equal(1, removed.GetProperty("total").GetInt32());
        var only = removed.GetProperty("items")[0];
        Assert.Equal(b.ProfileId, only.GetProperty("profileId").GetGuid());
        Assert.Equal("removed", only.GetProperty("visibility").GetString());

        var all = await Json(await admin.Http.GetAsync("/admin/profiles?visibility=all"));
        Assert.Equal(2, all.GetProperty("total").GetInt32());
        var forA = all.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("profileId").GetGuid() == a.ProfileId);
        Assert.Equal("מאושר", forA.GetProperty("productName").GetString());
    }

    [Fact]
    public async Task List_filters_by_cohort_and_pages_with_a_correct_total()
    {
        var (f, admin, cohortA) = await SetupAsync();
        var cohortB = await NewCohortAsync(admin, "מחזור ג");
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++) ids.Add((await SeedAsync(f, cohortA, RevisionStatus.Approved, $"a{i}@example.com", t.AddHours(i))).ProfileId);
        var other = await SeedAsync(f, cohortB, RevisionStatus.Approved, "b@example.com", t.AddHours(10));

        var onlyB = await Json(await admin.Http.GetAsync($"/admin/profiles?cohort={cohortB:D}"));
        Assert.Equal(1, onlyB.GetProperty("total").GetInt32());
        Assert.Equal(other.ProfileId, onlyB.GetProperty("items")[0].GetProperty("profileId").GetGuid());
        Assert.Equal(0, (await Json(await admin.Http.GetAsync($"/admin/profiles?cohort={Guid.NewGuid():D}"))).GetProperty("total").GetInt32());

        var page = await Json(await admin.Http.GetAsync($"/admin/profiles?cohort={cohortA:D}&limit=2&offset=1"));
        Assert.Equal(3, page.GetProperty("total").GetInt32());
        Assert.Equal([ids[1], ids[0]], page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("profileId").GetGuid()).ToList());

        var past = await Json(await admin.Http.GetAsync("/admin/profiles?offset=10"));
        Assert.Equal(0, past.GetProperty("items").GetArrayLength());
        Assert.Equal(4, past.GetProperty("total").GetInt32());
        Assert.Equal(4, (await Json(await admin.Http.GetAsync("/admin/profiles?limit=100000"))).GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task List_rejects_bad_parameters_with_400()
    {
        var (_, admin, _) = await SetupAsync();
        foreach (var query in new[] { "visibility=gone", "visibility=", "offset=-1", "limit=0", "limit=-3", "cohort=not-a-guid", "cohort=" })
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.Http.GetAsync("/admin/profiles?" + query)).StatusCode);
    }

    [Fact]
    public async Task Data_of_a_removed_profile_stays_available_to_the_admin_review_endpoints()
    {
        var (f, admin, cohort) = await SetupAsync();
        var s = await SeedAsync(f, cohort, RevisionStatus.Approved, "keep@example.com", DateTime.UtcNow);
        var pending = await SeedAsync(f, cohort, RevisionStatus.Pending, "", DateTime.UtcNow, profileId: s.ProfileId);
        Assert.Equal(HttpStatusCode.OK, (await RemoveAsync(admin, s.ProfileId)).StatusCode);

        var detail = await admin.Http.GetAsync($"/admin/submissions/{pending.RevisionId}");

        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal("keep@example.com", (await Json(detail)).GetProperty("email").GetString());
    }

    // ---- review follow-ups: reactivation signal, contact changes, tokens, cap, ordering -------------------------------

    private static async Task SetPendingEmailAsync(AuthApiFactory f, Guid revisionId, string email)
        => await Db(f, d => d.Database.ExecuteSqlInterpolatedAsync($"UPDATE profile_revisions SET pending_email = {email} WHERE id = {revisionId}"));

    [Fact]
    public async Task Queue_and_detail_flag_a_removed_profile_and_approval_reports_reactivation()
    {
        var (f, admin, cohort) = await SetupAsync();
        var t = DateTime.UtcNow;
        var live = await SeedAsync(f, cohort, RevisionStatus.Pending, "live@example.com", t.AddMinutes(-5));
        var gone = await SeedAsync(f, cohort, RevisionStatus.Approved, "gone@example.com", t.AddMinutes(-10));
        var goneUpdate = await SeedAsync(f, cohort, RevisionStatus.Pending, "", t.AddMinutes(-1), profileId: gone.ProfileId);
        Assert.Equal(HttpStatusCode.OK, (await RemoveAsync(admin, gone.ProfileId)).StatusCode);

        var queue = await Json(await admin.Http.GetAsync("/admin/submissions"));
        var items = queue.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.False(items.Single(i => i.GetProperty("revisionId").GetGuid() == live.RevisionId).GetProperty("profileRemoved").GetBoolean());
        Assert.True(items.Single(i => i.GetProperty("revisionId").GetGuid() == goneUpdate.RevisionId).GetProperty("profileRemoved").GetBoolean());

        Assert.True((await Json(await admin.Http.GetAsync($"/admin/submissions/{goneUpdate.RevisionId}"))).GetProperty("profileRemoved").GetBoolean());
        Assert.False((await Json(await admin.Http.GetAsync($"/admin/submissions/{live.RevisionId}"))).GetProperty("profileRemoved").GetBoolean());

        var plain = await Json(await admin.PostWithCsrfAsync($"/admin/submissions/{live.RevisionId}/approve", null));
        Assert.False(plain.GetProperty("reactivated").GetBoolean());
        var reactivated = await Json(await admin.PostWithCsrfAsync($"/admin/submissions/{goneUpdate.RevisionId}/approve", null));
        Assert.True(reactivated.GetProperty("reactivated").GetBoolean());
        Assert.Equal(ProfileVisibility.Active, (await StateAsync(f, gone.ProfileId)).Visibility);
    }

    [Fact]
    public async Task A_removed_profiles_contact_change_is_not_listed_and_cannot_be_resolved()
    {
        var (f, admin, cohort) = await SetupAsync();
        var kept = await SeedAsync(f, cohort, RevisionStatus.Pending, "k@example.com", DateTime.UtcNow);
        var gone = await SeedAsync(f, cohort, RevisionStatus.Pending, "g@example.com", DateTime.UtcNow);
        await SetPendingEmailAsync(f, kept.RevisionId, "k2@example.com");
        await SetPendingEmailAsync(f, gone.RevisionId, "g2@example.com");
        var before = await Json(await admin.Http.GetAsync("/admin/contact-changes"));
        Assert.Equal(2, before.GetProperty("total").GetInt32());

        Assert.Equal(HttpStatusCode.OK, (await RemoveAsync(admin, gone.ProfileId)).StatusCode);

        var after = await Json(await admin.Http.GetAsync("/admin/contact-changes"));
        Assert.Equal(1, after.GetProperty("total").GetInt32());
        Assert.Equal(kept.RevisionId, after.GetProperty("items")[0].GetProperty("revisionId").GetGuid());
        foreach (var action in new[] { "approve-contact-change", "reject-contact-change" })
        {
            var r = await admin.PostWithCsrfAsync($"/admin/submissions/{gone.RevisionId}/{action}", new { pendingEmail = "g2@example.com" });
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("revision-not-eligible", (await Json(r)).GetProperty("code").GetString());
        }
        var priv = await Db(f, d => d.ParticipantPrivates.AsNoTracking().SingleAsync(p => p.ProfileId == gone.ProfileId));
        Assert.Equal("g@example.com", priv.Email);
        Assert.Equal("g2@example.com", (await Db(f, d => d.ProfileRevisions.AsNoTracking().SingleAsync(r => r.Id == gone.RevisionId))).PendingEmail);
    }

    private sealed class StubProfileAdminRepository : IProfileAdminRepository
    {
        public int? Limit { get; private set; }
        public Task<AdminProfilePage> ListAsync(VisibilityFilter visibility, Guid? cohortId, int limit, int offset, CancellationToken ct)
        {
            Limit = limit;
            return Task.FromResult(new AdminProfilePage([], 0));
        }
        public Task<RemoveOutcome> RemoveAsync(Guid profileId, Guid actorId, DateTime nowUtc, CancellationToken ct) => Task.FromResult(new RemoveOutcome(RemoveStatus.NotFound));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public async Task The_service_caps_the_page_size_at_100()
    {
        var stub = new StubProfileAdminRepository();
        var service = new ProfileAdminService(stub, TimeProvider.System);

        await service.ListAsync(VisibilityFilter.Active, null, 100000, 0, CancellationToken.None);
        Assert.Equal(100, stub.Limit);
        await service.ListAsync(VisibilityFilter.Active, null, 7, 0, CancellationToken.None);
        Assert.Equal(7, stub.Limit);
        Assert.Equal(50, ProfileAdminService.DefaultLimit);
    }

    [Fact]
    public async Task The_endpoint_default_page_size_is_50_and_a_huge_limit_is_capped_at_100()
    {
        var (f, admin, cohort) = await SetupAsync();
        for (var i = 0; i < 101; i++) await SeedAsync(f, cohort, RevisionStatus.Pending, $"p{i}@example.com", DateTime.UtcNow.AddSeconds(i), photo: false);

        var def = await Json(await admin.Http.GetAsync("/admin/profiles"));
        Assert.Equal(50, def.GetProperty("items").GetArrayLength());
        Assert.Equal(101, def.GetProperty("total").GetInt32());
        Assert.Equal(100, (await Json(await admin.Http.GetAsync("/admin/profiles?limit=100000"))).GetProperty("items").GetArrayLength());
    }

    private static async Task AddTokenAsync(AuthApiFactory f, Guid profileId, string purpose, string hash, DateTime? usedAt = null, DateTime? revokedAt = null)
    {
        await Db(f, async d =>
        {
            d.PersonalTokens.Add(PersonalToken.Create(hash, purpose, profileId, DateTime.UtcNow, TimeSpan.FromHours(1)));
            await d.SaveChangesAsync();
            await d.Database.ExecuteSqlInterpolatedAsync($"UPDATE personal_tokens SET used_at = {usedAt}, revoked_at = {revokedAt} WHERE token_hash = {hash}");
            return 0;
        });
    }

    private static Task<PersonalToken> TokenAsync(AuthApiFactory f, string hash) => Db(f, d => d.PersonalTokens.AsNoTracking().SingleAsync(t => t.TokenHash == hash));

    [Fact]
    public async Task Revocation_only_touches_unredeemed_edit_tokens_and_uses_the_service_clock()
    {
        var (f, _, cohort) = await SetupAsync();
        var s = await SeedAsync(f, cohort, RevisionStatus.Approved, "t@example.com", DateTime.UtcNow);
        var used = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var revoked = new DateTime(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc);
        await AddTokenAsync(f, s.ProfileId, PersonalToken.EditProfilePurpose, "h-live");
        await AddTokenAsync(f, s.ProfileId, PersonalToken.EditProfilePurpose, "h-used", usedAt: used);
        await AddTokenAsync(f, s.ProfileId, PersonalToken.EditProfilePurpose, "h-revoked", revokedAt: revoked);
        await AddTokenAsync(f, s.ProfileId, "contact-decision", "h-other");
        var actor = Guid.NewGuid();
        var clockTime = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);

        await using (var scope = f.Services.CreateAsyncScope())
        {
            var service = new ProfileAdminService(scope.ServiceProvider.GetRequiredService<IProfileAdminRepository>(), new FixedClock(clockTime));
            Assert.Equal(RemoveStatus.Removed, (await service.RemoveAsync(s.ProfileId, actor, CancellationToken.None)).Status);
        }

        Assert.Equal(clockTime, DateTime.SpecifyKind((await TokenAsync(f, "h-live")).RevokedAt!.Value, DateTimeKind.Utc));
        var usedRow = await TokenAsync(f, "h-used");
        Assert.Equal(used, DateTime.SpecifyKind(usedRow.UsedAt!.Value, DateTimeKind.Utc));
        Assert.Null(usedRow.RevokedAt);
        Assert.Equal(revoked, DateTime.SpecifyKind((await TokenAsync(f, "h-revoked")).RevokedAt!.Value, DateTimeKind.Utc));
        Assert.Null((await TokenAsync(f, "h-other")).RevokedAt);
        var audit = Assert.Single(await Db(f, d => d.AuditLog.AsNoTracking().ToListAsync()));
        Assert.Equal(clockTime, DateTime.SpecifyKind(audit.At, DateTimeKind.Utc));
        Assert.Equal(actor, audit.ActorId);
    }

    [Fact]
    public async Task The_409_path_leaves_token_state_untouched()
    {
        var (f, admin, cohort) = await SetupAsync();
        var s = await SeedAsync(f, cohort, RevisionStatus.Approved, "t2@example.com", DateTime.UtcNow);
        Assert.Equal(HttpStatusCode.OK, (await RemoveAsync(admin, s.ProfileId)).StatusCode);
        await AddTokenAsync(f, s.ProfileId, PersonalToken.EditProfilePurpose, "h-late");

        Assert.Equal(HttpStatusCode.Conflict, (await RemoveAsync(admin, s.ProfileId)).StatusCode);

        var row = await TokenAsync(f, "h-late");
        Assert.Null(row.RevokedAt);
        Assert.Null(row.UsedAt);
    }

    [Fact]
    public async Task A_token_issued_after_removal_cannot_start_a_session_and_stays_unconsumed()
    {
        var (f, admin, cohort) = await SetupAsync();
        var s = await SeedAsync(f, cohort, RevisionStatus.Approved, "late@example.com", DateTime.UtcNow);
        Assert.Equal(HttpStatusCode.OK, (await RemoveAsync(admin, s.ProfileId)).StatusCode);
        var token = await IssueAsync(f, s.ProfileId);

        var client = f.NewClient();
        var exchange = await client.PostAsync("/profiles/session", new { token }, await Csrf(client));

        Assert.Equal(HttpStatusCode.Unauthorized, exchange.StatusCode);
        Assert.Equal("link-invalid", (await Json(exchange)).GetProperty("code").GetString());
        Assert.False(exchange.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(c => c.StartsWith(CookieName)));
        var row = Assert.Single(await Db(f, d => d.PersonalTokens.AsNoTracking().Where(t => t.ProfileId == s.ProfileId && t.RevokedAt == null).ToListAsync()));
        Assert.Null(row.UsedAt);
    }

    [Fact]
    public async Task The_404_for_a_missing_profile_is_identical_in_body_and_headers_for_every_bad_id()
    {
        var (f, admin, cohort) = await SetupAsync();
        var s = await SeedAsync(f, cohort, RevisionStatus.Approved, "n@example.com", DateTime.UtcNow);

        static string Normalise(string body)
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(body)!.AsObject();
            node.Remove("traceId");
            return node.ToJsonString();
        }
        async Task<(string Body, string? Cache, string? Type)> Probe(string id)
        {
            var r = await admin.PostWithCsrfAsync($"/admin/profiles/{Uri.EscapeDataString(id)}/remove", null);
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
            return (Normalise(await r.Content.ReadAsStringAsync()), r.Headers.CacheControl?.ToString(), r.Content.Headers.ContentType?.ToString());
        }

        var baseline = await Probe(Guid.NewGuid().ToString());
        Assert.Equal("no-store", baseline.Cache);
        Assert.StartsWith("application/problem+json", baseline.Type);
        foreach (var id in new[] { s.ProfileId.ToString("N"), s.ProfileId.ToString("B"), "not-a-guid" })
            Assert.Equal(baseline, await Probe(id));
    }

    [Fact]
    public async Task Equal_creation_times_are_ordered_by_id_descending()
    {
        var (f, admin, cohort) = await SetupAsync();
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 4; i++) await SeedAsync(f, cohort, RevisionStatus.Approved, $"e{i}@example.com", t);
        var expected = await Db(f, d => d.Profiles.AsNoTracking().OrderByDescending(p => p.Id).Select(p => p.Id).ToListAsync());

        var json = await Json(await admin.Http.GetAsync("/admin/profiles"));

        Assert.Equal(expected, json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("profileId").GetGuid()).ToList());
    }
}
