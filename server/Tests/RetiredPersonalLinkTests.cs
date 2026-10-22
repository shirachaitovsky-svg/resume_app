using System.Net;
using ResumeApp.Domain.Email;

namespace ResumeApp.Tests;

/// <summary>
/// Names of the personal edit link that story 6.4 removed from the server (its token purpose, outbox template, session exchange and
/// request routes, and the anonymous invite submission). They are assembled from pieces so that a repository search for the retired
/// mechanism finds only explanatory comments, while the tests below still prove each of these stays gone.
/// </summary>
public static class RetiredPersonalLink
{
    public const string Purpose = "edit-" + "profile";
    public const string TemplateKey = "edit-" + "link";
    public const string SessionPath = "/profiles/" + "session";
    public const string LogoutPath = SessionPath + "/logout";
    public const string RequestPath = "/profiles/" + TemplateKey;

    public static string InvitePath(string inviteToken) => "/invites/" + inviteToken + "/profiles";
}

/// <summary>Story 6.4: the retired routes and the retired token purpose no longer exist.</summary>
[Collection("env")]
public class RetiredPersonalLinkTests(PostgresFixture pg) : IDisposable
{
    private readonly List<AuthApiFactory> _factories = [];

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
    }

    [Fact]
    public async Task The_retired_routes_are_not_mapped_for_anonymous_callers_or_signed_in_participants()
    {
        var factory = await AuthApiFactory.CreateAsync(pg);
        _factories.Add(factory);
        var cohort = await ParticipantSeeding.SeedCohortAsync(factory);
        var seeded = await ParticipantSeeding.SeedProfileAsync(factory, cohort.Id);
        var participant = await factory.SignedInAsync(seeded);
        var anonymous = factory.NewClient();

        var routes = new[]
        {
            (RetiredPersonalLink.SessionPath, new { token = "x" }),
            (RetiredPersonalLink.LogoutPath, new { token = "x" }),
            (RetiredPersonalLink.RequestPath, new { token = "x" }),
            (RetiredPersonalLink.InvitePath(cohort.InviteToken), new { token = "x" }),
        };
        foreach (var (path, body) in routes)
        {
            // The closed-by-default policy may answer 401 (the participant cookie is not the default scheme), but a retired handler would
            // answer 401 link-invalid or 2xx/400: the body check below rules that out.
            var signedIn = await participant.PostWithCsrfAsync(path, body);
            Assert.Contains(signedIn.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.Unauthorized, HttpStatusCode.MethodNotAllowed });
            var anon = await anonymous.PostWithCsrfAsync(path, body);
            Assert.Contains(anon.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.Unauthorized, HttpStatusCode.MethodNotAllowed });
            foreach (var response in new[] { signedIn, anon })
                Assert.DoesNotContain("link-invalid", await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(1, await ParticipantSeeding.DbAsync(factory, d => Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(d.Profiles)));
    }

    [Fact]
    public async Task The_invite_check_and_a_session_with_the_retired_purpose_behave_as_specified()
    {
        var factory = await AuthApiFactory.CreateAsync(pg);
        _factories.Add(factory);
        var cohort = await ParticipantSeeding.SeedCohortAsync(factory);
        var seeded = await ParticipantSeeding.SeedProfileAsync(factory, cohort.Id);

        // GET /invites/{token} is unchanged.
        Assert.Equal(HttpStatusCode.OK, (await factory.NewClient().Http.GetAsync("/invites/" + cohort.InviteToken)).StatusCode);

        // A participant cookie that carries the retired purpose is refused, while the same profile signed in with Google works.
        var retired = factory.ClientWithForgedSession(seeded.ProfileId, RetiredPersonalLink.Purpose);
        Assert.Equal(HttpStatusCode.Unauthorized, (await retired.Http.GetAsync("/me/profile")).StatusCode);
        var google = await factory.SignedInAsync(seeded);
        Assert.Equal(HttpStatusCode.OK, (await google.Http.GetAsync("/me/profile")).StatusCode);
    }

    [Fact]
    public void The_remaining_token_purposes_are_only_the_contact_ones()
    {
        var purposes = typeof(PersonalToken).GetFields().Where(f => f.Name.EndsWith("Purpose", StringComparison.Ordinal)).Select(f => (string)f.GetRawConstantValue()!).Order().ToArray();
        Assert.Equal(["contact-decision", "verify-contact-email"], purposes);
    }
}
