using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ResumeApp.Domain.Contacts;
using ResumeApp.Domain.Email;
using ResumeApp.Domain.Profiles;
using Microsoft.Extensions.Configuration;
using ResumeApp.Features.Email;
using ResumeApp.Infrastructure;

namespace ResumeApp.Tests;

/// <summary>Story 3.2: the manager notification (outbox row and mail) and the admin queue of verified contact requests.</summary>
[Collection("env")]
public class ContactRequestQueueTests(PostgresFixture pg) : IDisposable
{
    private const string NewPassword = "Brand-New-Pass-42#Lk";
    private const string Visitor = "visitor@example.com";
    private const string VisitorName = "יוסי המבקר";
    private const string Reason = "אשמח לשוחח על שיתוף פעולה בפרויקט";
    private const string ParticipantEmail = "dana@example.com";
    private const string ParticipantPhone = "050-123 4567";
    private readonly List<AuthApiFactory> _factories = [];

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
    }

    private sealed record Setup(AuthApiFactory Factory, ApiClient Admin, Guid ProfileId);

    private async Task<Setup> NewSetupAsync(Action<IServiceCollection>? configure = null)
    {
        var factory = await AuthApiFactory.CreateAsync(pg, configureServices: configure);
        _factories.Add(factory);
        var admin = factory.NewClient();
        await admin.EnrollAsync(AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword, NewPassword);
        var cohort = await (await admin.PostWithCsrfAsync("/admin/cohorts", new { name = "מחזור" })).Content.ReadFromJsonAsync<JsonElement>();
        var response = await factory.NewClient().PostWithCsrfAsync($"/invites/{cohort.GetProperty("inviteToken").GetString()}/profiles", new
        {
            name = "דנה כהן",
            productName = "מוצר",
            productUrl = "https://example.com/app",
            email = ParticipantEmail,
            phone = ParticipantPhone,
        });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var profileId = await Db(factory, d => d.ParticipantPrivates.AsNoTracking().Select(p => p.ProfileId).SingleAsync());
        return new(factory, admin, profileId);
    }

    private static async Task<T> Db<T>(AuthApiFactory factory, Func<AppDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static Task Sql(AuthApiFactory f, FormattableString sql) => Db(f, async d => await d.Database.ExecuteSqlInterpolatedAsync(sql));

    private static Task Raw(AuthApiFactory f, string sql) => Db(f, async d => await d.Database.ExecuteSqlRawAsync(sql));

    private static Task<List<EmailOutboxEntry>> Outbox(AuthApiFactory f) => Db(f, d => d.EmailOutbox.AsNoTracking().OrderBy(e => e.CreatedAt).ToListAsync());

    private static Task<List<EmailOutboxEntry>> Notifications(AuthApiFactory f)
        => Db(f, d => d.EmailOutbox.AsNoTracking().Where(e => e.TemplateKey == "contact-request-notification").ToListAsync());

    private static async Task<int> ProcessAsync(AuthApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<EmailOutboxProcessor>().ProcessBatchAsync(CancellationToken.None);
    }

    private static Task<HttpResponseMessage> Submit(AuthApiFactory f, Guid profileId, string email = Visitor)
        => f.NewClient().PostWithCsrfAsync($"/profiles/{profileId:D}/contact-requests", new { name = VisitorName, email, reason = Reason });

    private static Task<HttpResponseMessage> Verify(AuthApiFactory f, string token) => f.NewClient().PostWithCsrfAsync("/contact-requests/verify", new { token });

    /// <summary>Submits, sends the verification mail through the worker and returns the raw token.</summary>
    private static async Task<string> SubmitAndMailAsync(Setup s, string email = Visitor)
    {
        Assert.Equal(HttpStatusCode.Accepted, (await Submit(s.Factory, s.ProfileId, email)).StatusCode);
        await ProcessAsync(s.Factory);
        var mail = s.Factory.Email.Messages.Last(m => m.Message.To == email).Message.TextBody;
        return Regex.Match(mail, @"#token=([A-Za-z0-9_-]{43})").Groups[1].Value;
    }

    private static async Task VerifiedRequestAsync(Setup s, string email = Visitor)
    {
        var token = await SubmitAndMailAsync(s, email);
        Assert.Equal(HttpStatusCode.OK, (await Verify(s.Factory, token)).StatusCode);
    }

    private static async Task<Guid> InsertAsync(AuthApiFactory f, Guid profileId, string status, DateTime? verifiedAt, string email,
        string? decision = null, DateTime? decidedAt = null, DateTime? managerAt = null)
    {
        var id = Guid.NewGuid();
        var created = DateTime.UtcNow.AddDays(-3);
        // The open-state unique index is keyed by (profile, visitor address): every row gets its own address.
        await Sql(f, $@"INSERT INTO contact_requests (id, profile_id, visitor_name, visitor_email, reason, status, created_at, expires_at, verified_at,
                participant_decision, participant_decided_at, manager_approved_at)
            VALUES ({id}, {profileId}, {VisitorName}, {email}, {Reason}, {status}, {created}, {created.AddHours(24)}, {verifiedAt}, {decision}, {decidedAt}, {managerAt})");
        return id;
    }

    private static async Task<JsonElement> GetJson(ApiClient client, string path, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await client.Http.GetAsync(path);
        Assert.Equal(expected, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static string[] Emails(JsonElement page) => page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("visitorEmail").GetString()!).ToArray();

    // ---- notification: outbox row ------------------------------------------------------------------------------------

    [Fact]
    public async Task Verification_writes_exactly_one_notification_row_in_the_same_transaction_without_any_personal_data()
    {
        var s = await NewSetupAsync();
        var token = await SubmitAndMailAsync(s);
        Assert.Empty(await Notifications(s.Factory)); // nothing before verification

        Assert.Equal(HttpStatusCode.OK, (await Verify(s.Factory, token)).StatusCode);

        var row = Assert.Single(await Notifications(s.Factory));
        var requestId = await Db(s.Factory, d => d.ContactRequests.AsNoTracking().Select(r => r.Id).SingleAsync());
        Assert.Equal(requestId, row.RequestId);
        Assert.Equal($"contact-notification:{requestId:D}", row.DeduplicationKey);
        Assert.Equal(OutboxStatus.Pending, row.Status);
        Assert.Null(row.ProfileId);
        Assert.Null(row.RevisionId);
        // The table has no column that could hold a name, address, reason or contact detail, and the row text carries none.
        var dump = JsonSerializer.Serialize(new { row.TemplateKey, row.DeduplicationKey, row.LastErrorCategory });
        foreach (var secret in new[] { Visitor, VisitorName, Reason, ParticipantEmail, ParticipantPhone, token }) Assert.DoesNotContain(secret, dump);
    }

    [Fact]
    public async Task Five_parallel_verifications_leave_one_notification_row_and_a_repeat_adds_none()
    {
        var s = await NewSetupAsync();
        var token = await SubmitAndMailAsync(s);

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Verify(s.Factory, token)));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(await Notifications(s.Factory));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Verify(s.Factory, token)).StatusCode);
        Assert.Single(await Notifications(s.Factory));
    }

    [Fact]
    public async Task A_notification_row_that_cannot_be_written_keeps_the_request_awaiting_and_the_token_unused()
    {
        var s = await NewSetupAsync();
        var token = await SubmitAndMailAsync(s);
        await Raw(s.Factory, @"
            CREATE FUNCTION fail_notification() RETURNS trigger AS $$ BEGIN
                IF NEW.template_key = 'contact-request-notification' THEN RAISE EXCEPTION 'boom'; END IF;
                RETURN NEW; END $$ LANGUAGE plpgsql;
            CREATE TRIGGER fail_notification BEFORE INSERT ON email_outbox FOR EACH ROW EXECUTE FUNCTION fail_notification();");
        try
        {
            var failed = await Verify(s.Factory, token);

            Assert.False(failed.IsSuccessStatusCode);
            var request = await Db(s.Factory, d => d.ContactRequests.AsNoTracking().SingleAsync());
            Assert.Equal(ContactRequestStatus.AwaitingEmailVerification, request.Status);
            Assert.Null(request.VerifiedAt);
            Assert.Null((await Db(s.Factory, d => d.PersonalTokens.AsNoTracking().SingleAsync(t => t.Purpose == "verify-contact-email"))).UsedAt);
            Assert.Empty(await Notifications(s.Factory));
        }
        finally
        {
            // Always remove both, whatever the assertions did.
            await Raw(s.Factory, "DROP TRIGGER IF EXISTS fail_notification ON email_outbox; DROP FUNCTION IF EXISTS fail_notification();");
        }

        // Once the fault is gone the same link works.
        Assert.Equal(HttpStatusCode.OK, (await Verify(s.Factory, token)).StatusCode);
        Assert.Single(await Notifications(s.Factory));
    }

    // ---- notification: the mail --------------------------------------------------------------------------------------

    [Fact]
    public async Task The_worker_sends_a_plain_text_notice_to_the_manager_without_visitor_or_participant_details()
    {
        var s = await NewSetupAsync();
        await VerifiedRequestAsync(s);
        var before = s.Factory.Email.Messages.Count;

        await ProcessAsync(s.Factory);

        var sent = Assert.Single(s.Factory.Email.Messages.Skip(before));
        Assert.Equal("admin@example.test", sent.Message.To); // the Development default
        Assert.Equal(ContactRequestNotificationEmail.Subject, sent.Message.Subject);
        var body = sent.Message.TextBody;
        Assert.Contains("מחזור", body);
        Assert.Contains("דנה כהן", body);
        Assert.Contains($"{AuthApiFactory.PublicBaseUrl}/admin/contact-requests", body);
        Assert.Matches(@"\d{4}-\d{2}-\d{2} \d{2}:\d{2}", body);
        foreach (var secret in new[] { Visitor, VisitorName, Reason, ParticipantEmail, ParticipantPhone, "<", "href" })
        {
            Assert.DoesNotContain(secret, body);
            Assert.DoesNotContain(secret, sent.Message.Subject);
        }
        var row = Assert.Single(await Notifications(s.Factory));
        Assert.Equal(OutboxStatus.Sent, row.Status);
        Assert.Empty(await Db(s.Factory, d => d.PersonalTokens.AsNoTracking().Where(t => t.Purpose != "verify-contact-email").ToListAsync()));
    }

    [Fact]
    public async Task The_notice_goes_to_every_configured_address_and_the_row_is_sent_only_when_all_were_sent()
    {
        using var env = new EnvScope().Set("ADMIN_NOTIFICATION_EMAIL", " A@x.co ,b@x.co");
        var s = await NewSetupAsync();
        await VerifiedRequestAsync(s);
        var before = s.Factory.Email.Messages.Count;

        // The first address fails (temporary), the second is delivered: the whole row is retried later.
        s.Factory.Email.Script(EmailSendResult.Failed("boom"));
        await ProcessAsync(s.Factory);

        var afterFirst = s.Factory.Email.Messages.Skip(before).ToList();
        Assert.Equal(["b@x.co"], afterFirst.Select(m => m.Message.To).ToArray());
        var row = Assert.Single(await Notifications(s.Factory));
        Assert.Equal(OutboxStatus.Pending, row.Status);
        Assert.Equal(1, row.Attempts);

        await Raw(s.Factory, "UPDATE email_outbox SET next_attempt_at = now() - interval '1 second'");
        await ProcessAsync(s.Factory);

        var all = s.Factory.Email.Messages.Skip(before).Select(m => m.Message.To).ToList();
        Assert.Equal(1, all.Count(t => t == "a@x.co"));
        Assert.Equal(2, all.Count(t => t == "b@x.co")); // at-least-once: the address that already had it gets it again
        Assert.Equal(OutboxStatus.Sent, Assert.Single(await Notifications(s.Factory)).Status);
        Assert.Equal(afterFirst.Count + 2, s.Factory.Email.Messages.Count - before);
    }

    [Fact]
    public async Task Each_address_of_a_two_address_configuration_gets_exactly_one_mail_with_its_own_idempotency_key()
    {
        using var env = new EnvScope().Set("ADMIN_NOTIFICATION_EMAIL", "a@x.co,b@x.co");
        var s = await NewSetupAsync();
        await VerifiedRequestAsync(s);
        var before = s.Factory.Email.Messages.Count;

        await ProcessAsync(s.Factory);

        var sent = s.Factory.Email.Messages.Skip(before).ToList();
        Assert.Equal(["a@x.co", "b@x.co"], sent.Select(m => m.Message.To).OrderBy(t => t).ToArray());
        Assert.Equal(2, sent.Select(m => m.IdempotencyKey).Distinct().Count());
        Assert.Equal(OutboxStatus.Sent, Assert.Single(await Notifications(s.Factory)).Status);
    }

    [Fact]
    public async Task Without_manager_addresses_the_row_is_dead_with_no_recipient_the_failure_is_visible_and_the_request_stays_in_the_queue()
    {
        var s = await NewSetupAsync(services =>
        {
            services.RemoveAll<EmailOutboxSettings>();
            services.AddSingleton(new EmailOutboxSettings(AuthApiFactory.PublicBaseUrl, AdminRecipients: []));
        });
        await VerifiedRequestAsync(s);
        var before = s.Factory.Email.Messages.Count;

        await ProcessAsync(s.Factory);

        var row = Assert.Single(await Notifications(s.Factory));
        Assert.Equal(OutboxStatus.Dead, row.Status);
        Assert.Equal("no-recipient", row.LastErrorCategory);
        Assert.Equal(before, s.Factory.Email.Messages.Count);

        var outbox = await GetJson(s.Admin, "/admin/email-outbox");
        Assert.Contains(outbox.GetProperty("items").EnumerateArray(), i => i.GetProperty("templateKey").GetString() == "contact-request-notification"
            && i.GetProperty("lastErrorCategory").GetString() == "no-recipient");
        Assert.Equal([Visitor], Emails(await GetJson(s.Admin, "/admin/contact-requests")));
    }

    [Fact]
    public async Task A_notification_that_fails_five_times_ends_dead_and_the_request_is_still_in_the_queue()
    {
        var s = await NewSetupAsync();
        await VerifiedRequestAsync(s);
        s.Factory.Email.Script(Enumerable.Repeat(EmailSendResult.Failed("boom"), 5).ToArray());

        for (var i = 0; i < 5; i++)
        {
            await Raw(s.Factory, "UPDATE email_outbox SET next_attempt_at = now() - interval '1 second' WHERE status = 'Pending'");
            await ProcessAsync(s.Factory);
        }

        var row = Assert.Single(await Notifications(s.Factory));
        Assert.Equal(OutboxStatus.Dead, row.Status);
        Assert.Equal(5, row.Attempts);
        var outbox = await GetJson(s.Admin, "/admin/email-outbox");
        Assert.Contains(outbox.GetProperty("items").EnumerateArray(), i => i.GetProperty("templateKey").GetString() == "contact-request-notification");
        var request = Assert.Single((await GetJson(s.Admin, "/admin/contact-requests")).GetProperty("items").EnumerateArray());
        Assert.Equal("submitted", request.GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_notification_for_a_request_that_was_never_verified_is_dead_as_request_not_verified()
    {
        var s = await NewSetupAsync();
        await Submit(s.Factory, s.ProfileId);
        var requestId = await Db(s.Factory, d => d.ContactRequests.AsNoTracking().Select(r => r.Id).SingleAsync());
        await Sql(s.Factory, $@"INSERT INTO email_outbox (id, template_key, request_id, deduplication_key, status, attempts, claim_count, next_attempt_at, created_at)
            VALUES ({Guid.NewGuid()}, 'contact-request-notification', {requestId}, {"contact-notification:" + requestId:D}, 'Pending', 0, 0, now(), now())");

        await ProcessAsync(s.Factory);

        var row = Assert.Single(await Notifications(s.Factory));
        Assert.Equal(OutboxStatus.Dead, row.Status);
        Assert.Equal("request-not-verified", row.LastErrorCategory);
        Assert.DoesNotContain(s.Factory.Email.Messages, m => m.Message.To == "admin@example.test");
    }

    [Fact]
    public async Task A_verified_request_that_expired_afterwards_still_gets_its_notification()
    {
        var s = await NewSetupAsync();
        await VerifiedRequestAsync(s);
        Assert.Equal(HttpStatusCode.OK, (await s.Admin.PostWithCsrfAsync($"/admin/profiles/{s.ProfileId:D}/remove", null)).StatusCode);

        await ProcessAsync(s.Factory);

        Assert.Equal(OutboxStatus.Sent, Assert.Single(await Notifications(s.Factory)).Status);
        Assert.Contains(s.Factory.Email.Messages, m => m.Message.To == "admin@example.test");
    }

    // ---- configuration -----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("a@x.co", new[] { "a@x.co" })]
    [InlineData(" A@X.co , b@x.co ", new[] { "a@x.co", "b@x.co" })]
    [InlineData("a@x.co,A@x.co", new[] { "a@x.co" })]
    [InlineData("a@x.co,", new[] { "a@x.co" })]
    [InlineData("a@x.co,,b@x.co, ", new[] { "a@x.co", "b@x.co" })]
    [InlineData("a@x.co,b@x.co,c@x.co,d@x.co,e@x.co,A@x.co,b@x.co", new[] { "a@x.co", "b@x.co", "c@x.co", "d@x.co", "e@x.co" })]
    [InlineData("a@x.co,b@x.co,c@x.co,d@x.co,e@x.co", new[] { "a@x.co", "b@x.co", "c@x.co", "d@x.co", "e@x.co" })]
    public void Admin_recipients_are_parsed_and_normalised(string raw, string[] expected)
        => Assert.Equal(expected, EmailOptions.ParseAdminRecipients(raw));

    [Theory]
    [InlineData("not-an-email")]
    [InlineData(",")]
    [InlineData(" , ")]
    [InlineData("a@x.co,b@x.co,c@x.co,d@x.co,e@x.co,f@x.co")]
    [InlineData("a@x.co;b@x.co")]
    [InlineData("a@x.co,b@")]
    public void A_malformed_or_oversized_admin_recipient_list_fails_with_a_clear_message(string raw)
        => Assert.Contains("ADMIN_NOTIFICATION_EMAIL", Assert.Throws<InvalidOperationException>(() => EmailOptions.ParseAdminRecipients(raw)).Message);

    [Fact]
    public void The_admin_address_defaults_in_development_and_is_required_elsewhere_and_parsed_wherever_set()
    {
        IConfiguration Config(params (string, string?)[] v) => new ConfigurationBuilder().AddInMemoryCollection(v.ToDictionary(x => x.Item1, x => x.Item2)).Build();
        var dev = new HostEnv("Development");
        Assert.Equal(["admin@example.test"], EmailOptions.Resolve(Config(), dev).AdminRecipients);
        Assert.Equal(["m@x.co"], EmailOptions.Resolve(Config(("ADMIN_NOTIFICATION_EMAIL", "M@x.co")), dev).AdminRecipients);
        Assert.Contains("ADMIN_NOTIFICATION_EMAIL", Assert.Throws<InvalidOperationException>(() => EmailOptions.Resolve(Config(("ADMIN_NOTIFICATION_EMAIL", "nope")), dev)).Message);

        var prodValues = new (string, string?)[] { ("EMAIL_PROVIDER", "smtp"), ("EMAIL_FROM", "a@b.co"), ("PUBLIC_BASE_URL", "https://app.test"), ("SMTP_HOST", "smtp.test") };
        var prod = new HostEnv("Production");
        Assert.Contains("ADMIN_NOTIFICATION_EMAIL", Assert.Throws<InvalidOperationException>(() => EmailOptions.Resolve(Config(prodValues), prod)).Message);
        Assert.Contains("ADMIN_NOTIFICATION_EMAIL", Assert.Throws<InvalidOperationException>(() => EmailOptions.Resolve(Config([.. prodValues, ("ADMIN_NOTIFICATION_EMAIL", "  ")]), prod)).Message);
        Assert.Equal(["m@x.co", "n@x.co"], EmailOptions.Resolve(Config([.. prodValues, ("ADMIN_NOTIFICATION_EMAIL", "m@x.co,n@x.co")]), prod).AdminRecipients);
    }

    private sealed class HostEnv(string name) : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "t";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    [Fact]
    public async Task The_environment_variable_is_parsed_at_startup_in_the_real_wiring()
    {
        using var env = new EnvScope().Set("ADMIN_NOTIFICATION_EMAIL", "a@x.co,not-an-email");
        await Assert.ThrowsAnyAsync<Exception>(() => AuthApiFactory.CreateAsync(pg));
    }

    // ---- queue: content, filters, paging -----------------------------------------------------------------------------

    [Fact]
    public async Task The_queue_lists_verified_requests_oldest_first_with_both_approvals_in_separate_fields()
    {
        var s = await NewSetupAsync();
        var now = DateTime.UtcNow;
        await InsertAsync(s.Factory, s.ProfileId, "awaiting_email_verification", null, "awaiting@example.com");
        await InsertAsync(s.Factory, s.ProfileId, "expired", null, "expired-unverified@example.com");
        var newest = await InsertAsync(s.Factory, s.ProfileId, "submitted", now.AddHours(-1), "newest@example.com");
        var oldest = await InsertAsync(s.Factory, s.ProfileId, "submitted", now.AddHours(-5), "oldest@example.com");
        await InsertAsync(s.Factory, s.ProfileId, "sent_to_participant", now.AddHours(-3), "sent@example.com");
        await InsertAsync(s.Factory, s.ProfileId, "participant_approved", now.AddHours(-2), "approved@example.com", "approved", now.AddHours(-1.5));
        await InsertAsync(s.Factory, s.ProfileId, "participant_declined", now.AddHours(-4), "declined@example.com", "declined", now.AddHours(-3.5));
        await InsertAsync(s.Factory, s.ProfileId, "delivered", now.AddHours(-6), "delivered@example.com", "approved", now.AddHours(-5.5), now.AddHours(-5));
        await InsertAsync(s.Factory, s.ProfileId, "expired", now.AddHours(-7), "expired-verified@example.com");

        var response = await s.Admin.Http.GetAsync("/admin/contact-requests");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        var page = await response.Content.ReadFromJsonAsync<JsonElement>();

        // The default is the open states, never an unverified request, oldest verification first.
        Assert.Equal(["oldest@example.com", "sent@example.com", "approved@example.com", "newest@example.com"], Emails(page));
        Assert.Equal(4, page.GetProperty("total").GetInt32());

        var first = page.GetProperty("items")[0];
        Assert.Equal(oldest, first.GetProperty("requestId").GetGuid());
        Assert.Equal(
            ["cohortName", "createdAt", "managerApprovedAt", "notificationStatus", "participantDecidedAt", "participantDecision", "productName", "profileId", "profileName",
                "profileVisibility", "reason", "requestId", "status", "verifiedAt", "visitorEmail", "visitorName"],
            first.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        Assert.Equal(VisitorName, first.GetProperty("visitorName").GetString());
        Assert.Equal(Reason, first.GetProperty("reason").GetString());
        Assert.Equal(s.ProfileId, first.GetProperty("profileId").GetGuid());
        Assert.Equal("דנה כהן", first.GetProperty("profileName").GetString());
        Assert.Equal("מוצר", first.GetProperty("productName").GetString());
        Assert.Equal("מחזור", first.GetProperty("cohortName").GetString());
        Assert.Equal("active", first.GetProperty("profileVisibility").GetString());
        Assert.Equal("submitted", first.GetProperty("status").GetString());
        Assert.NotEqual(default, first.GetProperty("createdAt").GetDateTime());
        Assert.NotEqual(default, first.GetProperty("verifiedAt").GetDateTime());
        // Neither approval has been given: two separate nulls.
        Assert.Equal(JsonValueKind.Null, first.GetProperty("participantDecision").ValueKind);
        Assert.Equal(JsonValueKind.Null, first.GetProperty("participantDecidedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, first.GetProperty("managerApprovedAt").ValueKind);

        var approved = page.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("status").GetString() == "participant_approved");
        Assert.Equal("approved", approved.GetProperty("participantDecision").GetString());
        Assert.NotEqual(JsonValueKind.Null, approved.GetProperty("participantDecidedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, approved.GetProperty("managerApprovedAt").ValueKind);

        Assert.Equal(newest, page.GetProperty("items")[3].GetProperty("requestId").GetGuid());
    }

    [Theory]
    [InlineData("open", new[] { "oldest@example.com", "sent@example.com", "approved@example.com", "newest@example.com" })]
    [InlineData("submitted", new[] { "oldest@example.com", "newest@example.com" })]
    [InlineData("sent_to_participant", new[] { "sent@example.com" })]
    [InlineData("participant_approved", new[] { "approved@example.com" })]
    [InlineData("participant_declined", new[] { "declined@example.com" })]
    [InlineData("delivered", new[] { "delivered@example.com" })]
    [InlineData("expired", new[] { "expired-verified@example.com" })]
    [InlineData("all", new[] { "expired-verified@example.com", "delivered@example.com", "oldest@example.com", "declined@example.com", "sent@example.com", "approved@example.com", "newest@example.com" })]
    public async Task The_status_filter_selects_verified_requests_only(string status, string[] expected)
    {
        var s = await NewSetupAsync();
        var now = DateTime.UtcNow;
        await InsertAsync(s.Factory, s.ProfileId, "awaiting_email_verification", null, "awaiting@example.com");
        await InsertAsync(s.Factory, s.ProfileId, "expired", null, "expired-unverified@example.com");
        await InsertAsync(s.Factory, s.ProfileId, "submitted", now.AddHours(-1), "newest@example.com");
        await InsertAsync(s.Factory, s.ProfileId, "submitted", now.AddHours(-5), "oldest@example.com");
        await InsertAsync(s.Factory, s.ProfileId, "sent_to_participant", now.AddHours(-3), "sent@example.com");
        await InsertAsync(s.Factory, s.ProfileId, "participant_approved", now.AddHours(-2), "approved@example.com", "approved", now.AddHours(-1.5));
        await InsertAsync(s.Factory, s.ProfileId, "participant_declined", now.AddHours(-4), "declined@example.com", "declined", now.AddHours(-3.5));
        await InsertAsync(s.Factory, s.ProfileId, "delivered", now.AddHours(-6), "delivered@example.com", "approved", now.AddHours(-5.5), now.AddHours(-5));
        await InsertAsync(s.Factory, s.ProfileId, "expired", now.AddHours(-7), "expired-verified@example.com");

        var page = await GetJson(s.Admin, $"/admin/contact-requests?status={status}");

        Assert.Equal(expected, Emails(page));
        Assert.Equal(expected.Length, page.GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData("status=awaiting_email_verification")]
    [InlineData("status=bogus")]
    [InlineData("status=OPEN")]
    [InlineData("status=")]
    [InlineData("limit=0")]
    [InlineData("limit=-3")]
    [InlineData("offset=-1")]
    [InlineData("limit=abc")]
    [InlineData("limit=1.5")]
    [InlineData("offset=abc")]
    [InlineData("status=open&status=all")]
    public async Task Odd_query_values_are_a_400(string query)
    {
        var s = await NewSetupAsync();
        var response = await s.Admin.Http.GetAsync($"/admin/contact-requests?{query}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Paging_returns_the_right_slice_with_the_full_total_and_a_limit_over_100_is_capped()
    {
        var s = await NewSetupAsync();
        var now = DateTime.UtcNow;
        for (var i = 0; i < 5; i++) await InsertAsync(s.Factory, s.ProfileId, "submitted", now.AddHours(-10 + i), $"r{i}@example.com");

        var page = await GetJson(s.Admin, "/admin/contact-requests?limit=2&offset=1");
        Assert.Equal(["r1@example.com", "r2@example.com"], Emails(page));
        Assert.Equal(5, page.GetProperty("total").GetInt32());

        var past = await GetJson(s.Admin, "/admin/contact-requests?offset=10");
        Assert.Empty(past.GetProperty("items").EnumerateArray());
        Assert.Equal(5, past.GetProperty("total").GetInt32());

        var capped = await GetJson(s.Admin, "/admin/contact-requests?limit=100000");
        Assert.Equal(5, capped.GetProperty("items").GetArrayLength());

        // Many rows: the cap is 100 per page.
        await Raw(s.Factory, @"INSERT INTO contact_requests (id, profile_id, visitor_name, visitor_email, reason, status, created_at, expires_at, verified_at)
            SELECT gen_random_uuid(), (SELECT id FROM profiles LIMIT 1), 'n', 'bulk' || g || '@example.com', 'r', 'submitted', now(), now(), now() + g * interval '1 second'
            FROM generate_series(1, 120) g");
        var big = await GetJson(s.Admin, "/admin/contact-requests?limit=100000");
        Assert.Equal(100, big.GetProperty("items").GetArrayLength());
        Assert.Equal(125, big.GetProperty("total").GetInt32());
        Assert.Equal(50, (await GetJson(s.Admin, "/admin/contact-requests")).GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task A_request_of_a_removed_profile_shows_expired_and_removed_only_for_the_expired_and_all_filters()
    {
        var s = await NewSetupAsync();
        await VerifiedRequestAsync(s);
        Assert.Equal(HttpStatusCode.OK, (await s.Admin.PostWithCsrfAsync($"/admin/profiles/{s.ProfileId:D}/remove", null)).StatusCode);

        Assert.Empty((await GetJson(s.Admin, "/admin/contact-requests")).GetProperty("items").EnumerateArray());
        foreach (var filter in new[] { "expired", "all" })
        {
            var item = Assert.Single((await GetJson(s.Admin, $"/admin/contact-requests?status={filter}")).GetProperty("items").EnumerateArray());
            Assert.Equal("expired", item.GetProperty("status").GetString());
            Assert.Equal("removed", item.GetProperty("profileVisibility").GetString());
            Assert.Equal(Visitor, item.GetProperty("visitorEmail").GetString());
        }
    }

    [Fact]
    public async Task A_request_appears_in_the_queue_only_after_verification_and_the_participant_details_never_do()
    {
        var s = await NewSetupAsync();
        var token = await SubmitAndMailAsync(s);
        Assert.Empty((await GetJson(s.Admin, "/admin/contact-requests?status=all")).GetProperty("items").EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, (await Verify(s.Factory, token)).StatusCode);

        var raw = await (await s.Admin.Http.GetAsync("/admin/contact-requests?status=all")).Content.ReadAsStringAsync();
        Assert.Contains(Visitor, raw);
        Assert.DoesNotContain(ParticipantEmail, raw);
        Assert.DoesNotContain("0501234567", raw);
        Assert.DoesNotContain("050-123", raw);
        Assert.DoesNotContain("phone", raw, StringComparison.OrdinalIgnoreCase);
        var id = (await GetJson(s.Admin, "/admin/contact-requests")).GetProperty("items")[0].GetProperty("requestId").GetString();
        var single = await (await s.Admin.Http.GetAsync($"/admin/contact-requests/{id}")).Content.ReadAsStringAsync();
        Assert.DoesNotContain(ParticipantEmail, single);
        Assert.DoesNotContain("0501234567", single);
        Assert.DoesNotContain("050-123", single);
    }

    [Fact]
    public async Task The_projection_uses_the_approved_revision_when_there_is_one_even_if_a_newer_revision_exists()
    {
        var s = await NewSetupAsync();
        await VerifiedRequestAsync(s);
        await using (var scope = s.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var extras = new ProfileExtras("תיאור", "תפקיד", "מקום", "היכרות", "א1", "א2", "א3", "girl-1", null, null, "# קורות חיים");
            var approved = ProfileRevision.CreatePending(s.ProfileId, "שם מאושר", "מוצר מאושר", "https://example.com/p", DateTime.UtcNow.AddMinutes(-5), extras);
            var newer = ProfileRevision.CreatePending(s.ProfileId, "שם חדש יותר", "מוצר חדש יותר", "https://example.com/p", DateTime.UtcNow, extras);
            db.ProfileRevisions.AddRange(approved, newer);
            db.Entry(approved).Property(r => r.Status).CurrentValue = RevisionStatus.Approved;
            db.Entry(newer).Property(r => r.Status).CurrentValue = RevisionStatus.Superseded;
            await db.SaveChangesAsync();
            var profile = await db.Profiles.SingleAsync(p => p.Id == s.ProfileId);
            db.Entry(profile).Property(p => p.ApprovedRevisionId).CurrentValue = approved.Id;
            await db.SaveChangesAsync();
        }

        var item = Assert.Single((await GetJson(s.Admin, "/admin/contact-requests")).GetProperty("items").EnumerateArray());
        Assert.Equal("שם מאושר", item.GetProperty("profileName").GetString());
        Assert.Equal("מוצר מאושר", item.GetProperty("productName").GetString());

        var before = s.Factory.Email.Messages.Count;
        await ProcessAsync(s.Factory);
        Assert.Contains("שם מאושר", Assert.Single(s.Factory.Email.Messages.Skip(before)).Message.TextBody);
    }

    // ---- queue: single item ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Get_by_id_returns_the_same_item_and_hides_unverified_malformed_and_unknown_ids_behind_one_404()
    {
        var s = await NewSetupAsync();
        var now = DateTime.UtcNow;
        var verified = await InsertAsync(s.Factory, s.ProfileId, "submitted", now.AddHours(-1), "v@example.com");
        var awaiting = await InsertAsync(s.Factory, s.ProfileId, "awaiting_email_verification", null, "a@example.com");
        var expiredUnverified = await InsertAsync(s.Factory, s.ProfileId, "expired", null, "e@example.com");

        var fromList = (await GetJson(s.Admin, "/admin/contact-requests")).GetProperty("items")[0];
        var single = await GetJson(s.Admin, $"/admin/contact-requests/{verified:D}");
        Assert.Equal(fromList.GetRawText(), single.GetRawText());

        var bodies = new List<string>();
        foreach (var path in new[] { $"{awaiting:D}", $"{expiredUnverified:D}", $"{Guid.NewGuid():D}", "not-a-guid", $"{verified:N}", $"{{{verified:D}}}" })
        {
            var response = await s.Admin.Http.GetAsync($"/admin/contact-requests/{path}");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
            bodies.Add(Regex.Replace(await response.Content.ReadAsStringAsync(), "\"traceId\":\"[^\"]*\"", ""));
        }
        Assert.Single(bodies.Distinct());
    }

    // ---- authorisation -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Both_reads_need_an_admin_anonymous_is_401_and_a_signed_in_non_admin_is_403()
    {
        var s = await NewSetupAsync();
        var id = await InsertAsync(s.Factory, s.ProfileId, "submitted", DateTime.UtcNow, "v@example.com");
        await s.Factory.AddUserAsync("viewer2", "Quiet-Horse-Pass-77!Qx", admin: false);
        var plain = s.Factory.NewClient();
        Assert.Equal(HttpStatusCode.OK, (await plain.LoginAsync("viewer2", "Quiet-Horse-Pass-77!Qx")).StatusCode);
        var anon = s.Factory.NewClient();

        foreach (var path in new[] { "/admin/contact-requests", $"/admin/contact-requests/{id:D}" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anon.Http.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await plain.Http.GetAsync(path)).StatusCode);
        }
    }

    [Fact]
    public async Task There_is_no_write_action_on_requests_yet()
    {
        var s = await NewSetupAsync();
        var id = await InsertAsync(s.Factory, s.ProfileId, "submitted", DateTime.UtcNow, "v@example.com");
        foreach (var path in new[] { "/admin/contact-requests", $"/admin/contact-requests/{id:D}" })
        {
            var response = await s.Admin.PostWithCsrfAsync(path, new { });
            Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed);
        }
    }

    // ---- schema ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_new_decision_column_has_a_check_constraint_and_the_new_columns_start_empty()
    {
        var s = await NewSetupAsync();
        var id = await InsertAsync(s.Factory, s.ProfileId, "submitted", DateTime.UtcNow, "v@example.com");
        var request = await Db(s.Factory, d => d.ContactRequests.AsNoTracking().SingleAsync(r => r.Id == id));
        Assert.Null(request.ParticipantDecision);
        Assert.Null(request.ParticipantDecidedAt);
        Assert.Null(request.ManagerApprovedAt);

        await Assert.ThrowsAnyAsync<Exception>(() => InsertAsync(s.Factory, s.ProfileId, "submitted", DateTime.UtcNow, "w@example.com", "maybe", DateTime.UtcNow));
        await InsertAsync(s.Factory, s.ProfileId, "participant_declined", DateTime.UtcNow, "x@example.com", "declined", DateTime.UtcNow);
    }
}
