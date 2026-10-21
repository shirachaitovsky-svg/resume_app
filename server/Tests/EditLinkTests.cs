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
public partial class EditLinkTests(PostgresFixture pg) : IDisposable
{
    private const string NewPassword = "Brand-New-Pass-42#Lk";
    private readonly List<AuthApiFactory> _factories = [];

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
    }

    private sealed record Setup(AuthApiFactory Factory, ApiClient Admin, string Token, string CohortId);

    private async Task<Setup> NewSetupAsync(IReadOnlyDictionary<string, string>? limits = null, bool emailWorker = false)
    {
        var factory = await AuthApiFactory.CreateAsync(pg, limits: limits, emailWorker: emailWorker);
        _factories.Add(factory);
        var admin = factory.NewClient();
        await admin.EnrollAsync(AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword, NewPassword);
        var (token, id) = await CreateCohortAsync(admin);
        return new(factory, admin, token, id);
    }

    private static async Task<(string Token, string Id)> CreateCohortAsync(ApiClient admin, string name = "מחזור")
    {
        var response = await admin.PostWithCsrfAsync("/admin/cohorts", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (json.GetProperty("inviteToken").GetString()!, json.GetProperty("id").GetString()!);
    }

    private static async Task SubmitProfileAsync(AuthApiFactory factory, string inviteToken, string email)
    {
        var response = await factory.NewClient().PostWithCsrfAsync($"/invites/{inviteToken}/profiles", new
        {
            name = "דנה כהן",
            productName = "מוצר",
            productUrl = "https://example.com/app",
            email,
            phone = "050-123 4567",
        });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private static Task<HttpResponseMessage> RequestLink(ApiClient client, string? email)
        => client.PostWithCsrfAsync("/profiles/edit-link", new { email });

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

    private static string TokenFrom(FakeEmailSender.Sent sent)
    {
        var match = Regex.Match(sent.Message.TextBody, @"/edit#token=([A-Za-z0-9_-]{43})(?![A-Za-z0-9_-])");
        Assert.True(match.Success, "no token link in the body");
        return match.Groups[1].Value;
    }

    // ---- request endpoint -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Registered_email_gets_202_ok_and_one_outbox_row_without_a_token_or_sending()
    {
        var s = await NewSetupAsync();
        await SubmitProfileAsync(s.Factory, s.Token, "dana@example.com");

        var response = await RequestLink(s.Factory.NewClient(), " Dana@Example.COM ");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["status"], json.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("ok", json.GetProperty("status").GetString());

        var rows = await Outbox(s.Factory);
        var row = Assert.Single(rows);
        Assert.Equal("edit-link", row.TemplateKey);
        Assert.Equal(OutboxStatus.Pending, row.Status);
        Assert.Equal(0, row.Attempts);
        Assert.Matches(@"^edit-link:[0-9a-f-]{36}:\d{10}$", row.DeduplicationKey);
        Assert.Equal(0, await Db(s.Factory, d => d.PersonalTokens.CountAsync()));
        Assert.Empty(s.Factory.Email.Messages); // nothing is sent from the request path
        Assert.Equal(0, s.Factory.Email.Attempts);
    }

    [Fact]
    public async Task Unregistered_email_gets_the_identical_response_and_no_row()
    {
        var s = await NewSetupAsync();
        await SubmitProfileAsync(s.Factory, s.Token, "dana@example.com");

        var known = await RequestLink(s.Factory.NewClient(), "dana@example.com");
        var unknown = await RequestLink(s.Factory.NewClient(), "nobody@example.com");

        Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        Assert.Equal(known.Content.Headers.ContentType, unknown.Content.Headers.ContentType);
        var headers = (HttpResponseMessage r) => r.Headers.Select(h => h.Key + "=" + string.Join(",", h.Value)).OrderBy(x => x).ToArray();
        Assert.Equal(headers(known), headers(unknown));
        Assert.Single(await Outbox(s.Factory)); // only the registered address wrote a row
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("a@b")]
    public async Task Invalid_email_is_a_400_validation_error(string? email)
    {
        var s = await NewSetupAsync();
        var response = await RequestLink(s.Factory.NewClient(), email);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("errors").TryGetProperty("email", out _));
        Assert.Empty(await Outbox(s.Factory));
    }

    [Fact]
    public async Task Malformed_json_is_400_and_a_non_json_body_is_415()
    {
        var s = await NewSetupAsync();
        var client = s.Factory.NewClient();
        var csrf = await client.GetCsrfAsync();

        var bad = new HttpRequestMessage(HttpMethod.Post, "/profiles/edit-link") { Content = new StringContent("{nope", System.Text.Encoding.UTF8, "application/json") };
        bad.Headers.Add("Origin", ApiClient.Origin);
        bad.Headers.Add("X-CSRF-TOKEN", csrf);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.Http.SendAsync(bad)).StatusCode);

        var text = new HttpRequestMessage(HttpMethod.Post, "/profiles/edit-link") { Content = new StringContent("email=a@b.co", System.Text.Encoding.UTF8, "text/plain") };
        text.Headers.Add("Origin", ApiClient.Origin);
        text.Headers.Add("X-CSRF-TOKEN", csrf);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await client.Http.SendAsync(text)).StatusCode);
    }

    [Fact]
    public async Task Five_parallel_requests_for_one_address_write_one_row()
    {
        var s = await NewSetupAsync();
        await SubmitProfileAsync(s.Factory, s.Token, "dana@example.com");

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => RequestLink(s.Factory.NewClient(), "dana@example.com")));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Accepted, r.StatusCode));
        Assert.Single(await Outbox(s.Factory));
    }

    [Fact]
    public async Task Removed_profiles_are_not_queued_but_active_ones_without_an_approved_revision_are()
    {
        var s = await NewSetupAsync();
        await SubmitProfileAsync(s.Factory, s.Token, "gone@example.com");
        await SubmitProfileAsync(s.Factory, s.Token, "here@example.com");
        await Sql(s.Factory, $"UPDATE profiles SET visibility = 'Removed' WHERE id = (SELECT profile_id FROM participant_private WHERE email = 'gone@example.com')");

        await RequestLink(s.Factory.NewClient(), "gone@example.com");
        Assert.Empty(await Outbox(s.Factory));

        await RequestLink(s.Factory.NewClient(), "here@example.com");
        Assert.Single(await Outbox(s.Factory));
    }

    [Fact]
    public async Task One_email_in_two_cohorts_queues_one_row_per_active_profile()
    {
        var s = await NewSetupAsync();
        var (second, _) = await CreateCohortAsync(s.Admin, "מחזור ב");
        await SubmitProfileAsync(s.Factory, s.Token, "dana@example.com");
        await SubmitProfileAsync(s.Factory, second, "dana@example.com");

        await RequestLink(s.Factory.NewClient(), "dana@example.com");

        var rows = await Outbox(s.Factory);
        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows.Select(r => r.DeduplicationKey).Distinct().Count());
    }

    [Fact]
    public async Task Email_limit_returns_429_with_retry_after_and_code_and_does_not_affect_other_addresses()
    {
        var s = await NewSetupAsync(new Dictionary<string, string> { ["RATE_LIMIT_EDIT_LINK_EMAIL"] = "2" });

        Assert.Equal(HttpStatusCode.Accepted, (await RequestLink(s.Factory.NewClient(), "a@example.com")).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await RequestLink(s.Factory.NewClient(), "A@example.com ")).StatusCode); // same normalised address
        var limited = await RequestLink(s.Factory.NewClient(), "a@example.com");

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.TryGetValues("Retry-After", out var retry) && int.Parse(retry.Single()) >= 1);
        Assert.Equal("rate-limited", (await limited.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Accepted, (await RequestLink(s.Factory.NewClient(), "b@example.com")).StatusCode);
    }

    [Fact]
    public async Task Ip_limit_returns_429_across_different_addresses()
    {
        var s = await NewSetupAsync(new Dictionary<string, string> { ["RATE_LIMIT_EDIT_LINK_IP"] = "2" });

        Assert.Equal(HttpStatusCode.Accepted, (await RequestLink(s.Factory.NewClient(), "a@example.com")).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await RequestLink(s.Factory.NewClient(), "b@example.com")).StatusCode);
        var limited = await RequestLink(s.Factory.NewClient(), "c@example.com");

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task Missing_turnstile_origin_or_csrf_are_rejected_and_write_nothing()
    {
        var s = await NewSetupAsync();
        await SubmitProfileAsync(s.Factory, s.Token, "dana@example.com");

        var noBot = s.Factory.NewClient();
        noBot.Http.DefaultRequestHeaders.Remove("X-Turnstile-Token");
        Assert.Equal(HttpStatusCode.Forbidden, (await RequestLink(noBot, "dana@example.com")).StatusCode);

        var client = s.Factory.NewClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/profiles/edit-link", new { email = "dana@example.com" }, await client.GetCsrfAsync(), origin: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/profiles/edit-link", new { email = "dana@example.com" }, csrf: null)).StatusCode);

        Assert.Empty(await Outbox(s.Factory));
    }

    // ---- sending ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Sending_issues_a_hashed_token_and_sends_a_plain_text_email_with_a_fragment_link()
    {
        var s = await NewSetupAsync();
        await SubmitProfileAsync(s.Factory, s.Token, "dana@example.com");
        await RequestLink(s.Factory.NewClient(), "Dana@Example.com");

        Assert.Equal(1, await ProcessAsync(s.Factory));

        var sent = Assert.Single(s.Factory.Email.Messages);
        Assert.Equal("dana@example.com", sent.Message.To);
        Assert.Equal(EditLinkEmail.Subject, sent.Message.Subject);
        var token = TokenFrom(sent);
        Assert.Contains($"{AuthApiFactory.PublicBaseUrl}/edit#token={token}", sent.Message.TextBody);
        Assert.DoesNotContain("?token", sent.Message.TextBody);
        Assert.DoesNotContain("<", sent.Message.TextBody);

        var row = Assert.Single(await Outbox(s.Factory));
        Assert.Equal(OutboxStatus.Sent, row.Status);
        Assert.NotNull(row.SentAt);
        Assert.Null(row.LeaseUntil);
        Assert.Equal($"{row.Id:D}:1", sent.IdempotencyKey); // row id plus claim number

        var stored = Assert.Single(await Db(s.Factory, d => d.PersonalTokens.AsNoTracking().ToListAsync()));
        Assert.Equal(PersonalTokens.Hash(token), stored.TokenHash);
        Assert.NotEqual(token, stored.TokenHash);
        Assert.Equal("edit-profile", stored.Purpose);
        Assert.Null(stored.UsedAt);
        Assert.Null(stored.RevokedAt);
        Assert.InRange((stored.ExpiresAt - stored.CreatedAt).TotalHours, 23.99, 24.01);
    }

    [Fact]
    public async Task Neither_the_outbox_nor_the_token_table_holds_the_raw_token_or_the_email()
    {
        var s = await NewSetupAsync();
        await SubmitProfileAsync(s.Factory, s.Token, "dana@example.com");
        await RequestLink(s.Factory.NewClient(), "dana@example.com");
        await ProcessAsync(s.Factory);
        var token = TokenFrom(s.Factory.Email.Messages.Single());

        var dump = await Db(s.Factory, async d =>
            string.Join("\n", await d.Database.SqlQuery<string>($"SELECT row_to_json(e)::text AS \"Value\" FROM email_outbox e").ToListAsync())
            + string.Join("\n", await d.Database.SqlQuery<string>($"SELECT row_to_json(t)::text AS \"Value\" FROM personal_tokens t").ToListAsync()));

        Assert.DoesNotContain(token, dump);
        Assert.DoesNotContain("dana", dump, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("example.com", dump);
    }

    [Fact]
    public async Task A_newer_token_for_the_same_profile_revokes_the_earlier_unused_one()
    {
        var s = await NewSetupAsync();
        await SubmitProfileAsync(s.Factory, s.Token, "dana@example.com");
        await RequestLink(s.Factory.NewClient(), "dana@example.com");
        await ProcessAsync(s.Factory);
        // A request in a later hour has another deduplication key.
        await Sql(s.Factory, $"UPDATE email_outbox SET deduplication_key = deduplication_key || '-earlier'");
        await RequestLink(s.Factory.NewClient(), "dana@example.com");
        await ProcessAsync(s.Factory);

        var messages = s.Factory.Email.Messages;
        Assert.Equal(2, messages.Count);
        var first = PersonalTokens.Hash(TokenFrom(messages[0]));
        var second = PersonalTokens.Hash(TokenFrom(messages[1]));
        var tokens = await Db(s.Factory, d => d.PersonalTokens.AsNoTracking().ToListAsync());
        Assert.NotNull(tokens.Single(t => t.TokenHash == first).RevokedAt);
        Assert.Null(tokens.Single(t => t.TokenHash == second).RevokedAt);
    }

    [Fact]
    public async Task A_transient_failure_counts_an_attempt_and_backs_off_then_the_retry_succeeds()
    {
        var s = await NewSetupAsync();
        await SubmitProfileAsync(s.Factory, s.Token, "dana@example.com");
        await RequestLink(s.Factory.NewClient(), "dana@example.com");
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
        await SubmitProfileAsync(s.Factory, s.Token, "dana@example.com");
        await RequestLink(s.Factory.NewClient(), "dana@example.com");
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
        await SubmitProfileAsync(s.Factory, s.Token, "dana@example.com");
        await RequestLink(s.Factory.NewClient(), "dana@example.com");
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
        await SubmitProfileAsync(s.Factory, s.Token, "dana@example.com");
        await RequestLink(s.Factory.NewClient(), "dana@example.com");

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
        Assert.Equal("edit-link", item.GetProperty("templateKey").GetString());
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
        await SubmitProfileAsync(s.Factory, s.Token, "dana@example.com");
        await RequestLink(s.Factory.NewClient(), "dana@example.com");
        await Sql(s.Factory, $"UPDATE profiles SET visibility = 'Removed'");

        await ProcessAsync(s.Factory);

        Assert.Empty(s.Factory.Email.Messages);
        Assert.Equal(0, await Db(s.Factory, d => d.PersonalTokens.CountAsync()));
        Assert.Equal(OutboxStatus.Dead, Assert.Single(await Outbox(s.Factory)).Status);
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
        await SubmitProfileAsync(s.Factory, s.Token, "dana@example.com");
        await RequestLink(s.Factory.NewClient(), "dana@example.com");

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
        for (var i = 0; i < count; i++) await SubmitProfileAsync(s.Factory, s.Token, $"p{i}@example.com");
        for (var i = 0; i < count; i++) await RequestLink(s.Factory.NewClient(), $"p{i}@example.com");
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
        Assert.Equal(count, await Db(s.Factory, d => d.PersonalTokens.CountAsync()));
    }

    [Fact]
    public async Task The_background_worker_sends_pending_rows_on_its_own()
    {
        var s = await NewSetupAsync(emailWorker: true);
        await SubmitProfileAsync(s.Factory, s.Token, "dana@example.com");
        await RequestLink(s.Factory.NewClient(), "dana@example.com");

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
        await SubmitProfileAsync(s.Factory, s.Token, "dana@example.com");
        await RequestLink(s.Factory.NewClient(), "dana@example.com");
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

    [Fact]
    public void The_link_puts_the_token_in_the_fragment_only()
    {
        var link = EditLinkEmail.Link("https://app.example.test/", "abc");
        Assert.Equal("https://app.example.test/edit#token=abc", link);
        Assert.DoesNotContain('\r', EditLinkEmail.Subject);
        Assert.DoesNotContain('\n', EditLinkEmail.Subject);
    }
}
