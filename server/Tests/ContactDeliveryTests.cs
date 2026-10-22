using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResumeApp.Domain.Contacts;
using ResumeApp.Domain.Email;
using ResumeApp.Features.Email;
using ResumeApp.Infrastructure;

namespace ResumeApp.Tests;

/// <summary>Story 3.4: the manager approves delivery and the worker re-checks everything and sends the details to the verified visitor address.</summary>
[Collection("env")]
public class ContactDeliveryTests(PostgresFixture pg) : IDisposable
{
    private const string NewPassword = "Brand-New-Pass-42#Lk";
    private const string Visitor = "visitor@example.com";
    private const string VisitorName = "יוסי המבקר";
    private const string Reason = "אשמח לשוחח על שיתוף פעולה בפרויקט";
    private const string ParticipantEmail = "dana@example.com";
    private const string ParticipantPhone = "050-123 4567";
    private const string DeliveryTemplate = "contact-delivery";
    private readonly List<AuthApiFactory> _factories = [];

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
    }

    private sealed record Setup(AuthApiFactory Factory, ApiClient Admin, Guid ProfileId, Guid RequestId);

    private async Task<Setup> NewSetupAsync()
    {
        var factory = await AuthApiFactory.CreateAsync(pg);
        _factories.Add(factory);
        var admin = factory.NewClient();
        await admin.EnrollAsync(AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword, NewPassword);
        var cohort = await (await admin.PostWithCsrfAsync("/admin/cohorts", new { name = "מחזור" })).Content.ReadFromJsonAsync<JsonElement>();
        var created = await factory.NewClient().PostWithCsrfAsync($"/invites/{cohort.GetProperty("inviteToken").GetString()}/profiles", new
        {
            name = "דנה כהן",
            productName = "מוצר",
            productUrl = "https://example.com/app",
            email = ParticipantEmail,
            phone = ParticipantPhone,
        });
        Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
        var profileId = await Db(factory, d => d.ParticipantPrivates.AsNoTracking().Select(p => p.ProfileId).SingleAsync());

        Assert.Equal(HttpStatusCode.Accepted, (await factory.NewClient().PostWithCsrfAsync($"/profiles/{profileId:D}/contact-requests",
            new { name = VisitorName, email = Visitor, reason = Reason })).StatusCode);
        await ProcessAsync(factory);
        var mail = factory.Email.Messages.Last(m => m.Message.To == Visitor).Message.TextBody;
        var token = Regex.Match(mail, @"#token=([A-Za-z0-9_-]{43})").Groups[1].Value;
        Assert.Equal(HttpStatusCode.OK, (await factory.NewClient().PostWithCsrfAsync("/contact-requests/verify", new { token })).StatusCode);
        var requestId = await Db(factory, d => d.ContactRequests.AsNoTracking().Select(r => r.Id).SingleAsync());
        await ProcessAsync(factory);
        return new(factory, admin, profileId, requestId);
    }

    /// <summary>A request the participant approved (the manager has not yet). Her mails are flushed.</summary>
    private async Task<Setup> ParticipantApprovedAsync()
    {
        var s = await NewSetupAsync();
        Assert.Equal(HttpStatusCode.OK, (await s.Admin.PostWithCsrfAsync($"/admin/contact-requests/{s.RequestId:D}/send-to-participant", null)).StatusCode);
        var before = s.Factory.Email.Messages.Count;
        await ProcessAsync(s.Factory);
        var mail = Assert.Single(s.Factory.Email.Messages.Skip(before), m => m.Message.To == ParticipantEmail).Message.TextBody;
        var token = Regex.Match(mail, @"#token=([A-Za-z0-9_-]{43})").Groups[1].Value;
        Assert.Equal(HttpStatusCode.OK, (await s.Factory.NewClient().PostWithCsrfAsync("/contact-decisions/decide", new { token, decision = "approved" })).StatusCode);
        return s;
    }

    private static async Task<T> Db<T>(AuthApiFactory factory, Func<AppDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static Task Sql(AuthApiFactory f, FormattableString sql) => Db(f, async d => await d.Database.ExecuteSqlInterpolatedAsync(sql));

    private static async Task<int> ProcessAsync(AuthApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<EmailOutboxProcessor>().ProcessBatchAsync(CancellationToken.None);
    }

    private static Task<ContactRequest> Request(Setup s) => Db(s.Factory, d => d.ContactRequests.AsNoTracking().SingleAsync(r => r.Id == s.RequestId));

    private static Task<EmailOutboxEntry> DeliveryRow(Setup s) => Db(s.Factory, d => d.EmailOutbox.AsNoTracking().SingleAsync(e => e.TemplateKey == DeliveryTemplate));

    private static Task<HttpResponseMessage> Approve(Setup s, string? id = null)
        => s.Admin.PostWithCsrfAsync($"/admin/contact-requests/{id ?? s.RequestId.ToString("D")}/approve-delivery", null);

    private static async Task<string?> CodeOf(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    private static int DeliveryMails(Setup s) => s.Factory.Email.Messages.Count(m => m.Message.Subject == ContactDeliveryEmail.Subject);

    // ---- approval --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Approving_sets_the_manager_approval_and_writes_one_outbox_row_and_one_audit_row_without_any_personal_data()
    {
        var s = await ParticipantApprovedAsync();

        var response = await Approve(s);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var request = await Request(s);
        Assert.Equal(ContactRequestStatus.ParticipantApproved, request.Status);
        Assert.NotNull(request.ManagerApprovedAt);
        var row = await DeliveryRow(s);
        Assert.Equal(s.RequestId, row.RequestId);
        Assert.Equal($"contact-delivery:{s.RequestId:D}", row.DeduplicationKey);
        Assert.Equal(OutboxStatus.Pending, row.Status);
        var dump = JsonSerializer.Serialize(new { row.TemplateKey, row.DeduplicationKey, row.LastErrorCategory });
        foreach (var secret in new[] { Visitor, VisitorName, Reason, ParticipantEmail, ParticipantPhone }) Assert.DoesNotContain(secret, dump);
        var audit = Assert.Single(await Db(s.Factory, d => d.AuditLog.AsNoTracking().Where(a => a.Action == "approve-delivery").ToListAsync()));
        Assert.Equal("contact-request", audit.EntityType);
        Assert.Equal(s.RequestId, audit.EntityId);
        Assert.DoesNotContain(ParticipantEmail, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Approving_is_409_unless_the_participant_approved_and_unknown_ids_are_404()
    {
        // Submitted (no link sent yet).
        var s = await NewSetupAsync();
        var notSent = await Approve(s);
        Assert.Equal(HttpStatusCode.Conflict, notSent.StatusCode);
        Assert.Equal("request-not-approved", await CodeOf(notSent));

        // Sent to the participant, no answer.
        Assert.Equal(HttpStatusCode.OK, (await s.Admin.PostWithCsrfAsync($"/admin/contact-requests/{s.RequestId:D}/send-to-participant", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Approve(s)).StatusCode);

        // Declined.
        await Sql(s.Factory, $"UPDATE contact_requests SET status = 'participant_declined', participant_decision = 'declined', participant_decided_at = now() WHERE id = {s.RequestId}");
        Assert.Equal(HttpStatusCode.Conflict, (await Approve(s)).StatusCode);

        Assert.Empty(await Db(s.Factory, d => d.EmailOutbox.AsNoTracking().Where(e => e.TemplateKey == DeliveryTemplate).ToListAsync()));
        Assert.Empty(await Db(s.Factory, d => d.AuditLog.AsNoTracking().Where(a => a.Action == "approve-delivery").ToListAsync()));
        Assert.Equal(HttpStatusCode.NotFound, (await Approve(s, Guid.NewGuid().ToString("D"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Approve(s, "not-a-guid")).StatusCode);
    }

    [Fact]
    public async Task Approving_twice_is_409_and_parallel_approvals_succeed_once()
    {
        var s = await ParticipantApprovedAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Approve(s)));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(3, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Single(await Db(s.Factory, d => d.EmailOutbox.AsNoTracking().Where(e => e.TemplateKey == DeliveryTemplate).ToListAsync()));
        Assert.Single(await Db(s.Factory, d => d.AuditLog.AsNoTracking().Where(a => a.Action == "approve-delivery").ToListAsync()));
        Assert.Equal(HttpStatusCode.Conflict, (await Approve(s)).StatusCode);
    }

    [Fact]
    public async Task Approving_needs_an_admin()
    {
        var s = await ParticipantApprovedAsync();

        var anonymous = await s.Factory.NewClient().PostWithCsrfAsync($"/admin/contact-requests/{s.RequestId:D}/approve-delivery", null);

        Assert.True(anonymous.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, anonymous.StatusCode.ToString());
        Assert.Null((await Request(s)).ManagerApprovedAt);
    }

    // ---- delivery --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_worker_mails_the_details_only_to_the_verified_visitor_and_marks_the_request_delivered()
    {
        var s = await ParticipantApprovedAsync();
        Assert.Equal(HttpStatusCode.OK, (await Approve(s)).StatusCode);
        var before = s.Factory.Email.Messages.Count;

        await ProcessAsync(s.Factory);

        var sent = Assert.Single(s.Factory.Email.Messages.Skip(before));
        Assert.Equal(Visitor, sent.Message.To);
        Assert.Equal(ContactDeliveryEmail.Subject, sent.Message.Subject);
        Assert.Contains("דנה כהן", sent.Message.TextBody);
        Assert.Contains(ParticipantEmail, sent.Message.TextBody);
        var storedPhone = await Db(s.Factory, d => d.ParticipantPrivates.AsNoTracking().Select(p => p.Phone).SingleAsync());
        Assert.Contains(storedPhone, sent.Message.TextBody);
        var row = await DeliveryRow(s);
        Assert.Equal(OutboxStatus.Sent, row.Status);
        Assert.Equal(row.Id.ToString("D"), sent.IdempotencyKey);
        Assert.Equal(ContactRequestStatus.Delivered, (await Request(s)).Status);
        Assert.DoesNotContain(ParticipantEmail, JsonSerializer.Serialize(new { row.TemplateKey, row.DeduplicationKey, row.LastErrorCategory }));

        // The queue and the outbox overview never carry the participant's details.
        var queue = await s.Admin.Http.GetAsync($"/admin/contact-requests/{s.RequestId:D}");
        var text = await queue.Content.ReadAsStringAsync();
        Assert.DoesNotContain(ParticipantEmail, text);
        Assert.DoesNotContain(ParticipantPhone, text);
        var item = JsonSerializer.Deserialize<JsonElement>(text);
        Assert.Equal("delivered", item.GetProperty("status").GetString());
        Assert.Equal("sent", item.GetProperty("deliveryStatus").GetString());
    }

    [Theory]
    [InlineData("consent-expired", "consent-expired")]
    [InlineData("contact-changed", "contact-changed")]
    [InlineData("profile-not-active", "profile-not-active")]
    [InlineData("no-recipient", "no-recipient")]
    [InlineData("delivery-not-approved", "delivery-not-approved")]
    [InlineData("manager-null", "delivery-not-approved")]
    [InlineData("email-blank", "no-recipient")]
    [InlineData("version-null", "contact-changed")]
    public async Task A_failed_recheck_at_send_time_sends_nothing_and_ends_the_row_dead_with_a_visible_reason(string scenario, string category)
    {
        var s = await ParticipantApprovedAsync();
        Assert.Equal(HttpStatusCode.OK, (await Approve(s)).StatusCode);
        switch (scenario)
        {
            case "manager-null":
                await Sql(s.Factory, $"UPDATE contact_requests SET manager_approved_at = NULL WHERE id = {s.RequestId}");
                break;
            case "email-blank":
                await Sql(s.Factory, $"UPDATE participant_private SET email = '' WHERE profile_id = {s.ProfileId}");
                break;
            case "version-null":
                await Sql(s.Factory, $"UPDATE contact_requests SET participant_contact_version = NULL WHERE id = {s.RequestId}");
                break;
            case "consent-expired":
                await Sql(s.Factory, $"UPDATE contact_requests SET participant_decided_at = {DateTime.UtcNow.AddDays(-15)} WHERE id = {s.RequestId}");
                break;
            case "contact-changed":
                await Sql(s.Factory, $"UPDATE participant_private SET contact_version = contact_version + 1 WHERE profile_id = {s.ProfileId}");
                break;
            case "profile-not-active":
                await Sql(s.Factory, $"UPDATE profiles SET visibility = 'Removed' WHERE id = {s.ProfileId}");
                break;
            case "no-recipient":
                await Sql(s.Factory, $"UPDATE participant_private SET phone = '' WHERE profile_id = {s.ProfileId}");
                break;
            default:
                await Sql(s.Factory, $"UPDATE contact_requests SET status = 'expired' WHERE id = {s.RequestId}");
                break;
        }
        var before = s.Factory.Email.Messages.Count;

        await ProcessAsync(s.Factory);

        Assert.Equal(before, s.Factory.Email.Messages.Count);
        var row = await DeliveryRow(s);
        Assert.Equal(OutboxStatus.Dead, row.Status);
        Assert.Equal(category, row.LastErrorCategory);
        Assert.NotEqual(ContactRequestStatus.Delivered, (await Request(s)).Status);

        var item = await s.Admin.Http.GetAsync($"/admin/contact-requests/{s.RequestId:D}");
        var json = JsonSerializer.Deserialize<JsonElement>(await item.Content.ReadAsStringAsync());
        Assert.Equal("dead", json.GetProperty("deliveryStatus").GetString());
        Assert.Equal(category, json.GetProperty("deliveryErrorCategory").GetString());
    }

    [Fact]
    public async Task A_transient_provider_failure_retries_without_marking_delivered()
    {
        var s = await ParticipantApprovedAsync();
        Assert.Equal(HttpStatusCode.OK, (await Approve(s)).StatusCode);
        s.Factory.Email.Script(EmailSendResult.Failed("provider-down"));

        await ProcessAsync(s.Factory);

        Assert.Equal(0, DeliveryMails(s));
        var row = await DeliveryRow(s);
        Assert.Equal(OutboxStatus.Pending, row.Status);
        Assert.Equal(1, row.Attempts);
        Assert.Equal(ContactRequestStatus.ParticipantApproved, (await Request(s)).Status);

        await Sql(s.Factory, $"UPDATE email_outbox SET next_attempt_at = {DateTime.UtcNow.AddMinutes(-1)} WHERE template_key = {DeliveryTemplate}");
        await ProcessAsync(s.Factory);

        Assert.Equal(1, DeliveryMails(s));
        Assert.Equal(ContactRequestStatus.Delivered, (await Request(s)).Status);
        Assert.Equal(OutboxStatus.Sent, (await DeliveryRow(s)).Status);
    }

    [Fact]
    public async Task A_row_claimed_again_after_delivery_is_marked_sent_without_a_second_mail()
    {
        var s = await ParticipantApprovedAsync();
        Assert.Equal(HttpStatusCode.OK, (await Approve(s)).StatusCode);
        await ProcessAsync(s.Factory);
        Assert.Equal(1, DeliveryMails(s));
        await Sql(s.Factory, $"UPDATE email_outbox SET status = 'Pending', sent_at = NULL, next_attempt_at = {DateTime.UtcNow.AddMinutes(-1)} WHERE template_key = {DeliveryTemplate}");

        await ProcessAsync(s.Factory);

        Assert.Equal(1, DeliveryMails(s));
        Assert.Equal(OutboxStatus.Sent, (await DeliveryRow(s)).Status);
        Assert.Equal(ContactRequestStatus.Delivered, (await Request(s)).Status);
    }

    // ---- manual retry ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Approving_again_after_a_dead_delivery_resets_the_same_row_and_then_delivers()
    {
        var s = await ParticipantApprovedAsync();
        Assert.Equal(HttpStatusCode.OK, (await Approve(s)).StatusCode);
        await Sql(s.Factory, $"UPDATE participant_private SET contact_version = contact_version + 1 WHERE profile_id = {s.ProfileId}");
        await ProcessAsync(s.Factory);
        var dead = await DeliveryRow(s);
        Assert.Equal(OutboxStatus.Dead, dead.Status);
        var approvedAt = (await Request(s)).ManagerApprovedAt;

        // The participant's details are back at the consented version: a manual retry delivers.
        await Sql(s.Factory, $"UPDATE participant_private SET contact_version = contact_version - 1 WHERE profile_id = {s.ProfileId}");
        var retry = await Approve(s);

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var row = await DeliveryRow(s); // Single: no duplicate row
        Assert.Equal(dead.Id, row.Id);
        Assert.Equal(OutboxStatus.Pending, row.Status);
        Assert.Equal(0, row.Attempts);
        Assert.Null(row.LastErrorCategory);
        Assert.Equal(approvedAt, (await Request(s)).ManagerApprovedAt);
        await ProcessAsync(s.Factory);
        Assert.Equal(1, DeliveryMails(s));
        Assert.Equal(ContactRequestStatus.Delivered, (await Request(s)).Status);

        // Delivered: no further approval.
        Assert.Equal(HttpStatusCode.Conflict, (await Approve(s)).StatusCode);
        Assert.Equal(2, await Db(s.Factory, d => d.AuditLog.CountAsync(a => a.Action == "approve-delivery")));
    }

    // ---- review follow-ups -----------------------------------------------------------------------------------------

    /// <summary>Delegates to the real outbox; MarkDeliverySentAsync throws the first <c>failures</c> times, or returns 0 when <c>lose</c> is set.</summary>
    private sealed class FaultyDeliveryOutbox(IEmailOutbox inner, int failures, bool lose = false) : IEmailOutbox
    {
        public int MarkCalls;
        public Task<int> EnqueueEditLinksAsync(string e, DateTime n, CancellationToken ct) => inner.EnqueueEditLinksAsync(e, n, ct);
        public Task<ClaimedEmail?> ClaimNextAsync(TimeSpan l, DateTime n, CancellationToken ct) => inner.ClaimNextAsync(l, n, ct);
        public Task<EditLinkRecipient?> GetEditLinkRecipientAsync(Guid p, CancellationToken ct) => inner.GetEditLinkRecipientAsync(p, ct);
        public Task<ReturnNoteResult> GetReturnNoteAsync(Guid r, CancellationToken ct) => inner.GetReturnNoteAsync(r, ct);
        public Task<ContactVerificationRecipient?> GetContactVerificationRecipientAsync(Guid r, DateTime n, CancellationToken ct) => inner.GetContactVerificationRecipientAsync(r, n, ct);
        public Task<ContactNotificationInfo?> GetContactNotificationAsync(Guid r, CancellationToken ct) => inner.GetContactNotificationAsync(r, ct);
        public Task<ContactDecisionRecipient?> GetContactDecisionRecipientAsync(Guid r, CancellationToken ct) => inner.GetContactDecisionRecipientAsync(r, ct);
        public Task<ContactDeliveryInfo?> GetContactDeliveryAsync(Guid r, CancellationToken ct) => inner.GetContactDeliveryAsync(r, ct);
        public Task<int> MarkSentAsync(ClaimedEmail c, DateTime n, CancellationToken ct) => inner.MarkSentAsync(c, n, ct);
        public Task<int> MarkDeliverySentAsync(ClaimedEmail c, DateTime n, CancellationToken ct)
        {
            if (++MarkCalls <= failures) throw new InvalidOperationException("db blip");
            return lose ? Task.FromResult(0) : inner.MarkDeliverySentAsync(c, n, ct);
        }
        public Task<int> RecordFailureAsync(ClaimedEmail c, string cat, bool a, bool t, int m, DateTime n, CancellationToken ct) => inner.RecordFailureAsync(c, cat, a, t, m, n, ct);
        public Task<OutboxOverview> GetOverviewAsync(int l, int o, CancellationToken ct) => inner.GetOverviewAsync(l, o, ct);
    }

    private static async Task ProcessWithAsync(AuthApiFactory factory, Func<IEmailOutbox, IEmailOutbox> wrap)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var processor = new EmailOutboxProcessor(wrap(sp.GetRequiredService<IEmailOutbox>()), sp.GetRequiredService<IPersonalTokenIssuer>(), sp.GetRequiredService<IEmailSender>(),
            new EmailOutboxSettings(AuthApiFactory.PublicBaseUrl), TimeProvider.System, sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<EmailOutboxProcessor>>());
        await processor.ProcessBatchAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_failure_to_mark_the_delivery_sent_is_retried_without_a_second_mail_and_ends_delivered()
    {
        var s = await ParticipantApprovedAsync();
        Assert.Equal(HttpStatusCode.OK, (await Approve(s)).StatusCode);
        FaultyDeliveryOutbox? faulty = null;

        await ProcessWithAsync(s.Factory, inner => faulty = new FaultyDeliveryOutbox(inner, failures: 1));

        Assert.Equal(2, faulty!.MarkCalls);
        Assert.Equal(1, DeliveryMails(s));
        Assert.Equal(OutboxStatus.Sent, (await DeliveryRow(s)).Status);
        Assert.Equal(ContactRequestStatus.Delivered, (await Request(s)).Status);
    }

    [Fact]
    public async Task A_lost_claim_when_marking_the_delivery_does_not_set_delivered()
    {
        var s = await ParticipantApprovedAsync();
        Assert.Equal(HttpStatusCode.OK, (await Approve(s)).StatusCode);

        await ProcessWithAsync(s.Factory, inner => new FaultyDeliveryOutbox(inner, failures: 0, lose: true));

        Assert.Equal(ContactRequestStatus.ParticipantApproved, (await Request(s)).Status);
        Assert.NotEqual(OutboxStatus.Sent, (await DeliveryRow(s)).Status);
    }

    [Fact]
    public async Task Approving_as_a_signed_in_non_admin_is_403_and_anonymous_is_401()
    {
        var s = await ParticipantApprovedAsync();
        await s.Factory.AddUserAsync("plain-user", NewPassword, admin: false);
        var user = s.Factory.NewClient();
        Assert.Equal(HttpStatusCode.OK, (await user.LoginAsync("plain-user", NewPassword)).StatusCode);

        var forbidden = await user.PostWithCsrfAsync($"/admin/contact-requests/{s.RequestId:D}/approve-delivery", null);
        var anonymous = await s.Factory.NewClient().PostWithCsrfAsync($"/admin/contact-requests/{s.RequestId:D}/approve-delivery", null);

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Null((await Request(s)).ManagerApprovedAt);
    }

    [Fact]
    public async Task The_approval_responses_are_not_cacheable()
    {
        var s = await ParticipantApprovedAsync();

        var ok = await Approve(s);
        var conflict = await Approve(s);
        var missing = await Approve(s, Guid.NewGuid().ToString("D"));

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        foreach (var r in new[] { ok, conflict, missing }) Assert.Equal("no-store", r.Headers.CacheControl?.ToString());
    }
}
