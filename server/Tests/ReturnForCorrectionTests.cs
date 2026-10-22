using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResumeApp.Domain.Audit;
using ResumeApp.Domain.Email;
using ResumeApp.Domain.Profiles;
using ResumeApp.Features.Email;
using ResumeApp.Infrastructure;

namespace ResumeApp.Tests;

/// <summary>Story 2.4: returning a pending revision to the participant with a note, the email via the outbox, and the participant and public sides.</summary>
[Collection("env")]
public class ReturnForCorrectionTests(PostgresFixture pg) : IDisposable
{
    private const string NewPassword = "Brand-New-Pass-42#Lk";
    private readonly List<AuthApiFactory> _factories = [];

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
        var response = await admin.PostWithCsrfAsync("/admin/cohorts", new { name = "מחזור ד" });
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (factory, admin, Guid.Parse(json.GetProperty("id").GetString()!));
    }

    private static async Task<Guid> NewProfileAsync(AuthApiFactory f, Guid cohortId, string email = "dana@example.com", string phone = "0501234567")
    {
        await using var scope = f.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var profile = Profile.Create(cohortId, DateTime.UtcNow, FakeOidcProvider.SubFor(email));
        db.Profiles.Add(profile);
        await db.SaveChangesAsync();
        db.ParticipantPrivates.Add(ParticipantPrivate.Create(profile.Id, cohortId, email, phone));
        await db.SaveChangesAsync();
        return profile.Id;
    }

    private static async Task<Guid> AddRevisionAsync(AuthApiFactory f, Guid profileId, RevisionStatus status, string name = "דנה", string? pendingEmail = null,
        string? pendingPhone = null, bool pointer = false, TimeSpan? ago = null)
    {
        await using var scope = f.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var extras = new ProfileExtras("תיאור", "תפקיד", "מקום", "היכרות", "א1", "א2", "א3", "girl-1", null, null, null);
        var rev = ProfileRevision.CreatePending(profileId, name, "מוצר", "https://example.com/p", DateTime.UtcNow - (ago ?? TimeSpan.Zero), extras, pendingEmail, pendingPhone);
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

    private static Task<HttpResponseMessage> ReturnAsync(ApiClient admin, Guid revision, string? note)
        => admin.PostWithCsrfAsync($"/admin/submissions/{revision}/return", new { note });

    private static Task<ProfileRevision> RevAsync(AuthApiFactory f, Guid id) => DbAsync(f, d => d.ProfileRevisions.AsNoTracking().SingleAsync(r => r.Id == id));

    private static Task<List<EmailOutboxEntry>> OutboxAsync(AuthApiFactory f) => DbAsync(f, d => d.EmailOutbox.AsNoTracking().ToListAsync());

    private static Task<List<AuditLogEntry>> AuditAsync(AuthApiFactory f) => DbAsync(f, d => d.AuditLog.AsNoTracking().ToListAsync());

    private static async Task<int> ProcessAsync(AuthApiFactory f)
    {
        await using var scope = f.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<EmailOutboxProcessor>().ProcessBatchAsync(CancellationToken.None);
    }

    private static async Task<string> CsrfAsync(ApiClient c) => (await Json(await c.Http.GetAsync("/profiles/csrf"))).GetProperty("token").GetString()!;

    private static async Task<ApiClient> SessionAsync(AuthApiFactory f, Guid profileId)
    {
        var email = await DbAsync(f, d => d.ParticipantPrivates.AsNoTracking().Where(p => p.ProfileId == profileId).Select(p => p.Email).SingleAsync());
        var client = f.NewClient();
        await client.SignInAsync(email);
        return client;
    }

    private static async Task<HttpResponseMessage> PutAsync(ApiClient c, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/me/profile") { Content = JsonContent.Create(body) };
        request.Headers.Add("Origin", ApiClient.Origin);
        request.Headers.Add("X-CSRF-TOKEN", await CsrfAsync(c));
        return await c.Http.SendAsync(request);
    }

    private static object Edit(string name, string email = "dana@example.com", string phone = "0501234567")
        => new { name, productName = "מוצר", productUrl = "https://example.com/p", email, phone, description = "תיאור" };

    private static async Task AssertUntouchedAsync(AuthApiFactory f, Guid rev)
    {
        var stored = await RevAsync(f, rev);
        Assert.Equal(RevisionStatus.Pending, stored.Status);
        Assert.Null(stored.ReturnNote);
        Assert.Empty(await OutboxAsync(f));
        Assert.DoesNotContain(await AuditAsync(f), a => a.Action == AuditLogEntry.ReturnRevision);
    }

    // ---- the return itself ----

    [Fact]
    public async Task Returning_a_pending_revision_marks_it_returned_with_the_note_audit_and_outbox_row()
    {
        var (f, admin, cohort) = await SetupAsync();
        var profile = await NewProfileAsync(f, cohort);
        var rev = await AddRevisionAsync(f, profile, RevisionStatus.Pending);

        var response = await ReturnAsync(admin, rev, "  שורה ראשונה\r\nשורה שניה  ");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await Json(response);
        Assert.Equal(rev, json.GetProperty("revisionId").GetGuid());
        Assert.Equal("returned", json.GetProperty("status").GetString());
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("dana@example.com", body);
        Assert.DoesNotContain("0501234567", body);

        var stored = await RevAsync(f, rev);
        Assert.Equal(RevisionStatus.Returned, stored.Status);
        Assert.Equal("שורה ראשונה\nשורה שניה", stored.ReturnNote);
        Assert.NotNull(stored.ReturnedAt);

        var audit = Assert.Single(await AuditAsync(f), a => a.Action == AuditLogEntry.ReturnRevision);
        Assert.Equal(AuditLogEntry.RevisionEntity, audit.EntityType);
        Assert.Equal(rev, audit.EntityId);

        var row = Assert.Single(await OutboxAsync(f));
        Assert.Equal("submission-returned", row.TemplateKey);
        Assert.Equal(profile, row.ProfileId);
        Assert.Equal(rev, row.RevisionId);
        Assert.Equal($"submission-returned:{rev:D}", row.DeduplicationKey);
        Assert.Equal(OutboxStatus.Pending, row.Status);
        Assert.Empty(f.Email.Messages);
    }

    [Fact]
    public async Task The_outbox_row_holds_no_note_token_email_or_phone()
    {
        var (f, admin, cohort) = await SetupAsync();
        var rev = await AddRevisionAsync(f, await NewProfileAsync(f, cohort), RevisionStatus.Pending);
        Assert.Equal(HttpStatusCode.OK, (await ReturnAsync(admin, rev, "הערה סודית ייחודית")).StatusCode);

        var text = await DbAsync(f, d => d.Database.SqlQuery<string>(
            $"SELECT concat_ws('|', id, template_key, profile_id, revision_id, deduplication_key, status, last_error_category) AS \"Value\" FROM email_outbox").SingleAsync());
        Assert.DoesNotContain("הערה סודית", text);
        Assert.DoesNotContain("dana@example.com", text);
        Assert.DoesNotContain("0501234567", text);
        var columns = await DbAsync(f, d => d.Database.SqlQuery<string>(
            $"SELECT column_name AS \"Value\" FROM information_schema.columns WHERE table_name = 'email_outbox'").ToListAsync());
        Assert.DoesNotContain(columns, c => c.Contains("note") || c.Contains("token") || c.Contains("email") || c.Contains("phone") || c.Contains("body"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t ")]
    public async Task A_missing_or_blank_note_is_400_on_the_note_field(string? note)
    {
        var (f, admin, cohort) = await SetupAsync();
        var rev = await AddRevisionAsync(f, await NewProfileAsync(f, cohort), RevisionStatus.Pending);

        var response = await ReturnAsync(admin, rev, note);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await Json(response)).GetProperty("errors").TryGetProperty("note", out _));
        await AssertUntouchedAsync(f, rev);
    }

    [Fact]
    public async Task A_note_over_1000_characters_is_400_and_exactly_1000_is_accepted()
    {
        var (f, admin, cohort) = await SetupAsync();
        var rev = await AddRevisionAsync(f, await NewProfileAsync(f, cohort), RevisionStatus.Pending);

        var tooLong = await ReturnAsync(admin, rev, new string('א', 1001));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.True((await Json(tooLong)).GetProperty("errors").TryGetProperty("note", out _));
        await AssertUntouchedAsync(f, rev);

        Assert.Equal(HttpStatusCode.OK, (await ReturnAsync(admin, rev, new string('א', 1000))).StatusCode);
    }

    [Theory]
    [InlineData("a\u0000b")]
    [InlineData("a\u202Eb")]
    [InlineData("a\u200Bb")]
    [InlineData("a\u2066b")]
    [InlineData("a\uFEFFb")]
    [InlineData("a\u001Bb")]
    public async Task Unsafe_characters_in_the_note_are_400_on_the_note_field(string note)
    {
        var (f, admin, cohort) = await SetupAsync();
        var rev = await AddRevisionAsync(f, await NewProfileAsync(f, cohort), RevisionStatus.Pending);

        var response = await ReturnAsync(admin, rev, note);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await Json(response)).GetProperty("errors").TryGetProperty("note", out _));
        await AssertUntouchedAsync(f, rev);
    }

    [Fact]
    public async Task Tab_and_line_breaks_are_allowed_in_the_note()
    {
        var (f, admin, cohort) = await SetupAsync();
        var rev = await AddRevisionAsync(f, await NewProfileAsync(f, cohort), RevisionStatus.Pending);
        Assert.Equal(HttpStatusCode.OK, (await ReturnAsync(admin, rev, "א\tב\nג\rד")).StatusCode);
        Assert.Equal("א\tב\nג\nד", (await RevAsync(f, rev)).ReturnNote);
    }

    // ---- not pending / not found ----

    [Theory]
    [InlineData(RevisionStatus.Approved)]
    [InlineData(RevisionStatus.Superseded)]
    [InlineData(RevisionStatus.Returned)]
    public async Task A_revision_that_is_not_pending_is_409_with_the_latest_pending_id_and_nothing_changes(RevisionStatus status)
    {
        var (f, admin, cohort) = await SetupAsync();
        var profile = await NewProfileAsync(f, cohort);
        var rev = await AddRevisionAsync(f, profile, status, pointer: status == RevisionStatus.Approved, ago: TimeSpan.FromHours(2));
        Guid? latest = status == RevisionStatus.Superseded ? await AddRevisionAsync(f, profile, RevisionStatus.Pending) : null;

        var response = await ReturnAsync(admin, rev, "הערה");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var json = await Json(response);
        Assert.Equal("revision-not-pending", json.GetProperty("code").GetString());
        if (latest is { } l) Assert.Equal(l, json.GetProperty("latestPendingRevisionId").GetGuid());
        else Assert.Equal(JsonValueKind.Null, json.GetProperty("latestPendingRevisionId").ValueKind);
        Assert.Equal(status, (await RevAsync(f, rev)).Status);
        Assert.Empty(await OutboxAsync(f));
        Assert.DoesNotContain(await AuditAsync(f), a => a.Action == AuditLogEntry.ReturnRevision);
    }

    [Theory]
    [InlineData("random")]
    [InlineData("not-a-guid")]
    [InlineData("N")]
    [InlineData("B")]
    public async Task An_unknown_or_non_canonical_id_is_a_uniform_404(string kind)
    {
        var (f, admin, cohort) = await SetupAsync();
        var existing = await AddRevisionAsync(f, await NewProfileAsync(f, cohort), RevisionStatus.Pending);
        var segment = kind switch
        {
            "random" => Guid.NewGuid().ToString("D"),
            "not-a-guid" => "not-a-guid",
            "N" => existing.ToString("N"),
            _ => Uri.EscapeDataString(existing.ToString("B")),
        };

        var response = await admin.PostWithCsrfAsync($"/admin/submissions/{segment}/return", new { note = "הערה" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertUntouchedAsync(f, existing);
    }

    // ---- concurrency ----

    [Fact]
    public async Task Five_parallel_returns_succeed_once_with_one_audit_row_and_one_outbox_row()
    {
        var (f, admin, cohort) = await SetupAsync();
        var rev = await AddRevisionAsync(f, await NewProfileAsync(f, cohort), RevisionStatus.Pending);
        var csrf = await admin.GetCsrfAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(i => admin.PostAsync($"/admin/submissions/{rev}/return", new { note = $"הערה {i}" }, csrf)));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(4, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Single(await OutboxAsync(f));
        Assert.Single(await AuditAsync(f), a => a.Action == AuditLogEntry.ReturnRevision);
    }

    [Fact]
    public async Task Approve_and_return_in_parallel_exactly_one_wins()
    {
        var (f, admin, cohort) = await SetupAsync();
        var rev = await AddRevisionAsync(f, await NewProfileAsync(f, cohort), RevisionStatus.Pending);
        var csrf = await admin.GetCsrfAsync();

        var results = await Task.WhenAll(
            admin.PostAsync($"/admin/submissions/{rev}/approve", null, csrf),
            admin.PostAsync($"/admin/submissions/{rev}/return", new { note = "הערה" }, csrf));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        var status = (await RevAsync(f, rev)).Status;
        var outbox = await OutboxAsync(f);
        if (status == RevisionStatus.Returned) Assert.Single(outbox);
        else
        {
            Assert.Equal(RevisionStatus.Approved, status);
            Assert.Empty(outbox);
        }
    }

    // ---- authorisation and request protection ----

    [Fact]
    public async Task Return_requires_admin_and_request_protection()
    {
        var (f, admin, cohort) = await SetupAsync();
        var rev = await AddRevisionAsync(f, await NewProfileAsync(f, cohort), RevisionStatus.Pending);
        await f.AddUserAsync("viewer5", "Quiet-Horse-Pass-77!Qx", admin: false);
        var plain = f.NewClient();
        Assert.Equal(HttpStatusCode.OK, (await plain.LoginAsync("viewer5", "Quiet-Horse-Pass-77!Qx")).StatusCode);
        var anon = f.NewClient();
        var path = $"/admin/submissions/{rev}/return";
        var body = new { note = "הערה" };

        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsync(path, body, await anon.GetCsrfAsync())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.PostWithCsrfAsync(path, body)).StatusCode);
        Assert.False((await admin.PostAsync(path, body, csrf: null)).IsSuccessStatusCode);
        Assert.False((await admin.PostAsync(path, body, await admin.GetCsrfAsync(), origin: "https://evil.example")).IsSuccessStatusCode);

        await AssertUntouchedAsync(f, rev);
    }

    // ---- the email ----

    [Fact]
    public async Task The_worker_sends_a_plain_text_email_with_the_note_cohort_and_a_site_link_without_any_token()
    {
        var (f, admin, cohort) = await SetupAsync();
        var profile = await NewProfileAsync(f, cohort, "dana@example.com");
        var rev = await AddRevisionAsync(f, profile, RevisionStatus.Pending);
        Assert.Equal(HttpStatusCode.OK, (await ReturnAsync(admin, rev, "קצר מדי <100 מילים, <b>את</b> התיאור\nותודה")).StatusCode);

        Assert.Equal(1, await ProcessAsync(f));

        var sent = Assert.Single(f.Email.Messages);
        Assert.Equal("dana@example.com", sent.Message.To);
        Assert.Equal(SubmissionReturnedEmail.Subject, sent.Message.Subject);
        var text = sent.Message.TextBody;
        Assert.Contains("קצר מדי <100 מילים, <b>את</b> התיאור\nותודה", text);
        Assert.Contains("מחזור ד", text);
        // Story 6.4: a plain link to the site (sign-in with Google happens there); no personal link, no token, no fragment.
        Assert.Contains($"{AuthApiFactory.PublicBaseUrl}/me", text);
        Assert.DoesNotContain("#token", text);
        Assert.DoesNotContain("/edit", text);
        Assert.DoesNotContain("הקישור האישי", text);
        Assert.False(Regex.IsMatch(text, @"[A-Za-z0-9_-]{43}"));

        var row = Assert.Single(await OutboxAsync(f));
        Assert.Equal(OutboxStatus.Sent, row.Status);
        Assert.Equal(0, await DbAsync(f, d => d.PersonalTokens.CountAsync()));
    }

    [Fact]
    public async Task A_profile_removed_before_sending_makes_the_row_dead_with_a_category()
    {
        var (f, admin, cohort) = await SetupAsync();
        var profile = await NewProfileAsync(f, cohort);
        var rev = await AddRevisionAsync(f, profile, RevisionStatus.Pending);
        Assert.Equal(HttpStatusCode.OK, (await ReturnAsync(admin, rev, "הערה")).StatusCode);
        await DbAsync(f, d => d.Database.ExecuteSqlInterpolatedAsync($"UPDATE profiles SET visibility = 'Removed' WHERE id = {profile}"));

        await ProcessAsync(f);

        var row = Assert.Single(await OutboxAsync(f));
        Assert.Equal(OutboxStatus.Dead, row.Status);
        Assert.Equal(EmailOutboxProcessor.CategoryNoRecipient, row.LastErrorCategory);
        Assert.Empty(f.Email.Messages);
    }

    [Fact]
    public void Note_cleaning_in_the_email_keeps_angle_brackets_removes_unsafe_characters_and_bounds_length()
    {
        Assert.Equal("<script>alert</script> a < b > c", SubmissionReturnedEmail.CleanNote("<script>alert</script> a < b > c"));
        Assert.Equal("ab\ncd", SubmissionReturnedEmail.CleanNote("a\u200Bb\r\nc\u202Ed"));
        Assert.Equal(ResumeApp.Domain.Profiles.ProfileRevision.ReturnNoteMaxLength, SubmissionReturnedEmail.CleanNote(new string('x', 5000)).Length);
        Assert.Equal(string.Empty, SubmissionReturnedEmail.CleanNote(null));
    }

    [Fact]
    public async Task The_note_keeps_angle_brackets_from_entry_to_storage_to_the_email()
    {
        var (f, admin, cohort) = await SetupAsync();
        var rev = await AddRevisionAsync(f, await NewProfileAsync(f, cohort), RevisionStatus.Pending);
        const string note = "קצר מדי <100 מילים> ו-<script>alert(1)</script>";

        Assert.Equal(HttpStatusCode.OK, (await ReturnAsync(admin, rev, note)).StatusCode);
        Assert.Equal(note, (await RevAsync(f, rev)).ReturnNote);
        await ProcessAsync(f);

        Assert.Contains(note, Assert.Single(f.Email.Messages).Message.TextBody);
    }

    // ---- worker-level failures ----

    private static async Task<Guid> InsertRowAsync(AuthApiFactory f, Guid? profile, Guid? revision, string key = "submission-returned")
    {
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var dedup = "t:" + id;
        await DbAsync(f, d => d.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO email_outbox (id, template_key, profile_id, revision_id, deduplication_key, status, attempts, claim_count, next_attempt_at, created_at)
            VALUES ({id}, {key}, {profile}, {revision}, {dedup}, 'Pending', 0, 0, {now}, {now})"));
        return id;
    }

    private static async Task AssertDeadNothingSentAsync(AuthApiFactory f, string category)
    {
        var row = Assert.Single(await OutboxAsync(f));
        Assert.Equal(OutboxStatus.Dead, row.Status);
        Assert.Equal(category, row.LastErrorCategory);
        Assert.Empty(f.Email.Messages);
        Assert.Equal(0, await DbAsync(f, d => d.PersonalTokens.CountAsync()));
    }

    [Fact]
    public async Task A_row_whose_revision_is_still_pending_is_dead_revision_not_returned()
    {
        var (f, _, cohort) = await SetupAsync();
        var profile = await NewProfileAsync(f, cohort);
        var rev = await AddRevisionAsync(f, profile, RevisionStatus.Pending);
        await InsertRowAsync(f, profile, rev);

        await ProcessAsync(f);

        await AssertDeadNothingSentAsync(f, "revision-not-returned");
    }

    [Fact]
    public async Task A_row_whose_revision_does_not_exist_is_dead_revision_missing()
    {
        var (f, _, cohort) = await SetupAsync();
        var profile = await NewProfileAsync(f, cohort);
        await DbAsync(f, d => d.Database.ExecuteSqlInterpolatedAsync($"ALTER TABLE email_outbox DROP CONSTRAINT IF EXISTS \"FK_email_outbox_profile_revisions_revision_id\""));
        await InsertRowAsync(f, profile, Guid.NewGuid());

        await ProcessAsync(f);

        await AssertDeadNothingSentAsync(f, "revision-missing");
    }

    [Fact]
    public async Task A_returned_row_without_a_revision_id_is_dead_unknown_template()
    {
        var (f, _, cohort) = await SetupAsync();
        var profile = await NewProfileAsync(f, cohort);
        await InsertRowAsync(f, profile, null);

        await ProcessAsync(f);

        await AssertDeadNothingSentAsync(f, EmailOutboxProcessor.CategoryUnknownTemplate);
    }

    [Fact]
    public async Task A_returned_revision_with_an_empty_note_is_dead_empty_note()
    {
        var (f, _, cohort) = await SetupAsync();
        var profile = await NewProfileAsync(f, cohort);
        var rev = await AddRevisionAsync(f, profile, RevisionStatus.Returned);
        await InsertRowAsync(f, profile, rev);

        await ProcessAsync(f);

        await AssertDeadNothingSentAsync(f, "empty-note");
    }

    // ---- photo carried through a return ----

    [Fact]
    public async Task A_put_without_a_photo_after_a_return_keeps_the_returned_revisions_photo()
    {
        var (f, admin, cohort) = await SetupAsync();
        var profile = await NewProfileAsync(f, cohort);
        var approvedPhoto = new byte[] { 0xFF, 0xD8, 0xFF, 1, 1, 1 };
        var returnedPhoto = new byte[] { 0xFF, 0xD8, 0xFF, 2, 2, 2, 2 };
        await AddRevisionAsync(f, profile, RevisionStatus.Approved, pointer: true, ago: TimeSpan.FromHours(3));
        var returned = await AddRevisionAsync(f, profile, RevisionStatus.Pending, ago: TimeSpan.FromHours(1));
        await DbAsync(f, d => d.Database.ExecuteSqlInterpolatedAsync($"UPDATE profile_revisions SET photo = {approvedPhoto}, photo_content_type = 'image/jpeg' WHERE profile_id = {profile} AND status = 'Approved'"));
        await DbAsync(f, d => d.Database.ExecuteSqlInterpolatedAsync($"UPDATE profile_revisions SET photo = {returnedPhoto}, photo_content_type = 'image/webp' WHERE id = {returned}"));
        Assert.Equal(HttpStatusCode.OK, (await ReturnAsync(admin, returned, "נא לתקן")).StatusCode);
        var me = await SessionAsync(f, profile);

        Assert.Equal(HttpStatusCode.Accepted, (await PutAsync(me, Edit("מתוקנת"))).StatusCode);

        var pending = await DbAsync(f, d => d.ProfileRevisions.AsNoTracking().SingleAsync(r => r.ProfileId == profile && r.Status == RevisionStatus.Pending));
        Assert.Equal(returnedPhoto, pending.Photo);
        Assert.Equal("image/webp", pending.PhotoContentType);
    }

    // ---- participant side ----

    [Fact]
    public async Task The_participant_sees_the_note_and_edits_the_returned_revision()
    {
        var (f, admin, cohort) = await SetupAsync();
        var profile = await NewProfileAsync(f, cohort);
        await AddRevisionAsync(f, profile, RevisionStatus.Approved, name: "מאושרת", pointer: true, ago: TimeSpan.FromHours(3));
        var returned = await AddRevisionAsync(f, profile, RevisionStatus.Pending, name: "הוחזרה", ago: TimeSpan.FromHours(1));
        Assert.Equal(HttpStatusCode.OK, (await ReturnAsync(admin, returned, "נא לתקן")).StatusCode);
        var me = await SessionAsync(f, profile);

        var own = await Json(await me.Http.GetAsync("/me/profile"));

        Assert.Equal("הוחזרה", own.GetProperty("name").GetString());
        var note = own.GetProperty("returned");
        Assert.Equal("נא לתקן", note.GetProperty("note").GetString());
        Assert.NotEqual(default, note.GetProperty("returnedAt").GetDateTime());
        Assert.True(own.GetProperty("hasPublished").GetBoolean());
        Assert.Equal(JsonValueKind.Null, own.GetProperty("pending").ValueKind);
    }

    [Fact]
    public async Task Resubmitting_creates_a_pending_revision_keeps_the_returned_one_as_history_and_hides_the_note()
    {
        var (f, admin, cohort) = await SetupAsync();
        var profile = await NewProfileAsync(f, cohort);
        var returned = await AddRevisionAsync(f, profile, RevisionStatus.Pending, name: "הוחזרה", ago: TimeSpan.FromHours(1));
        Assert.Equal(HttpStatusCode.OK, (await ReturnAsync(admin, returned, "נא לתקן")).StatusCode);
        var me = await SessionAsync(f, profile);

        var put = await PutAsync(me, Edit("מתוקנת"));

        Assert.Equal(HttpStatusCode.Accepted, put.StatusCode);
        var revisions = await DbAsync(f, d => d.ProfileRevisions.AsNoTracking().Where(r => r.ProfileId == profile).OrderBy(r => r.SubmittedAt).ToListAsync());
        Assert.Equal([RevisionStatus.Returned, RevisionStatus.Pending], revisions.Select(r => r.Status));
        Assert.Equal("נא לתקן", revisions[0].ReturnNote);
        var own = await Json(await me.Http.GetAsync("/me/profile"));
        Assert.Equal(JsonValueKind.Null, own.GetProperty("returned").ValueKind);
        Assert.Equal("מתוקנת", own.GetProperty("name").GetString());
        Assert.NotEqual(JsonValueKind.Null, own.GetProperty("pending").ValueKind);
    }

    [Fact]
    public async Task Without_a_returned_revision_returned_is_null()
    {
        var (f, _, cohort) = await SetupAsync();
        var profile = await NewProfileAsync(f, cohort);
        await AddRevisionAsync(f, profile, RevisionStatus.Pending);
        var me = await SessionAsync(f, profile);

        var own = await Json(await me.Http.GetAsync("/me/profile"));

        Assert.Equal(JsonValueKind.Null, own.GetProperty("returned").ValueKind);
    }

    [Fact]
    public async Task An_older_returned_revision_does_not_override_a_newer_approved_one()
    {
        var (f, _, cohort) = await SetupAsync();
        var profile = await NewProfileAsync(f, cohort);
        var old = await AddRevisionAsync(f, profile, RevisionStatus.Returned, name: "ישנה", ago: TimeSpan.FromHours(5));
        await DbAsync(f, d => d.Database.ExecuteSqlInterpolatedAsync($"UPDATE profile_revisions SET return_note = 'x', returned_at = now() WHERE id = {old}"));
        await AddRevisionAsync(f, profile, RevisionStatus.Approved, name: "חדשה", pointer: true, ago: TimeSpan.FromHours(1));
        var me = await SessionAsync(f, profile);

        var own = await Json(await me.Http.GetAsync("/me/profile"));

        Assert.Equal("חדשה", own.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, own.GetProperty("returned").ValueKind);
    }

    [Fact]
    public async Task A_pending_email_change_on_the_returned_revision_still_prefills_the_form()
    {
        var (f, admin, cohort) = await SetupAsync();
        var profile = await NewProfileAsync(f, cohort, "dana@example.com");
        var rev = await AddRevisionAsync(f, profile, RevisionStatus.Pending, pendingEmail: "new@example.com", pendingPhone: "0509999999");
        Assert.Equal(HttpStatusCode.OK, (await ReturnAsync(admin, rev, "נא לתקן")).StatusCode);
        var me = await SessionAsync(f, profile);

        var own = await Json(await me.Http.GetAsync("/me/profile"));

        Assert.Equal("new@example.com", own.GetProperty("email").GetString());
        Assert.Equal("0509999999", own.GetProperty("phone").GetString());
        Assert.Equal("dana@example.com", own.GetProperty("currentEmail").GetString());
        Assert.True(own.GetProperty("pendingContactChange").GetBoolean());

        Assert.Equal(HttpStatusCode.Accepted, (await PutAsync(me, Edit("דנה", "new@example.com", "0509999999"))).StatusCode);
        var next = await DbAsync(f, d => d.ProfileRevisions.AsNoTracking().SingleAsync(r => r.ProfileId == profile && r.Status == RevisionStatus.Pending));
        Assert.Equal("new@example.com", next.PendingEmail);
        Assert.Equal("0509999999", next.PendingPhone);
    }

    // ---- public side and queue ----

    [Fact]
    public async Task A_returned_revision_never_changes_the_public_site_and_the_approved_one_stays_visible()
    {
        var (f, admin, cohort) = await SetupAsync();
        var profile = await NewProfileAsync(f, cohort);
        await AddRevisionAsync(f, profile, RevisionStatus.Approved, name: "מפורסמת", pointer: true, ago: TimeSpan.FromHours(3));
        var pending = await AddRevisionAsync(f, profile, RevisionStatus.Pending, name: "הוחזרה", ago: TimeSpan.FromHours(1));
        Assert.Equal(HttpStatusCode.OK, (await ReturnAsync(admin, pending, "נא לתקן")).StatusCode);

        var gallery = await (await f.NewClient().Http.GetAsync("/profiles")).Content.ReadAsStringAsync();
        var detail = await (await f.NewClient().Http.GetAsync($"/profiles/{profile}")).Content.ReadAsStringAsync();

        Assert.Contains("מפורסמת", gallery);
        Assert.DoesNotContain("הוחזרה", gallery);
        Assert.Contains("מפורסמת", detail);
        Assert.DoesNotContain("הוחזרה", detail);
        Assert.DoesNotContain("נא לתקן", gallery + detail);
        Assert.NotNull((await DbAsync(f, d => d.Profiles.AsNoTracking().SingleAsync(p => p.Id == profile))).ApprovedRevisionId);
    }

    [Fact]
    public async Task A_first_submission_that_was_returned_is_not_public()
    {
        var (f, admin, cohort) = await SetupAsync();
        var profile = await NewProfileAsync(f, cohort);
        var rev = await AddRevisionAsync(f, profile, RevisionStatus.Pending, name: "ראשונה");
        Assert.Equal(HttpStatusCode.OK, (await ReturnAsync(admin, rev, "נא לתקן")).StatusCode);

        var gallery = await f.NewClient().Http.GetAsync("/profiles");
        Assert.DoesNotContain("ראשונה", await gallery.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await f.NewClient().Http.GetAsync($"/profiles/{profile}")).StatusCode);
    }

    [Fact]
    public async Task A_returned_revision_leaves_the_review_queue_and_its_details()
    {
        var (f, admin, cohort) = await SetupAsync();
        var profile = await NewProfileAsync(f, cohort);
        var rev = await AddRevisionAsync(f, profile, RevisionStatus.Pending);
        var before = await Json(await admin.Http.GetAsync("/admin/submissions"));
        Assert.Equal(1, before.GetProperty("total").GetInt32());

        Assert.Equal(HttpStatusCode.OK, (await ReturnAsync(admin, rev, "נא לתקן")).StatusCode);

        var after = await Json(await admin.Http.GetAsync("/admin/submissions"));
        Assert.Equal(0, after.GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Http.GetAsync($"/admin/submissions/{rev}")).StatusCode);
    }
}
