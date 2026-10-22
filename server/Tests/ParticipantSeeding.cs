using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using ResumeApp.Api.Security;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResumeApp.Domain.Cohorts;
using ResumeApp.Domain.Profiles;
using ResumeApp.Features.Profiles;
using ResumeApp.Infrastructure;

namespace ResumeApp.Tests;

/// <summary>A profile written straight to the database by <see cref="ParticipantSeeding"/>.</summary>
public sealed record SeededProfile(Guid ProfileId, Guid CohortId, Guid RevisionId, string Email, string Phone, string GoogleEmail);

/// <summary>
/// Story 6.4: the one shared way tests create participants. Profiles, revisions and private rows are written directly through
/// <see cref="AppDbContext"/> (no invite submission, no personal link), and a participant signs in through the fake Google provider.
/// </summary>
public static class ParticipantSeeding
{
    public const string DefaultEmail = "dana@example.com";
    public const string DefaultPhone = "0501234567";
    public const string DefaultName = "דנה כהן";

    public static async Task<T> DbAsync<T>(AuthApiFactory f, Func<AppDbContext, Task<T>> work)
    {
        await using var scope = f.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public static async Task<Cohort> SeedCohortAsync(AuthApiFactory f, string name = "מחזור")
    {
        var cohort = Cohort.Create(name, DateTime.UtcNow);
        await DbAsync(f, async d =>
        {
            d.Cohorts.Add(cohort);
            await d.SaveChangesAsync();
            return 0;
        });
        return cohort;
    }

    /// <summary>
    /// Creates a profile with its private row and one revision (pending by default; <see cref="RevisionStatus.Approved"/> also publishes it).
    /// The profile is linked to the fake Google account of <paramref name="googleEmail"/> (default: the stored email; pass <c>""</c> to leave it unlinked).
    /// Email and phone are normalised like a real submission. A new cohort is created unless <paramref name="cohortId"/> is given.
    /// </summary>
    public static async Task<SeededProfile> SeedProfileAsync(
        AuthApiFactory f, Guid? cohortId = null, string email = DefaultEmail, string phone = DefaultPhone, string name = DefaultName,
        string productName = "מוצר", string productUrl = "https://example.com/app", RevisionStatus status = RevisionStatus.Pending,
        string? googleEmail = null, ProfileExtras? extras = null, string? pendingEmail = null, string? pendingPhone = null, DateTime? now = null)
    {
        var at = now ?? DateTime.UtcNow;
        var normalizedEmail = ProfileService.NormalizeEmail(email)!;
        var normalizedPhone = ProfileService.NormalizePhone(phone) ?? phone;
        var google = googleEmail ?? normalizedEmail;
        var cid = cohortId ?? (await SeedCohortAsync(f)).Id;

        return await DbAsync(f, async d =>
        {
            var profile = google.Length > 0 ? Profile.Create(cid, at, FakeOidcProvider.SubFor(google)) : Profile.Create(cid, at);
            d.Profiles.Add(profile);
            await d.SaveChangesAsync();
            d.ParticipantPrivates.Add(ParticipantPrivate.Create(profile.Id, cid, normalizedEmail, normalizedPhone));
            var revision = ProfileRevision.CreatePending(profile.Id, name, productName, productUrl, at, extras, pendingEmail, pendingPhone);
            d.ProfileRevisions.Add(revision);
            if (status != RevisionStatus.Pending) d.Entry(revision).Property(r => r.Status).CurrentValue = status;
            await d.SaveChangesAsync();
            if (status == RevisionStatus.Approved)
            {
                d.Entry(profile).Property(p => p.ApprovedRevisionId).CurrentValue = revision.Id;
                await d.SaveChangesAsync();
            }
            return new SeededProfile(profile.Id, cid, revision.Id, normalizedEmail, normalizedPhone, google);
        });
    }

    private static string PathAndQuery(Uri uri) => uri.IsAbsoluteUri ? uri.PathAndQuery : uri.OriginalString;

    private static string ServerPath(string pathAndQuery) => pathAndQuery.StartsWith("/api/", StringComparison.Ordinal) ? pathAndQuery[4..] : pathAndQuery;

    /// <summary>Signs in through the fake Google provider (start, authorize, callback). The client must hold cookies.</summary>
    public static async Task SignInAsync(this ApiClient c, string googleEmail)
    {
        var start = await c.Http.GetAsync("/auth/google/start");
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        var authorizeUrl = PathAndQuery(start.Headers.Location!);
        var authorize = await c.Http.GetAsync(ServerPath(QueryHelpers.AddQueryString(authorizeUrl, new Dictionary<string, string?> { ["fake_email"] = googleEmail })));
        var callback = await c.Http.GetAsync(ServerPath(PathAndQuery(authorize.Headers.Location!)));
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
    }

    /// <summary>A new cookie-holding client signed in as the participant of <paramref name="profile"/>.</summary>
    public static async Task<ApiClient> SignedInAsync(this AuthApiFactory f, SeededProfile profile)
    {
        var c = f.NewClient();
        await c.SignInAsync(profile.GoogleEmail);
        return c;
    }

    /// <summary>
    /// A cookie-less client that presents a hand-made participant session cookie (the participant scheme's own data protection) with the
    /// given purpose claim. Used to prove that sessions of a purpose the server no longer issues (the retired personal link) are refused.
    /// </summary>
    public static ApiClient ClientWithForgedSession(this AuthApiFactory f, Guid profileId, string purpose)
    {
        var options = f.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityServiceExtensions.ParticipantScheme);
        var now = DateTimeOffset.UtcNow;
        var identity = new ClaimsIdentity(
            [new Claim(IdentityServiceExtensions.ProfileIdClaim, profileId.ToString("D")), new Claim(IdentityServiceExtensions.PurposeClaim, purpose)],
            IdentityServiceExtensions.ParticipantScheme);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true, IssuedUtc = now, ExpiresUtc = now.AddMinutes(30) }, IdentityServiceExtensions.ParticipantScheme);
        var client = f.NewClient();
        client.Http.DefaultRequestHeaders.Add("Cookie", $"{IdentityServiceExtensions.ParticipantCookieName}={options.TicketDataFormat.Protect(ticket)}");
        return client;
    }

    /// <summary>A fresh Google account email, so every creation in a test is a different account.</summary>
    public static string NewGoogleEmail() => $"user-{Guid.NewGuid():N}@example.com";

    /// <summary>The JSON body of <paramref name="body"/> with <c>cohortId</c> set (the email field, when present, is ignored by the server).</summary>
    public static Dictionary<string, object?> WithCohort(object body, string cohortId)
    {
        var element = JsonSerializer.SerializeToElement(body);
        var result = new Dictionary<string, object?>();
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject()) result[property.Name] = property.Value.Clone();
        result["cohortId"] = cohortId;
        return result;
    }

    /// <summary>Signs a new client in as a new Google account (or <paramref name="googleEmail"/>) and creates a profile with <c>POST /me/profile</c> (JSON).</summary>
    public static async Task<HttpResponseMessage> CreateProfileAsync(AuthApiFactory f, string cohortId, object body, string? googleEmail = null)
    {
        var c = f.NewClient();
        await c.SignInAsync(googleEmail ?? NewGoogleEmail());
        return await c.PostWithCsrfAsync("/me/profile", WithCohort(body, cohortId));
    }

    /// <summary>Like <see cref="CreateProfileAsync"/> with a multipart body (text fields plus an optional <c>photo</c> part).</summary>
    public static async Task<HttpResponseMessage> CreateProfileMultipartAsync(AuthApiFactory f, string cohortId, Dictionary<string, string> fields,
        byte[]? photo = null, string fileName = "me.png", string photoType = "image/png", string? googleEmail = null)
    {
        var c = f.NewClient();
        await c.SignInAsync(googleEmail ?? NewGoogleEmail());
        var content = new MultipartFormDataContent();
        foreach (var (k, v) in fields) content.Add(new StringContent(v, Encoding.UTF8), k);
        content.Add(new StringContent(cohortId, Encoding.UTF8), "cohortId");
        if (photo is not null)
        {
            var file = new ByteArrayContent(photo);
            file.Headers.ContentType = new MediaTypeHeaderValue(photoType);
            content.Add(file, "photo", fileName);
        }
        var request = new HttpRequestMessage(HttpMethod.Post, "/me/profile") { Content = content };
        request.Headers.Add("Origin", ApiClient.Origin);
        request.Headers.Add("X-CSRF-TOKEN", await c.GetCsrfAsync());
        return await c.Http.SendAsync(request);
    }

    /// <summary>Signs a new client in and posts raw content to <c>/me/profile</c>.</summary>
    public static async Task<HttpResponseMessage> CreateProfileRawAsync(AuthApiFactory f, HttpContent content, string? googleEmail = null)
    {
        var c = f.NewClient();
        await c.SignInAsync(googleEmail ?? NewGoogleEmail());
        var request = new HttpRequestMessage(HttpMethod.Post, "/me/profile") { Content = content };
        request.Headers.Add("Origin", ApiClient.Origin);
        request.Headers.Add("X-CSRF-TOKEN", await c.GetCsrfAsync());
        return await c.Http.SendAsync(request);
    }
}
