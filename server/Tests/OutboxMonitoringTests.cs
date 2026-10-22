using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ResumeApp.Domain.Audit;
using ResumeApp.Domain.Email;
using ResumeApp.Features.Email;
using ResumeApp.Infrastructure;

namespace ResumeApp.Tests;

/// <summary>Story 4.2: stuck-email list, manual retry, manager alert, /healthz/details and the read-only audit log.</summary>
[Collection("env")]
public class OutboxMonitoringTests(PostgresFixture pg) : IDisposable
{
    private const string NewPassword = "Brand-New-Pass-42#Lk";
    private const string ParticipantEmail = "dana-private@example.com";
    private const string ParticipantPhone = "050-123 4567";
    private readonly List<AuthApiFactory> _factories = [];

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
    }

    private sealed record Setup(AuthApiFactory Factory, ApiClient Admin, ApiClient Plain, Guid ProfileId, Guid AdminId);

    private async Task<Setup> NewSetupAsync(Action<IServiceCollection>? configure = null)
    {
        var factory = await AuthApiFactory.CreateAsync(pg, configureServices: configure);
        _factories.Add(factory);
        var admin = factory.NewClient();
        await admin.EnrollAsync(AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword, NewPassword);
        await factory.AddUserAsync("viewer2", "Quiet-Horse-Pass-77!Qx", admin: false);
        var plain = factory.NewClient();
        Assert.Equal(HttpStatusCode.OK, (await plain.LoginAsync("viewer2", "Quiet-Horse-Pass-77!Qx")).StatusCode);
        var profileId = (await ParticipantSeeding.SeedProfileAsync(factory, email: ParticipantEmail, phone: ParticipantPhone)).ProfileId;
        var adminId = await Db(factory, d => d.Users.AsNoTracking().Where(u => u.UserName == AuthApiFactory.AdminUser).Select(u => u.Id).SingleAsync());
        return new(factory, admin, plain, profileId, adminId);
    }

    private static async Task<T> Db<T>(AuthApiFactory factory, Func<AppDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static Task Sql(AuthApiFactory f, FormattableString sql) => Db(f, async d => await d.Database.ExecuteSqlInterpolatedAsync(sql));

    private static async Task<Guid> InsertRowAsync(AuthApiFactory f, string status, DateTime nextAttemptAt, Guid? profileId = null, string template = "submission-returned",
        int attempts = 0, string? category = null, DateTime? createdAt = null)
    {
        var id = Guid.NewGuid();
        var created = createdAt ?? nextAttemptAt;
        await Sql(f, $@"INSERT INTO email_outbox (id, template_key, profile_id, deduplication_key, status, attempts, claim_count, next_attempt_at, created_at, last_error_category)
            VALUES ({id}, {template}, {profileId}, {"dedup:" + id:D}, {status}, {attempts}, 3, {nextAttemptAt}, {created}, {category})");
        return id;
    }

    private static Task<EmailOutboxEntry> Row(AuthApiFactory f, Guid id) => Db(f, d => d.EmailOutbox.AsNoTracking().SingleAsync(e => e.Id == id));

    private static async Task<string> BodyAsync(HttpResponseMessage response) => await response.Content.ReadAsStringAsync();

    private static void AssertNoPersonalData(string text)
    {
        Assert.DoesNotContain(ParticipantEmail, text);
        Assert.DoesNotContain(ParticipantPhone, text);
        Assert.DoesNotContain("@", text);
    }

    // ---- access ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Every_new_admin_endpoint_refuses_anonymous_with_401_and_a_plain_user_with_403()
    {
        var s = await NewSetupAsync();
        var id = await InsertRowAsync(s.Factory, "Dead", DateTime.UtcNow);
        var anon = s.Factory.NewClient();

        foreach (var path in new[] { "/admin/email-outbox/stuck", "/admin/audit-log", "/healthz/details" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anon.Http.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await s.Plain.Http.GetAsync(path)).StatusCode);
        }
        var retry = $"/admin/email-outbox/{id:D}/retry";
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsync(retry, null, await anon.GetCsrfAsync())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Plain.PostWithCsrfAsync(retry, null)).StatusCode);
        Assert.Equal(OutboxStatus.Dead, (await Row(s.Factory, id)).Status);

        // The retry also needs anti-forgery and a matching Origin.
        Assert.False((await s.Admin.PostAsync(retry, null, csrf: null)).IsSuccessStatusCode);
        Assert.False((await s.Admin.PostAsync(retry, null, await s.Admin.GetCsrfAsync(), origin: "https://evil.example")).IsSuccessStatusCode);
        Assert.Equal(OutboxStatus.Dead, (await Row(s.Factory, id)).Status);
        Assert.Empty(await Db(s.Factory, d => d.AuditLog.AsNoTracking().ToListAsync()));
    }

    [Fact]
    public async Task The_plain_healthz_stays_anonymous_and_unchanged()
    {
        var s = await NewSetupAsync();
        var response = await s.Factory.NewClient().Http.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", await BodyAsync(response));
    }

    // ---- stuck list ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_list_has_dead_and_old_pending_rows_only_without_any_address_or_text()
    {
        var s = await NewSetupAsync();
        var old = DateTime.UtcNow.AddHours(-2);
        var dead = await InsertRowAsync(s.Factory, "Dead", old, s.ProfileId, attempts: 5, category: "send-rejected");
        var stuck = await InsertRowAsync(s.Factory, "Pending", old, s.ProfileId);
        await InsertRowAsync(s.Factory, "Pending", DateTime.UtcNow.AddMinutes(-1), s.ProfileId); // due only a minute ago: not stuck
        await InsertRowAsync(s.Factory, "Pending", DateTime.UtcNow.AddHours(1), s.ProfileId); // backing off in the future: not stuck
        await InsertRowAsync(s.Factory, "Sent", old, s.ProfileId);

        var response = await s.Admin.Http.GetAsync("/admin/email-outbox/stuck");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        var text = await BodyAsync(response);
        AssertNoPersonalData(text);
        Assert.DoesNotContain("dedup:", text); // the deduplication key stays internal

        var json = JsonDocument.Parse(text).RootElement;
        Assert.Equal(2, json.GetProperty("total").GetInt32());
        var items = json.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(new[] { dead, stuck }.Order(), items.Select(i => i.GetProperty("id").GetGuid()).Order());
        var deadItem = items.Single(i => i.GetProperty("id").GetGuid() == dead);
        Assert.Equal("dead", deadItem.GetProperty("status").GetString());
        Assert.Equal("submission-returned", deadItem.GetProperty("templateKey").GetString());
        Assert.Equal(s.ProfileId, deadItem.GetProperty("profileId").GetGuid());
        Assert.Equal(5, deadItem.GetProperty("attempts").GetInt32());
        Assert.Equal("send-rejected", deadItem.GetProperty("lastErrorCategory").GetString());
        Assert.True(deadItem.GetProperty("ageSeconds").GetInt64() >= 2 * 3600 - 5);
        Assert.Equal("pending", items.Single(i => i.GetProperty("id").GetGuid() == stuck).GetProperty("status").GetString());
    }

    [Fact]
    public async Task The_list_pages_and_rejects_bad_paging_with_400()
    {
        var s = await NewSetupAsync();
        for (var i = 0; i < 3; i++) await InsertRowAsync(s.Factory, "Dead", DateTime.UtcNow.AddMinutes(-i), createdAt: DateTime.UtcNow.AddMinutes(-i));

        var first = JsonDocument.Parse(await BodyAsync(await s.Admin.Http.GetAsync("/admin/email-outbox/stuck?limit=2"))).RootElement;
        Assert.Equal(3, first.GetProperty("total").GetInt32());
        Assert.Equal(2, first.GetProperty("items").GetArrayLength());
        var second = JsonDocument.Parse(await BodyAsync(await s.Admin.Http.GetAsync("/admin/email-outbox/stuck?limit=2&offset=2"))).RootElement;
        Assert.Equal(1, second.GetProperty("items").GetArrayLength());

        foreach (var query in new[] { "limit=0", "limit=101", "limit=-1", "offset=-1", "limit=abc" })
            Assert.Equal(HttpStatusCode.BadRequest, (await s.Admin.Http.GetAsync("/admin/email-outbox/stuck?" + query)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.Admin.Http.GetAsync("/admin/email-outbox/stuck?limit=100")).StatusCode);
    }

    // ---- retry ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Retry_moves_a_dead_row_back_to_pending_keeping_its_key_and_writes_an_audit_entry()
    {
        var s = await NewSetupAsync();
        var id = await InsertRowAsync(s.Factory, "Dead", DateTime.UtcNow.AddHours(-1), s.ProfileId, attempts: 5, category: "send-rejected");
        await Sql(s.Factory, $"UPDATE email_outbox SET lease_until = now() WHERE id = {id}");
        var before = await Row(s.Factory, id);

        var response = await s.Admin.PostWithCsrfAsync($"/admin/email-outbox/{id:D}/retry", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoPersonalData(await BodyAsync(response));

        var after = await Row(s.Factory, id);
        Assert.Equal(OutboxStatus.Pending, after.Status);
        Assert.Equal(0, after.Attempts);
        Assert.Equal(before.ClaimCount, after.ClaimCount); // monotonic: the provider key is {id}:{claimCount}
        Assert.Null(after.LastErrorCategory);
        Assert.Null(after.LeaseUntil);
        Assert.True(after.NextAttemptAt >= DateTime.UtcNow.AddMinutes(-1));
        Assert.Equal(before.DeduplicationKey, after.DeduplicationKey);
        Assert.Single(await Db(s.Factory, d => d.EmailOutbox.AsNoTracking().ToListAsync()));

        var audit = Assert.Single(await Db(s.Factory, d => d.AuditLog.AsNoTracking().ToListAsync()));
        Assert.Equal(AuditLogEntry.RetryEmail, audit.Action);
        Assert.Equal(AuditLogEntry.EmailOutboxEntity, audit.EntityType);
        Assert.Equal(id, audit.EntityId);
        Assert.Equal(s.AdminId, audit.ActorId);
    }

    [Fact]
    public async Task Retry_resets_the_claim_count_only_for_a_row_that_died_of_too_many_claims()
    {
        var s = await NewSetupAsync();
        var id = await InsertRowAsync(s.Factory, "Dead", DateTime.UtcNow.AddHours(-1), s.ProfileId, category: "too-many-claims");
        await Sql(s.Factory, $"UPDATE email_outbox SET claim_count = 10 WHERE id = {id}");

        Assert.Equal(HttpStatusCode.OK, (await s.Admin.PostWithCsrfAsync($"/admin/email-outbox/{id:D}/retry", null)).StatusCode);

        Assert.Equal(0, (await Row(s.Factory, id)).ClaimCount); // otherwise the next claim would kill it again at once
    }

    [Fact]
    public async Task The_first_send_after_a_retry_uses_a_different_provider_key_than_the_original_first_send()
    {
        var s = await NewSetupAsync();
        var seeded = await ParticipantSeeding.SeedProfileAsync(s.Factory, email: "ret@example.com", phone: "050-765 4321", status: ResumeApp.Domain.Profiles.RevisionStatus.Returned);
        var revisionId = seeded.RevisionId;
        var profile = seeded.ProfileId;
        await Sql(s.Factory, $"UPDATE profile_revisions SET return_note = 'note' WHERE id = {revisionId}");
        await Sql(s.Factory, $"UPDATE profiles SET visibility = 'Active' WHERE id = {profile}");
        var id = Guid.NewGuid();
        var past = DateTime.UtcNow.AddMinutes(-5);
        await Sql(s.Factory, $@"INSERT INTO email_outbox (id, template_key, profile_id, revision_id, deduplication_key, status, attempts, claim_count, next_attempt_at, created_at)
            VALUES ({id}, 'submission-returned', {profile}, {revisionId}, {"k:" + id:D}, 'Pending', 0, 0, {past}, {past})");

        await ProcessOnceAsync(s.Factory);
        var first = Assert.Single(s.Factory.Email.Messages).IdempotencyKey;
        Assert.Equal(OutboxStatus.Sent, (await Row(s.Factory, id)).Status);

        await Sql(s.Factory, $"UPDATE email_outbox SET status = 'Dead', last_error_category = 'send-rejected' WHERE id = {id}");
        Assert.Equal(HttpStatusCode.OK, (await s.Admin.PostWithCsrfAsync($"/admin/email-outbox/{id:D}/retry", null)).StatusCode);
        await ProcessOnceAsync(s.Factory);

        Assert.Equal(2, s.Factory.Email.Messages.Count);
        Assert.NotEqual(first, s.Factory.Email.Messages[1].IdempotencyKey);
    }

    [Fact]
    public async Task A_second_retry_is_409_and_creates_no_duplicate_and_no_second_audit_entry()
    {
        var s = await NewSetupAsync();
        var id = await InsertRowAsync(s.Factory, "Dead", DateTime.UtcNow.AddHours(-1), s.ProfileId);
        var path = $"/admin/email-outbox/{id:D}/retry";

        Assert.Equal(HttpStatusCode.OK, (await s.Admin.PostWithCsrfAsync(path, null)).StatusCode);
        var second = await s.Admin.PostWithCsrfAsync(path, null);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);

        Assert.Single(await Db(s.Factory, d => d.EmailOutbox.AsNoTracking().ToListAsync()));
        Assert.Single(await Db(s.Factory, d => d.AuditLog.AsNoTracking().ToListAsync()));
    }

    [Fact]
    public async Task Two_parallel_retries_give_one_success_and_one_409()
    {
        var s = await NewSetupAsync();
        var id = await InsertRowAsync(s.Factory, "Dead", DateTime.UtcNow.AddHours(-1), s.ProfileId);
        var path = $"/admin/email-outbox/{id:D}/retry";

        var statuses = (await Task.WhenAll(s.Admin.PostWithCsrfAsync(path, null), s.Admin.PostWithCsrfAsync(path, null))).Select(r => r.StatusCode).Order().ToArray();

        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, statuses);
        Assert.Single(await Db(s.Factory, d => d.EmailOutbox.AsNoTracking().ToListAsync()));
        Assert.Single(await Db(s.Factory, d => d.AuditLog.AsNoTracking().ToListAsync()));
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Sent")]
    public async Task Retry_of_a_row_that_is_not_dead_is_409_and_changes_nothing(string status)
    {
        var s = await NewSetupAsync();
        var id = await InsertRowAsync(s.Factory, status, DateTime.UtcNow.AddHours(-1), s.ProfileId, attempts: 2);

        Assert.Equal(HttpStatusCode.Conflict, (await s.Admin.PostWithCsrfAsync($"/admin/email-outbox/{id:D}/retry", null)).StatusCode);

        var row = await Row(s.Factory, id);
        Assert.Equal(status, row.Status.ToString());
        Assert.Equal(2, row.Attempts);
        Assert.Empty(await Db(s.Factory, d => d.AuditLog.AsNoTracking().ToListAsync()));
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000000000000000000000000001")]
    public async Task Retry_of_an_invalid_id_is_404(string id)
        => Assert.Equal(HttpStatusCode.NotFound, (await (await NewSetupAsync()).Admin.PostWithCsrfAsync($"/admin/email-outbox/{id}/retry", null)).StatusCode);

    [Fact]
    public async Task Retry_of_an_unknown_row_is_404()
        => Assert.Equal(HttpStatusCode.NotFound, (await (await NewSetupAsync()).Admin.PostWithCsrfAsync($"/admin/email-outbox/{Guid.NewGuid():D}/retry", null)).StatusCode);

    [Fact]
    public async Task A_retried_row_is_claimed_and_sent_again_by_the_worker_once()
    {
        var s = await NewSetupAsync();
        // A dead row of an unknown template ends dead again, which proves it was picked up (and never duplicated).
        var id = await InsertRowAsync(s.Factory, "Dead", DateTime.UtcNow.AddHours(-1), s.ProfileId, template: "retired-template");
        Assert.Equal(HttpStatusCode.OK, (await s.Admin.PostWithCsrfAsync($"/admin/email-outbox/{id:D}/retry", null)).StatusCode);

        await using (var scope = s.Factory.Services.CreateAsyncScope())
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<EmailOutboxProcessor>().ProcessBatchAsync(CancellationToken.None));

        var row = await Row(s.Factory, id);
        Assert.Equal(OutboxStatus.Dead, row.Status);
        Assert.Equal("unknown-template", row.LastErrorCategory);
        Assert.Single(await Db(s.Factory, d => d.EmailOutbox.AsNoTracking().ToListAsync()));
    }

    // ---- healthz/details -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Healthz_details_is_ok_when_clean_and_degraded_with_counts_when_something_is_dead_or_stuck()
    {
        var s = await NewSetupAsync();
        var clean = await s.Admin.Http.GetAsync("/healthz/details");
        Assert.Equal(HttpStatusCode.OK, clean.StatusCode);
        Assert.Contains("no-store", clean.Headers.CacheControl?.ToString());
        var cleanJson = JsonDocument.Parse(await BodyAsync(clean)).RootElement;
        Assert.Equal("ok", cleanJson.GetProperty("status").GetString());
        Assert.Equal(0, cleanJson.GetProperty("outbox").GetProperty("dead").GetInt32());
        Assert.Equal(JsonValueKind.Null, cleanJson.GetProperty("outbox").GetProperty("oldestAgeSeconds").ValueKind);

        await InsertRowAsync(s.Factory, "Dead", DateTime.UtcNow.AddHours(-3), s.ProfileId);
        await InsertRowAsync(s.Factory, "Pending", DateTime.UtcNow.AddHours(-1), s.ProfileId);

        var response = await s.Admin.Http.GetAsync("/healthz/details");
        var text = await BodyAsync(response);
        AssertNoPersonalData(text);
        var outbox = JsonDocument.Parse(text).RootElement;
        Assert.Equal("degraded", outbox.GetProperty("status").GetString());
        Assert.Equal(1, outbox.GetProperty("outbox").GetProperty("dead").GetInt32());
        Assert.Equal(1, outbox.GetProperty("outbox").GetProperty("stuckPending").GetInt32());
        Assert.True(outbox.GetProperty("outbox").GetProperty("oldestAgeSeconds").GetInt64() >= 3 * 3600 - 5);
    }

    // ---- alert ---------------------------------------------------------------------------------------------------------

    private static async Task<bool> EnqueueAsync(AuthApiFactory f, DateTime now)
    {
        await using var scope = f.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IOutboxMonitor>().EnqueueAlertIfNeededAsync(now, CancellationToken.None);
    }

    private static Task<List<EmailOutboxEntry>> Alerts(AuthApiFactory f) => Db(f, d => d.EmailOutbox.AsNoTracking().Where(e => e.TemplateKey == "outbox-alert").ToListAsync());

    [Fact]
    public async Task No_alert_is_queued_when_nothing_is_dead_or_stuck()
    {
        var s = await NewSetupAsync();
        await InsertRowAsync(s.Factory, "Pending", DateTime.UtcNow.AddMinutes(-1), s.ProfileId);

        Assert.False(await EnqueueAsync(s.Factory, DateTime.UtcNow));
        Assert.Empty(await Alerts(s.Factory));
    }

    [Fact]
    public async Task One_alert_per_window_is_queued_and_sent_without_personal_data_and_is_not_counted_as_stuck()
    {
        var s = await NewSetupAsync();
        await InsertRowAsync(s.Factory, "Dead", DateTime.UtcNow.AddHours(-1), s.ProfileId);
        var now = DateTime.UtcNow.Date.AddDays(-1).AddMinutes(5); // inside a past window, so the alert row is already due

        Assert.True(await EnqueueAsync(s.Factory, now));
        Assert.False(await EnqueueAsync(s.Factory, now.AddHours(10))); // same UTC day, same fault: no second alert
        var alert = Assert.Single(await Alerts(s.Factory));
        Assert.Equal($"outbox-alert:{now:yyyyMMdd}", alert.DeduplicationKey);
        Assert.Null(alert.ProfileId);
        Assert.Null(alert.RequestId);

        Assert.True(await EnqueueAsync(s.Factory, now.AddDays(1))); // the fault lasts: one reminder in the next window
        Assert.Equal(2, (await Alerts(s.Factory)).Count);
        await Sql(s.Factory, $"DELETE FROM email_outbox WHERE template_key = 'outbox-alert' AND deduplication_key <> {alert.DeduplicationKey}");

        await using (var scope = s.Factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<EmailOutboxProcessor>().ProcessBatchAsync(CancellationToken.None);

        var mail = Assert.Single(s.Factory.Email.Messages).Message;
        Assert.Equal(OutboxAlertEmail.Subject, mail.Subject);
        Assert.Contains("/admin", mail.TextBody);
        Assert.DoesNotContain(ParticipantEmail, mail.TextBody);
        Assert.DoesNotContain(ParticipantPhone, mail.TextBody);
        Assert.Equal(OutboxStatus.Sent, (await Alerts(s.Factory)).Single().Status);

        // A dead alert would still not count as something stuck or dead.
        await Sql(s.Factory, $"UPDATE email_outbox SET status = 'Dead' WHERE template_key = 'outbox-alert'");
        await Sql(s.Factory, $"UPDATE email_outbox SET status = 'Sent' WHERE template_key <> 'outbox-alert'");
        await using var health = s.Factory.Services.CreateAsyncScope();
        var counts = await health.ServiceProvider.GetRequiredService<IOutboxMonitor>().GetHealthAsync(DateTime.UtcNow, CancellationToken.None);
        Assert.Equal(0, counts.Dead + counts.StuckPending);
    }

    private static async Task InsertAlertRowAsync(AuthApiFactory f)
    {
        var id = Guid.NewGuid();
        var past = DateTime.UtcNow.AddMinutes(-5);
        await Sql(f, $@"INSERT INTO email_outbox (id, template_key, deduplication_key, status, attempts, claim_count, next_attempt_at, created_at)
            VALUES ({id}, 'outbox-alert', {"outbox-alert:test-" + id:D}, 'Pending', 0, 0, {past}, {past})");
    }

    private static async Task ProcessOnceAsync(AuthApiFactory f)
    {
        await using var scope = f.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<EmailOutboxProcessor>().ProcessBatchAsync(CancellationToken.None);
    }

    private static async Task SweepAsync(AuthApiFactory f)
    {
        await using var scope = f.Services.CreateAsyncScope();
        await EmailOutboxWorker.SweepOutboxAlertAsync(scope.ServiceProvider, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, CancellationToken.None);
    }

    private static void NoManagerAddress(IServiceCollection services)
    {
        services.RemoveAll<EmailOutboxSettings>();
        services.AddSingleton(new EmailOutboxSettings(AuthApiFactory.PublicBaseUrl));
    }

    [Fact]
    public async Task An_alert_queued_before_the_fault_was_cleared_is_closed_as_sent_without_a_mail()
    {
        var s = await NewSetupAsync();
        await InsertAlertRowAsync(s.Factory);

        await ProcessOnceAsync(s.Factory);

        Assert.Empty(s.Factory.Email.Messages);
        Assert.Equal(OutboxStatus.Sent, Assert.Single(await Alerts(s.Factory)).Status);
    }

    [Fact]
    public async Task The_worker_sweep_queues_one_alert_for_a_dead_row_and_no_second_in_the_same_window()
    {
        var s = await NewSetupAsync();
        await InsertRowAsync(s.Factory, "Dead", DateTime.UtcNow.AddHours(-1), s.ProfileId);

        await SweepAsync(s.Factory);
        await SweepAsync(s.Factory);

        Assert.Single(await Alerts(s.Factory));
    }

    [Fact]
    public async Task The_worker_sweep_queues_nothing_without_a_manager_address()
    {
        var s = await NewSetupAsync(NoManagerAddress);
        await InsertRowAsync(s.Factory, "Dead", DateTime.UtcNow.AddHours(-1), s.ProfileId);

        await SweepAsync(s.Factory);

        Assert.Empty(await Alerts(s.Factory));
    }

    [Fact]
    public async Task An_alert_row_processed_without_a_manager_address_is_dead_with_no_recipient_and_sends_nothing()
    {
        var s = await NewSetupAsync(NoManagerAddress);
        await InsertRowAsync(s.Factory, "Dead", DateTime.UtcNow.AddHours(-1), s.ProfileId);
        await InsertAlertRowAsync(s.Factory);

        await ProcessOnceAsync(s.Factory);

        Assert.Empty(s.Factory.Email.Messages);
        var alert = Assert.Single(await Alerts(s.Factory));
        Assert.Equal(OutboxStatus.Dead, alert.Status);
        Assert.Equal("no-recipient", alert.LastErrorCategory);
    }

    // ---- audit log -----------------------------------------------------------------------------------------------------

    private static async Task SeedAuditAsync(AuthApiFactory f, Guid actor, int count)
    {
        var start = DateTime.UtcNow.AddDays(-1);
        for (var i = 0; i < count; i++)
            await Sql(f, $"INSERT INTO audit_log (id, actor_id, at, action, entity_type, entity_id) VALUES ({Guid.NewGuid()}, {actor}, {start.AddSeconds(i)}, 'retry-email', 'email-outbox', {Guid.NewGuid()})");
    }

    [Fact]
    public async Task The_audit_log_lists_newest_first_with_the_actor_and_no_personal_data()
    {
        var s = await NewSetupAsync();
        var older = Guid.NewGuid();
        var newer = Guid.NewGuid();
        await Sql(s.Factory, $"INSERT INTO audit_log (id, actor_id, at, action, entity_type, entity_id) VALUES ({Guid.NewGuid()}, {s.AdminId}, {DateTime.UtcNow.AddHours(-2)}, 'remove-profile', 'profile', {older})");
        await Sql(s.Factory, $"INSERT INTO audit_log (id, actor_id, at, action, entity_type, entity_id) VALUES ({Guid.NewGuid()}, {Guid.NewGuid()}, {DateTime.UtcNow.AddHours(-1)}, 'retry-email', 'email-outbox', {newer})");

        var response = await s.Admin.Http.GetAsync("/admin/audit-log");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        var text = await BodyAsync(response);
        AssertNoPersonalData(text);
        var json = JsonDocument.Parse(text).RootElement;
        Assert.Equal(2, json.GetProperty("total").GetInt32());
        var items = json.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(newer, items[0].GetProperty("entityId").GetGuid());
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("actorName").ValueKind); // an actor that no longer exists has no name
        Assert.Equal(older, items[1].GetProperty("entityId").GetGuid());
        Assert.Equal(AuthApiFactory.AdminUser, items[1].GetProperty("actorName").GetString());
        Assert.Equal(s.AdminId, items[1].GetProperty("actorId").GetGuid());
        Assert.Equal("remove-profile", items[1].GetProperty("action").GetString());
        Assert.Equal("profile", items[1].GetProperty("entityType").GetString());
    }

    [Fact]
    public async Task The_audit_log_pages_clamps_the_limit_to_100_and_rejects_invalid_values()
    {
        var s = await NewSetupAsync();
        await SeedAuditAsync(s.Factory, s.AdminId, 105);

        var firstPage = JsonDocument.Parse(await BodyAsync(await s.Admin.Http.GetAsync("/admin/audit-log"))).RootElement;
        Assert.Equal(105, firstPage.GetProperty("total").GetInt32());
        Assert.Equal(50, firstPage.GetProperty("items").GetArrayLength());
        var clamped = JsonDocument.Parse(await BodyAsync(await s.Admin.Http.GetAsync("/admin/audit-log?limit=500"))).RootElement;
        Assert.Equal(100, clamped.GetProperty("items").GetArrayLength());
        var tail = JsonDocument.Parse(await BodyAsync(await s.Admin.Http.GetAsync("/admin/audit-log?limit=100&offset=100"))).RootElement;
        Assert.Equal(5, tail.GetProperty("items").GetArrayLength());

        foreach (var query in new[] { "limit=0", "limit=-5", "offset=-1", "limit=abc", "offset=x" })
            Assert.Equal(HttpStatusCode.BadRequest, (await s.Admin.Http.GetAsync("/admin/audit-log?" + query)).StatusCode);
    }

    [Fact]
    public async Task The_audit_log_route_maps_no_verb_other_than_get()
    {
        var s = await NewSetupAsync();
        var routes = s.Factory.Services.GetServices<EndpointDataSource>().SelectMany(d => d.Endpoints).OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.TrimEnd('/') == "/admin/audit-log").ToList();

        Assert.NotEmpty(routes);
        var methods = routes.SelectMany(r => r.Metadata.OfType<HttpMethodMetadata>()).SelectMany(m => m.HttpMethods).Distinct().ToList();
        Assert.Equal(new[] { "GET" }, methods);
    }
}
