using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ResumeApp.Domain.Email;
using ResumeApp.Features.Email;
using ResumeApp.Infrastructure;

namespace ResumeApp.Tests;

[Collection("env")]
/// <summary>The generic email outbox and worker (claims, leases, retries, dead rows), exercised through the "submission-returned" template (story 6.4 moved it off the removed edit-link template).</summary>
public partial class EmailOutboxTests(PostgresFixture pg) : IDisposable
{
    private const string NewPassword = "Brand-New-Pass-42#Lk";
    private readonly List<AuthApiFactory> _factories = [];

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
    }

    private sealed record Setup(AuthApiFactory Factory, ApiClient Admin);

    private async Task<Setup> NewSetupAsync(IReadOnlyDictionary<string, string>? limits = null, bool emailWorker = false)
    {
        var factory = await AuthApiFactory.CreateAsync(pg, limits: limits, emailWorker: emailWorker);
        _factories.Add(factory);
        var admin = factory.NewClient();
        await admin.EnrollAsync(AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword, NewPassword);
        return new(factory, admin);
    }

    /// <summary>Seeds a pending profile and returns its revision as the admin does, which queues one "submission-returned" outbox row.</summary>
    private static async Task<Guid> QueueMailAsync(Setup s, string email, Guid? cohortId = null)
    {
        var seeded = await ParticipantSeeding.SeedProfileAsync(s.Factory, cohortId, email: email, googleEmail: "");
        var response = await s.Admin.PostWithCsrfAsync($"/admin/submissions/{seeded.RevisionId}/return", new { note = "הערה" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return seeded.ProfileId;
    }

    private static async Task<T> Db<T>(AuthApiFactory factory, Func<AppDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static Task<List<EmailOutboxEntry>> Outbox(AuthApiFactory f) => Db(f, d => d.EmailOutbox.AsNoTracking().OrderBy(e => e.CreatedAt).ToListAsync());

    private static async Task<int> ProcessAsync(AuthApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<EmailOutboxProcessor>().ProcessBatchAsync(CancellationToken.None);
    }

    private static Task Sql(AuthApiFactory f, FormattableString sql) => Db(f, async d => await d.Database.ExecuteSqlInterpolatedAsync(sql));

    private static Task MakeDue(AuthApiFactory f) => Sql(f, $"UPDATE email_outbox SET next_attempt_at = now() - interval '1 minute', lease_until = NULL");

    // ---- sending ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Sending_sends_a_plain_text_email_and_marks_the_row_sent_without_any_token()
    {
        var s = await NewSetupAsync();
        await QueueMailAsync(s, "Dana@Example.com");

        Assert.Equal(1, await ProcessAsync(s.Factory));

        var sent = Assert.Single(s.Factory.Email.Messages);
        Assert.Equal("dana@example.com", sent.Message.To);
        Assert.Equal(SubmissionReturnedEmail.Subject, sent.Message.Subject);
        Assert.DoesNotContain("<", sent.Message.TextBody);
        Assert.DoesNotContain("#token", sent.Message.TextBody);

        var row = Assert.Single(await Outbox(s.Factory));
        Assert.Equal(OutboxStatus.Sent, row.Status);
        Assert.NotNull(row.SentAt);
        Assert.Null(row.LeaseUntil);
        Assert.Equal($"{row.Id:D}:1", sent.IdempotencyKey); // row id plus claim number
        Assert.Equal(0, await Db(s.Factory, d => d.PersonalTokens.CountAsync()));
    }

    [Fact]
    public async Task The_outbox_table_holds_neither_the_email_nor_the_note()
    {
        var s = await NewSetupAsync();
        await QueueMailAsync(s, "dana@example.com");
        await ProcessAsync(s.Factory);

        var dump = await Db(s.Factory, async d =>
            string.Join("\n", await d.Database.SqlQuery<string>($"SELECT row_to_json(e)::text AS \"Value\" FROM email_outbox e").ToListAsync()));

        Assert.DoesNotContain("dana", dump, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("example.com", dump);
        Assert.DoesNotContain("הערה", dump);
    }

    [Fact]
    public async Task A_transient_failure_counts_an_attempt_and_backs_off_then_the_retry_succeeds()
    {
        var s = await NewSetupAsync();
        await QueueMailAsync(s, "dana@example.com");
        s.Factory.Email.Script(EmailSendResult.Failed("smtp-unavailable"));

        Assert.Equal(1, await ProcessAsync(s.Factory));

        var row = Assert.Single(await Outbox(s.Factory));
        Assert.Equal(OutboxStatus.Pending, row.Status);
        Assert.Equal(1, row.Attempts);
        Assert.Equal("smtp-unavailable", row.LastErrorCategory);
        Assert.Null(row.LeaseUntil);
        Assert.True(row.NextAttemptAt > DateTime.UtcNow.AddSeconds(15));
        Assert.Equal(0, await ProcessAsync(s.Factory)); // not due yet

        await MakeDue(s.Factory);
        Assert.Equal(1, await ProcessAsync(s.Factory));
        Assert.Equal(OutboxStatus.Sent, Assert.Single(await Outbox(s.Factory)).Status);
        Assert.Single(s.Factory.Email.Messages);
    }

    [Fact]
    public async Task A_provider_429_defers_without_counting_a_failed_attempt()
    {
        var s = await NewSetupAsync();
        await QueueMailAsync(s, "dana@example.com");
        s.Factory.Email.Script(EmailSendResult.RateLimited(TimeSpan.FromMinutes(10)));

        await ProcessAsync(s.Factory);

        var row = Assert.Single(await Outbox(s.Factory));
        Assert.Equal(OutboxStatus.Pending, row.Status);
        Assert.Equal(0, row.Attempts);
        Assert.InRange((row.NextAttemptAt - DateTime.UtcNow).TotalMinutes, 9, 10.1);
    }

    [Fact]
    public async Task Repeated_429s_never_make_a_row_dead()
    {
        var s = await NewSetupAsync();
        await QueueMailAsync(s, "dana@example.com");
        for (var i = 0; i < 8; i++)
        {
            s.Factory.Email.Script(EmailSendResult.RateLimited(null));
            await MakeDue(s.Factory);
            await ProcessAsync(s.Factory);
        }

        var row = Assert.Single(await Outbox(s.Factory));
        Assert.Equal(OutboxStatus.Pending, row.Status);
        Assert.Equal(0, row.Attempts);
    }

    [Fact]
    public async Task Five_failed_attempts_make_the_row_dead_and_visible_to_the_admin_without_private_data()
    {
        var s = await NewSetupAsync();
        await QueueMailAsync(s, "dana@example.com");

        for (var i = 1; i <= 5; i++)
        {
            s.Factory.Email.Script(EmailSendResult.Failed("smtp-unavailable"));
            await MakeDue(s.Factory);
            Assert.Equal(1, await ProcessAsync(s.Factory));
            var current = Assert.Single(await Outbox(s.Factory));
            Assert.Equal(i, current.Attempts);
            Assert.Equal(i < 5 ? OutboxStatus.Pending : OutboxStatus.Dead, current.Status);
        }
        await MakeDue(s.Factory);
        Assert.Equal(0, await ProcessAsync(s.Factory)); // dead rows are never claimed again

        var response = await s.Admin.Http.GetAsync("/admin/email-outbox");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(text).RootElement;
        Assert.Equal(0, json.GetProperty("counts").GetProperty("pending").GetInt32());
        Assert.Equal(0, json.GetProperty("counts").GetProperty("sent").GetInt32());
        Assert.Equal(1, json.GetProperty("counts").GetProperty("dead").GetInt32());
        Assert.Equal(1, json.GetProperty("total").GetInt32());
        var item = Assert.Single(json.GetProperty("items").EnumerateArray().ToList());
        Assert.Equal("submission-returned", item.GetProperty("templateKey").GetString());
        Assert.Equal(5, item.GetProperty("attempts").GetInt32());
        Assert.Equal("smtp-unavailable", item.GetProperty("lastErrorCategory").GetString());
        Assert.True(item.TryGetProperty("createdAt", out _));
        Assert.DoesNotContain("dana", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", text.Replace("lastErrorCategory", ""), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_removed_profile_is_never_emailed()
    {
        var s = await NewSetupAsync();
        await QueueMailAsync(s, "dana@example.com");
        await Sql(s.Factory, $"UPDATE profiles SET visibility = 'Removed'");

        await ProcessAsync(s.Factory);

        Assert.Empty(s.Factory.Email.Messages);
        Assert.Equal(0, await Db(s.Factory, d => d.PersonalTokens.CountAsync()));
        var row = Assert.Single(await Outbox(s.Factory));
        Assert.Equal(OutboxStatus.Dead, row.Status);
        Assert.Equal(EmailOutboxProcessor.CategoryNoRecipient, row.LastErrorCategory);
    }

    [Fact]
    public async Task Admin_outbox_requires_an_authenticated_admin_and_validates_paging()
    {
        var s = await NewSetupAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Factory.NewClient().Http.GetAsync("/admin/email-outbox")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Admin.Http.GetAsync("/admin/email-outbox?limit=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Admin.Http.GetAsync("/admin/email-outbox?offset=-1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.Admin.Http.GetAsync("/admin/email-outbox")).StatusCode);
    }

    // ---- claim, lease, concurrency ---------------------------------------------------------------------------------

    [Fact]
    public async Task A_claimed_row_is_not_claimable_until_its_lease_expires()
    {
        var s = await NewSetupAsync();
        await QueueMailAsync(s, "dana@example.com");

        async Task<int> Claim()
        {
            await using var scope = s.Factory.Services.CreateAsyncScope();
            return (await scope.ServiceProvider.GetRequiredService<IEmailOutbox>().ClaimNextAsync(TimeSpan.FromMinutes(2), DateTime.UtcNow, CancellationToken.None)) is null ? 0 : 1;
        }

        Assert.Equal(1, await Claim());
        Assert.Equal(0, await Claim()); // leased by the first claimer
        Assert.NotNull(Assert.Single(await Outbox(s.Factory)).LeaseUntil);

        // The claiming instance "crashed": its lease passes.
        await Sql(s.Factory, $"UPDATE email_outbox SET lease_until = now() - interval '1 second'");
        Assert.Equal(1, await Claim());
    }

    [Fact]
    public async Task Two_workers_in_parallel_send_every_row_exactly_once()
    {
        var s = await NewSetupAsync();
        const int count = 8;
        for (var i = 0; i < count; i++) await QueueMailAsync(s, $"p{i}@example.com");
        s.Factory.Email.Delay = TimeSpan.FromMilliseconds(80);

        async Task Worker()
        {
            await using var scope = s.Factory.Services.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var processor = new EmailOutboxProcessor(sp.GetRequiredService<IEmailOutbox>(), sp.GetRequiredService<IPersonalTokenIssuer>(),
                sp.GetRequiredService<IEmailSender>(), new EmailOutboxSettings(AuthApiFactory.PublicBaseUrl), TimeProvider.System,
                sp.GetRequiredService<ILogger<EmailOutboxProcessor>>());
            while (await processor.ProcessBatchAsync(CancellationToken.None) > 0) { }
        }

        await Task.WhenAll(Worker(), Worker());

        var messages = s.Factory.Email.Messages;
        Assert.Equal(count, messages.Count);
        Assert.Equal(count, messages.Select(m => m.IdempotencyKey).Distinct().Count());
        Assert.Equal(count, messages.Select(m => m.Message.To).Distinct().Count());
        Assert.All(await Outbox(s.Factory), r => Assert.Equal(OutboxStatus.Sent, r.Status));
    }

    [Fact]
    public async Task The_background_worker_sends_pending_rows_on_its_own()
    {
        var s = await NewSetupAsync(emailWorker: true);
        await QueueMailAsync(s, "dana@example.com");

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (s.Factory.Email.Messages.Count == 0 && DateTime.UtcNow < deadline) await Task.Delay(200);

        Assert.Single(s.Factory.Email.Messages);
    }

    // ---- unexpected errors and logs --------------------------------------------------------------------------------

    private sealed class ThrowingSender : IEmailSender
    {
        public Task<EmailSendResult> SendAsync(EmailMessage message, string idempotencyKey, CancellationToken ct)
            => throw new InvalidOperationException("boom for " + message.To);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Lines { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Lines.Add(formatter(state, exception) + (exception?.ToString() ?? ""));
    }

    [Fact]
    public async Task An_unexpected_sender_error_is_recorded_as_a_failure_and_the_log_has_no_address_or_token()
    {
        var s = await NewSetupAsync();
        await QueueMailAsync(s, "dana@example.com");
        var logger = new CapturingLogger<EmailOutboxProcessor>();

        await using (var scope = s.Factory.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var processor = new EmailOutboxProcessor(sp.GetRequiredService<IEmailOutbox>(), sp.GetRequiredService<IPersonalTokenIssuer>(),
                new ThrowingSender(), new EmailOutboxSettings(AuthApiFactory.PublicBaseUrl), TimeProvider.System, logger);
            await processor.ProcessBatchAsync(CancellationToken.None);
        }

        var row = Assert.Single(await Outbox(s.Factory));
        Assert.Equal(1, row.Attempts);
        Assert.Equal("unexpected-error", row.LastErrorCategory);
        Assert.NotEmpty(logger.Lines);
        Assert.All(logger.Lines, l =>
        {
            Assert.DoesNotContain("dana", l, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("example.com", l);
        });
    }

    // ---- units -----------------------------------------------------------------------------------------------------

    [Fact]
    public void Tokens_are_256_bit_base64url_and_hash_to_64_hex_characters()
    {
        var tokens = Enumerable.Range(0, 50).Select(_ => PersonalTokens.Generate()).ToList();
        Assert.Equal(50, tokens.Distinct().Count());
        Assert.All(tokens, t =>
        {
            Assert.Equal(43, t.Length); // 32 bytes
            Assert.Matches("^[A-Za-z0-9_-]{43}$", t);
            Assert.Equal(32, Convert.FromBase64String(t.Replace('-', '+').Replace('_', '/') + "=").Length);
            Assert.Matches("^[0-9a-f]{64}$", PersonalTokens.Hash(t));
        });
    }

    [Fact]
    public void Backoff_grows_exponentially_up_to_a_ceiling()
    {
        var processor = new EmailOutboxProcessor(null!, null!, null!, new EmailOutboxSettings("https://x.test", BaseDelay: TimeSpan.FromSeconds(30), MaxDelay: TimeSpan.FromMinutes(5)),
            TimeProvider.System, new CapturingLogger<EmailOutboxProcessor>());
        Assert.Equal(TimeSpan.FromSeconds(30), processor.Backoff(1));
        Assert.Equal(TimeSpan.FromSeconds(60), processor.Backoff(2));
        Assert.Equal(TimeSpan.FromSeconds(120), processor.Backoff(3));
        Assert.Equal(TimeSpan.FromMinutes(5), processor.Backoff(6));
        Assert.Equal(TimeSpan.FromMinutes(5), processor.Backoff(1000));
    }

}
