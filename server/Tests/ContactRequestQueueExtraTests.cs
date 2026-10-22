using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ResumeApp.Domain.Contacts;
using ResumeApp.Domain.Email;
using ResumeApp.Features.Contacts;
using ResumeApp.Features.Email;
using ResumeApp.Infrastructure;

namespace ResumeApp.Tests;

/// <summary>Story 3.2 review follow-ups: queue consistency, notification status, multi-address aggregation, config and schema checks.</summary>
[Collection("env")]
public class ContactRequestQueueExtraTests(PostgresFixture pg) : IDisposable
{
    private const string NewPassword = "Brand-New-Pass-42#Lk";
    private const string Visitor = "visitor@example.com";
    private const string VisitorName = "יוסי המבקר";
    private const string Reason = "אשמח לשוחח על שיתוף פעולה בפרויקט";
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
            email = "dana@example.com",
            phone = "050-123 4567",
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

    private static Task<List<EmailOutboxEntry>> Notifications(AuthApiFactory f)
        => Db(f, d => d.EmailOutbox.AsNoTracking().Where(e => e.TemplateKey == "contact-request-notification").ToListAsync());

    private static async Task<int> ProcessAsync(AuthApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<EmailOutboxProcessor>().ProcessBatchAsync(CancellationToken.None);
    }

    private static async Task<string> SubmitAndMailAsync(Setup s, string email, Func<IEnumerable<EmailMessage>>? mails = null)
    {
        var submitted = await s.Factory.NewClient().PostWithCsrfAsync($"/profiles/{s.ProfileId:D}/contact-requests", new { name = VisitorName, email, reason = Reason });
        Assert.Equal(HttpStatusCode.Accepted, submitted.StatusCode);
        await ProcessAsync(s.Factory);
        var mail = (mails?.Invoke() ?? s.Factory.Email.Messages.Select(m => m.Message)).Last(m => m.To == email).TextBody;
        return System.Text.RegularExpressions.Regex.Match(mail, "#token=([A-Za-z0-9_-]{43})").Groups[1].Value;
    }

    private static async Task VerifiedRequestAsync(Setup s, string email = Visitor, Func<IEnumerable<EmailMessage>>? mails = null)
    {
        var token = await SubmitAndMailAsync(s, email, mails);
        Assert.Equal(HttpStatusCode.OK, (await s.Factory.NewClient().PostWithCsrfAsync("/contact-requests/verify", new { token })).StatusCode);
    }

    private static async Task<Guid> InsertAsync(AuthApiFactory f, Guid profileId, string status, DateTime? verifiedAt, string email,
        string? decision = null, DateTime? decidedAt = null, DateTime? managerAt = null)
    {
        var id = Guid.NewGuid();
        var created = DateTime.UtcNow.AddDays(-3);
        await Sql(f, $@"INSERT INTO contact_requests (id, profile_id, visitor_name, visitor_email, reason, status, created_at, expires_at, verified_at,
                participant_decision, participant_decided_at, manager_approved_at)
            VALUES ({id}, {profileId}, {VisitorName}, {email}, {Reason}, {status}, {created}, {created.AddHours(24)}, {verifiedAt}, {decision}, {decidedAt}, {managerAt})");
        return id;
    }

    private static async Task<JsonElement> GetJson(ApiClient client, string path)
    {
        var response = await client.Http.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static List<Guid> Ids(JsonElement page) => page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("requestId").GetGuid()).ToList();

    // ---- queue ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task All_malformed_queries_return_the_same_400_shape()
    {
        var s = await NewSetupAsync();
        var shapes = new List<(string Members, string Field)>();
        foreach (var q in new[] { "limit=abc", "limit=1.5", "limit=99999999999", "limit=1&limit=2", "offset=abc", "offset=1e2", "status=open&status=all", "status=bogus", "limit=0", "offset=-1" })
        {
            var response = await s.Admin.Http.GetAsync($"/admin/contact-requests?{q}");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            shapes.Add((string.Join(",", json.EnumerateObject().Select(p => p.Name).OrderBy(n => n)), string.Join(",", json.GetProperty("errors").EnumerateObject().Select(p => p.Name))));
        }
        Assert.Single(shapes.Select(x => x.Members).Distinct());
        Assert.Equal(["limit", "offset", "status"], shapes.Select(x => x.Field).Distinct().OrderBy(n => n).ToArray());
    }

    [Fact]
    public async Task Equal_verification_times_are_ordered_by_id()
    {
        var s = await NewSetupAsync();
        var at = DateTime.UtcNow.AddHours(-2);
        for (var i = 0; i < 6; i++) await InsertAsync(s.Factory, s.ProfileId, "submitted", at, $"tie{i}@example.com");
        var byId = await Db(s.Factory, d => d.ContactRequests.AsNoTracking().OrderBy(r => r.Id).Select(r => r.Id).ToListAsync());

        Assert.Equal(byId, Ids(await GetJson(s.Admin, "/admin/contact-requests")));
        Assert.Equal(byId.Skip(2).Take(2).ToList(), Ids(await GetJson(s.Admin, "/admin/contact-requests?limit=2&offset=2")));
    }

    [Fact]
    public async Task The_total_never_exceeds_what_the_list_can_return_even_for_a_request_with_a_missing_profile()
    {
        var s = await NewSetupAsync();
        await Raw(s.Factory, @"DO $$ DECLARE c text; BEGIN
            SELECT conname INTO c FROM pg_constraint WHERE conrelid = 'contact_requests'::regclass AND contype = 'f' AND confrelid = 'profiles'::regclass;
            EXECUTE 'ALTER TABLE contact_requests DROP CONSTRAINT ' || quote_ident(c); END $$;");
        var orphan = await InsertAsync(s.Factory, Guid.NewGuid(), "submitted", DateTime.UtcNow.AddHours(-3), "orphan@example.com");
        await InsertAsync(s.Factory, s.ProfileId, "submitted", DateTime.UtcNow.AddHours(-1), "normal@example.com");

        var page = await GetJson(s.Admin, "/admin/contact-requests");

        Assert.Equal(2, page.GetProperty("total").GetInt32());
        Assert.Equal(2, page.GetProperty("items").GetArrayLength());
        var item = page.GetProperty("items")[0];
        Assert.Equal(orphan, item.GetProperty("requestId").GetGuid());
        Assert.Equal("removed", item.GetProperty("profileVisibility").GetString());
        Assert.Equal(ContactRequestQueueService.Placeholder, item.GetProperty("profileName").GetString());
        Assert.Equal(ContactRequestQueueService.Placeholder, item.GetProperty("cohortName").GetString());
        Assert.Equal(HttpStatusCode.OK, (await s.Admin.Http.GetAsync($"/admin/contact-requests/{orphan:D}")).StatusCode);
    }

    [Fact]
    public async Task A_dangling_approved_pointer_falls_back_to_the_latest_revision()
    {
        var s = await NewSetupAsync();
        await VerifiedRequestAsync(s);
        await Raw(s.Factory, @"DO $$ DECLARE c text; BEGIN
            FOR c IN SELECT conname FROM pg_constraint WHERE conrelid = 'profiles'::regclass AND contype = 'f' AND confrelid = 'profile_revisions'::regclass LOOP
                EXECUTE 'ALTER TABLE profiles DROP CONSTRAINT ' || quote_ident(c); END LOOP; END $$;");
        await Raw(s.Factory, "UPDATE profiles SET approved_revision_id = gen_random_uuid()");

        var item = Assert.Single((await GetJson(s.Admin, "/admin/contact-requests")).GetProperty("items").EnumerateArray());
        Assert.Equal("דנה כהן", item.GetProperty("profileName").GetString());
        Assert.Equal("מוצר", item.GetProperty("productName").GetString());
        var before = s.Factory.Email.Messages.Count;
        await ProcessAsync(s.Factory);
        Assert.Contains("דנה כהן", Assert.Single(s.Factory.Email.Messages.Skip(before)).Message.TextBody);
    }

    [Fact]
    public async Task A_request_with_no_revision_shows_a_placeholder_and_the_notice_has_no_blank_name()
    {
        var s = await NewSetupAsync();
        await VerifiedRequestAsync(s);
        await Raw(s.Factory, "UPDATE profiles SET approved_revision_id = NULL; DELETE FROM profile_revisions");

        var item = Assert.Single((await GetJson(s.Admin, "/admin/contact-requests")).GetProperty("items").EnumerateArray());
        Assert.Equal(ContactRequestQueueService.Placeholder, item.GetProperty("profileName").GetString());
        Assert.Equal(ContactRequestQueueService.Placeholder, item.GetProperty("productName").GetString());
        var before = s.Factory.Email.Messages.Count;
        await ProcessAsync(s.Factory);
        var body = Assert.Single(s.Factory.Email.Messages.Skip(before)).Message.TextBody;
        Assert.Contains($"פרופיל מבוקש: {ContactRequestNotificationEmail.Placeholder}", body);
    }

    private static async Task<string> NotificationStatus(Setup s, Guid requestId)
        => (await GetJson(s.Admin, $"/admin/contact-requests/{requestId:D}")).GetProperty("notificationStatus").GetString()!;

    [Fact]
    public async Task The_queue_shows_none_pending_sent_and_dead_notification_status()
    {
        var s = await NewSetupAsync();
        var bare = await InsertAsync(s.Factory, s.ProfileId, "submitted", DateTime.UtcNow.AddHours(-9), "bare@example.com");
        Assert.Equal("none", await NotificationStatus(s, bare));

        await VerifiedRequestAsync(s);
        var real = await Db(s.Factory, d => d.ContactRequests.AsNoTracking().Where(r => r.VisitorEmail == Visitor).Select(r => r.Id).SingleAsync());
        Assert.Equal("pending", await NotificationStatus(s, real));
        var listed = (await GetJson(s.Admin, "/admin/contact-requests")).GetProperty("items").EnumerateArray()
            .ToDictionary(i => i.GetProperty("requestId").GetGuid(), i => i.GetProperty("notificationStatus").GetString());
        Assert.Equal("none", listed[bare]);
        Assert.Equal("pending", listed[real]);

        await ProcessAsync(s.Factory);
        Assert.Equal("sent", await NotificationStatus(s, real));

        // A second request whose notification dies after five failed sends.
        await VerifiedRequestAsync(s, "second@example.com");
        var second = await Db(s.Factory, d => d.ContactRequests.AsNoTracking().Where(r => r.VisitorEmail == "second@example.com").Select(r => r.Id).SingleAsync());
        s.Factory.Email.Script(Enumerable.Repeat(EmailSendResult.Failed("boom"), 5).ToArray());
        for (var i = 0; i < 5; i++)
        {
            await Raw(s.Factory, "UPDATE email_outbox SET next_attempt_at = now() - interval '1 second' WHERE status = 'Pending'");
            await ProcessAsync(s.Factory);
        }
        Assert.Equal("dead", await NotificationStatus(s, second));
        Assert.Equal("sent", await NotificationStatus(s, real));
        var all = (await GetJson(s.Admin, "/admin/contact-requests")).GetProperty("items").EnumerateArray()
            .ToDictionary(i => i.GetProperty("requestId").GetGuid(), i => i.GetProperty("notificationStatus").GetString());
        Assert.Equal("dead", all[second]);
    }

    // ---- aggregation over several addresses ----------------------------------------------------------------------------

    private async Task<(Setup S, int Before)> TwoAddressRequestAsync(EnvScope env, ThrowingForAddressSender? sender = null)
    {
        env.Set("ADMIN_NOTIFICATION_EMAIL", "a@x.co,b@x.co");
        var s = await NewSetupAsync(sender is null ? null : services => UseSender(services, sender));
        Func<IEnumerable<EmailMessage>>? mails = sender is null ? null : () => sender.Sent.ToList();
        await VerifiedRequestAsync(s, Visitor, mails);
        return (s, s.Factory.Email.Messages.Count);
    }

    [Fact]
    public async Task One_permanent_failure_and_one_delivery_marks_the_row_sent_and_does_not_resend()
    {
        using var env = new EnvScope();
        var (s, before) = await TwoAddressRequestAsync(env);
        s.Factory.Email.Script(EmailSendResult.Permanent("rejected"));

        await ProcessAsync(s.Factory);

        Assert.Equal(["b@x.co"], s.Factory.Email.Messages.Skip(before).Select(m => m.Message.To).ToArray());
        Assert.Equal(OutboxStatus.Sent, Assert.Single(await Notifications(s.Factory)).Status);
    }

    [Fact]
    public async Task Permanent_failure_for_every_address_is_dead()
    {
        using var env = new EnvScope();
        var (s, before) = await TwoAddressRequestAsync(env);
        s.Factory.Email.Script(EmailSendResult.Permanent("rejected"), EmailSendResult.Permanent("rejected"));

        await ProcessAsync(s.Factory);

        Assert.Equal(before, s.Factory.Email.Messages.Count);
        var row = Assert.Single(await Notifications(s.Factory));
        Assert.Equal(OutboxStatus.Dead, row.Status);
        Assert.Equal("rejected", row.LastErrorCategory);
    }

    [Fact]
    public async Task A_rate_limited_address_stops_the_loop_and_defers_the_row_without_counting_an_attempt()
    {
        using var env = new EnvScope();
        var (s, before) = await TwoAddressRequestAsync(env);
        s.Factory.Email.Script(EmailSendResult.RateLimited(TimeSpan.FromMinutes(10)));

        await ProcessAsync(s.Factory);

        Assert.Equal(before, s.Factory.Email.Messages.Count); // the second address was not tried
        var row = Assert.Single(await Notifications(s.Factory));
        Assert.Equal(OutboxStatus.Pending, row.Status);
        Assert.Equal(0, row.Attempts);
        Assert.InRange((row.NextAttemptAt - DateTime.UtcNow).TotalMinutes, 8, 11);
    }

    [Fact]
    public async Task A_rate_limit_on_the_second_address_defers_the_row_even_though_the_first_was_delivered()
    {
        using var env = new EnvScope();
        var (s, before) = await TwoAddressRequestAsync(env);
        s.Factory.Email.Script(EmailSendResult.Sent, EmailSendResult.RateLimited(TimeSpan.FromMinutes(5)));

        await ProcessAsync(s.Factory);

        var row = Assert.Single(await Notifications(s.Factory));
        Assert.Equal(OutboxStatus.Pending, row.Status);
        Assert.Equal(0, row.Attempts);
        Assert.Equal(1, s.Factory.Email.Messages.Count - before); // the first address got its mail; the row is retried later
    }

    private sealed class ThrowingForAddressSender(string throwFor) : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];
        public List<string> Keys { get; } = [];
        public bool Armed { get; set; }

        public Task<EmailSendResult> SendAsync(EmailMessage message, string idempotencyKey, CancellationToken ct)
        {
            if (Armed && message.To == throwFor) throw new InvalidOperationException("provider exploded for " + message.To);
            lock (Sent) { Sent.Add(message); Keys.Add(idempotencyKey); }
            return Task.FromResult(EmailSendResult.Sent);
        }
    }

    private static void UseSender(IServiceCollection services, IEmailSender sender)
    {
        services.RemoveAll<IEmailSender>();
        services.AddSingleton(sender);
    }

    [Fact]
    public async Task An_exception_for_one_address_is_a_transient_failure_of_that_address_and_the_other_is_still_sent()
    {
        using var env = new EnvScope();
        var thrower = new ThrowingForAddressSender("a@x.co");
        var (s, _) = await TwoAddressRequestAsync(env, thrower);
        thrower.Sent.Clear();
        thrower.Armed = true;

        await ProcessAsync(s.Factory);

        Assert.Equal(["b@x.co"], thrower.Sent.Select(m => m.To).ToArray());
        var row = Assert.Single(await Notifications(s.Factory));
        Assert.Equal(OutboxStatus.Pending, row.Status);
        Assert.Equal(1, row.Attempts);
        Assert.Equal("send-exception", row.LastErrorCategory);
    }

    [Fact]
    public async Task The_provider_idempotency_key_is_stable_per_address_across_retries()
    {
        using var env = new EnvScope();
        var sender = new ThrowingForAddressSender("a@x.co");
        var (s, _) = await TwoAddressRequestAsync(env, sender);
        sender.Sent.Clear();
        sender.Keys.Clear();
        sender.Armed = true;

        await ProcessAsync(s.Factory); // b delivered, a threw: the row is retried
        await Raw(s.Factory, "UPDATE email_outbox SET next_attempt_at = now() - interval '1 second' WHERE status = 'Pending'");
        await ProcessAsync(s.Factory);

        Assert.Equal(2, sender.Keys.Count);
        Assert.Single(sender.Keys.Distinct()); // the same key for b both times, although the claim count differs
        Assert.DoesNotContain("b@x.co", sender.Keys[0]);
    }

    [Fact]
    public async Task A_notification_row_without_a_request_id_is_dead_as_unknown_template()
    {
        var s = await NewSetupAsync();
        await Sql(s.Factory, $@"INSERT INTO email_outbox (id, template_key, deduplication_key, status, attempts, claim_count, next_attempt_at, created_at)
            VALUES ({Guid.NewGuid()}, 'contact-request-notification', 'contact-notification:orphan', 'Pending', 0, 0, now() - interval '1 minute', now())");

        await ProcessAsync(s.Factory);

        var row = Assert.Single(await Notifications(s.Factory));
        Assert.Equal(OutboxStatus.Dead, row.Status);
        Assert.Equal("unknown-template", row.LastErrorCategory);
        Assert.DoesNotContain(s.Factory.Email.Messages, m => m.Message.To == "admin@example.test");
    }

    // ---- config ---------------------------------------------------------------------------------------------------------

    private sealed class HostEnv(string name) : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "t";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static IConfiguration Config(params (string, string?)[] v)
        => new ConfigurationBuilder().AddInMemoryCollection(v.ToDictionary(x => x.Item1, x => x.Item2)).Build();

    [Fact]
    public void A_web_only_instance_boots_without_an_admin_address_but_a_worker_instance_needs_one()
    {
        var prod = new HostEnv("Production");
        var values = new (string, string?)[] { ("EMAIL_PROVIDER", "smtp"), ("EMAIL_FROM", "a@b.co"), ("PUBLIC_BASE_URL", "https://app.test"), ("SMTP_HOST", "smtp.test") };

        Assert.Empty(EmailOptions.Resolve(Config([.. values, ("EMAIL_WORKER", "false")]), prod).AdminRecipients!);
        Assert.Contains("ADMIN_NOTIFICATION_EMAIL", Assert.Throws<InvalidOperationException>(() => EmailOptions.Resolve(Config([.. values, ("EMAIL_WORKER", "true")]), prod)).Message);
        Assert.Contains("ADMIN_NOTIFICATION_EMAIL", Assert.Throws<InvalidOperationException>(() => EmailOptions.Resolve(Config(values), prod)).Message);
        // Whenever it is set it is parsed, worker or not.
        Assert.Throws<InvalidOperationException>(() => EmailOptions.Resolve(Config([.. values, ("EMAIL_WORKER", "false"), ("ADMIN_NOTIFICATION_EMAIL", "nope")]), prod));
        Assert.Equal(["m@x.co"], EmailOptions.Resolve(Config([.. values, ("EMAIL_WORKER", "false"), ("ADMIN_NOTIFICATION_EMAIL", "m@x.co")]), prod).AdminRecipients);
    }

    [Fact]
    public void A_parse_error_names_the_position_of_the_entry_and_never_its_value()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => EmailOptions.ParseAdminRecipients("ok@x.co,secret-not-an-address,c@x.co"));
        Assert.Contains("entry 2", ex.Message);
        Assert.DoesNotContain("secret-not-an-address", ex.Message);
        Assert.Contains("at most 5 distinct", Assert.Throws<InvalidOperationException>(() => EmailOptions.ParseAdminRecipients("a@x.co,b@x.co,c@x.co,d@x.co,e@x.co,f@x.co")).Message);
    }

    // ---- schema ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_decision_and_its_time_are_set_together_and_manager_approval_needs_an_approving_participant()
    {
        var s = await NewSetupAsync();
        var now = DateTime.UtcNow;
        await Assert.ThrowsAnyAsync<Exception>(() => InsertAsync(s.Factory, s.ProfileId, "participant_approved", now, "c1@example.com", "approved", null));
        await Assert.ThrowsAnyAsync<Exception>(() => InsertAsync(s.Factory, s.ProfileId, "submitted", now, "c2@example.com", null, now));
        await Assert.ThrowsAnyAsync<Exception>(() => InsertAsync(s.Factory, s.ProfileId, "submitted", now, "c3@example.com", null, null, now));
        await Assert.ThrowsAnyAsync<Exception>(() => InsertAsync(s.Factory, s.ProfileId, "participant_declined", now, "c4@example.com", "declined", now, now));
        await InsertAsync(s.Factory, s.ProfileId, "participant_approved", now, "c5@example.com", "approved", now);
        await InsertAsync(s.Factory, s.ProfileId, "delivered", now, "c6@example.com", "approved", now, now);
        await InsertAsync(s.Factory, s.ProfileId, "participant_declined", now, "c7@example.com", "declined", now);
        await InsertAsync(s.Factory, s.ProfileId, "submitted", now, "c8@example.com");
    }
}
