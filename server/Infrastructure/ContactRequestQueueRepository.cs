using Microsoft.EntityFrameworkCore;
using ResumeApp.Domain.Contacts;
using ResumeApp.Domain.Email;
using ResumeApp.Domain.Profiles;
using ResumeApp.Features.Contacts;
using ResumeApp.Features.Profiles;

namespace ResumeApp.Infrastructure;

/// <summary>
/// Read side of the admin contact-request queue. Reads contact_requests, profiles, revisions, cohorts and the notification's outbox row only:
/// never ParticipantPrivate. The list and its total are both taken from contact_requests alone (profile, cohort and revision data are
/// correlated subqueries, i.e. left-join semantics), so a request can never be counted yet unreachable.
/// </summary>
public sealed class ContactRequestQueueRepository(AppDbContext db) : IContactRequestQueueRepository
{
    private sealed record Row(
        Guid Id, string VisitorName, string VisitorEmail, string Reason, Guid ProfileId, ContactRequestStatus Status, DateTime CreatedAt, DateTime VerifiedAt,
        string? ParticipantDecision, DateTime? ParticipantDecidedAt, DateTime? ManagerApprovedAt,
        ProfileVisibility? Visibility, string? CohortName, string? ProfileName, string? ProductName, OutboxStatus? Notification);

    private IQueryable<Row> Project(IQueryable<ContactRequest> requests)
    {
        var template = EmailOutboxEntry.ContactRequestNotificationTemplate;
        return requests.Select(r => new Row(r.Id, r.VisitorName, r.VisitorEmail, r.Reason, r.ProfileId, r.Status, r.CreatedAt, r.VerifiedAt!.Value,
            r.ParticipantDecision, r.ParticipantDecidedAt, r.ManagerApprovedAt,
            db.Profiles.Where(p => p.Id == r.ProfileId).Select(p => (ProfileVisibility?)p.Visibility).FirstOrDefault(),
            db.Profiles.Where(p => p.Id == r.ProfileId).Join(db.Cohorts, p => p.CohortId, c => c.Id, (p, c) => c.Name).FirstOrDefault(),
            // The approved revision (the pointer) when it exists, otherwise the latest revision.
            (from v in db.ProfileRevisions
             where v.ProfileId == r.ProfileId
             orderby (db.Profiles.Any(p => p.Id == r.ProfileId && p.ApprovedRevisionId == v.Id) ? 0 : 1), v.SubmittedAt descending, v.Id descending
             select v.Name).FirstOrDefault(),
            (from v in db.ProfileRevisions
             where v.ProfileId == r.ProfileId
             orderby (db.Profiles.Any(p => p.Id == r.ProfileId && p.ApprovedRevisionId == v.Id) ? 0 : 1), v.SubmittedAt descending, v.Id descending
             select v.ProductName).FirstOrDefault(),
            db.EmailOutbox.Where(o => o.RequestId == r.Id && o.TemplateKey == template).Select(o => (OutboxStatus?)o.Status).FirstOrDefault()));
    }

    private static string OrPlaceholder(string? text) => string.IsNullOrWhiteSpace(text) ? ContactRequestQueueService.Placeholder : text;

    private static ContactRequestQueueItem ToItem(Row x) => new(
        x.Id, x.VisitorName, x.VisitorEmail, x.Reason, x.ProfileId, OrPlaceholder(x.ProfileName), OrPlaceholder(x.ProductName),
        OrPlaceholder(x.CohortName), x.Visibility is { } v ? VisibilityNames.Wire(v) : "removed", x.CreatedAt, x.VerifiedAt,
        ContactRequestStatusNames.ToWire(x.Status), x.ParticipantDecision, x.ParticipantDecidedAt, x.ManagerApprovedAt,
        x.Notification is { } n ? n.ToString().ToLowerInvariant() : ContactRequestQueueService.NotificationNone);

    public async Task<ContactRequestQueuePage> ListAsync(IReadOnlyCollection<ContactRequestStatus> statuses, int limit, int offset, CancellationToken ct)
    {
        var wanted = statuses.ToArray();
        var verified = db.ContactRequests.AsNoTracking().Where(r => r.VerifiedAt != null && wanted.Contains(r.Status));
        var total = await verified.CountAsync(ct);
        var rows = await Project(verified.OrderBy(r => r.VerifiedAt).ThenBy(r => r.Id).Skip(offset).Take(limit)).ToListAsync(ct);
        // Project's own ordering is lost in the Select over a paged query on some providers: order again by the verified key.
        return new(rows.OrderBy(x => x.VerifiedAt).ThenBy(x => x.Id).Select(ToItem).ToList(), total);
    }

    public async Task<ContactRequestQueueItem?> GetAsync(Guid requestId, CancellationToken ct)
    {
        var row = await Project(db.ContactRequests.AsNoTracking().Where(r => r.Id == requestId && r.VerifiedAt != null)).FirstOrDefaultAsync(ct);
        return row is null ? null : ToItem(row);
    }
}
