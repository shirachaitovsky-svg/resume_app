namespace ResumeApp.Domain.Audit;

/// <summary>Who did what and when to which entity (AD-22). Holds identifiers only: never a name, email, phone or free text.</summary>
public sealed class AuditLogEntry
{
    public const int ActionMaxLength = 40;
    public const int EntityTypeMaxLength = 40;

    public const string ApproveRevision = "approve-revision";
    public const string ReturnRevision = "return-revision";
    public const string ApproveContactChange = "approve-contact-change";
    public const string RejectContactChange = "reject-contact-change";
    public const string RemoveProfile = "remove-profile";
    public const string RevisionEntity = "profile-revision";
    public const string ProfileEntity = "profile";

    public Guid Id { get; private set; }
    public Guid ActorId { get; private set; }
    public DateTime At { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string EntityType { get; private set; } = string.Empty;
    public Guid EntityId { get; private set; }

    private AuditLogEntry() { }

    public static AuditLogEntry Create(Guid actorId, DateTime nowUtc, string action, string entityType, Guid entityId) => new()
    {
        Id = Guid.NewGuid(),
        ActorId = actorId,
        At = nowUtc,
        Action = action,
        EntityType = entityType,
        EntityId = entityId,
    };
}
