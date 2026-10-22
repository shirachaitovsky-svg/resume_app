using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ResumeApp.Domain.Audit;
using ResumeApp.Domain.Contacts;
using ResumeApp.Domain.Profiles;
using ResumeApp.Infrastructure;

namespace ResumeApp.Tests;

/// <summary>
/// Story 4.3: runs the same pg_dump -Fc and pg_restore (--no-owner --exit-on-error) commands as scripts/backup-restore.ps1 inside the
/// test container, and proves the server starts against the restored database with identical data. The development database is never touched.
/// </summary>
[Collection("env")]
public class BackupRestoreTests(PostgresFixture pg) : IDisposable
{
    private static readonly byte[] PhotoBytes = [0xFF, 0xD8, 0xFF, 0xE0, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0, 255];
    private readonly List<AuthApiFactory> _factories = [];

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
    }

    private static string DatabaseName(string url) => new Uri(url).AbsolutePath.TrimStart('/');

    private static async Task<List<string>> HistoryAsync(string url)
    {
        await using var conn = new NpgsqlConnection(ConnectionStringResolver.FromUri(url));
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT \"MigrationId\" || '/' || \"ProductVersion\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\"", conn);
        var list = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) list.Add(reader.GetString(0));
        return list;
    }

    private static async Task<string> BodyAsync(HttpClient http, string path)
    {
        var response = await http.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var node = JsonNode.Parse(body)!;
        if (node is JsonObject obj) obj.Remove("traceId");
        return node.ToJsonString();
    }

    private sealed record Counts(int Cohorts, int Profiles, int Revisions, int Privates, int Contacts, int Audit);

    private static Task<Counts> CountsAsync(AuthApiFactory f) => ParticipantSeeding.DbAsync(f, async d => new Counts(
        await d.Cohorts.CountAsync(), await d.Profiles.CountAsync(), await d.ProfileRevisions.CountAsync(),
        await d.ParticipantPrivates.CountAsync(), await d.ContactRequests.CountAsync(), await d.AuditLog.CountAsync()));

    [Fact]
    public async Task A_dump_restored_into_a_new_database_starts_the_server_with_identical_data()
    {
        // 1. Representative data in the source database.
        var source = await AuthApiFactory.CreateAsync(pg);
        _factories.Add(source);
        var cohort = await ParticipantSeeding.SeedCohortAsync(source, "מחזור גיבוי");
        var extras = new ProfileExtras("תיאור", "תפקיד", "עבודה", "היכרות", "תשובה-1", null, "תשובה-3", "girl-2", PhotoBytes, "image/jpeg", "## קורות חיים");
        var approved = await ParticipantSeeding.SeedProfileAsync(source, cohort.Id, status: RevisionStatus.Approved, extras: extras);
        var pending = await ParticipantSeeding.DbAsync(source, async d =>
        {
            var revision = ProfileRevision.CreatePending(approved.ProfileId, "דנה בגרסה חדשה", "מוצר חדש", "https://example.com/new", DateTime.UtcNow, null);
            d.ProfileRevisions.Add(revision);
            d.ContactRequests.Add(ContactRequest.Create(approved.ProfileId, "מבקרת", "visitor@example.com", "רוצה ליצור קשר", DateTime.UtcNow));
            d.AuditLog.Add(AuditLogEntry.Create(Guid.NewGuid(), DateTime.UtcNow, AuditLogEntry.ApproveRevision, "profile-revision", approved.RevisionId));
            await d.SaveChangesAsync();
            return revision.Id;
        });

        var http = source.NewClient().Http;
        var galleryBefore = await BodyAsync(http, "/profiles");
        var detailBefore = await BodyAsync(http, $"/profiles/{approved.ProfileId:D}");
        var photoResponse = await http.GetAsync($"/profiles/{approved.ProfileId:D}/photo");
        Assert.Equal(HttpStatusCode.OK, photoResponse.StatusCode);
        var photoBefore = await photoResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(PhotoBytes, photoBefore);
        var countsBefore = await CountsAsync(source);
        Assert.Equal(new Counts(1, 1, 2, 1, 1, 1), countsBefore);
        var historyBefore = await HistoryAsync(source.DatabaseUrl);
        Assert.NotEmpty(historyBefore);

        // 2. The script's commands: pg_dump -Fc, then pg_restore into a freshly created database.
        var file = $"/tmp/backup-{Guid.NewGuid():N}.dump";
        var dump = await pg.ExecInContainerAsync("pg_dump", "-U", pg.SuperUser, "-d", DatabaseName(source.DatabaseUrl), "-Fc", "-f", file);
        Assert.True(dump.ExitCode == 0, dump.Stderr);
        var targetUrl = await pg.CreateDatabaseAsync();
        try
        {
            var list = await pg.ExecInContainerAsync("pg_restore", "--list", file);
            Assert.True(list.ExitCode == 0, list.Stderr);
            var restore = await pg.ExecInContainerAsync("pg_restore", "-U", pg.SuperUser, "-d", DatabaseName(targetUrl), "--no-owner", "--exit-on-error", file);
            Assert.True(restore.ExitCode == 0, restore.Stderr);
            Assert.Equal(historyBefore, await HistoryAsync(targetUrl));

            // 3. The server starts against the restored database (MIGRATE_ON_STARTUP=true) and changes nothing in the history.
            source.Dispose();
            _factories.Remove(source);
            var restored = new AuthApiFactory(targetUrl);
            _factories.Add(restored);

            var restoredHttp = restored.NewClient().Http;
            Assert.Equal(galleryBefore, await BodyAsync(restoredHttp, "/profiles"));
            Assert.Equal(detailBefore, await BodyAsync(restoredHttp, $"/profiles/{approved.ProfileId:D}"));
            // Checked after the host has served requests, so a migration run on startup would show up here.
            Assert.Equal(historyBefore, await HistoryAsync(targetUrl));
            var photoAfter = await restoredHttp.GetAsync($"/profiles/{approved.ProfileId:D}/photo");
            Assert.Equal(HttpStatusCode.OK, photoAfter.StatusCode);
            Assert.Equal(photoBefore, await photoAfter.Content.ReadAsByteArrayAsync());

            // 4. Direct database checks: everything is present, nothing is duplicated.
            Assert.Equal(countsBefore, await CountsAsync(restored));
            await ParticipantSeeding.DbAsync(restored, async d =>
            {
                var revisions = await d.ProfileRevisions.AsNoTracking().ToListAsync();
                Assert.Equal(RevisionStatus.Pending, revisions.Single(r => r.Id == pending).Status);
                var approvedRevision = revisions.Single(r => r.Id == approved.RevisionId);
                Assert.Equal(RevisionStatus.Approved, approvedRevision.Status);
                Assert.Equal(PhotoBytes, approvedRevision.Photo);
                Assert.Equal(approved.RevisionId, (await d.Profiles.AsNoTracking().SingleAsync()).ApprovedRevisionId);
                var priv = await d.ParticipantPrivates.AsNoTracking().SingleAsync();
                Assert.Equal(approved.Email, priv.Email);
                Assert.Equal(approved.Phone, priv.Phone);
                Assert.Equal("visitor@example.com", (await d.ContactRequests.AsNoTracking().SingleAsync()).VisitorEmail);
                Assert.Equal(AuditLogEntry.ApproveRevision, (await d.AuditLog.AsNoTracking().SingleAsync()).Action);
                return 0;
            });
        }
        finally
        {
            await pg.ExecInContainerAsync("rm", "-f", file);
        }
    }
}
