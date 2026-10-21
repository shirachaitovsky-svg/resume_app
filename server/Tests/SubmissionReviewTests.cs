using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResumeApp.Domain.Audit;
using ResumeApp.Domain.Profiles;
using ResumeApp.Infrastructure;

namespace ResumeApp.Tests;

[Collection("env")]
public class SubmissionReviewTests(PostgresFixture pg) : IDisposable
{
    private const string NewPassword = "Brand-New-Pass-42#Lk";
    private readonly List<AuthApiFactory> _factories = [];
    private static readonly byte[] PhotoBytes = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
    }

    private async Task<(AuthApiFactory Factory, ApiClient Admin, Guid CohortId)> SetupAsync()
    {
        var factory = await AuthApiFactory.CreateAsync(pg);
        _factories.Add(factory);
        var admin = factory.NewClient();
        await admin.EnrollAsync(AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword, NewPassword);
        var response = await admin.PostWithCsrfAsync("/admin/cohorts", new { name = "מחזור ב" });
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (factory, admin, Guid.Parse(json.GetProperty("id").GetString()!));
    }

    /// <summary>Inserts a revision in the given state directly in the database (new profile unless profileId is given).</summary>
    private static async Task<(Guid ProfileId, Guid RevisionId)> SeedAsync(AuthApiFactory f, Guid cohortId, RevisionStatus status, string email,
        DateTime submittedAt, string? cv = null, bool photo = false, string name = "דנה", Guid? profileId = null, bool approvedPointer = false, bool withPrivate = true)
    {
        await using var scope = f.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Profile profile;
        if (profileId is null)
        {
            profile = Profile.Create(cohortId, submittedAt);
            db.Profiles.Add(profile);
            await db.SaveChangesAsync();
            if (withPrivate) db.ParticipantPrivates.Add(ParticipantPrivate.Create(profile.Id, cohortId, email, "0501234567"));
        }
        else
        {
            profile = await db.Profiles.SingleAsync(p => p.Id == profileId);
        }

        var extras = new ProfileExtras("תיאור", "תפקיד", "מקום", "היכרות", "א1", "א2", "א3", "girl-1",
            photo ? PhotoBytes : null, photo ? "image/jpeg" : null, cv);
        var rev = ProfileRevision.CreatePending(profile.Id, name, "מוצר", "https://example.com/p", submittedAt, extras);
        db.ProfileRevisions.Add(rev);
        db.Entry(rev).Property(r => r.Status).CurrentValue = status;
        await db.SaveChangesAsync();
        if (approvedPointer)
        {
            db.Entry(profile).Property(p => p.ApprovedRevisionId).CurrentValue = rev.Id;
            await db.SaveChangesAsync();
        }
        return (profile.Id, rev.Id);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task Queue_is_empty_when_nothing_is_pending()
    {
        var (_, admin, _) = await SetupAsync();
        var response = await admin.Http.GetAsync("/admin/submissions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await Json(response);
        Assert.Equal(0, json.GetProperty("items").GetArrayLength());
        Assert.Equal(0, json.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Queue_lists_only_pending_oldest_first_with_kind_and_paging()
    {
        var (f, admin, cohort) = await SetupAsync();
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var third = await SeedAsync(f, cohort, RevisionStatus.Pending, "c@example.com", t.AddHours(3), name: "שלישית");
        var first = await SeedAsync(f, cohort, RevisionStatus.Pending, "a@example.com", t.AddHours(1), name: "ראשונה");
        var second = await SeedAsync(f, cohort, RevisionStatus.Pending, "b@example.com", t.AddHours(2), name: "שנייה");
        await SeedAsync(f, cohort, RevisionStatus.Approved, "d@example.com", t, name: "מאושרת");
        await SeedAsync(f, cohort, RevisionStatus.Returned, "e@example.com", t, name: "הוחזרה");
        var approved = await SeedAsync(f, cohort, RevisionStatus.Approved, "u@example.com", t, name: "עדכון", approvedPointer: true);
        var update = await SeedAsync(f, cohort, RevisionStatus.Pending, "", t.AddHours(4), name: "עדכון חדש", profileId: approved.ProfileId);

        var json = await Json(await admin.Http.GetAsync("/admin/submissions"));
        Assert.Equal(4, json.GetProperty("total").GetInt32());
        var items = json.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal([first.RevisionId, second.RevisionId, third.RevisionId, update.RevisionId], items.Select(i => i.GetProperty("revisionId").GetGuid()));
        Assert.Equal("new", items[0].GetProperty("kind").GetString());
        Assert.Equal("update", items[3].GetProperty("kind").GetString());
        Assert.Equal("ראשונה", items[0].GetProperty("name").GetString());
        Assert.Equal("מוצר", items[0].GetProperty("productName").GetString());
        Assert.Equal("מחזור ב", items[0].GetProperty("cohortName").GetString());
        Assert.True(items[0].TryGetProperty("submittedAt", out _));
        Assert.False(items[0].TryGetProperty("email", out _));

        var page = await Json(await admin.Http.GetAsync("/admin/submissions?limit=2&offset=1"));
        Assert.Equal(4, page.GetProperty("total").GetInt32());
        var updateDetail = await Json(await admin.Http.GetAsync($"/admin/submissions/{update.RevisionId}"));
        Assert.Equal("update", updateDetail.GetProperty("kind").GetString());
        Assert.Equal([second.RevisionId, third.RevisionId], page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("revisionId").GetGuid()));

        var big = await admin.Http.GetAsync("/admin/submissions?limit=100000");
        Assert.Equal(HttpStatusCode.OK, big.StatusCode);
        Assert.Equal("no-store", big.Headers.CacheControl?.ToString());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Http.GetAsync("/admin/submissions?limit=-1")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Http.GetAsync("/admin/submissions?offset=-1")).StatusCode);
    }

    [Fact]
    public async Task Detail_returns_private_contact_fields_and_cleaned_cv()
    {
        var (f, admin, cohort) = await SetupAsync();
        var submitted = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        var s = await SeedAsync(f, cohort, RevisionStatus.Pending, "dana@example.com", submitted, cv: "# כותרת\n\n[קישור](https://a.co)", photo: true);

        var response = await admin.Http.GetAsync($"/admin/submissions/{s.RevisionId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var d = await Json(response);
        Assert.Equal(s.RevisionId, d.GetProperty("revisionId").GetGuid());
        Assert.Equal("דנה", d.GetProperty("name").GetString());
        Assert.Equal("מוצר", d.GetProperty("productName").GetString());
        Assert.Equal("מחזור ב", d.GetProperty("cohortName").GetString());
        Assert.Equal(submitted, d.GetProperty("submittedAt").GetDateTime().ToUniversalTime());
        Assert.Equal("תפקיד", d.GetProperty("role").GetString());
        Assert.Equal("מקום", d.GetProperty("workplace").GetString());
        Assert.Equal("היכרות", d.GetProperty("introText").GetString());
        Assert.Equal("א1", d.GetProperty("introAnswer1").GetString());
        Assert.Equal("א2", d.GetProperty("introAnswer2").GetString());
        Assert.Equal("dana@example.com", d.GetProperty("email").GetString());
        Assert.Equal("0501234567", d.GetProperty("phone").GetString());
        Assert.Equal("https://example.com/p", d.GetProperty("productUrl").GetString());
        Assert.Equal("girl-1", d.GetProperty("avatarId").GetString());
        Assert.True(d.GetProperty("hasPhoto").GetBoolean());
        Assert.False(d.TryGetProperty("photo", out _));
        Assert.False(d.GetProperty("cvInvalid").GetBoolean());
        Assert.Contains("[קישור](https://a.co)", d.GetProperty("cv").GetString());
        Assert.Equal("new", d.GetProperty("kind").GetString());
        Assert.Equal("תיאור", d.GetProperty("description").GetString());
        Assert.Equal("א3", d.GetProperty("introAnswer3").GetString());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Script_tag_cv_in_the_database_is_returned_stripped()
    {
        var (f, admin, cohort) = await SetupAsync();
        var s = await SeedAsync(f, cohort, RevisionStatus.Pending, "x@example.com", DateTime.UtcNow, cv: "hello <script>alert(1)</script> world");
        var d = await Json(await admin.Http.GetAsync($"/admin/submissions/{s.RevisionId}"));
        var text = d.GetProperty("cv").GetString();
        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.DoesNotContain("<script", text, StringComparison.OrdinalIgnoreCase);
        Assert.False(d.GetProperty("cvInvalid").GetBoolean());
    }

    [Fact]
    public async Task Javascript_link_cv_in_the_database_is_returned_null_and_flagged()
    {
        var (f, admin, cohort) = await SetupAsync();
        var s = await SeedAsync(f, cohort, RevisionStatus.Pending, "x@example.com", DateTime.UtcNow, cv: "[x](javascript:alert(1))");
        var d = await Json(await admin.Http.GetAsync($"/admin/submissions/{s.RevisionId}"));
        Assert.Equal(JsonValueKind.Null, d.GetProperty("cv").ValueKind);
        Assert.True(d.GetProperty("cvInvalid").GetBoolean());
    }

    [Fact]
    public async Task Limit_zero_is_rejected_and_non_canonical_guids_are_404()
    {
        var (f, admin, cohort) = await SetupAsync();
        var s = await SeedAsync(f, cohort, RevisionStatus.Pending, "g@example.com", DateTime.UtcNow, photo: true);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Http.GetAsync("/admin/submissions?limit=0")).StatusCode);
        foreach (var spelling in new[] { s.RevisionId.ToString("N"), s.RevisionId.ToString("B"), s.RevisionId.ToString("P") })
        {
            var enc = Uri.EscapeDataString(spelling);
            Assert.Equal(HttpStatusCode.NotFound, (await admin.Http.GetAsync($"/admin/submissions/{enc}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await admin.Http.GetAsync($"/admin/submissions/{enc}/photo")).StatusCode);
        }
        Assert.Equal(HttpStatusCode.OK, (await admin.Http.GetAsync($"/admin/submissions/{s.RevisionId}")).StatusCode);
    }

    [Fact]
    public async Task Pending_revision_without_a_private_row_is_neither_listed_nor_openable()
    {
        var (f, admin, cohort) = await SetupAsync();
        var orphan = await SeedAsync(f, cohort, RevisionStatus.Pending, "o@example.com", DateTime.UtcNow, withPrivate: false);
        var ok = await SeedAsync(f, cohort, RevisionStatus.Pending, "k@example.com", DateTime.UtcNow);
        var json = await Json(await admin.Http.GetAsync("/admin/submissions"));
        Assert.Equal(1, json.GetProperty("total").GetInt32());
        Assert.Equal([ok.RevisionId], json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("revisionId").GetGuid()));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Http.GetAsync($"/admin/submissions/{orphan.RevisionId}")).StatusCode);
    }


    [Fact]
    public async Task Detail_and_photo_return_404_for_unknown_non_pending_and_malformed_ids()
    {
        var (f, admin, cohort) = await SetupAsync();
        var approved = await SeedAsync(f, cohort, RevisionStatus.Approved, "a@example.com", DateTime.UtcNow, photo: true);
        var returned = await SeedAsync(f, cohort, RevisionStatus.Returned, "r@example.com", DateTime.UtcNow, photo: true);
        foreach (var id in new[] { Guid.NewGuid().ToString(), approved.RevisionId.ToString(), returned.RevisionId.ToString(), "not-a-guid" })
        {
            foreach (var path in new[] { $"/admin/submissions/{id}", $"/admin/submissions/{id}/photo" })
            {
                var r = await admin.Http.GetAsync(path);
                Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
                Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
            }
        }
    }

    [Fact]
    public async Task Photo_is_served_to_admin_with_safe_headers_and_404_when_absent()
    {
        var (f, admin, cohort) = await SetupAsync();
        var withPhoto = await SeedAsync(f, cohort, RevisionStatus.Pending, "p@example.com", DateTime.UtcNow, photo: true);
        var without = await SeedAsync(f, cohort, RevisionStatus.Pending, "n@example.com", DateTime.UtcNow);

        var r = await admin.Http.GetAsync($"/admin/submissions/{withPhoto.RevisionId}/photo");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("image/jpeg", r.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-store", r.Headers.CacheControl?.ToString());
        Assert.Equal(PhotoBytes, await r.Content.ReadAsByteArrayAsync());

        Assert.Equal(HttpStatusCode.NotFound, (await admin.Http.GetAsync($"/admin/submissions/{without.RevisionId}/photo")).StatusCode);
        // The public endpoint still does not serve a pending photo.
        Assert.Equal(HttpStatusCode.NotFound, (await f.NewClient().Http.GetAsync($"/profiles/{withPhoto.ProfileId}/photo")).StatusCode);
        var detail = await Json(await admin.Http.GetAsync($"/admin/submissions/{without.RevisionId}"));
        Assert.False(detail.GetProperty("hasPhoto").GetBoolean());
    }

    [Fact]
    public async Task All_endpoints_reject_anonymous_with_401_and_non_admin_with_403()
    {
        var (f, _, cohort) = await SetupAsync();
        var s = await SeedAsync(f, cohort, RevisionStatus.Pending, "z@example.com", DateTime.UtcNow, photo: true);
        string[] paths = ["/admin/submissions", $"/admin/submissions/{s.RevisionId}", $"/admin/submissions/{s.RevisionId}/photo"];

        var anon = f.NewClient();
        foreach (var p in paths) Assert.Equal(HttpStatusCode.Unauthorized, (await anon.Http.GetAsync(p)).StatusCode);

        await f.AddUserAsync("viewer1", "Quiet-Horse-Pass-77!Qx", admin: false);
        var plain = f.NewClient();
        Assert.Equal(HttpStatusCode.OK, (await plain.LoginAsync("viewer1", "Quiet-Horse-Pass-77!Qx")).StatusCode);
        foreach (var p in paths) Assert.Equal(HttpStatusCode.Forbidden, (await plain.Http.GetAsync(p)).StatusCode);
    }

    [Fact]
    public async Task Public_endpoints_never_expose_email_or_phone()
    {
        var (f, _, cohort) = await SetupAsync();
        var s = await SeedAsync(f, cohort, RevisionStatus.Pending, "secret-mail@example.com", DateTime.UtcNow, photo: true);
        var anon = f.NewClient();
        foreach (var p in new[] { $"/profiles/{s.ProfileId}/photo", $"/profiles/{s.ProfileId}", "/invites/xyz" })
        {
            var body = await (await anon.Http.GetAsync(p)).Content.ReadAsStringAsync();
            Assert.DoesNotContain("secret-mail@example.com", body);
            Assert.DoesNotContain("0501234567", body);
        }
    }

    private static async Task<(RevisionStatus Status, Guid? Pointer, ProfileVisibility Visibility)> StateAsync(AuthApiFactory f, Guid profileId, Guid revisionId)
    {
        await using var scope = f.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var status = await db.ProfileRevisions.AsNoTracking().Where(r => r.Id == revisionId).Select(r => r.Status).SingleAsync();
        var p = await db.Profiles.AsNoTracking().SingleAsync(x => x.Id == profileId);
        return (status, p.ApprovedRevisionId, p.Visibility);
    }

    private static async Task<List<AuditLogEntry>> AuditAsync(AuthApiFactory f)
    {
        await using var scope = f.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLog.AsNoTracking().ToListAsync();
    }

    [Fact]
    public async Task Approve_publishes_new_revision_and_writes_audit_row_without_personal_data()
    {
        var (f, admin, cohort) = await SetupAsync();
        var s = await SeedAsync(f, cohort, RevisionStatus.Pending, "priv@example.com", DateTime.UtcNow);
        var response = await admin.PostWithCsrfAsync($"/admin/submissions/{s.RevisionId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("priv@example.com", body);
        Assert.DoesNotContain("0501234567", body);
        var json = JsonSerializer.Deserialize<JsonElement>(body);
        Assert.Equal(s.RevisionId, json.GetProperty("revisionId").GetGuid());
        Assert.Equal(s.ProfileId, json.GetProperty("profileId").GetGuid());
        Assert.Equal("approved", json.GetProperty("status").GetString());

        Assert.Equal((RevisionStatus.Approved, (Guid?)s.RevisionId, ProfileVisibility.Active), await StateAsync(f, s.ProfileId, s.RevisionId));
        var audit = Assert.Single(await AuditAsync(f));
        Assert.Equal("approve-revision", audit.Action);
        Assert.Equal("profile-revision", audit.EntityType);
        Assert.Equal(s.RevisionId, audit.EntityId);
        await using (var userScope = f.Services.CreateAsyncScope())
        {
            var users = userScope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ResumeApp.Infrastructure.Identity.AppUser>>();
            var adminUser = await users.FindByNameAsync(AuthApiFactory.AdminUser);
            Assert.Equal(adminUser!.Id, audit.ActorId);
        }
        Assert.InRange((DateTime.UtcNow - audit.At.ToUniversalTime()).TotalSeconds, -5, 30);

        await using var scope = f.Services.CreateAsyncScope();
        var pp = await scope.ServiceProvider.GetRequiredService<AppDbContext>().ParticipantPrivates.AsNoTracking().SingleAsync(x => x.ProfileId == s.ProfileId);
        Assert.Equal("priv@example.com", pp.Email);
        Assert.Equal("0501234567", pp.Phone);
    }

    [Fact]
    public async Task Approve_update_supersedes_previous_and_moves_pointer()
    {
        var (f, admin, cohort) = await SetupAsync();
        var old = await SeedAsync(f, cohort, RevisionStatus.Approved, "u@example.com", DateTime.UtcNow.AddDays(-1), approvedPointer: true);
        var upd = await SeedAsync(f, cohort, RevisionStatus.Pending, "", DateTime.UtcNow, profileId: old.ProfileId);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostWithCsrfAsync($"/admin/submissions/{upd.RevisionId}/approve", null)).StatusCode);
        Assert.Equal(RevisionStatus.Superseded, (await StateAsync(f, old.ProfileId, old.RevisionId)).Status);
        Assert.Equal((RevisionStatus.Approved, (Guid?)upd.RevisionId, ProfileVisibility.Active), await StateAsync(f, old.ProfileId, upd.RevisionId));
    }

    [Fact]
    public async Task Approve_reactivates_a_removed_profile()
    {
        var (f, admin, cohort) = await SetupAsync();
        var s = await SeedAsync(f, cohort, RevisionStatus.Pending, "r@example.com", DateTime.UtcNow);
        await using (var scope = f.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Profiles.Where(p => p.Id == s.ProfileId).ExecuteUpdateAsync(u => u.SetProperty(p => p.Visibility, ProfileVisibility.Removed));
        }
        Assert.Equal(HttpStatusCode.OK, (await admin.PostWithCsrfAsync($"/admin/submissions/{s.RevisionId}/approve", null)).StatusCode);
        Assert.Equal(ProfileVisibility.Active, (await StateAsync(f, s.ProfileId, s.RevisionId)).Visibility);
    }

    [Fact]
    public async Task Approve_twice_and_non_pending_return_409_without_extra_audit_rows()
    {
        var (f, admin, cohort) = await SetupAsync();
        var s = await SeedAsync(f, cohort, RevisionStatus.Pending, "a@example.com", DateTime.UtcNow);
        var returned = await SeedAsync(f, cohort, RevisionStatus.Returned, "b@example.com", DateTime.UtcNow);
        var superseded = await SeedAsync(f, cohort, RevisionStatus.Superseded, "c@example.com", DateTime.UtcNow);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostWithCsrfAsync($"/admin/submissions/{s.RevisionId}/approve", null)).StatusCode);
        foreach (var id in new[] { s.RevisionId, returned.RevisionId, superseded.RevisionId })
        {
            var r = await admin.PostWithCsrfAsync($"/admin/submissions/{id}/approve", null);
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
            Assert.Equal("revision-not-pending", (await Json(r)).GetProperty("code").GetString());
        }
        Assert.Single(await AuditAsync(f));
        var state = await StateAsync(f, returned.ProfileId, returned.RevisionId);
        Assert.Equal(RevisionStatus.Returned, state.Status);
        Assert.Null(state.Pointer);
    }

    [Fact]
    public async Task Approve_unknown_or_non_canonical_id_returns_uniform_404()
    {
        var (f, admin, cohort) = await SetupAsync();
        var s = await SeedAsync(f, cohort, RevisionStatus.Pending, "n@example.com", DateTime.UtcNow);
        foreach (var id in new[] { Guid.NewGuid().ToString(), "not-a-guid", s.RevisionId.ToString("N"), s.RevisionId.ToString("B") })
            Assert.Equal(HttpStatusCode.NotFound, (await admin.PostWithCsrfAsync($"/admin/submissions/{id}/approve", null)).StatusCode);
        Assert.Empty(await AuditAsync(f));
        Assert.Equal(RevisionStatus.Pending, (await StateAsync(f, s.ProfileId, s.RevisionId)).Status);
    }

    [Fact]
    public async Task Concurrent_approvals_yield_exactly_one_success()
    {
        var (f, admin, cohort) = await SetupAsync();
        var old = await SeedAsync(f, cohort, RevisionStatus.Approved, "x@example.com", DateTime.UtcNow.AddDays(-1), approvedPointer: true);
        var upd = await SeedAsync(f, cohort, RevisionStatus.Pending, "", DateTime.UtcNow, profileId: old.ProfileId);
        var csrfs = new List<string>();
        for (var i = 0; i < 5; i++) csrfs.Add(await admin.GetCsrfAsync());
        var results = await Task.WhenAll(csrfs.Select(c => admin.PostAsync($"/admin/submissions/{upd.RevisionId}/approve", null, c)));
        var codes = results.Select(r => r.StatusCode).ToList();
        Assert.Single(codes, HttpStatusCode.OK);
        Assert.Equal(4, codes.Count(c => c == HttpStatusCode.Conflict));
        Assert.Single(await AuditAsync(f));
        Assert.Equal((RevisionStatus.Approved, (Guid?)upd.RevisionId, ProfileVisibility.Active), await StateAsync(f, old.ProfileId, upd.RevisionId));
        Assert.Equal(RevisionStatus.Superseded, (await StateAsync(f, old.ProfileId, old.RevisionId)).Status);
    }

    [Fact]
    public async Task Approve_with_pointer_to_non_approved_revision_fails_and_changes_nothing()
    {
        var (f, admin, cohort) = await SetupAsync();
        var bad = await SeedAsync(f, cohort, RevisionStatus.Returned, "i@example.com", DateTime.UtcNow.AddDays(-1), approvedPointer: true);
        var upd = await SeedAsync(f, cohort, RevisionStatus.Pending, "", DateTime.UtcNow, profileId: bad.ProfileId);
        var r = await admin.PostWithCsrfAsync($"/admin/submissions/{upd.RevisionId}/approve", null);
        Assert.Equal(HttpStatusCode.InternalServerError, r.StatusCode);
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        var state = await StateAsync(f, bad.ProfileId, upd.RevisionId);
        Assert.Equal(RevisionStatus.Pending, state.Status);
        Assert.Equal(bad.RevisionId, state.Pointer);
        Assert.Empty(await AuditAsync(f));
    }

    [Fact]
    public async Task Concurrent_approvals_of_two_pending_revisions_of_one_profile_leave_one_approved()
    {
        var (f, admin, cohort) = await SetupAsync();
        var old = await SeedAsync(f, cohort, RevisionStatus.Approved, "w@example.com", DateTime.UtcNow.AddDays(-1), approvedPointer: true);
        // The partial unique index normally forbids two pending revisions; drop it to force the race the profile lock must handle.
        await using (var scope = f.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync("DROP INDEX ux_profile_revisions_one_pending");
        var a = await SeedAsync(f, cohort, RevisionStatus.Pending, "", DateTime.UtcNow, profileId: old.ProfileId);
        var b = await SeedAsync(f, cohort, RevisionStatus.Pending, "", DateTime.UtcNow, profileId: old.ProfileId);
        var csrfA = await admin.GetCsrfAsync();
        var csrfB = await admin.GetCsrfAsync();
        var results = await Task.WhenAll(
            admin.PostAsync($"/admin/submissions/{a.RevisionId}/approve", null, csrfA),
            admin.PostAsync($"/admin/submissions/{b.RevisionId}/approve", null, csrfB));
        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        await using var check = f.Services.CreateAsyncScope();
        var db = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var approved = await db.ProfileRevisions.AsNoTracking().Where(r => r.ProfileId == old.ProfileId && r.Status == RevisionStatus.Approved).Select(r => r.Id).ToListAsync();
        var pointer = await db.Profiles.AsNoTracking().Where(p => p.Id == old.ProfileId).Select(p => p.ApprovedRevisionId).SingleAsync();
        Assert.Single(approved);
        Assert.Equal(pointer, approved[0]);
    }

    [Fact]
    public async Task Approve_requires_admin_and_request_protection()
    {
        var (f, admin, cohort) = await SetupAsync();
        var s = await SeedAsync(f, cohort, RevisionStatus.Pending, "p@example.com", DateTime.UtcNow);
        var path = $"/admin/submissions/{s.RevisionId}/approve";

        var anon = f.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsync(path, null, await anon.GetCsrfAsync())).StatusCode);

        await f.AddUserAsync("viewer2", "Quiet-Horse-Pass-77!Qx", admin: false);
        var plain = f.NewClient();
        Assert.Equal(HttpStatusCode.OK, (await plain.LoginAsync("viewer2", "Quiet-Horse-Pass-77!Qx")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.PostWithCsrfAsync(path, null)).StatusCode);

        Assert.False((await admin.PostAsync(path, null, csrf: null)).IsSuccessStatusCode);
        Assert.False((await admin.PostAsync(path, null, await admin.GetCsrfAsync(), origin: "https://evil.example")).IsSuccessStatusCode);
        Assert.Equal(RevisionStatus.Pending, (await StateAsync(f, s.ProfileId, s.RevisionId)).Status);
        Assert.Empty(await AuditAsync(f));
    }
}
