using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResumeApp.Domain.Audit;
using ResumeApp.Domain.Profiles;
using ResumeApp.Infrastructure;

namespace ResumeApp.Tests;

/// <summary>Story 2.3: published-vs-proposed review details and explicit approval of a pending email or phone change.</summary>
[Collection("env")]
public class ContactChangeReviewTests(PostgresFixture pg) : IDisposable
{
    private const string NewPassword = "Brand-New-Pass-42#Lk";
    private readonly List<AuthApiFactory> _factories = [];
    private static readonly byte[] PhotoA = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];
    private static readonly byte[] PhotoB = [0xFF, 0xD8, 0xFF, 0xE0, 9, 9, 9];

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
        var response = await admin.PostWithCsrfAsync("/admin/cohorts", new { name = "מחזור ג" });
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (factory, admin, Guid.Parse(json.GetProperty("id").GetString()!));
    }

    private static async Task<Guid> NewProfileAsync(AuthApiFactory f, Guid cohortId, string email = "old@example.com", string phone = "0501234567")
    {
        await using var scope = f.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var profile = Profile.Create(cohortId, DateTime.UtcNow);
        db.Profiles.Add(profile);
        await db.SaveChangesAsync();
        db.ParticipantPrivates.Add(ParticipantPrivate.Create(profile.Id, cohortId, email, phone));
        await db.SaveChangesAsync();
        return profile.Id;
    }

    private static async Task<Guid> AddRevisionAsync(AuthApiFactory f, Guid profileId, RevisionStatus status, string name = "דנה", byte[]? photo = null,
        string? cv = null, string? description = "תיאור", string? pendingEmail = null, string? pendingPhone = null, bool pointer = false, DateTime? at = null)
    {
        await using var scope = f.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var extras = new ProfileExtras(description, "תפקיד", "מקום", "היכרות", "א1", "א2", "א3", "girl-1",
            photo, photo is null ? null : "image/jpeg", cv);
        var rev = ProfileRevision.CreatePending(profileId, name, "מוצר", "https://example.com/p", at ?? DateTime.UtcNow, extras, pendingEmail, pendingPhone);
        db.ProfileRevisions.Add(rev);
        db.Entry(rev).Property(r => r.Status).CurrentValue = status;
        await db.SaveChangesAsync();
        if (pointer)
        {
            var profile = await db.Profiles.SingleAsync(p => p.Id == profileId);
            db.Entry(profile).Property(p => p.ApprovedRevisionId).CurrentValue = rev.Id;
            await db.SaveChangesAsync();
        }
        return rev.Id;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<T> DbAsync<T>(AuthApiFactory f, Func<AppDbContext, Task<T>> read)
    {
        await using var scope = f.Services.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static Task<ParticipantPrivate> PrivateAsync(AuthApiFactory f, Guid profileId)
        => DbAsync(f, db => db.ParticipantPrivates.AsNoTracking().SingleAsync(p => p.ProfileId == profileId));

    private static Task<(string? Email, string? Phone)> PendingAsync(AuthApiFactory f, Guid revisionId)
        => DbAsync(f, async db =>
        {
            var r = await db.ProfileRevisions.AsNoTracking().Where(x => x.Id == revisionId).Select(x => new { x.PendingEmail, x.PendingPhone }).SingleAsync();
            return (r.PendingEmail, r.PendingPhone);
        });

    private static Task<List<AuditLogEntry>> AuditAsync(AuthApiFactory f) => DbAsync(f, db => db.AuditLog.AsNoTracking().ToListAsync());

    private static string[] Changes(JsonElement d) => d.GetProperty("changes").EnumerateArray().Select(c => c.GetString()!).ToArray();

    // ---- details: published and changes ----

    [Fact]
    public async Task Update_detail_has_published_and_lists_changed_fields()
    {
        var (f, admin, cohort) = await SetupAsync();
        var profile = await NewProfileAsync(f, cohort);
        await AddRevisionAsync(f, profile, RevisionStatus.Approved, name: "דנה", photo: PhotoA, cv: "# קודם", pointer: true);
        var pending = await AddRevisionAsync(f, profile, RevisionStatus.Pending, name: "דנה כהן", photo: PhotoA, cv: "# חדש", description: "אחר");

        var response = await admin.Http.GetAsync($"/admin/submissions/{pending}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var d = await Json(response);
        Assert.Equal("update", d.GetProperty("kind").GetString());
        var pub = d.GetProperty("published");
        Assert.Equal("דנה", pub.GetProperty("name").GetString());
        Assert.Equal("תיאור", pub.GetProperty("description").GetString());
        Assert.Equal("מוצר", pub.GetProperty("productName").GetString());
        Assert.Equal("https://example.com/p", pub.GetProperty("productUrl").GetString());
        Assert.Equal("תפקיד", pub.GetProperty("role").GetString());
        Assert.Equal("מקום", pub.GetProperty("workplace").GetString());
        Assert.Equal("היכרות", pub.GetProperty("introText").GetString());
        Assert.Equal("א1", pub.GetProperty("introAnswer1").GetString());
        Assert.Equal("א2", pub.GetProperty("introAnswer2").GetString());
        Assert.Equal("א3", pub.GetProperty("introAnswer3").GetString());
        Assert.Equal("girl-1", pub.GetProperty("avatarId").GetString());
        Assert.True(pub.GetProperty("hasPhoto").GetBoolean());
        Assert.Contains("קודם", pub.GetProperty("cv").GetString());
        Assert.False(pub.TryGetProperty("email", out _));
        Assert.False(pub.TryGetProperty("phone", out _));
        Assert.Equal(["name", "description", "cv"], Changes(d));
        Assert.Equal(JsonValueKind.Null, d.GetProperty("contactChange").ValueKind);
        Assert.Equal("old@example.com", d.GetProperty("email").GetString());
    }

    [Theory]
    [InlineData(true, 1, false)]   // same photo: no change
    [InlineData(true, 2, true)]    // replaced
    [InlineData(true, 0, true)]    // removed
    [InlineData(false, 0, false)]  // no photo on either side
    [InlineData(false, 1, true)]   // added
    public async Task Photo_is_in_changes_only_when_added_removed_or_replaced(bool publishedHasPhoto, int proposed, bool expected)
    {
        var (f, admin, cohort) = await SetupAsync();
        var profile = await NewProfileAsync(f, cohort);
        await AddRevisionAsync(f, profile, RevisionStatus.Approved, photo: publishedHasPhoto ? PhotoA : null, pointer: true);
        byte[]? proposedPhoto = proposed switch { 0 => null, 1 => PhotoA, _ => PhotoB };
        var pending = await AddRevisionAsync(f, profile, RevisionStatus.Pending, photo: proposedPhoto);
        var d = await Json(await admin.Http.GetAsync($"/admin/submissions/{pending}"));
        Assert.Equal(expected, Changes(d).Contains("photo"));
    }

    [Fact]
    public async Task New_submission_has_null_published_and_no_changes()
    {
        var (f, admin, cohort) = await SetupAsync();
        var profile = await NewProfileAsync(f, cohort);
        var pending = await AddRevisionAsync(f, profile, RevisionStatus.Pending);
        var d = await Json(await admin.Http.GetAsync($"/admin/submissions/{pending}"));
        Assert.Equal("new", d.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, d.GetProperty("published").ValueKind);
        Assert.Empty(Changes(d));
    }

    [Fact]
    public async Task Identical_update_has_no_changes()
    {
        var (f, admin, cohort) = await SetupAsync();
        var profile = await NewProfileAsync(f, cohort);
        await AddRevisionAsync(f, profile, RevisionStatus.Approved, photo: PhotoA, cv: "# אותו דבר", pointer: true);
        var pending = await AddRevisionAsync(f, profile, RevisionStatus.Pending, photo: PhotoA, cv: "# אותו דבר");
        var d = await Json(await admin.Http.GetAsync($"/admin/submissions/{pending}"));
        Assert.Empty(Changes(d));
        Assert.Equal(JsonValueKind.Object, d.GetProperty("published").ValueKind);
    }

    [Fact]
    public async Task Dangerous_cv_on_either_side_is_cleaned_or_null_and_does_not_fail()
    {
        var (f, admin, cohort) = await SetupAsync();
        var profile = await NewProfileAsync(f, cohort);
        await AddRevisionAsync(f, profile, RevisionStatus.Approved, cv: "hello <script>alert(1)</script> world", pointer: true);
        var pending = await AddRevisionAsync(f, profile, RevisionStatus.Pending, cv: "[x](javascript:alert(1))");
        var response = await admin.Http.GetAsync($"/admin/submissions/{pending}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var d = await Json(response);
        var pub = d.GetProperty("published");
        Assert.DoesNotContain("<script", pub.GetProperty("cv").GetString() ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.False(pub.GetProperty("cvInvalid").GetBoolean());
        Assert.Equal(JsonValueKind.Null, d.GetProperty("cv").ValueKind);
        Assert.True(d.GetProperty("cvInvalid").GetBoolean());
        Assert.Contains("cv", Changes(d));
    }

    [Fact]
    public async Task Detail_shows_contact_change_separately_from_changes_and_queue_flags_it()
    {
        var (f, admin, cohort) = await SetupAsync();
        var profile = await NewProfileAsync(f, cohort);
        await AddRevisionAsync(f, profile, RevisionStatus.Approved, pointer: true);
        var pending = await AddRevisionAsync(f, profile, RevisionStatus.Pending, pendingEmail: "new@example.com", pendingPhone: "0509999999");
        var other = await AddRevisionAsync(f, await NewProfileAsync(f, cohort, "other@example.com"), RevisionStatus.Pending);

        var d = await Json(await admin.Http.GetAsync($"/admin/submissions/{pending}"));
        Assert.Empty(Changes(d));
        var cc = d.GetProperty("contactChange");
        Assert.Equal("new@example.com", cc.GetProperty("pendingEmail").GetString());
        Assert.Equal("0509999999", cc.GetProperty("pendingPhone").GetString());
        Assert.Equal("old@example.com", d.GetProperty("email").GetString());

        var queue = await Json(await admin.Http.GetAsync("/admin/submissions"));
        var items = queue.GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("revisionId").GetGuid());
        Assert.True(items[pending].GetProperty("hasContactChange").GetBoolean());
        Assert.False(items[other].GetProperty("hasContactChange").GetBoolean());
        Assert.Equal("update", items[pending].GetProperty("kind").GetString());
    }

    // ---- approval of publication ----

    [Fact]
    public async Task Approving_publication_does_not_apply_contact_change_and_gallery_shows_new_version()
    {
        var (f, admin, cohort) = await SetupAsync();
        var profile = await NewProfileAsync(f, cohort);
        var old = await AddRevisionAsync(f, profile, RevisionStatus.Approved, name: "שם ישן", pointer: true);
        var pending = await AddRevisionAsync(f, profile, RevisionStatus.Pending, name: "שם חדש", pendingEmail: "new@example.com");

        var anon = f.NewClient();
        Assert.Contains("שם ישן", await (await anon.Http.GetAsync($"/profiles/{profile}")).Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, (await admin.PostWithCsrfAsync($"/admin/submissions/{pending}/approve", null)).StatusCode);

        var detail = await (await anon.Http.GetAsync($"/profiles/{profile}")).Content.ReadAsStringAsync();
        Assert.Contains("שם חדש", detail);
        Assert.DoesNotContain("שם ישן", detail);
        var gallery = await (await anon.Http.GetAsync("/profiles")).Content.ReadAsStringAsync();
        Assert.Contains("שם חדש", gallery);
        Assert.DoesNotContain("new@example.com", detail + gallery);

        Assert.Equal("old@example.com", (await PrivateAsync(f, profile)).Email);
        Assert.Equal(1, (await PrivateAsync(f, profile)).ContactVersion);
        Assert.Equal("new@example.com", (await PendingAsync(f, pending)).Email);
        Assert.Equal(RevisionStatus.Superseded, await DbAsync(f, db => db.ProfileRevisions.AsNoTracking().Where(r => r.Id == old).Select(r => r.Status).SingleAsync()));
        Assert.Equal("approve-revision", Assert.Single(await AuditAsync(f)).Action);
    }

    [Fact]
    public async Task Approving_a_superseded_revision_returns_409_with_the_latest_pending_revision()
    {
        var (f, admin, cohort) = await SetupAsync();
        var profile = await NewProfileAsync(f, cohort);
        await AddRevisionAsync(f, profile, RevisionStatus.Approved, pointer: true);
        var stale = await AddRevisionAsync(f, profile, RevisionStatus.Superseded);
        var latest = await AddRevisionAsync(f, profile, RevisionStatus.Pending);

        var r = await admin.PostWithCsrfAsync($"/admin/submissions/{stale}/approve", null);
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        var json = await Json(r);
        Assert.Equal("revision-not-pending", json.GetProperty("code").GetString());
        Assert.Equal(latest, json.GetProperty("latestPendingRevisionId").GetGuid());
        Assert.Empty(await AuditAsync(f));
    }

    [Fact]
    public async Task Approving_an_approved_or_returned_revision_returns_null_latest_pending()
    {
        var (f, admin, cohort) = await SetupAsync();
        var profile = await NewProfileAsync(f, cohort);
        var approved = await AddRevisionAsync(f, profile, RevisionStatus.Approved, pointer: true);
        var other = await NewProfileAsync(f, cohort, "o@example.com");
        var returned = await AddRevisionAsync(f, other, RevisionStatus.Returned);
        foreach (var id in new[] { approved, returned })
        {
            var json = await Json(await admin.PostWithCsrfAsync($"/admin/submissions/{id}/approve", null));
            Assert.Equal("revision-not-pending", json.GetProperty("code").GetString());
            Assert.True(json.TryGetProperty("latestPendingRevisionId", out var latest));
            Assert.Equal(JsonValueKind.Null, latest.ValueKind);
        }
    }

    // ---- contact change approve / reject ----

    private static object Seen(string? email, string? phone) => new { pendingEmail = email, pendingPhone = phone };

    [Fact]
    public async Task Approve_contact_change_updates_private_bumps_version_clears_pending_and_audits()
    {
        var (f, admin, cohort) = await SetupAsync();
        var profile = await NewProfileAsync(f, cohort);
        var rev = await AddRevisionAsync(f, profile, RevisionStatus.Pending, pendingEmail: "new@example.com", pendingPhone: "0509999999");

        var response = await admin.PostWithCsrfAsync($"/admin/submissions/{rev}/approve-contact-change", Seen(" New@Example.com ", "050-999-9999"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("example.com", body);
        Assert.DoesNotContain("0509999999", body);
        var json = JsonSerializer.Deserialize<JsonElement>(body);
        Assert.Equal(rev, json.GetProperty("revisionId").GetGuid());
        Assert.Equal("contact-change-approved", json.GetProperty("status").GetString());

        var pp = await PrivateAsync(f, profile);
        Assert.Equal("new@example.com", pp.Email);
        Assert.Equal("0509999999", pp.Phone);
        Assert.Equal(2, pp.ContactVersion);
        Assert.Equal((null, null), await PendingAsync(f, rev));
        var audit = Assert.Single(await AuditAsync(f));
        Assert.Equal("approve-contact-change", audit.Action);
        Assert.Equal("profile-revision", audit.EntityType);
        Assert.Equal(rev, audit.EntityId);
    }

    [Fact]
    public async Task Approve_contact_change_works_on_an_already_approved_revision_and_phone_only()
    {
        var (f, admin, cohort) = await SetupAsync();
        var profile = await NewProfileAsync(f, cohort);
        var rev = await AddRevisionAsync(f, profile, RevisionStatus.Approved, pendingPhone: "+972501112222", pointer: true);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostWithCsrfAsync($"/admin/submissions/{rev}/approve-contact-change", Seen(null, "+972501112222"))).StatusCode);
        var pp = await PrivateAsync(f, profile);
        Assert.Equal("old@example.com", pp.Email);
        Assert.Equal("+972501112222", pp.Phone);
        Assert.Equal(2, pp.ContactVersion);
    }

    [Fact]
    public async Task Reject_contact_change_clears_pending_and_leaves_private_unchanged()
    {
        var (f, admin, cohort) = await SetupAsync();
        var profile = await NewProfileAsync(f, cohort);
        var rev = await AddRevisionAsync(f, profile, RevisionStatus.Pending, pendingEmail: "new@example.com", pendingPhone: "0509999999");

        var response = await admin.PostWithCsrfAsync($"/admin/submissions/{rev}/reject-contact-change", Seen("new@example.com", "0509999999"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("contact-change-rejected", (await Json(response)).GetProperty("status").GetString());
        var pp = await PrivateAsync(f, profile);
        Assert.Equal("old@example.com", pp.Email);
        Assert.Equal("0501234567", pp.Phone);
        Assert.Equal(1, pp.ContactVersion);
        Assert.Equal((null, null), await PendingAsync(f, rev));
        Assert.Equal("reject-contact-change", Assert.Single(await AuditAsync(f)).Action);
    }

    [Fact]
    public async Task Email_taken_in_the_cohort_returns_409_changes_nothing_and_exposes_nothing()
    {
        var (f, admin, cohort) = await SetupAsync();
        await NewProfileAsync(f, cohort, "taken@example.com");
        var profile = await NewProfileAsync(f, cohort);
        var rev = await AddRevisionAsync(f, profile, RevisionStatus.Pending, pendingEmail: "taken@example.com", pendingPhone: "0509999999");

        var r = await admin.PostWithCsrfAsync($"/admin/submissions/{rev}/approve-contact-change", Seen("taken@example.com", "0509999999"));
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        var text = await r.Content.ReadAsStringAsync();
        Assert.Equal("email-taken", JsonSerializer.Deserialize<JsonElement>(text).GetProperty("code").GetString());
        Assert.DoesNotContain("taken@example.com", text);

        var pp = await PrivateAsync(f, profile);
        Assert.Equal("old@example.com", pp.Email);
        Assert.Equal("0501234567", pp.Phone);
        Assert.Equal(1, pp.ContactVersion);
        Assert.Equal(("taken@example.com", "0509999999"), await PendingAsync(f, rev));
        Assert.Empty(await AuditAsync(f));
    }

    [Fact]
    public async Task Same_email_in_another_cohort_is_allowed()
    {
        var (f, admin, cohort) = await SetupAsync();
        var other = await Json(await admin.PostWithCsrfAsync("/admin/cohorts", new { name = "מחזור ד" }));
        await NewProfileAsync(f, Guid.Parse(other.GetProperty("id").GetString()!), "shared@example.com");
        var profile = await NewProfileAsync(f, cohort);
        var rev = await AddRevisionAsync(f, profile, RevisionStatus.Pending, pendingEmail: "shared@example.com");
        Assert.Equal(HttpStatusCode.OK, (await admin.PostWithCsrfAsync($"/admin/submissions/{rev}/approve-contact-change", Seen("shared@example.com", null))).StatusCode);
    }

    [Fact]
    public async Task No_pending_contact_change_returns_409_no_contact_change()
    {
        var (f, admin, cohort) = await SetupAsync();
        var rev = await AddRevisionAsync(f, await NewProfileAsync(f, cohort), RevisionStatus.Pending);
        foreach (var action in new[] { "approve-contact-change", "reject-contact-change" })
        {
            var r = await admin.PostWithCsrfAsync($"/admin/submissions/{rev}/{action}", Seen("x@example.com", null));
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("no-contact-change", (await Json(r)).GetProperty("code").GetString());
        }
        Assert.Empty(await AuditAsync(f));
    }

    [Fact]
    public async Task Stale_values_return_409_and_change_nothing()
    {
        var (f, admin, cohort) = await SetupAsync();
        var profile = await NewProfileAsync(f, cohort);
        var rev = await AddRevisionAsync(f, profile, RevisionStatus.Pending, pendingEmail: "new@example.com", pendingPhone: "0509999999");
        foreach (var seen in new[] { Seen("other@example.com", "0509999999"), Seen("new@example.com", "0508888888"), Seen("new@example.com", null), Seen(null, "0509999999") })
        {
            foreach (var action in new[] { "approve-contact-change", "reject-contact-change" })
            {
                var r = await admin.PostWithCsrfAsync($"/admin/submissions/{rev}/{action}", seen);
                Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
                Assert.Equal("contact-change-stale", (await Json(r)).GetProperty("code").GetString());
            }
        }
        Assert.Equal(("new@example.com", "0509999999"), await PendingAsync(f, rev));
        Assert.Equal("old@example.com", (await PrivateAsync(f, profile)).Email);
        Assert.Empty(await AuditAsync(f));
    }

    [Fact]
    public async Task Superseded_or_returned_revisions_are_not_eligible()
    {
        var (f, admin, cohort) = await SetupAsync();
        var profile = await NewProfileAsync(f, cohort);
        var superseded = await AddRevisionAsync(f, profile, RevisionStatus.Superseded, pendingEmail: "a@example.com");
        var returned = await AddRevisionAsync(f, await NewProfileAsync(f, cohort, "r@example.com"), RevisionStatus.Returned, pendingEmail: "b@example.com");
        foreach (var (id, email) in new[] { (superseded, "a@example.com"), (returned, "b@example.com") })
            foreach (var action in new[] { "approve-contact-change", "reject-contact-change" })
            {
                var r = await admin.PostWithCsrfAsync($"/admin/submissions/{id}/{action}", Seen(email, null));
                Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
                Assert.Equal("revision-not-eligible", (await Json(r)).GetProperty("code").GetString());
            }
        Assert.Equal("a@example.com", (await PendingAsync(f, superseded)).Email);
        Assert.Empty(await AuditAsync(f));
    }

    [Fact]
    public async Task Unknown_or_non_canonical_id_returns_uniform_404_and_bad_bodies_return_400()
    {
        var (f, admin, cohort) = await SetupAsync();
        var rev = await AddRevisionAsync(f, await NewProfileAsync(f, cohort), RevisionStatus.Pending, pendingEmail: "n@example.com");
        foreach (var action in new[] { "approve-contact-change", "reject-contact-change" })
        {
            foreach (var id in new[] { Guid.NewGuid().ToString(), "not-a-guid", rev.ToString("N"), rev.ToString("B") })
                Assert.Equal(HttpStatusCode.NotFound, (await admin.PostWithCsrfAsync($"/admin/submissions/{id}/{action}", Seen("n@example.com", null))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostWithCsrfAsync($"/admin/submissions/{rev}/{action}", Seen(null, null))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostWithCsrfAsync($"/admin/submissions/{rev}/{action}", Seen("not-an-email", null))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostWithCsrfAsync($"/admin/submissions/{rev}/{action}", Seen(null, "12"))).StatusCode);
        }
        Assert.Equal("n@example.com", (await PendingAsync(f, rev)).Email);
        Assert.Empty(await AuditAsync(f));
    }

    [Fact]
    public async Task Concurrent_contact_change_approvals_yield_exactly_one_success()
    {
        var (f, admin, cohort) = await SetupAsync();
        var profile = await NewProfileAsync(f, cohort);
        var rev = await AddRevisionAsync(f, profile, RevisionStatus.Pending, pendingEmail: "new@example.com", pendingPhone: "0509999999");
        var csrfs = new List<string>();
        for (var i = 0; i < 5; i++) csrfs.Add(await admin.GetCsrfAsync());
        var results = await Task.WhenAll(csrfs.Select(c => admin.PostAsync($"/admin/submissions/{rev}/approve-contact-change", Seen("new@example.com", "0509999999"), c)));
        var codes = results.Select(r => r.StatusCode).ToList();
        Assert.Single(codes, HttpStatusCode.OK);
        Assert.Equal(4, codes.Count(c => c == HttpStatusCode.Conflict));
        Assert.Single(await AuditAsync(f));
        Assert.Equal(2, (await PrivateAsync(f, profile)).ContactVersion);
    }

    [Fact]
    public async Task Concurrent_approve_and_reject_succeed_exactly_once()
    {
        var (f, admin, cohort) = await SetupAsync();
        var profile = await NewProfileAsync(f, cohort);
        var rev = await AddRevisionAsync(f, profile, RevisionStatus.Pending, pendingEmail: "new@example.com");
        var a = await admin.GetCsrfAsync();
        var b = await admin.GetCsrfAsync();
        var results = await Task.WhenAll(
            admin.PostAsync($"/admin/submissions/{rev}/approve-contact-change", Seen("new@example.com", null), a),
            admin.PostAsync($"/admin/submissions/{rev}/reject-contact-change", Seen("new@example.com", null), b));
        var codes = results.Select(r => r.StatusCode).ToList();
        Assert.Single(codes, HttpStatusCode.OK);
        Assert.Single(codes, HttpStatusCode.Conflict);
        var audit = Assert.Single(await AuditAsync(f));
        var pp = await PrivateAsync(f, profile);
        if (audit.Action == "approve-contact-change") Assert.Equal("new@example.com", pp.Email);
        else Assert.Equal("old@example.com", pp.Email);
    }

    [Fact]
    public async Task Contact_change_endpoints_require_admin_and_request_protection()
    {
        var (f, admin, cohort) = await SetupAsync();
        var rev = await AddRevisionAsync(f, await NewProfileAsync(f, cohort), RevisionStatus.Pending, pendingEmail: "new@example.com");
        await f.AddUserAsync("viewer3", "Quiet-Horse-Pass-77!Qx", admin: false);
        var plain = f.NewClient();
        Assert.Equal(HttpStatusCode.OK, (await plain.LoginAsync("viewer3", "Quiet-Horse-Pass-77!Qx")).StatusCode);
        var anon = f.NewClient();
        foreach (var action in new[] { "approve-contact-change", "reject-contact-change" })
        {
            var path = $"/admin/submissions/{rev}/{action}";
            var body = Seen("new@example.com", null);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsync(path, body, await anon.GetCsrfAsync())).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await plain.PostWithCsrfAsync(path, body)).StatusCode);
            Assert.False((await admin.PostAsync(path, body, csrf: null)).IsSuccessStatusCode);
            Assert.False((await admin.PostAsync(path, body, await admin.GetCsrfAsync(), origin: "https://evil.example")).IsSuccessStatusCode);
        }
        Assert.Equal("new@example.com", (await PendingAsync(f, rev)).Email);
        Assert.Empty(await AuditAsync(f));
    }

    // ---- review follow-ups ----

    [Fact]
    public async Task Reject_on_an_approved_revision_clears_pending_and_leaves_private_unchanged()
    {
        var (f, admin, cohort) = await SetupAsync();
        var profile = await NewProfileAsync(f, cohort);
        var rev = await AddRevisionAsync(f, profile, RevisionStatus.Approved, pendingEmail: "new@example.com", pointer: true);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostWithCsrfAsync($"/admin/submissions/{rev}/reject-contact-change", Seen("new@example.com", null))).StatusCode);
        Assert.Equal((null, null), await PendingAsync(f, rev));
        var pp = await PrivateAsync(f, profile);
        Assert.Equal("old@example.com", pp.Email);
        Assert.Equal(1, pp.ContactVersion);
    }

    [Fact]
    public async Task Approved_revision_with_a_newer_pending_revision_is_not_eligible()
    {
        var (f, admin, cohort) = await SetupAsync();
        var profile = await NewProfileAsync(f, cohort);
        var approved = await AddRevisionAsync(f, profile, RevisionStatus.Approved, pendingEmail: "old-request@example.com", pointer: true);
        await AddRevisionAsync(f, profile, RevisionStatus.Pending, pendingEmail: "newer-request@example.com");
        foreach (var action in new[] { "approve-contact-change", "reject-contact-change" })
        {
            var r = await admin.PostWithCsrfAsync($"/admin/submissions/{approved}/{action}", Seen("old-request@example.com", null));
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("revision-not-eligible", (await Json(r)).GetProperty("code").GetString());
        }
        Assert.Equal("old@example.com", (await PrivateAsync(f, profile)).Email);
        Assert.Equal("old-request@example.com", (await PendingAsync(f, approved)).Email);
        Assert.Empty(await AuditAsync(f));
    }

    [Fact]
    public async Task Approving_an_email_equal_to_current_does_not_bump_version_but_clears_and_audits()
    {
        var (f, admin, cohort) = await SetupAsync();
        var profile = await NewProfileAsync(f, cohort);
        var rev = await AddRevisionAsync(f, profile, RevisionStatus.Pending, pendingEmail: "old@example.com");
        Assert.Equal(HttpStatusCode.OK, (await admin.PostWithCsrfAsync($"/admin/submissions/{rev}/approve-contact-change", Seen("old@example.com", null))).StatusCode);
        Assert.Equal(1, (await PrivateAsync(f, profile)).ContactVersion);
        Assert.Equal((null, null), await PendingAsync(f, rev));
        Assert.Equal("approve-contact-change", Assert.Single(await AuditAsync(f)).Action);
    }

    [Fact]
    public async Task Queue_flags_a_phone_only_contact_change()
    {
        var (f, admin, cohort) = await SetupAsync();
        var rev = await AddRevisionAsync(f, await NewProfileAsync(f, cohort), RevisionStatus.Pending, pendingPhone: "0509999999");
        var queue = await Json(await admin.Http.GetAsync("/admin/submissions"));
        var item = queue.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("revisionId").GetGuid() == rev);
        Assert.True(item.GetProperty("hasContactChange").GetBoolean());
    }

    [Fact]
    public async Task Contact_changes_list_includes_pending_and_published_excludes_resolved_superseded_returned()
    {
        var (f, admin, cohort) = await SetupAsync();
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var pendingP = await NewProfileAsync(f, cohort, "p1@example.com");
        var pending = await AddRevisionAsync(f, pendingP, RevisionStatus.Pending, name: "ממתינה", pendingEmail: "n1@example.com", at: t.AddHours(2));
        var publishedP = await NewProfileAsync(f, cohort, "p2@example.com");
        var published = await AddRevisionAsync(f, publishedP, RevisionStatus.Approved, name: "פורסמה", pendingPhone: "0508888888", pointer: true, at: t.AddHours(1));
        await AddRevisionAsync(f, await NewProfileAsync(f, cohort, "p3@example.com"), RevisionStatus.Superseded, pendingEmail: "x@example.com");
        await AddRevisionAsync(f, await NewProfileAsync(f, cohort, "p4@example.com"), RevisionStatus.Returned, pendingEmail: "y@example.com");
        await AddRevisionAsync(f, await NewProfileAsync(f, cohort, "p5@example.com"), RevisionStatus.Pending);
        var resolvedP = await NewProfileAsync(f, cohort, "p6@example.com");
        var resolved = await AddRevisionAsync(f, resolvedP, RevisionStatus.Pending, pendingEmail: "z@example.com");
        Assert.Equal(HttpStatusCode.OK, (await admin.PostWithCsrfAsync($"/admin/submissions/{resolved}/reject-contact-change", Seen("z@example.com", null))).StatusCode);

        var response = await admin.Http.GetAsync("/admin/contact-changes");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var json = await Json(response);
        Assert.Equal(2, json.GetProperty("total").GetInt32());
        var items = json.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal([published, pending], items.Select(i => i.GetProperty("revisionId").GetGuid()));
        Assert.Equal("approved", items[0].GetProperty("status").GetString());
        Assert.Equal("pending", items[1].GetProperty("status").GetString());
        Assert.Equal(publishedP, items[0].GetProperty("profileId").GetGuid());
        Assert.Equal("פורסמה", items[0].GetProperty("name").GetString());
        Assert.Equal("מוצר", items[0].GetProperty("productName").GetString());
        Assert.Equal("מחזור ג", items[0].GetProperty("cohortName").GetString());
        Assert.Equal("p2@example.com", items[0].GetProperty("email").GetString());
        Assert.Equal("0501234567", items[0].GetProperty("phone").GetString());
        Assert.Equal("0508888888", items[0].GetProperty("pendingPhone").GetString());
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("pendingEmail").ValueKind);
    }

    [Fact]
    public async Task Contact_changes_list_still_lists_change_after_publication_is_approved_and_pages()
    {
        var (f, admin, cohort) = await SetupAsync();
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var revs = new List<Guid>();
        for (var i = 0; i < 3; i++)
            revs.Add(await AddRevisionAsync(f, await NewProfileAsync(f, cohort, "q" + i + "@example.com"), RevisionStatus.Pending, pendingEmail: "n" + i + "@example.com", at: t.AddHours(i)));
        Assert.Equal(HttpStatusCode.OK, (await admin.PostWithCsrfAsync($"/admin/submissions/{revs[0]}/approve", null)).StatusCode);

        var all = await Json(await admin.Http.GetAsync("/admin/contact-changes"));
        Assert.Equal(revs, all.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("revisionId").GetGuid()));
        Assert.Equal("approved", all.GetProperty("items")[0].GetProperty("status").GetString());

        var page = await Json(await admin.Http.GetAsync("/admin/contact-changes?limit=1&offset=1"));
        Assert.Equal(3, page.GetProperty("total").GetInt32());
        Assert.Equal([revs[1]], page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("revisionId").GetGuid()));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Http.GetAsync("/admin/contact-changes?limit=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Http.GetAsync("/admin/contact-changes?offset=-1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.Http.GetAsync("/admin/contact-changes?limit=100000")).StatusCode);
    }

    [Fact]
    public async Task Contact_changes_list_requires_admin()
    {
        var (f, _, _) = await SetupAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.NewClient().Http.GetAsync("/admin/contact-changes")).StatusCode);
        await f.AddUserAsync("viewer4", "Quiet-Horse-Pass-77!Qx", admin: false);
        var plain = f.NewClient();
        Assert.Equal(HttpStatusCode.OK, (await plain.LoginAsync("viewer4", "Quiet-Horse-Pass-77!Qx")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.Http.GetAsync("/admin/contact-changes")).StatusCode);
    }
}
