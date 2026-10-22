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

/// <summary>Story 3.3: sending a verified request to the participant, her personal link, and her decision.</summary>
[Collection("env")]
public class ContactDecisionTests(PostgresFixture pg) : IDisposable
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

    private sealed record Setup(AuthApiFactory Factory, ApiClient Admin, Guid ProfileId, Guid RequestId);

    /// <summary>An admin, a participant profile, and one verified (submitted) request of a visitor.</summary>
    private async Task<Setup> NewSetupAsync()
    {
        var factory = await AuthApiFactory.CreateAsync(pg);
        _factories.Add(factory);
        var admin = factory.NewClient();
        await admin.EnrollAsync(AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword, NewPassword);
        var profileId = (await ParticipantSeeding.SeedProfileAsync(factory, email: ParticipantEmail, phone: ParticipantPhone)).ProfileId;

        Assert.Equal(HttpStatusCode.Accepted, (await factory.NewClient().PostWithCsrfAsync($"/profiles/{profileId:D}/contact-requests",
            new { name = VisitorName, email = Visitor, reason = Reason })).StatusCode);
        await ProcessAsync(factory);
        var mail = factory.Email.Messages.Last(m => m.Message.To == Visitor).Message.TextBody;
        var token = Regex.Match(mail, @"#token=([A-Za-z0-9_-]{43})").Groups[1].Value;
        Assert.Equal(HttpStatusCode.OK, (await factory.NewClient().PostWithCsrfAsync("/contact-requests/verify", new { token })).StatusCode);
        var requestId = await Db(factory, d => d.ContactRequests.AsNoTracking().Select(r => r.Id).SingleAsync());
        await ProcessAsync(factory); // flush the manager notification, so later mail counts are only about the participant
        return new(factory, admin, profileId, requestId);
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

    private static Task<HttpResponseMessage> Send(Setup s, string? id = null) => s.Admin.PostWithCsrfAsync($"/admin/contact-requests/{id ?? s.RequestId.ToString("D")}/send-to-participant", null);

    private static Task<HttpResponseMessage> Preview(AuthApiFactory f, string token) => f.NewClient().PostWithCsrfAsync("/contact-decisions/preview", new { token });

    private static Task<HttpResponseMessage> Decide(AuthApiFactory f, string token, string decision) => f.NewClient().PostWithCsrfAsync("/contact-decisions/decide", new { token, decision });

    private static async Task<string?> CodeOf(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    /// <summary>Sends to the participant, runs the worker and returns the raw token from her mail.</summary>
    private static async Task<string> SentAndMailedAsync(Setup s)
    {
        Assert.Equal(HttpStatusCode.OK, (await Send(s)).StatusCode);
        var before = s.Factory.Email.Messages.Count;
        await ProcessAsync(s.Factory);
        var mail = Assert.Single(s.Factory.Email.Messages.Skip(before), m => m.Message.To == ParticipantEmail).Message.TextBody;
        return Regex.Match(mail, @"#token=([A-Za-z0-9_-]{43})").Groups[1].Value;
    }

    private static Task<string?> IssueAsync(Setup s)
        => Db(s.Factory, async d =>
        {
            await using var scope = s.Factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IPersonalTokenIssuer>()
                .IssueContactDecisionTokenAsync(s.RequestId, s.ProfileId, DateTime.UtcNow, CancellationToken.None);
        });

    // ---- sending ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Sending_moves_the_request_and_writes_one_outbox_row_and_one_audit_row_without_any_personal_data()
    {
        var s = await NewSetupAsync();

        var response = await Send(s);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ContactRequestStatus.SentToParticipant, (await Request(s)).Status);
        var row = Assert.Single(await Db(s.Factory, d => d.EmailOutbox.AsNoTracking().Where(e => e.TemplateKey == "contact-decision-request").ToListAsync()));
        Assert.Equal(s.RequestId, row.RequestId);
        Assert.Equal($"contact-decision:{s.RequestId:D}", row.DeduplicationKey);
        Assert.Equal(OutboxStatus.Pending, row.Status);
        var dump = JsonSerializer.Serialize(new { row.TemplateKey, row.DeduplicationKey, row.LastErrorCategory });
        foreach (var secret in new[] { Visitor, VisitorName, Reason, ParticipantEmail, ParticipantPhone }) Assert.DoesNotContain(secret, dump);
        // No token exists until the worker sends.
        Assert.Empty(await Db(s.Factory, d => d.PersonalTokens.AsNoTracking().Where(t => t.Purpose == "contact-decision").ToListAsync()));

        var audit = Assert.Single(await Db(s.Factory, d => d.AuditLog.AsNoTracking().Where(a => a.Action == "send-to-participant").ToListAsync()));
        Assert.Equal("contact-request", audit.EntityType);
        Assert.Equal(s.RequestId, audit.EntityId);
    }

    [Fact]
    public async Task Sending_twice_or_a_request_that_is_not_submitted_is_409_and_unknown_ids_are_404()
    {
        var s = await NewSetupAsync();
        Assert.Equal(HttpStatusCode.OK, (await Send(s)).StatusCode);

        var again = await Send(s);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("request-not-submitted", await CodeOf(again));
        Assert.Single(await Db(s.Factory, d => d.EmailOutbox.AsNoTracking().Where(e => e.TemplateKey == "contact-decision-request").ToListAsync()));
        Assert.Single(await Db(s.Factory, d => d.AuditLog.AsNoTracking().Where(a => a.Action == "send-to-participant").ToListAsync()));

        Assert.Equal(HttpStatusCode.NotFound, (await Send(s, Guid.NewGuid().ToString("D"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(s, "not-a-guid")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(s, s.RequestId.ToString("N"))).StatusCode);
    }

    [Fact]
    public async Task Parallel_sends_succeed_once()
    {
        var s = await NewSetupAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Send(s)));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(3, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Single(await Db(s.Factory, d => d.EmailOutbox.AsNoTracking().Where(e => e.TemplateKey == "contact-decision-request").ToListAsync()));
    }

    [Fact]
    public async Task Sending_needs_an_admin()
    {
        var s = await NewSetupAsync();

        var anonymous = await s.Factory.NewClient().PostWithCsrfAsync($"/admin/contact-requests/{s.RequestId:D}/send-to-participant", null);

        Assert.True(anonymous.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, anonymous.StatusCode.ToString());
        Assert.Equal(ContactRequestStatus.Submitted, (await Request(s)).Status);
    }

    // ---- the mail --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_worker_mails_the_participant_a_personal_link_with_the_visitor_name_and_reason_only()
    {
        var s = await NewSetupAsync();
        Assert.Equal(HttpStatusCode.OK, (await Send(s)).StatusCode);
        var before = s.Factory.Email.Messages.Count;

        await ProcessAsync(s.Factory);

        var sent = Assert.Single(s.Factory.Email.Messages.Skip(before));
        Assert.Equal(ParticipantEmail, sent.Message.To);
        Assert.Equal(ContactDecisionEmail.Subject, sent.Message.Subject);
        var body = sent.Message.TextBody;
        Assert.Contains(VisitorName, body);
        Assert.Contains(Reason, body);
        Assert.Matches(@"/contact/decide#token=[A-Za-z0-9_-]{43}", body);
        Assert.DoesNotContain(Visitor, body);
        Assert.DoesNotContain(ParticipantPhone, body);
        var token = Assert.Single(await Db(s.Factory, d => d.PersonalTokens.AsNoTracking().Where(t => t.Purpose == "contact-decision").ToListAsync()));
        Assert.Equal(s.RequestId, token.RequestId);
        Assert.Equal(s.ProfileId, token.ProfileId);
        Assert.InRange((token.ExpiresAt - token.CreatedAt).TotalDays, 13.99, 14.01);
        var raw = Regex.Match(body, @"#token=([A-Za-z0-9_-]{43})").Groups[1].Value;
        Assert.Equal(PersonalTokens.Hash(raw), token.TokenHash);
        Assert.Equal(OutboxStatus.Sent, (await Db(s.Factory, d => d.EmailOutbox.AsNoTracking().SingleAsync(e => e.TemplateKey == "contact-decision-request"))).Status);
    }

    [Fact]
    public async Task A_row_whose_request_is_no_longer_sent_is_closed_without_a_mail_or_a_token()
    {
        var s = await NewSetupAsync();
        Assert.Equal(HttpStatusCode.OK, (await Send(s)).StatusCode);
        await Sql(s.Factory, $"UPDATE contact_requests SET status = 'expired' WHERE id = {s.RequestId}");
        var before = s.Factory.Email.Messages.Count;

        await ProcessAsync(s.Factory);

        Assert.Equal(before, s.Factory.Email.Messages.Count);
        var row = await Db(s.Factory, d => d.EmailOutbox.AsNoTracking().SingleAsync(e => e.TemplateKey == "contact-decision-request"));
        Assert.Equal(OutboxStatus.Dead, row.Status);
        Assert.Equal("request-not-sent", row.LastErrorCategory);
        Assert.Empty(await Db(s.Factory, d => d.PersonalTokens.AsNoTracking().Where(t => t.Purpose == "contact-decision").ToListAsync()));
    }

    [Fact]
    public async Task A_row_of_a_profile_that_is_no_longer_active_is_closed_without_a_mail()
    {
        var s = await NewSetupAsync();
        Assert.Equal(HttpStatusCode.OK, (await Send(s)).StatusCode);
        await Sql(s.Factory, $"UPDATE profiles SET visibility = 'Removed' WHERE id = {s.ProfileId}");
        var before = s.Factory.Email.Messages.Count;

        await ProcessAsync(s.Factory);

        Assert.Equal(before, s.Factory.Email.Messages.Count);
        Assert.Equal("profile-not-active", (await Db(s.Factory, d => d.EmailOutbox.AsNoTracking().SingleAsync(e => e.TemplateKey == "contact-decision-request"))).LastErrorCategory);
    }

    // ---- preview ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Preview_shows_the_visitor_name_and_reason_only_and_does_not_consume_the_token()
    {
        var s = await NewSetupAsync();
        var token = await SentAndMailedAsync(s);

        var first = await Preview(s.Factory, token);
        var second = await Preview(s.Factory, token);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("no-store", first.Headers.CacheControl?.ToString());
        Assert.Equal("no-referrer", first.Headers.GetValues("Referrer-Policy").Single());
        var text = await first.Content.ReadAsStringAsync();
        var json = JsonSerializer.Deserialize<JsonElement>(text);
        Assert.Equal(VisitorName, json.GetProperty("visitorName").GetString());
        Assert.Equal(Reason, json.GetProperty("reason").GetString());
        Assert.Equal(2, json.EnumerateObject().Count());
        foreach (var secret in new[] { Visitor, ParticipantEmail, ParticipantPhone }) Assert.DoesNotContain(secret, text);
        Assert.Null((await Db(s.Factory, d => d.PersonalTokens.AsNoTracking().SingleAsync(t => t.Purpose == "contact-decision"))).UsedAt);
        Assert.Equal(ContactRequestStatus.SentToParticipant, (await Request(s)).Status);
    }

    [Fact]
    public async Task Preview_and_decision_reject_unknown_malformed_and_other_purpose_tokens_with_the_same_401()
    {
        var s = await NewSetupAsync();
        var token = await SentAndMailedAsync(s);
        // A live token of another purpose (a contact-verification token for this profile) must not open a decision.
        var otherPurposeToken = PersonalTokens.Generate();
        await Db(s.Factory, async d =>
        {
            d.PersonalTokens.Add(PersonalToken.Create(PersonalTokens.Hash(otherPurposeToken), PersonalToken.VerifyContactEmailPurpose, s.ProfileId, DateTime.UtcNow, TimeSpan.FromHours(1)));
            await d.SaveChangesAsync();
            return 0;
        });

        foreach (var bad in new[] { PersonalTokens.Generate(), "short", "", otherPurposeToken })
        {
            var preview = await Preview(s.Factory, bad);
            Assert.Equal(HttpStatusCode.Unauthorized, preview.StatusCode);
            Assert.Equal("link-invalid", await CodeOf(preview));
            Assert.Equal(HttpStatusCode.Unauthorized, (await Decide(s.Factory, bad, "approved")).StatusCode);
        }

        // The other direction: a decision token is not a verification token.
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Factory.NewClient().PostWithCsrfAsync("/contact-requests/verify", new { token })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Preview(s.Factory, token)).StatusCode);
    }

    [Fact]
    public async Task A_new_link_revokes_the_previous_one()
    {
        var s = await NewSetupAsync();
        var first = await SentAndMailedAsync(s);

        var second = await IssueAsync(s);

        Assert.NotNull(second);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Preview(s.Factory, first)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Decide(s.Factory, first, "approved")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Preview(s.Factory, second!)).StatusCode);
        Assert.Equal(1, await Db(s.Factory, d => d.PersonalTokens.CountAsync(t => t.Purpose == "contact-decision" && t.RevokedAt == null)));
    }

    // ---- decisions -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Approving_saves_the_decision_time_and_contact_version_and_consumes_the_token()
    {
        var s = await NewSetupAsync();
        await Sql(s.Factory, $"UPDATE participant_private SET contact_version = 7 WHERE profile_id = {s.ProfileId}");
        var token = await SentAndMailedAsync(s);

        var response = await Decide(s.Factory, token, "approved");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("approved", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("decision").GetString());
        var request = await Request(s);
        Assert.Equal(ContactRequestStatus.ParticipantApproved, request.Status);
        Assert.Equal("approved", request.ParticipantDecision);
        Assert.NotNull(request.ParticipantDecidedAt);
        Assert.Equal(7, request.ParticipantContactVersion);
        Assert.Null(request.ManagerApprovedAt);
        Assert.NotNull((await Db(s.Factory, d => d.PersonalTokens.AsNoTracking().SingleAsync(t => t.Purpose == "contact-decision"))).UsedAt);

        // Used link: preview and decide both answer 409.
        var again = await Preview(s.Factory, token);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("already-decided", await CodeOf(again));
        Assert.Equal(HttpStatusCode.Conflict, (await Decide(s.Factory, token, "declined")).StatusCode);
        Assert.Equal(ContactRequestStatus.ParticipantApproved, (await Request(s)).Status);

        // The manager sees the decision in the queue.
        var item = await (await s.Admin.Http.GetAsync($"/admin/contact-requests/{s.RequestId:D}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("participant_approved", item.GetProperty("status").GetString());
        Assert.Equal("approved", item.GetProperty("participantDecision").GetString());
        Assert.NotEqual(JsonValueKind.Null, item.GetProperty("participantDecidedAt").ValueKind);
    }

    [Fact]
    public async Task Declining_moves_the_request_to_declined_and_consumes_the_token_without_a_version_or_a_mail_to_the_visitor()
    {
        var s = await NewSetupAsync();
        var token = await SentAndMailedAsync(s);
        var mails = s.Factory.Email.Messages.Count;

        var response = await Decide(s.Factory, token, "declined");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var request = await Request(s);
        Assert.Equal(ContactRequestStatus.ParticipantDeclined, request.Status);
        Assert.Equal("declined", request.ParticipantDecision);
        Assert.NotNull(request.ParticipantDecidedAt);
        Assert.Null(request.ParticipantContactVersion);
        Assert.NotNull((await Db(s.Factory, d => d.PersonalTokens.AsNoTracking().SingleAsync(t => t.Purpose == "contact-decision"))).UsedAt);
        await ProcessAsync(s.Factory);
        Assert.Equal(mails, s.Factory.Email.Messages.Count);
        Assert.DoesNotContain(s.Factory.Email.Messages, m => m.Message.To == Visitor && m.Message.Subject != "אימות כתובת המייל לבקשת יצירת קשר");
    }

    [Fact]
    public async Task An_invalid_decision_value_is_400_and_leaves_the_token_usable()
    {
        var s = await NewSetupAsync();
        var token = await SentAndMailedAsync(s);

        foreach (var bad in new[] { "maybe", "", "APPROVED" })
            Assert.Equal(HttpStatusCode.BadRequest, (await Decide(s.Factory, token, bad)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Factory.NewClient().PostWithCsrfAsync("/contact-decisions/decide", new { token })).StatusCode);

        Assert.Null((await Db(s.Factory, d => d.PersonalTokens.AsNoTracking().SingleAsync(t => t.Purpose == "contact-decision"))).UsedAt);
        Assert.Equal(HttpStatusCode.OK, (await Decide(s.Factory, token, "approved")).StatusCode);
    }

    [Fact]
    public async Task Parallel_decisions_let_exactly_one_win()
    {
        var s = await NewSetupAsync();
        var token = await SentAndMailedAsync(s);

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => Decide(s.Factory, token, i % 2 == 0 ? "approved" : "declined")));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(5, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        var request = await Request(s);
        Assert.True(request.Status is ContactRequestStatus.ParticipantApproved or ContactRequestStatus.ParticipantDeclined);
        Assert.Equal(request.Status == ContactRequestStatus.ParticipantApproved ? "approved" : "declined", request.ParticipantDecision);
    }

    [Fact]
    public async Task A_link_past_fourteen_days_is_410_and_expires_the_request()
    {
        var s = await NewSetupAsync();
        var token = await SentAndMailedAsync(s);
        await Sql(s.Factory, $"UPDATE personal_tokens SET expires_at = {DateTime.UtcNow.AddMinutes(-1)} WHERE purpose = 'contact-decision'");

        var preview = await Preview(s.Factory, token);

        Assert.Equal(HttpStatusCode.Gone, preview.StatusCode);
        Assert.Equal("link-expired", await CodeOf(preview));
        Assert.Equal(ContactRequestStatus.Expired, (await Request(s)).Status);
        Assert.Equal(HttpStatusCode.Gone, (await Decide(s.Factory, token, "approved")).StatusCode);
        Assert.Equal(ContactRequestStatus.Expired, (await Request(s)).Status);
    }

    [Fact]
    public async Task Deciding_on_an_expired_link_expires_the_request_and_records_no_decision()
    {
        var s = await NewSetupAsync();
        var token = await SentAndMailedAsync(s);
        await Sql(s.Factory, $"UPDATE personal_tokens SET expires_at = {DateTime.UtcNow.AddMinutes(-1)} WHERE purpose = 'contact-decision'");

        var response = await Decide(s.Factory, token, "approved");

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        var request = await Request(s);
        Assert.Equal(ContactRequestStatus.Expired, request.Status);
        Assert.Null(request.ParticipantDecision);
    }

    // ---- profile removal -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Removing_the_profile_expires_a_sent_or_approved_request_and_revokes_its_link(bool approvedFirst)
    {
        var s = await NewSetupAsync();
        var token = await SentAndMailedAsync(s);
        if (approvedFirst) Assert.Equal(HttpStatusCode.OK, (await Decide(s.Factory, token, "approved")).StatusCode);
        var freshToken = approvedFirst ? null : token;

        Assert.Equal(HttpStatusCode.OK, (await s.Admin.PostWithCsrfAsync($"/admin/profiles/{s.ProfileId:D}/remove", null)).StatusCode);

        Assert.Equal(ContactRequestStatus.Expired, (await Request(s)).Status);
        Assert.All(await Db(s.Factory, d => d.PersonalTokens.AsNoTracking().Where(t => t.Purpose == "contact-decision").ToListAsync()),
            t => Assert.True(t.UsedAt is not null || t.RevokedAt is not null));
        var link = await Preview(s.Factory, token);
        Assert.True(link.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.Gone, link.StatusCode.ToString());
        if (freshToken is not null)
        {
            Assert.Equal(HttpStatusCode.Gone, link.StatusCode);
            Assert.Equal(HttpStatusCode.Gone, (await Decide(s.Factory, freshToken, "approved")).StatusCode);
        }
        Assert.Equal(ContactRequestStatus.Expired, (await Request(s)).Status);
    }

    [Fact]
    public async Task Sending_after_the_profile_was_removed_is_409()
    {
        var s = await NewSetupAsync();
        Assert.Equal(HttpStatusCode.OK, (await s.Admin.PostWithCsrfAsync($"/admin/profiles/{s.ProfileId:D}/remove", null)).StatusCode);

        var response = await Send(s);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(await Db(s.Factory, d => d.EmailOutbox.AsNoTracking().Where(e => e.TemplateKey == "contact-decision-request").ToListAsync()));
    }

    // ---- hardening ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Both_decision_routes_are_rate_limited_per_ip()
    {
        var factory = await AuthApiFactory.CreateAsync(pg, limits: new Dictionary<string, string> { ["RATE_LIMIT_CONTACT_DECISION_IP"] = "2" });
        _factories.Add(factory);
        var client = factory.NewClient();
        var bad = PersonalTokens.Generate();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostWithCsrfAsync("/contact-decisions/preview", new { token = bad })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostWithCsrfAsync("/contact-decisions/preview", new { token = bad })).StatusCode);
        var limited = await client.PostWithCsrfAsync("/contact-decisions/preview", new { token = bad });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"));

        var other = await AuthApiFactory.CreateAsync(pg, limits: new Dictionary<string, string> { ["RATE_LIMIT_CONTACT_DECISION_IP"] = "2" });
        _factories.Add(other);
        var c2 = other.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c2.PostWithCsrfAsync("/contact-decisions/decide", new { token = bad, decision = "approved" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c2.PostWithCsrfAsync("/contact-decisions/decide", new { token = bad, decision = "approved" })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await c2.PostWithCsrfAsync("/contact-decisions/decide", new { token = bad, decision = "approved" })).StatusCode);
    }

    [Fact]
    public async Task A_profile_removed_directly_makes_a_valid_link_410_and_expires_the_request_without_a_decision()
    {
        var s = await NewSetupAsync();
        var token = await SentAndMailedAsync(s);
        await Sql(s.Factory, $"UPDATE profiles SET visibility = 'Removed' WHERE id = {s.ProfileId}");

        var response = await Decide(s.Factory, token, "approved");

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        var request = await Request(s);
        Assert.Equal(ContactRequestStatus.Expired, request.Status);
        Assert.Null(request.ParticipantDecision);
    }

    [Fact]
    public void CleanFreeText_removes_control_and_invisible_characters_and_keeps_text_spaces_tabs_and_line_feeds()
    {
        var dirty = "שלום עולם\u0000\u0007\u200B\u202E\uFEFF\r\nשורה\tשנייה\rשלישית  כפולה";

        var clean = ResumeApp.Features.Contacts.ContactRequestService.CleanFreeText(dirty);

        Assert.Equal("שלום עולם\nשורה\tשנייה\nשלישית  כפולה", clean);
        Assert.DoesNotContain('\r', clean);
        Assert.Equal(string.Empty, ResumeApp.Features.Contacts.ContactRequestService.CleanFreeText(null));
    }

    [Fact]
    public async Task Non_json_bodies_are_415_and_oversized_bodies_are_401_on_both_decision_routes()
    {
        var s = await NewSetupAsync();
        var client = s.Factory.NewClient();
        foreach (var route in new[] { "/contact-decisions/preview", "/contact-decisions/decide" })
        {
            var csrf = await client.GetCsrfAsync();
            var text = new HttpRequestMessage(HttpMethod.Post, route) { Content = new StringContent("token=x", System.Text.Encoding.UTF8, "text/plain") };
            text.Headers.Add("Origin", ApiClient.Origin);
            text.Headers.Add("X-CSRF-TOKEN", csrf);
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await client.Http.SendAsync(text)).StatusCode);

            var big = new { token = new string('a', 2000), decision = "approved" };
            var oversized = await client.PostAsync(route, big, await client.GetCsrfAsync());
            Assert.Equal(HttpStatusCode.Unauthorized, oversized.StatusCode);
            Assert.Equal("link-invalid", await CodeOf(oversized));
        }
    }

    [Fact]
    public async Task Decision_routes_need_the_anonymous_anti_forgery_token()
    {
        var s = await NewSetupAsync();
        var token = await SentAndMailedAsync(s);
        var client = s.Factory.NewClient();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/contact-decisions/preview", new { token }, csrf: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/contact-decisions/decide", new { token, decision = "approved" }, csrf: null)).StatusCode);
        Assert.Equal(ContactRequestStatus.SentToParticipant, (await Request(s)).Status);
    }

    [Fact]
    public async Task The_decide_response_is_not_cacheable_and_sends_no_referrer()
    {
        var s = await NewSetupAsync();
        var token = await SentAndMailedAsync(s);

        var response = await Decide(s.Factory, token, "declined");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
    }
}
