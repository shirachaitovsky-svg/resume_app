using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ResumeApp.Domain.Email;
using ResumeApp.Features.Email;
using ResumeApp.Infrastructure;

namespace ResumeApp.Tests;

// Same class as EditLinkTests (shared helpers); this part covers ownership, claim limits, deferral age and concurrency.
public partial class EditLinkTests
{
    private static async Task<ClaimedEmail?> ClaimAsync(AuthApiFactory f)
    {
        await using var scope = f.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IEmailOutbox>().ClaimNextAsync(TimeSpan.FromMinutes(2), DateTime.UtcNow, CancellationToken.None);
    }

    private static async Task<T> WithOutbox<T>(AuthApiFactory f, Func<IEmailOutbox, Task<T>> action)
    {
        await using var scope = f.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IEmailOutbox>());
    }

    private async Task<Setup> OneQueuedRowAsync(string email = "dana@example.com")
    {
        var s = await NewSetupAsync();
        await SubmitProfileAsync(s.Factory, s.Token, email);
        await RequestLink(s.Factory.NewClient(), email);
        return s;
    }

    [Fact]
    public async Task A_stale_worker_cannot_overwrite_the_result_of_the_worker_that_now_owns_the_row()
    {
        var s = await OneQueuedRowAsync();
        var stale = await ClaimAsync(s.Factory);
        Assert.NotNull(stale);
        await Sql(s.Factory, $"UPDATE email_outbox SET lease_until = now() - interval '1 second'"); // its lease passed
        var current = await ClaimAsync(s.Factory);
        Assert.NotNull(current);
        Assert.NotEqual(stale!.LeaseUntil, current!.LeaseUntil);
        Assert.Equal(2, current.ClaimCount);

        // The stale worker's late results change nothing.
        Assert.Equal(0, await WithOutbox(s.Factory, o => o.MarkSentAsync(stale, DateTime.UtcNow, CancellationToken.None)));
        Assert.Equal(0, await WithOutbox(s.Factory, o => o.RecordFailureAsync(stale, "late", true, true, 5, DateTime.UtcNow, CancellationToken.None)));
        var row = Assert.Single(await Outbox(s.Factory));
        Assert.Equal(OutboxStatus.Pending, row.Status);
        Assert.Equal(0, row.Attempts);

        Assert.Equal(1, await WithOutbox(s.Factory, o => o.MarkSentAsync(current, DateTime.UtcNow, CancellationToken.None)));
        Assert.Equal(OutboxStatus.Sent, Assert.Single(await Outbox(s.Factory)).Status);
        // And once sent, even the owner's replay changes nothing.
        Assert.Equal(0, await WithOutbox(s.Factory, o => o.RecordFailureAsync(current, "x", true, true, 5, DateTime.UtcNow, CancellationToken.None)));
    }

    [Fact]
    public async Task A_row_claimed_ten_times_becomes_dead_without_being_sent()
    {
        var s = await OneQueuedRowAsync();
        await Sql(s.Factory, $"UPDATE email_outbox SET claim_count = 10");

        Assert.Equal(0, await ProcessAsync(s.Factory));

        var row = Assert.Single(await Outbox(s.Factory));
        Assert.Equal(OutboxStatus.Dead, row.Status);
        Assert.Equal("too-many-claims", row.LastErrorCategory);
        Assert.Empty(s.Factory.Email.Messages);
    }

    [Fact]
    public async Task Each_claim_increments_the_claim_count_and_the_provider_key_changes_with_it()
    {
        var s = await OneQueuedRowAsync();
        Assert.Equal(1, (await ClaimAsync(s.Factory))!.ClaimCount);
        await Sql(s.Factory, $"UPDATE email_outbox SET lease_until = now() - interval '1 second'");
        await ProcessAsync(s.Factory);

        var sent = Assert.Single(s.Factory.Email.Messages);
        Assert.EndsWith(":2", sent.IdempotencyKey);
    }

    [Fact]
    public async Task A_row_still_deferred_by_429_after_24_hours_becomes_dead()
    {
        var s = await OneQueuedRowAsync();
        await Sql(s.Factory, $"UPDATE email_outbox SET created_at = now() - interval '25 hours'");
        s.Factory.Email.Script(EmailSendResult.RateLimited(TimeSpan.FromMinutes(1)));

        await ProcessAsync(s.Factory);

        var row = Assert.Single(await Outbox(s.Factory));
        Assert.Equal(OutboxStatus.Dead, row.Status);
        Assert.Equal("deferred-too-long", row.LastErrorCategory);
        Assert.Equal(0, row.Attempts);
    }

    [Fact]
    public async Task A_permanent_failure_makes_the_row_dead_at_once()
    {
        var s = await OneQueuedRowAsync();
        s.Factory.Email.Script(EmailSendResult.Permanent("smtp-rejected"));

        await ProcessAsync(s.Factory);

        var row = Assert.Single(await Outbox(s.Factory));
        Assert.Equal(OutboxStatus.Dead, row.Status);
        Assert.Equal("smtp-rejected", row.LastErrorCategory);
        Assert.Equal(1, row.Attempts);
    }

    [Fact]
    public async Task The_mail_names_the_cohort_so_two_cohorts_with_one_address_can_be_told_apart()
    {
        var s = await NewSetupAsync();
        var (second, _) = await CreateCohortAsync(s.Admin, "מחזור שני");
        await SubmitProfileAsync(s.Factory, s.Token, "dana@example.com"); // cohort "מחזור"
        await SubmitProfileAsync(s.Factory, second, "dana@example.com");
        await RequestLink(s.Factory.NewClient(), "dana@example.com");

        await ProcessAsync(s.Factory);

        var bodies = s.Factory.Email.Messages.Select(m => m.Message.TextBody).ToList();
        Assert.Equal(2, bodies.Count);
        Assert.Single(bodies, b => b.Contains("במחזור: מחזור שני"));
        Assert.Single(bodies, b => b.Contains("במחזור: מחזור\n"));
        Assert.Equal(2, bodies.Select(b => b.Split("token=")[1].Split('\n')[0]).Distinct().Count());
    }

    /// <summary>Delegates to the real outbox but fails the first N MarkSent calls.</summary>
    private sealed class FlakyMarkOutbox(IEmailOutbox inner, int failures) : IEmailOutbox
    {
        public int MarkCalls;
        public Task<int> EnqueueEditLinksAsync(string e, DateTime n, CancellationToken ct) => inner.EnqueueEditLinksAsync(e, n, ct);
        public Task<ClaimedEmail?> ClaimNextAsync(TimeSpan l, DateTime n, CancellationToken ct) => inner.ClaimNextAsync(l, n, ct);
        public Task<EditLinkRecipient?> GetEditLinkRecipientAsync(Guid p, CancellationToken ct) => inner.GetEditLinkRecipientAsync(p, ct);
        public Task<ReturnNoteResult> GetReturnNoteAsync(Guid r, CancellationToken ct) => inner.GetReturnNoteAsync(r, ct);
        public Task<ContactVerificationRecipient?> GetContactVerificationRecipientAsync(Guid r, DateTime n, CancellationToken ct) => inner.GetContactVerificationRecipientAsync(r, n, ct);
        public Task<ContactNotificationInfo?> GetContactNotificationAsync(Guid r, CancellationToken ct) => inner.GetContactNotificationAsync(r, ct);
        public Task<ContactDecisionRecipient?> GetContactDecisionRecipientAsync(Guid r, CancellationToken ct) => inner.GetContactDecisionRecipientAsync(r, ct);
        public Task<int> MarkSentAsync(ClaimedEmail c, DateTime n, CancellationToken ct)
            => ++MarkCalls <= failures ? throw new InvalidOperationException("db blip") : inner.MarkSentAsync(c, n, ct);
        public Task<int> RecordFailureAsync(ClaimedEmail c, string cat, bool a, bool t, int m, DateTime n, CancellationToken ct) => inner.RecordFailureAsync(c, cat, a, t, m, n, ct);
        public Task<OutboxOverview> GetOverviewAsync(int l, int o, CancellationToken ct) => inner.GetOverviewAsync(l, o, ct);
    }

    [Fact]
    public async Task A_failure_to_mark_sent_is_retried_without_sending_again_or_counting_a_failure()
    {
        var s = await OneQueuedRowAsync();
        await using (var scope = s.Factory.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var flaky = new FlakyMarkOutbox(sp.GetRequiredService<IEmailOutbox>(), failures: 2);
            var processor = new EmailOutboxProcessor(flaky, sp.GetRequiredService<IPersonalTokenIssuer>(), sp.GetRequiredService<IEmailSender>(),
                new EmailOutboxSettings(AuthApiFactory.PublicBaseUrl), TimeProvider.System, sp.GetRequiredService<ILogger<EmailOutboxProcessor>>());
            await processor.ProcessBatchAsync(CancellationToken.None);
            Assert.Equal(3, flaky.MarkCalls);
        }

        Assert.Single(s.Factory.Email.Messages);
        var row = Assert.Single(await Outbox(s.Factory));
        Assert.Equal(OutboxStatus.Sent, row.Status);
        Assert.Equal(0, row.Attempts);
    }

    private sealed class CancellingSender : IEmailSender
    {
        public Task<EmailSendResult> SendAsync(EmailMessage message, string idempotencyKey, CancellationToken ct)
            => throw new OperationCanceledException(); // as when the host is shutting down
    }

    [Fact]
    public async Task Cancellation_during_a_send_records_no_failed_attempt()
    {
        var s = await OneQueuedRowAsync();
        await using (var scope = s.Factory.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var processor = new EmailOutboxProcessor(sp.GetRequiredService<IEmailOutbox>(), sp.GetRequiredService<IPersonalTokenIssuer>(), new CancellingSender(),
                new EmailOutboxSettings(AuthApiFactory.PublicBaseUrl), TimeProvider.System, sp.GetRequiredService<ILogger<EmailOutboxProcessor>>());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processor.ProcessBatchAsync(CancellationToken.None));
        }

        var row = Assert.Single(await Outbox(s.Factory));
        Assert.Equal(0, row.Attempts);
        Assert.Null(row.LastErrorCategory);
    }

    private async Task RunWorkerAsync(AuthApiFactory f)
    {
        await using var scope = f.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var processor = new EmailOutboxProcessor(sp.GetRequiredService<IEmailOutbox>(), sp.GetRequiredService<IPersonalTokenIssuer>(),
            sp.GetRequiredService<IEmailSender>(), new EmailOutboxSettings(AuthApiFactory.PublicBaseUrl), TimeProvider.System,
            sp.GetRequiredService<ILogger<EmailOutboxProcessor>>());
        await processor.ProcessBatchAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Two_pending_rows_for_one_profile_sent_by_two_parallel_workers_leave_exactly_one_unrevoked_token()
    {
        var s = await OneQueuedRowAsync();
        await Sql(s.Factory, $"UPDATE email_outbox SET deduplication_key = deduplication_key || '-earlier'");
        await RequestLink(s.Factory.NewClient(), "dana@example.com");
        Assert.Equal(2, (await Outbox(s.Factory)).Count);
        s.Factory.Email.Delay = TimeSpan.FromMilliseconds(150);

        await Task.WhenAll(RunWorkerAsync(s.Factory), RunWorkerAsync(s.Factory));

        Assert.Equal(2, s.Factory.Email.Messages.Count);
        var tokens = await Db(s.Factory, d => d.PersonalTokens.AsNoTracking().ToListAsync());
        Assert.Equal(2, tokens.Count);
        Assert.Single(tokens, t => t.RevokedAt == null && t.UsedAt == null);
    }

    [Fact]
    public async Task Admin_outbox_pages_dead_rows_newest_first()
    {
        var s = await NewSetupAsync();
        for (var i = 0; i < 3; i++)
        {
            await SubmitProfileAsync(s.Factory, s.Token, $"p{i}@example.com");
            await RequestLink(s.Factory.NewClient(), $"p{i}@example.com");
        }
        var ids = (await Outbox(s.Factory)).Select(r => r.Id).ToList();
        for (var i = 0; i < 3; i++)
        {
            var id = ids[i];
            var minutes = 3 - i; // ids[0] is the oldest
            await Sql(s.Factory, $"UPDATE email_outbox SET status = 'Dead', attempts = 5, last_error_category = 'smtp-unavailable', created_at = now() - make_interval(mins => {minutes}) WHERE id = {id}");
        }

        async Task<JsonElement> Get(string query)
        {
            var response = await s.Admin.Http.GetAsync("/admin/email-outbox" + query);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        }

        var all = await Get("");
        Assert.Equal(3, all.GetProperty("total").GetInt32());
        Assert.Equal(new[] { ids[2], ids[1], ids[0] }, all.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToArray());

        var page = await Get("?limit=1&offset=1");
        Assert.Equal(3, page.GetProperty("total").GetInt32());
        Assert.Equal(3, page.GetProperty("counts").GetProperty("dead").GetInt32());
        Assert.Equal(ids[1], Assert.Single(page.GetProperty("items").EnumerateArray().ToList()).GetProperty("id").GetGuid());
    }
}
