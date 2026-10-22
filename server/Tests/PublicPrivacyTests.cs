using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using ResumeApp.Domain.Cohorts;
using ResumeApp.Domain.Profiles;
using ResumeApp.Features.Email;

namespace ResumeApp.Tests;

/// <summary>
/// Story 4.4 (NFR1, AR4): seeds a participant with a unique email and phone, an approved profile (CV and photo), a pending contact change
/// and a contact request, then calls every public GET endpoint (and the error paths) and proves that no body or header carries the email,
/// the phone in any format, or any seeded token.
/// </summary>
[Collection("env")]
public class PublicPrivacyTests(PostgresFixture pg) : IDisposable
{
    private static readonly string Suffix = Guid.NewGuid().ToString("N")[..10];
    private readonly string _email = $"private-{Suffix}@example.org";
    private const string Phone = "0527391846";
    private const string PendingPhone = "0548264107";
    private readonly string _pendingEmail = $"pending-{Suffix}@example.org";
    private readonly string _visitorEmail = $"visitor-{Suffix}@example.org";
    private readonly List<AuthApiFactory> _factories = [];

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
    }

    private static string[] PhoneForms(string digits)
    {
        var national = digits[1..];
        return
        [
            digits, $"{digits[..3]}-{digits[3..6]}-{digits[6..]}", $"{digits[..3]}-{digits[3..]}", $"{digits[..3]} {digits[3..]}",
            "+972" + national, "972" + national, "00972" + national, national,
        ];
    }

    private static string TokenFrom(FakeEmailSender.Sent sent)
    {
        var match = Regex.Match(sent.Message.TextBody, @"/contact/verify#token=([A-Za-z0-9_-]{43})(?![A-Za-z0-9_-])");
        Assert.True(match.Success, "no verification link in the body");
        return match.Groups[1].Value;
    }

    [Fact]
    public async Task Public_get_endpoints_and_their_errors_never_expose_contact_details_or_tokens()
    {
        var factory = await AuthApiFactory.CreateAsync(pg);
        _factories.Add(factory);
        var cohort = await ParticipantSeeding.SeedCohortAsync(factory, "מחזור פרטיות");
        var photoBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4 };
        var extras = new ProfileExtras("תיאור", "תפקיד", "עבודה", "היכרות", "תשובה-1", "תשובה-2", "תשובה-3", "girl-2", photoBytes, "image/jpeg", "## ניסיון\n\n- [אתר](https://example.com)");
        var approved = await ParticipantSeeding.SeedProfileAsync(factory, cohort.Id, _email, Phone, status: RevisionStatus.Approved, extras: extras,
            pendingEmail: _pendingEmail, pendingPhone: PendingPhone);
        var pendingProfile = await ParticipantSeeding.SeedProfileAsync(factory, cohort.Id, $"waiting-{Suffix}@example.org", "0586013592", name: "ממתינה");

        // A contact request: its verification token only ever exists in the email to the visitor.
        var client = factory.NewClient();
        var submit = await client.PostWithCsrfAsync($"/profiles/{approved.ProfileId:D}/contact-requests", new { name = "מבקר", email = _visitorEmail, reason = "אשמח לשוחח" });
        Assert.Equal(HttpStatusCode.Accepted, submit.StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<EmailOutboxProcessor>().ProcessBatchAsync(CancellationToken.None);
        var contactToken = TokenFrom(factory.Email.Messages.Last(m => m.Message.To == _visitorEmail));

        var secrets = new List<string>
        {
            _email, _pendingEmail, _visitorEmail, $"waiting-{Suffix}@example.org", cohort.InviteToken, contactToken,
            Uri.EscapeDataString(_email), Uri.EscapeDataString(_visitorEmail), System.Net.WebUtility.UrlEncode(_email),
        };
        foreach (var digits in new[] { Phone, PendingPhone, "0586013592" }) secrets.AddRange(PhoneForms(digits));

        var paths = new List<string>
        {
            "/profiles", $"/profiles?cohort={cohort.Id:D}", "/profiles?limit=1&offset=0", "/cohorts", "/cohorts/active",
            $"/profiles/{approved.ProfileId:D}", $"/profiles/{approved.ProfileId:D}/photo",
            $"/profiles/{pendingProfile.ProfileId:D}", $"/profiles/{pendingProfile.ProfileId:D}/photo",
            $"/invites/{cohort.InviteToken}", "/healthz", "/profiles/csrf", "/auth/csrf", "/auth/google/me",
            // Error paths: unknown ids, malformed ids, bad tokens, bad paging, and the secrets themselves in the URL.
            $"/profiles/{Guid.NewGuid():D}", $"/profiles/{Guid.NewGuid():D}/photo", "/profiles/not-a-guid", "/profiles/not-a-guid/photo",
            $"/profiles/{Uri.EscapeDataString(_email)}", $"/profiles/{Uri.EscapeDataString(_email)}/photo",
            "/invites/short", $"/invites/{Cohort.GenerateToken()}", $"/invites/{Uri.EscapeDataString(_email)}",
            "/profiles?cohort=bad", "/profiles?limit=0", "/profiles?offset=-1", $"/profiles?cohort={Uri.EscapeDataString(_email)}",
        };

        // Positive controls: the seeded data must really be served, so a leak check cannot pass on empty or failing responses.
        var mustServe = new HashSet<string>
        {
            "/profiles", "/cohorts", "/cohorts/active", $"/profiles/{approved.ProfileId:D}", $"/profiles/{approved.ProfileId:D}/photo", $"/invites/{cohort.InviteToken}", "/healthz",
        };

        var reader = factory.NewClient(handleCookies: false);
        foreach (var path in paths)
        {
            var response = await reader.Http.GetAsync(path);
            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (mustServe.Contains(path))
            {
                Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {path} should serve the seeded data but answered {(int)response.StatusCode}");
                Assert.NotEmpty(bytes);
            }
            if (path == "/profiles") Assert.Contains(approved.ProfileId.ToString("D"), Encoding.UTF8.GetString(bytes));
            if (path == $"/profiles/{approved.ProfileId:D}/photo") Assert.Equal(photoBytes, bytes);
            var body = Encoding.UTF8.GetString(bytes) + Encoding.Latin1.GetString(bytes);
            var headers = string.Join("\n", response.Headers.Concat(response.Content.Headers).Select(h => $"{h.Key}: {string.Join(",", h.Value)}"));

            foreach (var secret in secrets)
            {
                // The one legitimate echo of a secret is the request URL itself, never the response.
                Assert.False(body.Contains(secret, StringComparison.OrdinalIgnoreCase), $"GET {path} body leaks '{Redact(secret)}'");
                Assert.False(headers.Contains(secret, StringComparison.OrdinalIgnoreCase), $"GET {path} headers leak '{Redact(secret)}'");
            }

            if ((int)response.StatusCode >= 400)
            {
                Assert.True(response.Content.Headers.ContentType?.MediaType == "application/problem+json",
                    $"GET {path} -> {(int)response.StatusCode} must be problem+json, was {response.Content.Headers.ContentType}");
                Assert.DoesNotContain("   at ", body); // no stack trace
            }
        }
    }

    /// <summary>Public GET routes the privacy test above calls (route patterns, as in the endpoint allowlist).</summary>
    private static readonly string[] CoveredGetPatterns =
    [
        "/healthz", "/auth/csrf", "/auth/google/me", "/profiles/csrf", "/profiles", "/profiles/{profileId}", "/profiles/{profileId}/photo", "/cohorts", "/cohorts/active", "/invites/{token}",
    ];

    /// <summary>Anonymous GET routes deliberately not called by the privacy test, each with the reason.</summary>
    private static readonly Dictionary<string, string> ExcludedGetPatterns = new()
    {
        ["/auth/google/start"] = "redirects to the identity provider with a signed flow cookie; it reads and returns no stored data",
        ["/auth/google/callback"] = "needs a real provider code and answers only with redirects; covered by GoogleSignInTests",
        ["/dev-idp/authorize"] = "fake identity provider, mapped only in Development; proved absent for GOOGLE_PROVIDER=google in EndpointSecurityTests",
    };

    [Fact]
    public void Every_anonymous_get_in_the_allowlist_is_covered_by_the_privacy_test_or_explicitly_excluded()
    {
        var anonymousGets = EndpointSecurityTests.AnonymousAllowlist.Where(e => e.StartsWith("GET ", StringComparison.Ordinal)).Select(e => e[4..]).ToList();
        Assert.NotEmpty(anonymousGets);

        var unaccounted = anonymousGets.Where(p => !CoveredGetPatterns.Contains(p) && !ExcludedGetPatterns.ContainsKey(p)).ToList();
        Assert.True(unaccounted.Count == 0, $"Anonymous GET endpoints not covered by PublicPrivacyTests nor excluded with a reason: [{string.Join("; ", unaccounted)}]");

        var stale = CoveredGetPatterns.Concat(ExcludedGetPatterns.Keys).Where(p => !anonymousGets.Contains(p)).ToList();
        Assert.True(stale.Count == 0, $"Privacy test lists GET endpoints that are not in the anonymous allowlist: [{string.Join("; ", stale)}]");
        Assert.All(ExcludedGetPatterns.Values, reason => Assert.False(string.IsNullOrWhiteSpace(reason)));
    }

    private static IEnumerable<string> EmailForms(string email)
        =>
        [
            email, Uri.EscapeDataString(email), System.Net.WebUtility.UrlEncode(email), System.Text.Json.JsonSerializer.Serialize(email).Trim('"'),
            email.Replace("@", "\\u0040", StringComparison.Ordinal), email.Replace("+", "\\u002B", StringComparison.Ordinal),
        ];

    private static async Task AssertNoLeakAsync(HttpResponseMessage response, IEnumerable<string> secrets, string label)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var body = Encoding.UTF8.GetString(bytes);
        var headers = string.Join("\n", response.Headers.Concat(response.Content.Headers).Select(h => $"{h.Key}: {string.Join(",", h.Value)}"));
        foreach (var secret in secrets)
        {
            Assert.False(body.Contains(secret, StringComparison.OrdinalIgnoreCase), $"{label} body leaks '{Redact(secret)}'");
            Assert.False(headers.Contains(secret, StringComparison.OrdinalIgnoreCase), $"{label} headers leak '{Redact(secret)}'");
        }
        if ((int)response.StatusCode >= 400)
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private static async Task<string> MailedTokenAsync(AuthApiFactory factory, string to)
    {
        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<EmailOutboxProcessor>().ProcessBatchAsync(CancellationToken.None);
        return TokenFrom(factory.Email.Messages.Last(m => m.Message.To == to));
    }

    [Fact]
    public async Task Public_post_endpoints_never_echo_contact_details_or_tokens()
    {
        var factory = await AuthApiFactory.CreateAsync(pg);
        _factories.Add(factory);
        var visitor = $"visitor+{Suffix}@example.org";
        var approved = await ParticipantSeeding.SeedProfileAsync(factory, email: _email, phone: Phone, status: RevisionStatus.Approved);
        var client = factory.NewClient();
        var profile = approved.ProfileId.ToString("D");

        var valid = await client.PostWithCsrfAsync($"/profiles/{profile}/contact-requests", new { name = "מבקר", email = visitor, reason = "אשמח לשוחח" });
        Assert.Equal(HttpStatusCode.Accepted, valid.StatusCode);
        Assert.NotEmpty(await valid.Content.ReadAsByteArrayAsync());
        var token = await MailedTokenAsync(factory, visitor);

        var secrets = new List<string> { token };
        secrets.AddRange(EmailForms(visitor));
        secrets.AddRange(EmailForms(_email));
        secrets.AddRange(PhoneForms(Phone));

        await AssertNoLeakAsync(valid, secrets, "POST contact-requests (valid)");
        await AssertNoLeakAsync(await client.PostWithCsrfAsync($"/profiles/{profile}/contact-requests", new { name = "", email = visitor, reason = "" }), secrets, "POST contact-requests (invalid)");
        await AssertNoLeakAsync(await client.PostWithCsrfAsync($"/profiles/{Guid.NewGuid():D}/contact-requests", new { name = "מבקר", email = visitor, reason = "אשמח לשוחח" }), secrets, "POST contact-requests (unknown profile)");

        // Verify: a wrong token, then the real one (the one legitimate success). Neither may echo the token.
        await AssertNoLeakAsync(await client.PostWithCsrfAsync("/contact-requests/verify", new { token = Cohort.GenerateToken() }), secrets, "POST verify (wrong token)");
        var verified = await client.PostWithCsrfAsync("/contact-requests/verify", new { token });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        Assert.NotEmpty(await verified.Content.ReadAsByteArrayAsync());
        await AssertNoLeakAsync(verified, secrets, "POST verify (real token)");

        // Decision links: well formed but unknown tokens, a malformed token, and a bad decision value.
        foreach (var bad in new[] { Cohort.GenerateToken(), "short", token })
        {
            await AssertNoLeakAsync(await client.PostWithCsrfAsync("/contact-decisions/preview", new { token = bad }), secrets, "POST preview");
            await AssertNoLeakAsync(await client.PostWithCsrfAsync("/contact-decisions/decide", new { token = bad, decision = "approve" }), secrets, "POST decide");
        }
        await AssertNoLeakAsync(await client.PostWithCsrfAsync("/contact-decisions/decide", new { token = Cohort.GenerateToken(), decision = "bogus" }), secrets, "POST decide (bad decision)");
    }

    private static string Redact(string secret) => secret.Length <= 6 ? "***" : secret[..3] + "..." + secret[^3..];
}
