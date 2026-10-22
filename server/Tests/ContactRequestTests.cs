using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResumeApp.Domain.Contacts;
using ResumeApp.Domain.Email;
using ResumeApp.Features.Contacts;
using ResumeApp.Api.Endpoints;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using ResumeApp.Features.Email;
using ResumeApp.Infrastructure;

namespace ResumeApp.Tests;

/// <summary>Story 3.1: contact request submission, email verification, expiry and removal.</summary>
[Collection("env")]
public class ContactRequestTests(PostgresFixture pg) : IDisposable
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

    private async Task<Setup> NewSetupAsync(IReadOnlyDictionary<string, string>? limits = null, bool emailWorker = false, Action<IServiceCollection>? configure = null)
    {
        var factory = await AuthApiFactory.CreateAsync(pg, limits: limits, emailWorker: emailWorker, configureServices: configure);
        _factories.Add(factory);
        var admin = factory.NewClient();
        await admin.EnrollAsync(AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword, NewPassword);
        var cohort = await (await admin.PostWithCsrfAsync("/admin/cohorts", new { name = "מחזור" })).Content.ReadFromJsonAsync<JsonElement>();
        var profileId = await NewProfileAsync(factory, cohort.GetProperty("inviteToken").GetString()!, "dana@example.com");
        return new(factory, admin, profileId);
    }

    private static async Task<Guid> NewProfileAsync(AuthApiFactory factory, string inviteToken, string email)
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
        return await Db(factory, d => d.ParticipantPrivates.AsNoTracking().Where(p => p.Email == email).Select(p => p.ProfileId).SingleAsync());
    }

    private static async Task<T> Db<T>(AuthApiFactory factory, Func<AppDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static Task Sql(AuthApiFactory f, FormattableString sql) => Db(f, async d => await d.Database.ExecuteSqlInterpolatedAsync(sql));

    private static Task<List<ContactRequest>> Requests(AuthApiFactory f) => Db(f, d => d.ContactRequests.AsNoTracking().OrderBy(r => r.CreatedAt).ToListAsync());

    private static Task<List<EmailOutboxEntry>> Outbox(AuthApiFactory f) => Db(f, d => d.EmailOutbox.AsNoTracking().OrderBy(e => e.CreatedAt).ToListAsync());

    private static Task<List<PersonalToken>> Tokens(AuthApiFactory f, string purpose)
        => Db(f, d => d.PersonalTokens.AsNoTracking().Where(t => t.Purpose == purpose).OrderBy(t => t.CreatedAt).ToListAsync());

    private static Task<HttpResponseMessage> Submit(ApiClient client, string profileId, string? name = VisitorName, string? email = Visitor, string? reason = Reason)
        => client.PostWithCsrfAsync($"/profiles/{profileId}/contact-requests", new { name, email, reason });

    private static Task<HttpResponseMessage> Verify(ApiClient client, string? token) => client.PostWithCsrfAsync("/contact-requests/verify", new { token });

    private static async Task<int> ProcessAsync(AuthApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<EmailOutboxProcessor>().ProcessBatchAsync(CancellationToken.None);
    }

    private static async Task<int> SweepAsync(AuthApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ContactRequestService>().ExpireStaleAsync(CancellationToken.None);
    }

    private static string TokenFrom(FakeEmailSender.Sent sent)
    {
        var match = Regex.Match(sent.Message.TextBody, @"/contact/verify#token=([A-Za-z0-9_-]{43})(?![A-Za-z0-9_-])");
        Assert.True(match.Success, "no verification link in the body");
        return match.Groups[1].Value;
    }

    /// <summary>Submits, sends the mail through the worker and returns the raw token.</summary>
    private static async Task<string> SubmitAndMailAsync(Setup s, string email = Visitor)
    {
        Assert.Equal(HttpStatusCode.Accepted, (await Submit(s.Factory.NewClient(), s.ProfileId.ToString("D"), email: email)).StatusCode);
        await ProcessAsync(s.Factory);
        return TokenFrom(s.Factory.Email.Messages.Last(m => m.Message.To == email));
    }

    /// <summary>Only the headers that matter for sameness: content type, caching and referrer policy.</summary>
    private static string[] HeaderLines(HttpResponseMessage r) =>
        [r.Content.Headers.ContentType?.ToString() ?? "", r.Headers.CacheControl?.ToString() ?? "", string.Join(",", r.Headers.TryGetValues("Referrer-Policy", out var v) ? v : [])];

    // ---- submission -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_valid_request_for_an_active_profile_is_202_and_writes_one_request_and_one_outbox_row_without_secrets()
    {
        var s = await NewSetupAsync();

        var response = await Submit(s.Factory.NewClient(), s.ProfileId.ToString("D"), email: "  Visitor@Example.COM ");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["status"], json.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("verification-sent", json.GetProperty("status").GetString());

        var request = Assert.Single(await Requests(s.Factory));
        Assert.Equal(ContactRequestStatus.AwaitingEmailVerification, request.Status);
        Assert.Equal(s.ProfileId, request.ProfileId);
        Assert.Equal(Visitor, request.VisitorEmail);
        Assert.Equal(VisitorName, request.VisitorName);
        Assert.Equal(Reason, request.Reason);
        Assert.Null(request.VerifiedAt);
        Assert.InRange((request.ExpiresAt - request.CreatedAt).TotalHours, 23.99, 24.01);

        var row = Assert.Single(await Outbox(s.Factory));
        Assert.Equal("contact-verification", row.TemplateKey);
        Assert.Equal(request.Id, row.RequestId);
        Assert.Null(row.ProfileId);
        Assert.Equal($"contact-verification:{request.Id:D}", row.DeduplicationKey);
        Assert.Equal(OutboxStatus.Pending, row.Status);
        Assert.Empty(await Tokens(s.Factory, "verify-contact-email")); // the token is issued at send time
        Assert.Empty(s.Factory.Email.Messages); // nothing is sent from the request path

        // Privacy: nothing about the visitor is in the outbox row.
        var text = await Db(s.Factory, async d =>
            string.Join("|", await d.Database.SqlQuery<string>($"SELECT concat_ws('|', template_key, deduplication_key, last_error_category) AS \"Value\" FROM email_outbox").ToListAsync()));
        Assert.DoesNotContain("visitor", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(VisitorName, text);
    }

    [Fact]
    public async Task Active_removed_unknown_and_malformed_profiles_get_the_identical_response()
    {
        var s = await NewSetupAsync();
        var removed = await NewProfileAsync(s.Factory, await InviteOf(s), "gone@example.com");
        await Sql(s.Factory, $"UPDATE profiles SET visibility = 'Removed' WHERE id = {removed}");

        var active = await Submit(s.Factory.NewClient(), s.ProfileId.ToString("D"));
        var gone = await Submit(s.Factory.NewClient(), removed.ToString("D"));
        var unknown = await Submit(s.Factory.NewClient(), Guid.NewGuid().ToString("D"));
        var malformed = await Submit(s.Factory.NewClient(), "not-a-guid");
        var compact = await Submit(s.Factory.NewClient(), s.ProfileId.ToString("N"));

        foreach (var other in new[] { gone, unknown, malformed, compact })
        {
            Assert.Equal(active.StatusCode, other.StatusCode);
            Assert.Equal(await active.Content.ReadAsStringAsync(), await other.Content.ReadAsStringAsync());
            Assert.Equal(active.Content.Headers.ContentType, other.Content.Headers.ContentType);
            Assert.Equal(HeaderLines(active), HeaderLines(other));
        }
        Assert.Equal(HttpStatusCode.Accepted, active.StatusCode);
        Assert.Equal(s.ProfileId, Assert.Single(await Requests(s.Factory)).ProfileId); // only the active one created anything
        Assert.Single(await Outbox(s.Factory));
    }

    private async Task<string> InviteOf(Setup s)
        => await Db(s.Factory, d => d.Cohorts.AsNoTracking().Select(c => c.InviteToken).SingleAsync());

    public static TheoryData<string?, string?, string?, string> InvalidBodies => new()
    {
        { null, Visitor, Reason, "name" },
        { "  ", Visitor, Reason, "name" },
        { new string('א', 101), Visitor, Reason, "name" },
        { "שם\u0000", Visitor, Reason, "name" },
        { "שם‮גרוע", Visitor, Reason, "name" },
        { "שם\nשורה", Visitor, Reason, "name" },
        { VisitorName, null, Reason, "email" },
        { VisitorName, "not-an-email", Reason, "email" },
        { VisitorName, "a@b", Reason, "email" },
        { VisitorName, Visitor, null, "reason" },
        { VisitorName, Visitor, "   ", "reason" },
        { VisitorName, Visitor, new string('א', 1001), "reason" },
        { VisitorName, Visitor, "ראו http://x.example", "reason" },
        { VisitorName, Visitor, "ראו HTTPS://x.example", "reason" },
        { VisitorName, Visitor, "ראו www.x.co", "reason" },
        { VisitorName, Visitor, "JavaScript:alert(1)", "reason" },
        { VisitorName, Visitor, "data:text/html,x", "reason" },
        { VisitorName, Visitor, "טקסט\u0000עם בקרה", "reason" },
        { VisitorName, Visitor, "טקסט​נסתר", "reason" },
        { VisitorName, Visitor, "טקסט‫bidi", "reason" },
        { VisitorName, Visitor, "טקסט\u0007פעמון", "reason" },
        { "ראו www.x.co", Visitor, Reason, "name" },
        { "http://evil.example", Visitor, Reason, "name" },
        { "ｈｔｔｐ：／／evil.example", Visitor, Reason, "name" },
        { "javascript:alert(1)", Visitor, Reason, "name" },
        { VisitorName, Visitor, "ｊａｖａｓｃｒｉｐｔ：alert(1)", "reason" },
        { VisitorName, Visitor, "ｈｔｔｐｓ：//x.example", "reason" },
        { VisitorName, Visitor, "שורה\u0085נוספת", "reason" },
    };

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task Invalid_fields_are_a_400_with_that_field_even_for_an_unknown_profile(string? name, string? email, string? reason, string field)
    {
        var s = await NewSetupAsync();

        var response = await Submit(s.Factory.NewClient(), Guid.NewGuid().ToString("D"), name, email, reason); // validation runs before the profile is looked up

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("errors").TryGetProperty(field, out _));
        Assert.Empty(await Requests(s.Factory));
        Assert.Empty(await Outbox(s.Factory));
    }

    [Fact]
    public async Task Newlines_and_tabs_are_allowed_in_the_reason_and_normalised()
    {
        var s = await NewSetupAsync();

        var response = await Submit(s.Factory.NewClient(), s.ProfileId.ToString("D"), reason: "שורה ראשונה\r\n\tשורה שנייה");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("שורה ראשונה\n\tשורה שנייה", Assert.Single(await Requests(s.Factory)).Reason);
    }

    [Fact]
    public async Task Missing_turnstile_origin_or_csrf_is_rejected()
    {
        var s = await NewSetupAsync();
        var id = s.ProfileId.ToString("D");
        var client = s.Factory.NewClient();
        var csrf = await client.GetCsrfAsync();
        var body = new { name = VisitorName, email = Visitor, reason = Reason };

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/profiles/{id}/contact-requests", body, csrf, origin: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/profiles/{id}/contact-requests", body, csrf, origin: "https://evil.example")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/profiles/{id}/contact-requests", body, csrf: null)).StatusCode);

        var noBot = s.Factory.NewClient();
        noBot.Http.DefaultRequestHeaders.Remove("X-Turnstile-Token");
        Assert.Equal(HttpStatusCode.Forbidden, (await Submit(noBot, id)).StatusCode);

        Assert.Empty(await Requests(s.Factory));
    }

    [Fact]
    public async Task Limits_per_ip_per_address_and_per_profile_answer_429_with_retry_after()
    {
        // per address: 2 allowed, the third is limited (other addresses are fine)
        var byEmail = await NewSetupAsync(new Dictionary<string, string> { ["RATE_LIMIT_CONTACT_REQUEST_EMAIL"] = "2" });
        var id = byEmail.ProfileId.ToString("D");
        Assert.Equal(HttpStatusCode.Accepted, (await Submit(byEmail.Factory.NewClient(), id)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await Submit(byEmail.Factory.NewClient(), id)).StatusCode);
        var limited = await Submit(byEmail.Factory.NewClient(), id);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"));
        Assert.Equal("rate-limited", (await limited.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Accepted, (await Submit(byEmail.Factory.NewClient(), id, email: "other@example.com")).StatusCode);

        // per profile: counted for unknown profiles too, so the limit does not reveal existence
        var byProfile = await NewSetupAsync(new Dictionary<string, string> { ["RATE_LIMIT_CONTACT_REQUEST_PROFILE"] = "2" });
        var unknown = Guid.NewGuid().ToString("D");
        Assert.Equal(HttpStatusCode.Accepted, (await Submit(byProfile.Factory.NewClient(), unknown, email: "a@example.com")).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await Submit(byProfile.Factory.NewClient(), unknown, email: "b@example.com")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Submit(byProfile.Factory.NewClient(), unknown, email: "c@example.com")).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await Submit(byProfile.Factory.NewClient(), byProfile.ProfileId.ToString("D"), email: "d@example.com")).StatusCode);

        // per IP
        var byIp = await NewSetupAsync(new Dictionary<string, string> { ["RATE_LIMIT_CONTACT_REQUEST_IP"] = "1" });
        Assert.Equal(HttpStatusCode.Accepted, (await Submit(byIp.Factory.NewClient(), byIp.ProfileId.ToString("D"))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Submit(byIp.Factory.NewClient(), byIp.ProfileId.ToString("D"), email: "e@example.com")).StatusCode);
    }

    [Fact]
    public async Task The_contact_rate_limit_environment_variables_are_parsed_by_the_DI_wiring()
    {
        var limits = new Dictionary<string, string>
        {
            ["RATE_LIMIT_CONTACT_REQUEST_IP"] = "7", ["RATE_LIMIT_CONTACT_REQUEST_EMAIL"] = "8",
            ["RATE_LIMIT_CONTACT_REQUEST_PROFILE"] = "9", ["RATE_LIMIT_CONTACT_VERIFY_IP"] = "11",
        };
        var s = await NewSetupAsync(limits);
        var options = s.Factory.Services.GetRequiredService<ResumeApp.Features.PublicForms.RateLimitOptions>();
        Assert.Equal((7, 8, 9, 11), (options.ContactRequestPerIp, options.ContactRequestPerEmail, options.ContactRequestPerProfile, options.ContactVerifyPerIp));

        // and the shipped defaults (no variables set) are 10, 3, 10 and 30
        var defaults = ResumeApp.Features.PublicForms.RateLimitOptions.Defaults;
        Assert.Equal((10, 3, 10, 30), (defaults.ContactRequestPerIp, defaults.ContactRequestPerEmail, defaults.ContactRequestPerProfile, defaults.ContactVerifyPerIp));
    }

    [Fact]
    public async Task A_repeat_for_the_same_profile_and_address_creates_nothing_new_until_the_first_is_closed()
    {
        var s = await NewSetupAsync();
        var id = s.ProfileId.ToString("D");

        var first = await Submit(s.Factory.NewClient(), id);
        var again = await Submit(s.Factory.NewClient(), id, name: "שם אחר", email: " VISITOR@example.com ");

        Assert.Equal(await first.Content.ReadAsStringAsync(), await again.Content.ReadAsStringAsync());
        Assert.Single(await Requests(s.Factory));
        Assert.Single(await Outbox(s.Factory));

        // Once the request is closed, the same visitor may ask again.
        await Sql(s.Factory, $"UPDATE contact_requests SET status = 'expired'");
        await Submit(s.Factory.NewClient(), id);
        Assert.Equal(2, (await Requests(s.Factory)).Count);
        Assert.Equal(2, (await Outbox(s.Factory)).Count);
    }

    [Fact]
    public async Task Five_parallel_identical_submissions_write_one_request_and_one_outbox_row()
    {
        var s = await NewSetupAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Submit(s.Factory.NewClient(), s.ProfileId.ToString("D"))));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Accepted, r.StatusCode));
        Assert.Single(await Requests(s.Factory));
        Assert.Single(await Outbox(s.Factory));
    }

    // ---- the verification mail --------------------------------------------------------------------------------------

    [Fact]
    public async Task The_worker_sends_a_plain_text_mail_to_the_visitor_with_the_token_in_the_fragment_and_nothing_about_the_participant()
    {
        var s = await NewSetupAsync();
        var token = await SubmitAndMailAsync(s);

        var sent = Assert.Single(s.Factory.Email.Messages);
        Assert.Equal(Visitor, sent.Message.To);
        Assert.Equal(ContactVerificationEmail.Subject, sent.Message.Subject);
        Assert.Contains($"{AuthApiFactory.PublicBaseUrl}/contact/verify#token={token}", sent.Message.TextBody);
        Assert.Contains(VisitorName, sent.Message.TextBody);
        Assert.DoesNotContain("דנה", sent.Message.TextBody);
        Assert.DoesNotContain("dana@example.com", sent.Message.TextBody);
        Assert.DoesNotContain("050", sent.Message.TextBody);
        Assert.DoesNotContain(Reason, sent.Message.TextBody);
        Assert.DoesNotContain("<", sent.Message.TextBody);

        var stored = Assert.Single(await Tokens(s.Factory, "verify-contact-email"));
        var request = Assert.Single(await Requests(s.Factory));
        Assert.Equal(PersonalTokens.Hash(token), stored.TokenHash);
        Assert.NotEqual(token, stored.TokenHash);
        Assert.Equal(request.Id, stored.RequestId);
        Assert.Equal(s.ProfileId, stored.ProfileId);
        Assert.InRange((stored.ExpiresAt - stored.CreatedAt).TotalHours, 23.99, 24.01);
        Assert.Equal(OutboxStatus.Sent, Assert.Single(await Outbox(s.Factory)).Status);
    }

    [Fact]
    public async Task A_new_verification_token_revokes_the_previous_one_for_the_same_request()
    {
        var s = await NewSetupAsync();
        var first = await SubmitAndMailAsync(s);
        await Sql(s.Factory, $"UPDATE email_outbox SET status = 'Pending', sent_at = NULL, next_attempt_at = now() - interval '1 minute', lease_until = NULL");
        await ProcessAsync(s.Factory);
        var second = TokenFrom(s.Factory.Email.Messages.Last());

        Assert.NotEqual(first, second);
        var tokens = await Tokens(s.Factory, "verify-contact-email");
        Assert.Equal(2, tokens.Count);
        Assert.NotNull(tokens.Single(t => t.TokenHash == PersonalTokens.Hash(first)).RevokedAt);
        Assert.Null(tokens.Single(t => t.TokenHash == PersonalTokens.Hash(second)).RevokedAt);

        var client = s.Factory.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Verify(client, first)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Verify(client, second)).StatusCode);
    }

    [Fact]
    public async Task A_request_closed_before_the_send_makes_the_row_dead_and_sends_nothing()
    {
        var s = await NewSetupAsync();
        await Submit(s.Factory.NewClient(), s.ProfileId.ToString("D"));
        await Sql(s.Factory, $"UPDATE contact_requests SET status = 'expired'");

        await ProcessAsync(s.Factory);

        var row = Assert.Single(await Outbox(s.Factory));
        Assert.Equal(OutboxStatus.Dead, row.Status);
        Assert.Equal("request-not-awaiting", row.LastErrorCategory);
        Assert.Empty(s.Factory.Email.Messages);
        Assert.Empty(await Tokens(s.Factory, "verify-contact-email"));
    }

    [Fact]
    public async Task A_permanent_send_failure_dead_ends_the_row_and_closes_the_request_so_the_visitor_can_ask_again()
    {
        var s = await NewSetupAsync();
        await Submit(s.Factory.NewClient(), s.ProfileId.ToString("D"));
        s.Factory.Email.Script(EmailSendResult.Permanent("rejected"));

        await ProcessAsync(s.Factory);

        var row = Assert.Single(await Outbox(s.Factory));
        Assert.Equal(OutboxStatus.Dead, row.Status);
        Assert.Equal("rejected", row.LastErrorCategory);
        Assert.Equal(ContactRequestStatus.Expired, Assert.Single(await Requests(s.Factory)).Status);

        Assert.Equal(HttpStatusCode.Accepted, (await Submit(s.Factory.NewClient(), s.ProfileId.ToString("D"))).StatusCode);
        Assert.Equal(2, (await Requests(s.Factory)).Count);
        Assert.Equal(ContactRequestStatus.AwaitingEmailVerification, (await Requests(s.Factory))[1].Status);
    }

    [Fact]
    public async Task A_temporary_send_failure_is_retried_later_with_a_fresh_token_and_the_request_stays_open()
    {
        var s = await NewSetupAsync();
        await Submit(s.Factory.NewClient(), s.ProfileId.ToString("D"));
        s.Factory.Email.Script(new EmailSendResult(EmailSendStatus.Failed, "network"));

        await ProcessAsync(s.Factory);

        var row = Assert.Single(await Outbox(s.Factory));
        Assert.Equal(OutboxStatus.Pending, row.Status);
        Assert.Equal(1, row.Attempts);
        Assert.Equal("network", row.LastErrorCategory);
        Assert.True(row.NextAttemptAt > DateTime.UtcNow);
        Assert.Empty(s.Factory.Email.Messages);
        Assert.Equal(ContactRequestStatus.AwaitingEmailVerification, Assert.Single(await Requests(s.Factory)).Status);

        await Sql(s.Factory, $"UPDATE email_outbox SET next_attempt_at = now() - interval '1 minute', lease_until = NULL");
        await ProcessAsync(s.Factory);

        Assert.Equal(OutboxStatus.Sent, Assert.Single(await Outbox(s.Factory)).Status);
        var token = TokenFrom(Assert.Single(s.Factory.Email.Messages));
        var tokens = await Tokens(s.Factory, "verify-contact-email");
        Assert.Equal(2, tokens.Count); // the first (never mailed) token was revoked by the second
        Assert.Single(tokens, t => t.RevokedAt == null);
        Assert.Equal(HttpStatusCode.OK, (await Verify(s.Factory.NewClient(), token)).StatusCode);
    }

    [Fact]
    public async Task A_contact_verification_row_without_a_request_id_is_dead_as_unknown_template()
    {
        var s = await NewSetupAsync();
        await Sql(s.Factory, $@"INSERT INTO email_outbox (id, template_key, request_id, deduplication_key, status, attempts, claim_count, next_attempt_at, created_at)
            VALUES ({Guid.NewGuid()}, 'contact-verification', NULL, 'contact-verification:orphan', 'Pending', 0, 0, now() - interval '1 minute', now())");

        await ProcessAsync(s.Factory);

        var row = Assert.Single(await Outbox(s.Factory));
        Assert.Equal(OutboxStatus.Dead, row.Status);
        Assert.Equal("unknown-template", row.LastErrorCategory);
        Assert.Empty(s.Factory.Email.Messages);
    }

    [Fact]
    public async Task A_profile_removed_before_the_send_makes_the_row_dead_sends_nothing_issues_no_token_and_closes_the_request()
    {
        var s = await NewSetupAsync();
        await Submit(s.Factory.NewClient(), s.ProfileId.ToString("D"));
        await Sql(s.Factory, $"UPDATE profiles SET visibility = 'Removed'"); // not via the admin endpoint, so the request is still awaiting

        await ProcessAsync(s.Factory);

        var row = Assert.Single(await Outbox(s.Factory));
        Assert.Equal(OutboxStatus.Dead, row.Status);
        Assert.Equal("profile-not-active", row.LastErrorCategory);
        Assert.Empty(s.Factory.Email.Messages);
        Assert.Empty(await Tokens(s.Factory, "verify-contact-email"));
        Assert.Equal(ContactRequestStatus.Expired, Assert.Single(await Requests(s.Factory)).Status);
    }

    [Fact]
    public async Task A_poison_contact_verification_row_dead_ends_as_too_many_claims_and_closes_the_request()
    {
        var s = await NewSetupAsync();
        await Submit(s.Factory.NewClient(), s.ProfileId.ToString("D"));
        await Sql(s.Factory, $"UPDATE email_outbox SET claim_count = 10, next_attempt_at = now() - interval '1 minute'");

        await ProcessAsync(s.Factory);

        Assert.Equal("too-many-claims", Assert.Single(await Outbox(s.Factory)).LastErrorCategory);
        Assert.Equal(ContactRequestStatus.Expired, Assert.Single(await Requests(s.Factory)).Status);
    }

    [Fact]
    public async Task A_stale_unverified_request_does_not_lock_the_visitor_out()
    {
        var s = await NewSetupAsync();
        var id = s.ProfileId.ToString("D");
        await Submit(s.Factory.NewClient(), id);
        await Sql(s.Factory, $"UPDATE contact_requests SET expires_at = now() - interval '1 minute'"); // the sweep has not run yet

        await Submit(s.Factory.NewClient(), id);

        var requests = await Requests(s.Factory);
        Assert.Equal(2, requests.Count);
        Assert.Equal(ContactRequestStatus.Expired, requests[0].Status);
        Assert.Equal(ContactRequestStatus.AwaitingEmailVerification, requests[1].Status);
        Assert.Equal(2, (await Outbox(s.Factory)).Count);
    }

    [Fact]
    public async Task An_expired_token_on_a_request_past_its_expiry_expires_the_request()
    {
        var s = await NewSetupAsync();
        var token = await SubmitAndMailAsync(s);
        await Sql(s.Factory, $"UPDATE personal_tokens SET expires_at = now() - interval '1 minute'");
        await Sql(s.Factory, $"UPDATE contact_requests SET expires_at = now() - interval '1 minute'");

        Assert.Equal(HttpStatusCode.Unauthorized, (await Verify(s.Factory.NewClient(), token)).StatusCode);

        Assert.Equal(ContactRequestStatus.Expired, Assert.Single(await Requests(s.Factory)).Status);
    }

    [Fact]
    public async Task The_status_has_a_check_constraint_and_the_open_state_list_has_one_source()
    {
        var s = await NewSetupAsync();
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => Sql(s.Factory,
            $@"INSERT INTO contact_requests (id, profile_id, visitor_name, visitor_email, reason, status, created_at, expires_at)
               VALUES ({Guid.NewGuid()}, {s.ProfileId}, 'x', 'x@example.com', 'y', 'bogus', now(), now())"));
        Assert.Contains("ck_contact_requests_status", ex.ToString());

        await using var scope = s.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var migrations = db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrationsAssembly>().Migrations;

        // The partial index filter in the migration equals the constant the repository and the model use.
        var create = (Microsoft.EntityFrameworkCore.Migrations.Migration)Activator.CreateInstance(migrations["20261022041053_ContactRequests"])!;
        var index = create.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.CreateIndexOperation>()
            .Single(o => o.Name == "ux_contact_requests_open_profile_visitor");
        Assert.Equal(ContactRequestStatusNames.OpenStatesSql, index.Filter);

        // The CHECK migration lists exactly every status.
        var check = (Microsoft.EntityFrameworkCore.Migrations.Migration)Activator.CreateInstance(migrations.Single(m => m.Key.EndsWith("_ContactRequestStatusCheck")).Value)!;
        Assert.Equal(ContactRequestStatusNames.AllStatesSql,
            check.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.AddCheckConstraintOperation>().Single().Sql);
        foreach (var status in Enum.GetValues<ContactRequestStatus>())
            Assert.Contains($"'{ContactRequestStatusNames.ToWire(status)}'", ContactRequestStatusNames.AllStatesSql);
    }

    // ---- verification -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_valid_token_moves_the_request_to_submitted_and_is_single_use()
    {
        var s = await NewSetupAsync();
        var token = await SubmitAndMailAsync(s);
        var client = s.Factory.NewClient();

        var ok = await Verify(client, token);

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var json = await ok.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["status"], json.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("submitted", json.GetProperty("status").GetString());
        Assert.Equal("no-referrer", ok.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("no-store", ok.Headers.CacheControl!.ToString());

        var request = Assert.Single(await Requests(s.Factory));
        Assert.Equal(ContactRequestStatus.Submitted, request.Status);
        Assert.NotNull(request.VerifiedAt);
        Assert.Equal(Visitor, request.VisitorEmail); // the verified address is locked
        Assert.NotNull(Assert.Single(await Tokens(s.Factory, "verify-contact-email")).UsedAt);

        var reuse = await Verify(client, token);
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
        var problem = await reuse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("link-invalid", problem.GetProperty("code").GetString());
        Assert.Equal(ContactRequestStatus.Submitted, Assert.Single(await Requests(s.Factory)).Status);
    }

    [Fact]
    public async Task Every_invalid_token_gets_the_identical_401()
    {
        var s = await NewSetupAsync();
        var token = await SubmitAndMailAsync(s);
        var editToken = await Db(s.Factory, async d =>
        {
            await using var scope = s.Factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IPersonalTokenIssuer>().IssueEditTokenAsync(s.ProfileId, DateTime.UtcNow, CancellationToken.None);
        });
        var client = s.Factory.NewClient();

        // used
        Assert.Equal(HttpStatusCode.OK, (await Verify(client, token)).StatusCode);
        var used = await Verify(client, token);
        // revoked, expired token, wrong purpose, unknown, malformed, empty
        await Sql(s.Factory, $"UPDATE contact_requests SET status = 'awaiting_email_verification', verified_at = NULL");
        var second = await Revisit(s);
        await Sql(s.Factory, $"UPDATE personal_tokens SET revoked_at = now() WHERE token_hash = {PersonalTokens.Hash(second)}");
        var revoked = await Verify(client, second);
        var third = await Revisit(s);
        await Sql(s.Factory, $"UPDATE personal_tokens SET expires_at = now() - interval '1 minute' WHERE token_hash = {PersonalTokens.Hash(third)}");
        var expired = await Verify(client, third);
        var wrongPurpose = await Verify(client, editToken);
        var unknown = await Verify(client, PersonalTokens.Generate());
        var garbage = await Verify(client, "not a token");
        var empty = await Verify(client, null);

        string Strip(string body) => Regex.Replace(body, "\"traceId\":\"[^\"]*\"", "");
        var reference = Strip(await used.Content.ReadAsStringAsync());
        foreach (var r in new[] { used, revoked, expired, wrongPurpose, unknown, garbage, empty })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
            Assert.Equal(reference, Strip(await r.Content.ReadAsStringAsync()));
            Assert.Equal(HeaderLines(used), HeaderLines(r));
        }
        Assert.Equal("link-invalid", (await used.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        // The edit-profile token was not consumed by the wrong endpoint.
        Assert.Null((await Tokens(s.Factory, "edit-profile")).Single().UsedAt);
    }

    /// <summary>Re-sends the verification mail for the (reopened) request and returns the new token.</summary>
    private static async Task<string> Revisit(Setup s)
    {
        await Sql(s.Factory, $"UPDATE email_outbox SET status = 'Pending', sent_at = NULL, next_attempt_at = now() - interval '1 minute', lease_until = NULL");
        await ProcessAsync(s.Factory);
        return TokenFrom(s.Factory.Email.Messages.Last());
    }

    [Fact]
    public async Task A_valid_token_for_a_request_that_ran_out_is_a_401_and_the_request_expires()
    {
        var s = await NewSetupAsync();
        var token = await SubmitAndMailAsync(s);
        await Sql(s.Factory, $"UPDATE contact_requests SET expires_at = now() - interval '1 minute'");

        var response = await Verify(s.Factory.NewClient(), token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ContactRequestStatus.Expired, Assert.Single(await Requests(s.Factory)).Status);
    }

    [Fact]
    public async Task A_valid_token_for_a_profile_that_is_no_longer_active_is_a_401_and_the_request_expires()
    {
        var s = await NewSetupAsync();
        var token = await SubmitAndMailAsync(s);
        await Sql(s.Factory, $"UPDATE profiles SET visibility = 'Removed'"); // removed without going through the admin endpoint

        var response = await Verify(s.Factory.NewClient(), token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ContactRequestStatus.Expired, Assert.Single(await Requests(s.Factory)).Status);
    }

    [Fact]
    public async Task Five_parallel_verifications_of_one_token_succeed_exactly_once()
    {
        var s = await NewSetupAsync();
        var token = await SubmitAndMailAsync(s);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Verify(s.Factory.NewClient(), token)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(4, responses.Count(r => r.StatusCode == HttpStatusCode.Unauthorized));
        Assert.Equal(ContactRequestStatus.Submitted, Assert.Single(await Requests(s.Factory)).Status);
    }

    [Fact]
    public async Task Verification_needs_origin_and_csrf_and_is_rate_limited_per_ip()
    {
        var s = await NewSetupAsync(new Dictionary<string, string> { ["RATE_LIMIT_CONTACT_VERIFY_IP"] = "3" });
        var token = await SubmitAndMailAsync(s);
        var client = s.Factory.NewClient();
        var csrf = await client.GetCsrfAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/contact-requests/verify", new { token }, csrf: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/contact-requests/verify", new { token }, csrf, origin: "https://evil.example")).StatusCode);
        Assert.Equal(ContactRequestStatus.AwaitingEmailVerification, Assert.Single(await Requests(s.Factory)).Status);

        // Rejected requests were not counted by the limiter (it runs after the integrity check), so three attempts remain.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Verify(client, "x")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Verify(client, "y")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Verify(client, "z")).StatusCode);
        var limited = await Verify(client, token);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task Opening_the_link_is_not_a_consumption_a_GET_does_nothing()
    {
        var s = await NewSetupAsync();
        var token = await SubmitAndMailAsync(s);
        var client = s.Factory.NewClient();

        var get = await client.Http.GetAsync($"/contact-requests/verify?token={token}");

        Assert.NotEqual(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(ContactRequestStatus.AwaitingEmailVerification, Assert.Single(await Requests(s.Factory)).Status);
        Assert.Null(Assert.Single(await Tokens(s.Factory, "verify-contact-email")).UsedAt);
    }

    // ---- expiry sweep and removal -----------------------------------------------------------------------------------

    [Fact]
    public async Task The_sweep_expires_only_stale_awaiting_requests_and_revokes_their_tokens()
    {
        var s = await NewSetupAsync();
        var staleToken = await SubmitAndMailAsync(s, "stale@example.com");
        var freshToken = await SubmitAndMailAsync(s, "fresh@example.com");
        var verifiedToken = await SubmitAndMailAsync(s, "done@example.com");
        Assert.Equal(HttpStatusCode.OK, (await Verify(s.Factory.NewClient(), verifiedToken)).StatusCode);
        await Sql(s.Factory, $"UPDATE contact_requests SET expires_at = now() - interval '1 hour' WHERE visitor_email IN ('stale@example.com', 'done@example.com')");

        var swept = await SweepAsync(s.Factory);

        Assert.Equal(1, swept);
        var requests = await Requests(s.Factory);
        Assert.Equal(ContactRequestStatus.Expired, requests.Single(r => r.VisitorEmail == "stale@example.com").Status);
        Assert.Equal(ContactRequestStatus.AwaitingEmailVerification, requests.Single(r => r.VisitorEmail == "fresh@example.com").Status);
        Assert.Equal(ContactRequestStatus.Submitted, requests.Single(r => r.VisitorEmail == "done@example.com").Status);
        var tokens = await Tokens(s.Factory, "verify-contact-email");
        Assert.NotNull(tokens.Single(t => t.TokenHash == PersonalTokens.Hash(staleToken)).RevokedAt);
        Assert.Null(tokens.Single(t => t.TokenHash == PersonalTokens.Hash(freshToken)).RevokedAt);
        Assert.Equal(0, await SweepAsync(s.Factory));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Verify(s.Factory.NewClient(), staleToken)).StatusCode);
    }

    [Fact]
    public async Task Removing_the_profile_expires_its_open_requests_and_revokes_their_tokens_in_the_same_transaction()
    {
        var s = await NewSetupAsync();
        var awaitingToken = await SubmitAndMailAsync(s, "awaiting@example.com");
        var submittedToken = await SubmitAndMailAsync(s, "submitted@example.com");
        Assert.Equal(HttpStatusCode.OK, (await Verify(s.Factory.NewClient(), submittedToken)).StatusCode);
        // a closed request is left alone
        await Submit(s.Factory.NewClient(), s.ProfileId.ToString("D"), email: "closed@example.com");
        await Sql(s.Factory, $"UPDATE contact_requests SET status = 'participant_declined' WHERE visitor_email = 'closed@example.com'");
        // another profile's request is untouched
        var otherProfile = await NewProfileAsync(s.Factory, await InviteOf(s), "other@example.com");
        await Submit(s.Factory.NewClient(), otherProfile.ToString("D"), email: "awaiting@example.com");

        var removed = await s.Admin.PostWithCsrfAsync($"/admin/profiles/{s.ProfileId:D}/remove", null);

        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        var requests = await Requests(s.Factory);
        Assert.Equal(ContactRequestStatus.Expired, requests.Single(r => r.ProfileId == s.ProfileId && r.VisitorEmail == "awaiting@example.com").Status);
        Assert.Equal(ContactRequestStatus.Expired, requests.Single(r => r.VisitorEmail == "submitted@example.com").Status);
        Assert.Equal(ContactRequestStatus.ParticipantDeclined, requests.Single(r => r.VisitorEmail == "closed@example.com").Status);
        Assert.Equal(ContactRequestStatus.AwaitingEmailVerification, requests.Single(r => r.ProfileId == otherProfile).Status);
        var tokens = await Tokens(s.Factory, "verify-contact-email");
        Assert.All(tokens, t => Assert.NotNull(t.RevokedAt ?? t.UsedAt));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Verify(s.Factory.NewClient(), awaitingToken)).StatusCode);
    }

    [Fact]
    public async Task A_removed_profile_takes_no_new_requests_and_a_failed_removal_leaves_requests_alone()
    {
        var s = await NewSetupAsync();
        Assert.Equal(HttpStatusCode.OK, (await s.Admin.PostWithCsrfAsync($"/admin/profiles/{s.ProfileId:D}/remove", null)).StatusCode);

        var response = await Submit(s.Factory.NewClient(), s.ProfileId.ToString("D"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(await Requests(s.Factory));
        Assert.Empty(await Outbox(s.Factory));

        // Removing again is a 409 and changes nothing (no request exists to touch).
        Assert.Equal(HttpStatusCode.Conflict, (await s.Admin.PostWithCsrfAsync($"/admin/profiles/{s.ProfileId:D}/remove", null)).StatusCode);
    }

    [Fact]
    public async Task An_unverified_request_is_not_visible_through_any_existing_endpoint()
    {
        var s = await NewSetupAsync();
        await Submit(s.Factory.NewClient(), s.ProfileId.ToString("D"));

        var outbox = await (await s.Admin.Http.GetAsync("/admin/email-outbox")).Content.ReadAsStringAsync();
        var profiles = await (await s.Admin.Http.GetAsync("/admin/profiles?visibility=all")).Content.ReadAsStringAsync();
        var gallery = await (await s.Factory.NewClient().Http.GetAsync("/profiles")).Content.ReadAsStringAsync();

        foreach (var body in new[] { outbox, profiles, gallery })
        {
            Assert.DoesNotContain(Visitor, body);
            Assert.DoesNotContain(VisitorName, body);
            Assert.DoesNotContain(Reason, body);
        }
    }

    // ---- request shape, guards order, races, worker ------------------------------------------------------------------

    private static async Task<HttpResponseMessage> PostRaw(ApiClient c, string path, HttpContent content)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
        request.Headers.Add("Origin", ApiClient.Origin);
        request.Headers.Add("X-CSRF-TOKEN", await c.GetCsrfAsync());
        return await c.Http.SendAsync(request);
    }

    private static StringContent Json(string body) => new(body, System.Text.Encoding.UTF8, "application/json");

    [Fact]
    public async Task Body_size_content_type_and_malformed_json_are_handled_on_both_endpoints()
    {
        var s = await NewSetupAsync();
        var client = s.Factory.NewClient();
        var path = $"/profiles/{s.ProfileId:D}/contact-requests";

        // submission: 413, 415, 400
        var big = "{\"name\":\"x\",\"email\":\"a@b.co\",\"reason\":\"" + new string('a', ContactRequestEndpoints.MaxBodyBytes) + "\"}";
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await PostRaw(client, path, Json(big))).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await PostRaw(client, path, new StringContent("x", System.Text.Encoding.UTF8, "text/plain"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostRaw(client, path, Json("{nope"))).StatusCode);

        // verification: an oversized or unreadable body is just another invalid token (401), a non-JSON body is 415
        var bigToken = "{\"token\":\"" + new string('a', 2000) + "\"}";
        var oversized = await PostRaw(client, "/contact-requests/verify", Json(bigToken));
        Assert.Equal(HttpStatusCode.Unauthorized, oversized.StatusCode);
        Assert.Equal("link-invalid", (await oversized.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await PostRaw(client, "/contact-requests/verify", new StringContent("x", System.Text.Encoding.UTF8, "text/plain"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostRaw(client, "/contact-requests/verify", Json("{nope"))).StatusCode);

        Assert.Empty(await Requests(s.Factory));
    }

    [Fact]
    public async Task The_ip_limit_and_turnstile_run_before_the_body_is_read()
    {
        var s = await NewSetupAsync(new Dictionary<string, string> { ["RATE_LIMIT_CONTACT_REQUEST_IP"] = "1", ["RATE_LIMIT_CONTACT_VERIFY_IP"] = "1" });
        var path = $"/profiles/{s.ProfileId:D}/contact-requests";
        StringContent NotJson() => new("x", System.Text.Encoding.UTF8, "text/plain");

        // Turnstile fails first: 403, not the 415 the body deserves (and the attempt does not use up the single IP slot's successor check)
        var noBot = s.Factory.NewClient();
        noBot.Http.DefaultRequestHeaders.Remove("X-Turnstile-Token");
        Assert.Equal(HttpStatusCode.Forbidden, (await PostRaw(noBot, path, NotJson())).StatusCode);

        // the IP limit (1 per hour) was consumed by that attempt, so the next one is 429 before any body handling
        var limited = await PostRaw(s.Factory.NewClient(), path, NotJson());
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);

        // verification: the first attempt reaches the body (415), the second is limited before it
        var verifier = s.Factory.NewClient();
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await PostRaw(verifier, "/contact-requests/verify", NotJson())).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await PostRaw(verifier, "/contact-requests/verify", NotJson())).StatusCode);
    }

    [Fact]
    public async Task Submissions_racing_an_admin_removal_never_leave_an_open_request()
    {
        var s = await NewSetupAsync();
        var id = s.ProfileId.ToString("D");

        var submissions = Enumerable.Range(0, 8).Select(i => Submit(s.Factory.NewClient(), id, email: $"racer{i}@example.com")).ToList();
        var removal = s.Admin.PostWithCsrfAsync($"/admin/profiles/{s.ProfileId:D}/remove", null);
        await Task.WhenAll(submissions.Cast<Task>().Append(removal));

        Assert.Equal(HttpStatusCode.OK, (await removal).StatusCode);
        Assert.All(submissions, t => Assert.Equal(HttpStatusCode.Accepted, t.Result.StatusCode));
        Assert.All(await Requests(s.Factory), r => Assert.Equal(ContactRequestStatus.Expired, r.Status));
    }

    [Theory]
    [InlineData("/contact-requests/verify", true)]
    [InlineData("/CONTACT-requests/Verify", true)]
    [InlineData("/profiles/abc/contact-requests", true)]
    [InlineData("/profiles/abc/contact-requests/", false)]
    [InlineData("/profiles/x/y/contact-requests", false)]
    [InlineData("/profiles//contact-requests", false)]
    [InlineData("/profiles/contact-requests", false)]
    [InlineData("/contact-requests/verify/x", false)]
    [InlineData("/contact-requests", false)]
    [InlineData("/x/contact-requests/verify", false)]
    [InlineData("/me/profile", true)]
    [InlineData("/profiles/edit-link", false)]
    public void The_anonymous_csrf_binding_matches_exactly_the_contact_paths(string path, bool expected)
        => Assert.Equal(expected, ResumeApp.Api.Security.RequestIntegrityMiddleware.UsesAnonymousToken(new Microsoft.AspNetCore.Http.PathString(path)));

    [Fact]
    public void Validation_rejects_links_in_the_name_and_accepts_ordinary_text()
    {
        Assert.Null(ContactRequestService.Validate("בדוק www.x.co", Visitor, Reason, out var errors));
        Assert.Contains("name", errors.Keys);

        // scheme-like tokens only: ordinary text with a colon, and bare domains, pass (best-effort filter)
        var ok = ContactRequestService.Validate(VisitorName, Visitor, "data: 5 נקודות\r\nדוגמה: example.com", out _);
        Assert.NotNull(ok);
        Assert.Equal("data: 5 נקודות\nדוגמה: example.com", ok!.Reason);
        Assert.Equal("a\nb", ContactRequestService.Validate(VisitorName, Visitor, "a\rb", out _)!.Reason);
        Assert.Null(ContactRequestService.Validate(VisitorName, Visitor, "data:text/html,x", out _));
        Assert.Null(ContactRequestService.Validate(VisitorName, Visitor, "javascript:alert", out _));
    }

    [Fact]
    public async Task A_stale_request_becomes_expired_through_a_worker_cycle_without_a_direct_service_call()
    {
        using var env = new EnvScope().Set("CONTACT_SWEEP_INTERVAL_SECONDS", "1");
        var s = await NewSetupAsync(emailWorker: true);
        await Submit(s.Factory.NewClient(), s.ProfileId.ToString("D"));
        await Sql(s.Factory, $"UPDATE contact_requests SET expires_at = now() - interval '1 minute'");

        var expired = false;
        for (var i = 0; i < 60 && !expired; i++)
        {
            expired = (await Requests(s.Factory)).All(r => r.Status == ContactRequestStatus.Expired);
            if (!expired) await Task.Delay(500);
        }
        Assert.True(expired, "the worker did not sweep the stale request");
    }

    private sealed class SweepFailingRepository(IContactRequestRepository inner, int[] calls) : IContactRequestRepository
    {
        public Task<bool> TryCreateAsync(Guid p, ValidContactRequest r, DateTime n, CancellationToken ct) => inner.TryCreateAsync(p, r, n, ct);
        public Task<bool> VerifyAsync(string h, DateTime n, CancellationToken ct) => inner.VerifyAsync(h, n, ct);
        public Task<int> ExpireStaleAsync(DateTime n, CancellationToken ct)
        {
            Interlocked.Increment(ref calls[0]);
            throw new InvalidOperationException("sweep blip");
        }
    }

    [Fact]
    public async Task A_failing_sweep_does_not_block_mail_sending()
    {
        using var env = new EnvScope().Set("CONTACT_SWEEP_INTERVAL_SECONDS", "1");
        var calls = new int[1];
        var s = await NewSetupAsync(emailWorker: true, configure: services =>
        {
            services.RemoveAll<IContactRequestRepository>();
            services.AddScoped<IContactRequestRepository>(sp =>
                new SweepFailingRepository(ActivatorUtilities.CreateInstance<ContactRequestRepository>(sp), calls));
        });
        await Submit(s.Factory.NewClient(), s.ProfileId.ToString("D"));

        for (var i = 0; i < 80 && s.Factory.Email.Messages.Count == 0; i++) await Task.Delay(500);

        Assert.Single(s.Factory.Email.Messages);
        Assert.True(Volatile.Read(ref calls[0]) >= 1, "the sweep was never attempted");
    }

    // ---- domain -----------------------------------------------------------------------------------------------------

    [Fact]
    public void The_state_machine_allows_only_the_AD3_transitions()
    {
        var allowed = new HashSet<(ContactRequestStatus, ContactRequestStatus)>
        {
            (ContactRequestStatus.AwaitingEmailVerification, ContactRequestStatus.Submitted),
            (ContactRequestStatus.AwaitingEmailVerification, ContactRequestStatus.Expired),
            (ContactRequestStatus.Submitted, ContactRequestStatus.SentToParticipant),
            (ContactRequestStatus.Submitted, ContactRequestStatus.Expired),
            (ContactRequestStatus.SentToParticipant, ContactRequestStatus.ParticipantApproved),
            (ContactRequestStatus.SentToParticipant, ContactRequestStatus.ParticipantDeclined),
            (ContactRequestStatus.SentToParticipant, ContactRequestStatus.Expired),
            (ContactRequestStatus.ParticipantApproved, ContactRequestStatus.Delivered),
            (ContactRequestStatus.ParticipantApproved, ContactRequestStatus.Expired),
        };
        foreach (var from in Enum.GetValues<ContactRequestStatus>())
            foreach (var to in Enum.GetValues<ContactRequestStatus>())
                Assert.Equal(allowed.Contains((from, to)), ContactRequest.CanTransition(from, to));

        Assert.True(ContactRequest.IsOpen(ContactRequestStatus.Submitted));
        Assert.False(ContactRequest.IsOpen(ContactRequestStatus.Expired));
        Assert.False(ContactRequest.IsOpen(ContactRequestStatus.Delivered));
        Assert.False(ContactRequest.IsOpen(ContactRequestStatus.ParticipantDeclined));
        foreach (var status in Enum.GetValues<ContactRequestStatus>())
            Assert.Equal(status, ContactRequestStatusNames.Parse(ContactRequestStatusNames.ToWire(status)));
    }
}
