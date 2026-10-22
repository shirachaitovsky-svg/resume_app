using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using ResumeApp.Domain.Profiles;
using ResumeApp.Features.Email;

namespace ResumeApp.Tests;

/// <summary>
/// Story 4.1: the logs (Serilog, every level, the test host runs in Development at Debug) never carry an email, a phone, a token, a CV, a manager's note
/// or a contact reason, in any of the flows that handle them. Complements the sign-in and profile-creation checks of the earlier stories.
/// </summary>
[Collection("env")]
public class LogPrivacyTests(PostgresFixture pg) : IDisposable
{
    private const string NewPassword = "Brand-New-Pass-42#Lk";
    private const string ParticipantEmail = "log.participant@example.com";
    private const string ParticipantPhone = "0521112223";
    private const string NewEmail = "log.new.mail@example.com";
    private const string NewPhone = "0539998887";
    private const string Visitor = "log.visitor@example.com";
    private const string VisitorName = "מבקרת-סודית-ZQX";
    private const string Reason = "סיבת-פנייה-סודית-ZQX לשיתוף פעולה";
    private const string CvText = "ניסיון-סודי-ZQX בניהול מוצר";
    private const string ManagerNote = "הערה-פנימית-למנהלת-ZQX";
    private readonly List<AuthApiFactory> _factories = [];
    private readonly CapturingSink _log = new();

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
    }

    private sealed record Setup(AuthApiFactory Factory, ApiClient Admin);

    private async Task<Setup> NewSetupAsync()
    {
        var factory = await AuthApiFactory.CreateAsync(pg, configureServices: s => s.AddSingleton<ILogEventSink>(_log));
        _factories.Add(factory);
        var admin = factory.NewClient();
        await admin.EnrollAsync(AuthApiFactory.AdminUser, AuthApiFactory.InitialPassword, NewPassword);
        return new(factory, admin);
    }

    private static async Task ProcessAsync(AuthApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<EmailOutboxProcessor>().ProcessBatchAsync(CancellationToken.None);
    }

    private static string TokenFrom(string mailBody) => Regex.Match(mailBody, @"#token=([A-Za-z0-9_-]{43})").Groups[1].Value;

    /// <summary>The log must have content (so the check is not vacuous) and none of it may contain any of the secrets.</summary>
    private void AssertLogsClean(params string[] secrets)
    {
        var lines = _log.Lines;
        Assert.NotEmpty(lines);
        foreach (var secret in secrets.Where(s => !string.IsNullOrEmpty(s)))
            Assert.DoesNotContain(lines, l => l.Contains(secret, StringComparison.OrdinalIgnoreCase));
        foreach (var generic in new[] { "id_token", "access_token", "refresh_token" })
            Assert.DoesNotContain(lines, l => l.Contains(generic, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A participant who started and finished the fake Google sign-in; returns the client plus the code and state of that round trip.</summary>
    private static async Task<(ApiClient Client, string Code, string State)> GoogleSignInAsync(AuthApiFactory f, string email)
    {
        var c = f.NewClient();
        var start = await c.Http.GetAsync("/auth/google/start");
        var state = QueryHelpers.ParseQuery(start.Headers.Location!.Query)["state"].ToString();
        var startPath = start.Headers.Location!.IsAbsoluteUri ? start.Headers.Location.PathAndQuery : start.Headers.Location.OriginalString;
        var authorize = await c.Http.GetAsync(Server(QueryHelpers.AddQueryString(startPath, new Dictionary<string, string?> { ["fake_email"] = email })));
        var callbackPath = Server(authorize.Headers.Location!.IsAbsoluteUri ? authorize.Headers.Location.PathAndQuery : authorize.Headers.Location.OriginalString);
        var code = QueryHelpers.ParseQuery(new Uri("http://x" + callbackPath).Query)["code"].ToString();
        Assert.Equal(HttpStatusCode.Redirect, (await c.Http.GetAsync(callbackPath)).StatusCode);
        return (c, code, state);
    }

    private static string Server(string pathAndQuery) => pathAndQuery.StartsWith("/api/", StringComparison.Ordinal) ? pathAndQuery[4..] : pathAndQuery;

    private static async Task<Guid> PendingRevisionAsync(AuthApiFactory f, Guid profileId)
        => await ParticipantSeeding.DbAsync(f, d => d.ProfileRevisions.AsNoTracking()
            .Where(r => r.ProfileId == profileId && r.Status == RevisionStatus.Pending).Select(r => r.Id).SingleAsync());

    [Fact]
    public async Task Editing_a_profile_with_a_cv_and_a_phone_change_logs_no_personal_data()
    {
        var s = await NewSetupAsync();
        var profile = await ParticipantSeeding.SeedProfileAsync(s.Factory, email: ParticipantEmail, phone: ParticipantPhone, status: RevisionStatus.Approved);
        var (client, code, state) = await GoogleSignInAsync(s.Factory, ParticipantEmail);

        var request = new HttpRequestMessage(HttpMethod.Put, "/me/profile")
        {
            Content = JsonContent.Create(new
            {
                name = "דנה כהן", productName = "מוצר", productUrl = "https://example.com/app", email = NewEmail, phone = NewPhone, cv = CvText,
            }),
        };
        request.Headers.Add("Origin", ApiClient.Origin);
        request.Headers.Add("X-CSRF-TOKEN", (await (await client.Http.GetAsync("/profiles/csrf")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!);
        var response = await client.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        AssertLogsClean(ParticipantEmail, ParticipantPhone, NewEmail, NewPhone, CvText, code, state);
    }

    [Fact]
    public async Task A_contact_request_and_the_address_verification_log_no_name_email_reason_or_token()
    {
        var s = await NewSetupAsync();
        var profile = await ParticipantSeeding.SeedProfileAsync(s.Factory, email: ParticipantEmail, phone: ParticipantPhone, status: RevisionStatus.Approved);

        var created = await s.Factory.NewClient().PostWithCsrfAsync($"/profiles/{profile.ProfileId:D}/contact-requests",
            new { name = VisitorName, email = Visitor, reason = Reason });
        Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
        await ProcessAsync(s.Factory);
        var token = TokenFrom(s.Factory.Email.Messages.Last(m => m.Message.To == Visitor).Message.TextBody);
        Assert.NotEmpty(token);
        var verified = await s.Factory.NewClient().PostWithCsrfAsync("/contact-requests/verify", new { token });

        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        AssertLogsClean(Visitor, VisitorName, Reason, token, ParticipantEmail, ParticipantPhone);
    }

    [Fact]
    public async Task The_participants_decision_the_managers_approval_and_the_delivery_log_no_personal_data()
    {
        var s = await NewSetupAsync();
        var profile = await ParticipantSeeding.SeedProfileAsync(s.Factory, email: ParticipantEmail, phone: ParticipantPhone, status: RevisionStatus.Approved);
        Assert.Equal(HttpStatusCode.Accepted, (await s.Factory.NewClient().PostWithCsrfAsync($"/profiles/{profile.ProfileId:D}/contact-requests",
            new { name = VisitorName, email = Visitor, reason = Reason })).StatusCode);
        await ProcessAsync(s.Factory);
        var verifyToken = TokenFrom(s.Factory.Email.Messages.Last(m => m.Message.To == Visitor).Message.TextBody);
        Assert.Equal(HttpStatusCode.OK, (await s.Factory.NewClient().PostWithCsrfAsync("/contact-requests/verify", new { token = verifyToken })).StatusCode);
        var requestId = await ParticipantSeeding.DbAsync(s.Factory, d => d.ContactRequests.AsNoTracking().Select(r => r.Id).SingleAsync());
        await ProcessAsync(s.Factory);

        Assert.Equal(HttpStatusCode.OK, (await s.Admin.PostWithCsrfAsync($"/admin/contact-requests/{requestId:D}/send-to-participant", null)).StatusCode);
        await ProcessAsync(s.Factory);
        var decisionToken = TokenFrom(s.Factory.Email.Messages.Last(m => m.Message.To == ParticipantEmail).Message.TextBody);
        Assert.NotEmpty(decisionToken);
        Assert.Equal(HttpStatusCode.OK, (await s.Factory.NewClient().PostWithCsrfAsync("/contact-decisions/decide", new { token = decisionToken, decision = "approved" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.Admin.PostWithCsrfAsync($"/admin/contact-requests/{requestId:D}/approve-delivery", null)).StatusCode);
        var before = s.Factory.Email.Messages.Count;
        await ProcessAsync(s.Factory);

        // The delivery mail itself carries the details to the visitor, but nothing of it reaches the logs.
        var delivery = Assert.Single(s.Factory.Email.Messages.Skip(before), m => m.Message.To == Visitor).Message.TextBody;
        Assert.Contains(ParticipantEmail, delivery);
        AssertLogsClean(Visitor, VisitorName, Reason, verifyToken, decisionToken, ParticipantEmail, ParticipantPhone);
    }

    [Fact]
    public async Task Returning_a_submission_for_correction_logs_no_note_or_personal_data()
    {
        var s = await NewSetupAsync();
        var profile = await ParticipantSeeding.SeedProfileAsync(s.Factory, email: ParticipantEmail, phone: ParticipantPhone);
        var revision = await PendingRevisionAsync(s.Factory, profile.ProfileId);

        var response = await s.Admin.PostWithCsrfAsync($"/admin/submissions/{revision:D}/return", new { note = ManagerNote });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await ProcessAsync(s.Factory);

        Assert.Contains(s.Factory.Email.Messages, m => m.Message.To == ParticipantEmail);
        AssertLogsClean(ManagerNote, ParticipantEmail, ParticipantPhone);
    }

    [Fact]
    public async Task An_unexpected_error_while_creating_a_profile_logs_no_personal_data()
    {
        var factory = await AuthApiFactory.CreateAsync(pg, configureServices: s =>
        {
            s.AddSingleton<ILogEventSink>(_log);
            s.AddScoped<ResumeApp.Features.Cohorts.ICohortRepository, ThrowingCohorts>();
        });
        _factories.Add(factory);
        var (client, code, state) = await GoogleSignInAsync(factory, ParticipantEmail);

        var response = await client.PostWithCsrfAsync("/me/profile", new
        {
            cohortId = Guid.NewGuid().ToString("D"), name = "דנה", productName = "מוצר", productUrl = "https://example.com/app", phone = ParticipantPhone,
        });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        foreach (var secret in new[] { ParticipantEmail, ParticipantPhone }) Assert.DoesNotContain(secret, body);
        Assert.Contains(_log.Lines, l => l.Contains("Unhandled exception of type", StringComparison.OrdinalIgnoreCase));
        AssertLogsClean(ParticipantEmail, ParticipantPhone, code, state);
    }

    /// <summary>Every cohort lookup fails with a message that carries the signed-in participant's email.</summary>
    private sealed class ThrowingCohorts : ResumeApp.Features.Cohorts.ICohortRepository
    {
        private static Exception Boom() => new InvalidOperationException("lookup failed for " + ParticipantEmail + " " + ParticipantPhone);
        public Task AddAsync(ResumeApp.Domain.Cohorts.Cohort cohort, CancellationToken ct) => throw Boom();
        public Task<IReadOnlyList<ResumeApp.Domain.Cohorts.Cohort>> ListAsync(CancellationToken ct) => throw Boom();
        public Task<ResumeApp.Domain.Cohorts.Cohort?> FindByTokenAsync(string token, CancellationToken ct) => throw Boom();
        public Task<bool> TryRevokeAsync(Guid id, DateTime nowUtc, CancellationToken ct) => throw Boom();
        public Task<bool> ExistsAsync(Guid id, CancellationToken ct) => throw Boom();
        public Task<ResumeApp.Domain.Cohorts.Cohort?> FindActiveByIdAsync(Guid id, CancellationToken ct) => throw Boom();
        public Task<IReadOnlyList<ResumeApp.Domain.Cohorts.Cohort>> ListActiveAsync(CancellationToken ct) => throw Boom();
    }
}
