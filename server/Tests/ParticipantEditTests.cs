using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResumeApp.Domain.Email;
using ResumeApp.Domain.Profiles;
using ResumeApp.Features.Email;
using ResumeApp.Infrastructure;
using SkiaSharp;

namespace ResumeApp.Tests;

/// <summary>Story 2.2 (session via Google since 6.4): participant edits as new pending revisions.</summary>
[Collection("env")]
public class ParticipantEditTests(PostgresFixture pg) : IDisposable
{
    private const string NewPassword = "Brand-New-Pass-42#Lk";
    private const string CookieName = "__Host-resume-participant";
    private readonly List<AuthApiFactory> _factories = [];

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
    }

    private sealed record Setup(AuthApiFactory Factory, ApiClient Admin);

    private async Task<Setup> NewSetupAsync(IReadOnlyDictionary<string, string>? limits = null)
    {
        var factory = await AuthApiFactory.CreateAsync(pg, limits: limits);
        _factories.Add(factory);
        var admin = factory.NewClient();
        await admin.EnrollAsync(AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword, NewPassword);
        return new(factory, admin);
    }

    private static Dictionary<string, string> Fields(string email = "dana@example.com", string phone = "050-123 4567", string name = "דנה כהן") => new()
    {
        ["name"] = name,
        ["productName"] = "מוצר מגניב",
        ["productUrl"] = "https://example.com/app",
        ["email"] = email,
        ["phone"] = phone,
        ["description"] = "תיאור מקורי",
    };

    private static byte[] Image(int width = 64, int height = 48)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    /// <summary>The anonymous-principal token for /me/*, whatever cookies the client holds.</summary>
    private static async Task<string> Csrf(ApiClient c)
    {
        var response = await c.Http.GetAsync("/profiles/csrf");
        response.EnsureSuccessStatusCode();
        return (await Json(response)).GetProperty("token").GetString()!;
    }

    private static async Task<HttpResponseMessage> Multipart(ApiClient c, HttpMethod method, string path, Dictionary<string, string> fields, byte[]? photo = null, string? csrf = null)
    {
        var content = new MultipartFormDataContent();
        foreach (var (k, v) in fields) content.Add(new StringContent(v, Encoding.UTF8), k);
        if (photo is not null)
        {
            var file = new ByteArrayContent(photo);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            content.Add(file, "photo", "me.png");
        }
        var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Add("Origin", ApiClient.Origin);
        request.Headers.Add("X-CSRF-TOKEN", csrf ?? await Csrf(c));
        return await c.Http.SendAsync(request);
    }

    private static async Task<Guid> PublishedProfileAsync(Setup s, string email = "dana@example.com", string name = "דנה כהן", byte[]? photo = null)
        => await SubmittedProfileAsync(s, email, name, photo, approve: true);

    /// <summary>Seeds a profile directly (optionally with a photo processed like an upload), approved or pending, and returns its id.</summary>
    private static async Task<Guid> SubmittedProfileAsync(Setup s, string email = "dana@example.com", string name = "דנה כהן", byte[]? photo = null, bool approve = false)
    {
        byte[]? stored = null;
        string? contentType = null;
        if (photo is not null)
        {
            await using var scope = s.Factory.Services.CreateAsyncScope();
            var processed = await scope.ServiceProvider.GetRequiredService<ResumeApp.Features.Profiles.IPhotoProcessor>().ProcessAsync(photo, CancellationToken.None);
            Assert.Equal(ResumeApp.Features.Profiles.PhotoOutcome.Ok, processed.Outcome);
            (stored, contentType) = (processed.Bytes, processed.ContentType);
        }
        var extras = new ProfileExtras("תיאור מקורי", null, null, null, null, null, null, ProfileAvatars.Default, stored, contentType);
        return (await ParticipantSeeding.SeedProfileAsync(s.Factory, email: email, phone: "050-123 4567", name: name, productName: "מוצר מגניב",
            status: approve ? RevisionStatus.Approved : RevisionStatus.Pending, extras: extras)).ProfileId;
    }

    /// <summary>A client that holds a Google participant session for the profile (its Google account is the stored email).</summary>
    private static async Task<ApiClient> SessionAsync(AuthApiFactory f, Guid profileId)
    {
        var email = await ParticipantSeeding.DbAsync(f, d => d.ParticipantPrivates.AsNoTracking().Where(p => p.ProfileId == profileId).Select(p => p.Email).SingleAsync());
        var client = f.NewClient();
        await client.SignInAsync(email);
        return client;
    }

    private static async Task<HttpResponseMessage> PutJson(ApiClient c, object body, string? csrf = null, string? origin = ApiClient.Origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/me/profile") { Content = JsonContent.Create(body) };
        if (origin is not null) request.Headers.Add("Origin", origin);
        var token = csrf ?? await Csrf(c);
        request.Headers.Add("X-CSRF-TOKEN", token);
        return await c.Http.SendAsync(request);
    }

    private static Dictionary<string, object?> Edit(string name = "שם חדש", string email = "dana@example.com", string phone = "0501234567") => new()
    {
        ["name"] = name, ["productName"] = "מוצר מגניב", ["productUrl"] = "https://example.com/app", ["email"] = email, ["phone"] = phone,
        ["description"] = "תיאור חדש",
    };

    private static async Task<T> Db<T>(AuthApiFactory factory, Func<AppDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static Task Sql(AuthApiFactory f, FormattableString sql) => Db(f, async d => await d.Database.ExecuteSqlInterpolatedAsync(sql));

    private static Task<List<ProfileRevision>> Revisions(AuthApiFactory f, Guid profileId)
        => Db(f, d => d.ProfileRevisions.AsNoTracking().Where(r => r.ProfileId == profileId).OrderBy(r => r.SubmittedAt).ToListAsync());

    /// <summary>The problem body without the per-request trace id.</summary>
    private static string Shape(JsonElement problem)
        => $"{problem.GetProperty("status")}|{problem.GetProperty("title")}|{problem.GetProperty("code")}";

    private static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

        // ---- reading ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Get_returns_the_sessions_own_profile_in_full_with_status_flags()
    {
        var s = await NewSetupAsync();
        var dana = await PublishedProfileAsync(s, "dana@example.com", "דנה");
        await PublishedProfileAsync(s, "ron@example.com", "רון");
        var client = await SessionAsync(s.Factory, dana);

        var response = await client.Http.GetAsync("/me/profile");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        var json = await Json(response);
        Assert.Equal("דנה", json.GetProperty("name").GetString());
        Assert.Equal("dana@example.com", json.GetProperty("email").GetString());
        Assert.Equal("0501234567", json.GetProperty("phone").GetString());
        Assert.Equal("תיאור מקורי", json.GetProperty("description").GetString());
        Assert.Equal("https://example.com/app", json.GetProperty("productUrl").GetString());
        Assert.False(json.GetProperty("hasPhoto").GetBoolean());
        Assert.True(json.GetProperty("hasPublished").GetBoolean());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("pending").ValueKind);
        Assert.False(json.GetProperty("pendingContactChange").GetBoolean());
        Assert.DoesNotContain("ron@example.com", await response.Content.ReadAsStringAsync());
    }

    // ---- editing ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Edit_of_a_published_profile_creates_a_pending_revision_and_leaves_the_public_site_unchanged()
    {
        var s = await NewSetupAsync();
        var profileId = await PublishedProfileAsync(s, name: "דנה");
        var client = await SessionAsync(s.Factory, profileId);
        var before = await Revisions(s.Factory, profileId);
        var approvedId = before.Single().Id;

        var response = await PutJson(client, Edit("שם חדש"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("pending", (await Json(response)).GetProperty("status").GetString());
        var revisions = await Revisions(s.Factory, profileId);
        Assert.Equal(2, revisions.Count);
        Assert.Equal(RevisionStatus.Approved, revisions.Single(r => r.Id == approvedId).Status);
        Assert.Equal("דנה", revisions.Single(r => r.Id == approvedId).Name);
        var pending = revisions.Single(r => r.Status == RevisionStatus.Pending);
        Assert.Equal("שם חדש", pending.Name);
        Assert.Equal("תיאור חדש", pending.Description);
        var profile = await Db(s.Factory, d => d.Profiles.AsNoTracking().SingleAsync(p => p.Id == profileId));
        Assert.Equal(approvedId, profile.ApprovedRevisionId);
        Assert.Equal(ProfileVisibility.Active, profile.Visibility);

        var anonymous = s.Factory.NewClient().Http;
        var gallery = await anonymous.GetStringAsync("/profiles");
        Assert.Contains("דנה", gallery);
        Assert.DoesNotContain("שם חדש", gallery);
        var detail = await anonymous.GetStringAsync($"/profiles/{profileId:D}");
        Assert.Contains("דנה", detail);
        Assert.DoesNotContain("שם חדש", detail);

        var own = await Json(await client.Http.GetAsync("/me/profile"));
        Assert.Equal("שם חדש", own.GetProperty("name").GetString()); // the owner edits the latest pending version
        Assert.NotEqual(JsonValueKind.Null, own.GetProperty("pending").ValueKind);
        Assert.True(own.GetProperty("hasPublished").GetBoolean());
    }

    [Fact]
    public async Task A_second_edit_supersedes_the_first_pending_revision()
    {
        var s = await NewSetupAsync();
        var profileId = await PublishedProfileAsync(s);
        var client = await SessionAsync(s.Factory, profileId);

        Assert.Equal(HttpStatusCode.Accepted, (await PutJson(client, Edit("ראשון"))).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await PutJson(client, Edit("שני"))).StatusCode);

        var revisions = await Revisions(s.Factory, profileId);
        Assert.Equal(3, revisions.Count);
        Assert.Equal(RevisionStatus.Superseded, revisions.Single(r => r.Name == "ראשון").Status);
        Assert.Equal(RevisionStatus.Pending, revisions.Single(r => r.Name == "שני").Status);
        Assert.Single(revisions, r => r.Status == RevisionStatus.Pending);
        Assert.Single(revisions, r => r.Status == RevisionStatus.Approved);
    }

    [Fact]
    public async Task Concurrent_edits_leave_exactly_one_pending_revision()
    {
        var s = await NewSetupAsync();
        var profileId = await PublishedProfileAsync(s);
        var client = await SessionAsync(s.Factory, profileId);
        var csrf = await Csrf(client);

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => PutJson(client, Edit($"גרסה {i}"), csrf)));

        Assert.All(results, r => Assert.Equal(HttpStatusCode.Accepted, r.StatusCode));
        var revisions = await Revisions(s.Factory, profileId);
        Assert.Equal(7, revisions.Count);
        Assert.Single(revisions, r => r.Status == RevisionStatus.Pending);
        Assert.Equal(5, revisions.Count(r => r.Status == RevisionStatus.Superseded));
        Assert.Single(revisions, r => r.Status == RevisionStatus.Approved);
    }

    [Fact]
    public async Task Photo_is_kept_by_default_removed_on_request_and_replaced_by_a_new_upload()
    {
        var s = await NewSetupAsync();
        var profileId = await PublishedProfileAsync(s, photo: Image());
        var client = await SessionAsync(s.Factory, profileId);
        var original = (await Revisions(s.Factory, profileId)).Single().Photo!;

        // kept: copied into the new revision
        Assert.Equal(HttpStatusCode.Accepted, (await PutJson(client, Edit())).StatusCode);
        var kept = (await Revisions(s.Factory, profileId)).Single(r => r.Status == RevisionStatus.Pending);
        Assert.Equal(original, kept.Photo);
        Assert.Equal("image/jpeg", kept.PhotoContentType, ignoreCase: true);
        Assert.True((await Json(await client.Http.GetAsync("/me/profile"))).GetProperty("hasPhoto").GetBoolean());

        // replaced: a new upload wins
        var replaced = await Multipart(client, HttpMethod.Put, "/me/profile", Fields(), Image(300, 200));
        Assert.Equal(HttpStatusCode.Accepted, replaced.StatusCode);
        var replacedRev = (await Revisions(s.Factory, profileId)).Single(r => r.Status == RevisionStatus.Pending);
        Assert.NotNull(replacedRev.Photo);
        Assert.NotEqual(original, replacedRev.Photo);

        // removed
        var body = new Dictionary<string, object?>(Edit()) { ["removePhoto"] = true };
        Assert.Equal(HttpStatusCode.Accepted, (await PutJson(client, body)).StatusCode);
        var removed = (await Revisions(s.Factory, profileId)).Single(r => r.Status == RevisionStatus.Pending);
        Assert.Null(removed.Photo);
        Assert.False((await Json(await client.Http.GetAsync("/me/profile"))).GetProperty("hasPhoto").GetBoolean());

        // the approved revision still has its photo
        var approved = (await Revisions(s.Factory, profileId)).Single(r => r.Status == RevisionStatus.Approved);
        Assert.Equal(original, approved.Photo);
    }

    [Fact]
    public async Task Changed_email_or_phone_is_only_a_pending_request()
    {
        var s = await NewSetupAsync();
        var profileId = await PublishedProfileAsync(s, "dana@example.com");
        var client = await SessionAsync(s.Factory, profileId);

        var response = await PutJson(client, Edit(email: "  New@Example.COM ", phone: "052-999 8877"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var pending = (await Revisions(s.Factory, profileId)).Single(r => r.Status == RevisionStatus.Pending);
        Assert.Equal("new@example.com", pending.PendingEmail);
        Assert.Equal("0529998877", pending.PendingPhone);
        var priv = await Db(s.Factory, d => d.ParticipantPrivates.AsNoTracking().SingleAsync(p => p.ProfileId == profileId));
        Assert.Equal("dana@example.com", priv.Email);
        Assert.Equal("0501234567", priv.Phone);

        var own = await Json(await client.Http.GetAsync("/me/profile"));
        Assert.True(own.GetProperty("pendingContactChange").GetBoolean());
        Assert.Equal("dana@example.com", own.GetProperty("currentEmail").GetString());
        Assert.Equal("new@example.com", own.GetProperty("pendingEmail").GetString());
    }

    [Fact]
    public async Task Unchanged_contact_details_are_not_a_change_and_the_new_email_is_not_checked_for_uniqueness()
    {
        var s = await NewSetupAsync();
        var profileId = await PublishedProfileAsync(s, "dana@example.com");
        await PublishedProfileAsync(s, "taken@example.com", "אחרת");
        var client = await SessionAsync(s.Factory, profileId);

        Assert.Equal(HttpStatusCode.Accepted, (await PutJson(client, Edit(email: " DANA@example.com "))).StatusCode);
        var same = (await Revisions(s.Factory, profileId)).Single(r => r.Status == RevisionStatus.Pending);
        Assert.Null(same.PendingEmail);
        Assert.Null(same.PendingPhone);
        Assert.False((await Json(await client.Http.GetAsync("/me/profile"))).GetProperty("pendingContactChange").GetBoolean());

        // an address that belongs to someone else is accepted as a request too (uniqueness is checked on approval, nothing revealed now)
        Assert.Equal(HttpStatusCode.Accepted, (await PutJson(client, Edit(email: "taken@example.com"))).StatusCode);
        Assert.Equal("taken@example.com", (await Revisions(s.Factory, profileId)).Single(r => r.Status == RevisionStatus.Pending).PendingEmail);
    }

    [Fact]
    public async Task Invalid_edits_get_400_and_create_nothing()
    {
        var s = await NewSetupAsync();
        var profileId = await PublishedProfileAsync(s);
        var client = await SessionAsync(s.Factory, profileId);

        var missingName = new Dictionary<string, object?>(Edit()) { ["name"] = "" };
        var badUrl = new Dictionary<string, object?>(Edit()) { ["productUrl"] = "javascript:alert(1)" };
        var badCv = new Dictionary<string, object?>(Edit()) { ["cv"] = "[x](javascript:alert(1))" };
        var badEmail = new Dictionary<string, object?>(Edit()) { ["email"] = "nope" };
        var longDescription = new Dictionary<string, object?>(Edit()) { ["description"] = new string('א', 121) };
        foreach (var body in new[] { missingName, badUrl, badCv, badEmail, longDescription })
        {
            var response = await PutJson(client, body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }

        var notImage = await Multipart(client, HttpMethod.Put, "/me/profile", Fields(), Encoding.UTF8.GetBytes("<svg onload=alert(1)/>"));
        Assert.Equal(HttpStatusCode.BadRequest, notImage.StatusCode);

        Assert.Single(await Revisions(s.Factory, profileId));
    }

    [Fact]
    public async Task Cv_and_text_get_the_same_cleaning_as_a_first_submission()
    {
        var s = await NewSetupAsync();
        var profileId = await PublishedProfileAsync(s);
        var client = await SessionAsync(s.Factory, profileId);
        var body = new Dictionary<string, object?>(Edit()) { ["cv"] = "## ניסיון\r\n\r\n- [אתר](https://example.com)\r\n\r\n<b>כבד</b>" };

        var response = await PutJson(client, body);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var cv = (await Revisions(s.Factory, profileId)).Single(r => r.Status == RevisionStatus.Pending).Cv;
        Assert.NotNull(cv);
        Assert.DoesNotContain("<b>", cv);
        Assert.DoesNotContain("\r", cv);
        Assert.Contains("[אתר](https://example.com)", cv);
    }

    [Fact]
    public async Task Edits_need_anti_forgery_and_same_origin()
    {
        var s = await NewSetupAsync();
        var profileId = await PublishedProfileAsync(s);
        var client = await SessionAsync(s.Factory, profileId);

        var noToken = new HttpRequestMessage(HttpMethod.Put, "/me/profile") { Content = JsonContent.Create(Edit()) };
        noToken.Headers.Add("Origin", ApiClient.Origin);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.Http.SendAsync(noToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PutJson(client, Edit(), origin: "https://evil.example")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PutJson(client, Edit(), origin: null)).StatusCode);

        Assert.Single(await Revisions(s.Factory, profileId));
    }

    [Fact]
    public async Task A_session_whose_profile_was_removed_gets_401_and_edits_nothing()
    {
        var s = await NewSetupAsync();
        var profileId = await PublishedProfileAsync(s);
        var client = await SessionAsync(s.Factory, profileId);
        await Sql(s.Factory, $"UPDATE profiles SET visibility = 'Removed'");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.Http.GetAsync("/me/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PutJson(client, Edit())).StatusCode);
        Assert.Single(await Revisions(s.Factory, profileId));
    }

    // ---- csrf, identity separation and limits ------------------------------------------------------------------------

    private static async Task<ApiClient> ClientWithAdminAndSessionAsync(Setup s, Guid profileId)
    {
        var both = s.Admin; // already holds the admin session
        await both.SignInAsync(await ParticipantSeeding.DbAsync(s.Factory, d => d.ParticipantPrivates.AsNoTracking().Where(p => p.ProfileId == profileId).Select(p => p.Email).SingleAsync()));
        return both;
    }

    [Fact]
    public async Task A_browser_holding_both_admin_and_participant_cookies_can_edit_with_a_profiles_csrf_token()
    {
        var s = await NewSetupAsync();
        var profileId = await PublishedProfileAsync(s);
        var both = await ClientWithAdminAndSessionAsync(s, profileId);

        // a token from /auth/csrf would be bound to the admin identity here; the one from /profiles/csrf is anonymous-bound
        var response = await PutJson(both, Edit("עם שני סשנים"));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Contains(await Revisions(s.Factory, profileId), r => r.Status == RevisionStatus.Pending && r.Name == "עם שני סשנים");
        Assert.Equal(HttpStatusCode.OK, (await both.Http.GetAsync("/admin/submissions")).StatusCode); // the admin session is unaffected
        Assert.Equal(HttpStatusCode.OK, (await both.Http.GetAsync("/me/profile")).StatusCode);
    }

    [Fact]
    public async Task Exact_statuses_for_identity_separation_with_valid_csrf_and_origin()
    {
        var s = await NewSetupAsync();
        var profileId = await PublishedProfileAsync(s);

        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Factory.NewClient().Http.GetAsync("/me/profile")).StatusCode);
        // admin cookie only: authorization is the only thing that can fail (a valid token and Origin are sent)
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Admin.Http.GetAsync("/me/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PutJson(s.Admin, Edit())).StatusCode);

        var participant = await SessionAsync(s.Factory, profileId);
        Assert.Equal(HttpStatusCode.Unauthorized, (await participant.Http.GetAsync("/admin/submissions")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await participant.Http.GetAsync("/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Put_enforces_body_limits_and_content_type()
    {
        var s = await NewSetupAsync();
        var profileId = await PublishedProfileAsync(s);
        var client = await SessionAsync(s.Factory, profileId);

        var bigJson = new Dictionary<string, object?>(Edit()) { ["introText"] = new string('a', 170 * 1024) };
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await PutJson(client, bigJson)).StatusCode);

        var bigMultipart = await Multipart(client, HttpMethod.Put, "/me/profile", Fields(), new byte[600 * 1024]);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, bigMultipart.StatusCode);

        var wrongType = new HttpRequestMessage(HttpMethod.Put, "/me/profile") { Content = new StringContent("name=x", Encoding.UTF8, "text/plain") };
        wrongType.Headers.Add("Origin", ApiClient.Origin);
        wrongType.Headers.Add("X-CSRF-TOKEN", await Csrf(client));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await client.Http.SendAsync(wrongType)).StatusCode);

        Assert.Single(await Revisions(s.Factory, profileId));
    }

    [Fact]
    public async Task Two_sessions_each_read_only_their_own_data()
    {
        var s = await NewSetupAsync();
        var dana = await PublishedProfileAsync(s, "dana@example.com", "דנה");
        var ron = await PublishedProfileAsync(s, "ron@example.com", "רון");
        var danaClient = await SessionAsync(s.Factory, dana);
        var ronClient = await SessionAsync(s.Factory, ron);

        var d = await danaClient.Http.GetStringAsync("/me/profile");
        var r = await ronClient.Http.GetStringAsync("/me/profile");

        Assert.Contains("dana@example.com", d);
        Assert.DoesNotContain("ron@example.com", d);
        Assert.DoesNotContain("רון", d);
        Assert.Contains("ron@example.com", r);
        Assert.DoesNotContain("dana@example.com", r);

        Assert.Equal(HttpStatusCode.Accepted, (await PutJson(danaClient, Edit("רק של דנה"))).StatusCode);
        Assert.Single(await Revisions(s.Factory, ron));
    }

    // ---- photo source selection and removePhoto -----------------------------------------------------------------------

    [Fact]
    public async Task Plain_edit_after_removing_or_replacing_the_photo_keeps_that_state_not_the_approved_photo()
    {
        var s = await NewSetupAsync();
        var profileId = await PublishedProfileAsync(s, photo: Image());
        var client = await SessionAsync(s.Factory, profileId);
        var approvedPhoto = (await Revisions(s.Factory, profileId)).Single().Photo!;

        var remove = new Dictionary<string, object?>(Edit()) { ["removePhoto"] = true };
        Assert.Equal(HttpStatusCode.Accepted, (await PutJson(client, remove)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await PutJson(client, Edit("אחרי הסרה"))).StatusCode);
        var afterRemove = (await Revisions(s.Factory, profileId)).Single(r => r.Status == RevisionStatus.Pending);
        Assert.Equal("אחרי הסרה", afterRemove.Name);
        Assert.Null(afterRemove.Photo);
        Assert.False((await Json(await client.Http.GetAsync("/me/profile"))).GetProperty("hasPhoto").GetBoolean());

        Assert.Equal(HttpStatusCode.Accepted, (await Multipart(client, HttpMethod.Put, "/me/profile", Fields(), Image(300, 200))).StatusCode);
        var replaced = (await Revisions(s.Factory, profileId)).Single(r => r.Status == RevisionStatus.Pending).Photo!;
        Assert.NotEqual(approvedPhoto, replaced);
        Assert.Equal(HttpStatusCode.Accepted, (await PutJson(client, Edit("אחרי החלפה"))).StatusCode);
        var afterReplace = (await Revisions(s.Factory, profileId)).Single(r => r.Status == RevisionStatus.Pending);
        Assert.Equal("אחרי החלפה", afterReplace.Name);
        Assert.Equal(replaced, afterReplace.Photo);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("1")]
    public async Task Multipart_remove_photo_without_a_photo_part_removes_the_photo(string flag)
    {
        var s = await NewSetupAsync();
        var profileId = await PublishedProfileAsync(s, photo: Image());
        var client = await SessionAsync(s.Factory, profileId);
        var fields = Fields();
        fields["removePhoto"] = flag;

        Assert.Equal(HttpStatusCode.Accepted, (await Multipart(client, HttpMethod.Put, "/me/profile", fields)).StatusCode);

        Assert.Null((await Revisions(s.Factory, profileId)).Single(r => r.Status == RevisionStatus.Pending).Photo);
    }

    // ---- pending contact change semantics ------------------------------------------------------------------------------

    [Fact]
    public async Task Pending_contact_change_is_prefilled_kept_cancelled_or_replaced_explicitly()
    {
        var s = await NewSetupAsync();
        var profileId = await PublishedProfileAsync(s, "dana@example.com");
        var client = await SessionAsync(s.Factory, profileId);
        Assert.Equal(HttpStatusCode.Accepted, (await PutJson(client, Edit(email: "new@example.com", phone: "0529998877"))).StatusCode);

        var own = await Json(await client.Http.GetAsync("/me/profile"));
        Assert.Equal("new@example.com", own.GetProperty("email").GetString()); // the value to prefill
        Assert.Equal("0529998877", own.GetProperty("phone").GetString());
        Assert.Equal("new@example.com", own.GetProperty("pendingEmail").GetString());
        Assert.Equal("0529998877", own.GetProperty("pendingPhone").GetString());
        Assert.Equal("dana@example.com", own.GetProperty("currentEmail").GetString());
        Assert.Equal("0501234567", own.GetProperty("currentPhone").GetString());

        // keep: an unrelated edit that submits the prefilled values keeps the request
        Assert.Equal(HttpStatusCode.Accepted, (await PutJson(client, Edit("שם אחר", "new@example.com", "0529998877"))).StatusCode);
        var kept = (await Revisions(s.Factory, profileId)).Single(r => r.Status == RevisionStatus.Pending);
        Assert.Equal("new@example.com", kept.PendingEmail);
        Assert.Equal("0529998877", kept.PendingPhone);

        // replace: another value replaces the request
        Assert.Equal(HttpStatusCode.Accepted, (await PutJson(client, Edit(email: "third@example.com", phone: "0529998877"))).StatusCode);
        var replaced = (await Revisions(s.Factory, profileId)).Single(r => r.Status == RevisionStatus.Pending);
        Assert.Equal("third@example.com", replaced.PendingEmail);
        Assert.Equal("0529998877", replaced.PendingPhone);

        // cancel: submitting the current values cancels the request explicitly
        Assert.Equal(HttpStatusCode.Accepted, (await PutJson(client, Edit(email: "dana@example.com", phone: "0501234567"))).StatusCode);
        var cancelled = (await Revisions(s.Factory, profileId)).Single(r => r.Status == RevisionStatus.Pending);
        Assert.Null(cancelled.PendingEmail);
        Assert.Null(cancelled.PendingPhone);
        var after = await Json(await client.Http.GetAsync("/me/profile"));
        Assert.False(after.GetProperty("pendingContactChange").GetBoolean());
        Assert.Equal("dana@example.com", after.GetProperty("email").GetString());
        Assert.Equal(JsonValueKind.Null, after.GetProperty("pendingEmail").ValueKind);
    }

    // ---- never-approved profile and broken sessions ---------------------------------------------------------------------

    [Fact]
    public async Task A_never_approved_profile_can_be_edited_and_stays_unpublished()
    {
        var s = await NewSetupAsync();
        var profileId = await SubmittedProfileAsync(s, "dana@example.com");
        var client = await SessionAsync(s.Factory, profileId);

        var own = await Json(await client.Http.GetAsync("/me/profile"));
        Assert.False(own.GetProperty("hasPublished").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, own.GetProperty("pending").ValueKind);

        Assert.Equal(HttpStatusCode.Accepted, (await PutJson(client, Edit("עריכה לפני אישור"))).StatusCode);

        var revisions = await Revisions(s.Factory, profileId);
        Assert.Equal(2, revisions.Count);
        Assert.Single(revisions, r => r.Status == RevisionStatus.Pending && r.Name == "עריכה לפני אישור");
        Assert.Single(revisions, r => r.Status == RevisionStatus.Superseded);
        Assert.DoesNotContain(revisions, r => r.Status == RevisionStatus.Approved);
        Assert.Null((await Db(s.Factory, d => d.Profiles.AsNoTracking().SingleAsync(p => p.Id == profileId))).ApprovedRevisionId);
        Assert.False((await Json(await client.Http.GetAsync("/me/profile"))).GetProperty("hasPublished").GetBoolean());
    }

    [Fact]
    public async Task An_active_profile_without_a_private_row_gets_401_session_invalid_not_500()
    {
        var s = await NewSetupAsync();
        var profileId = await PublishedProfileAsync(s);
        var client = await SessionAsync(s.Factory, profileId);
        var other = await SessionAsync(s.Factory, profileId); // the first dead-session answer drops that client's cookie
        await Sql(s.Factory, $"DELETE FROM participant_private WHERE profile_id = {profileId}");

        var get = await client.Http.GetAsync("/me/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
        Assert.Equal("session-invalid", (await Json(get)).GetProperty("code").GetString());
        var put = await PutJson(other, Edit());
        Assert.Equal(HttpStatusCode.Unauthorized, put.StatusCode);
        Assert.Equal("session-invalid", (await Json(put)).GetProperty("code").GetString());
        Assert.Single(await Revisions(s.Factory, profileId));
    }

    [Fact]
    public async Task A_dead_session_gets_its_cookie_dropped()
    {
        var s = await NewSetupAsync();
        var profileId = await PublishedProfileAsync(s);
        var client = await SessionAsync(s.Factory, profileId);
        await Sql(s.Factory, $"UPDATE profiles SET visibility = 'Removed'");

        var response = await client.Http.GetAsync("/me/profile");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith(CookieName + "=", StringComparison.Ordinal));
        Assert.StartsWith(CookieName + "=;", cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.Http.GetAsync("/me/profile")).StatusCode);
    }

}
