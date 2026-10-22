using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ResumeApp.Domain.Audit;
using ResumeApp.Domain.Cohorts;
using ResumeApp.Domain.Contacts;
using ResumeApp.Domain.Email;
using ResumeApp.Domain.Profiles;
using ResumeApp.Infrastructure.Identity;

namespace ResumeApp.Infrastructure;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options), IDataProtectionKeyContext
{
    /// <summary>Data Protection keys live in the database because the server is stateless (AD-21).</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;

    public DbSet<Cohort> Cohorts { get; set; } = null!;

    public DbSet<Profile> Profiles { get; set; } = null!;
    public DbSet<ProfileRevision> ProfileRevisions { get; set; } = null!;
    public DbSet<ParticipantPrivate> ParticipantPrivates { get; set; } = null!;
    public DbSet<RateLimitCounter> RateLimitCounters { get; set; } = null!;
    public DbSet<AuditLogEntry> AuditLog { get; set; } = null!;
    public DbSet<EmailOutboxEntry> EmailOutbox { get; set; } = null!;
    public DbSet<PersonalToken> PersonalTokens { get; set; } = null!;
    public DbSet<ContactRequest> ContactRequests { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Tables in snake_case per the project conventions.
        builder.Entity<AppUser>().ToTable("users");
        builder.Entity<IdentityRole<Guid>>().ToTable("roles");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");
        builder.Entity<DataProtectionKey>().ToTable("data_protection_keys");

        builder.Entity<Cohort>(e =>
        {
            e.ToTable("cohorts");
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(c => c.Name).HasColumnName("name").HasMaxLength(Cohort.NameMaxLength).IsRequired();
            e.Property(c => c.InviteToken).HasColumnName("invite_token").HasMaxLength(Cohort.TokenLength).IsRequired();
            e.Property(c => c.CreatedAt).HasColumnName("created_at");
            e.Property(c => c.RevokedAt).HasColumnName("revoked_at");
            e.HasIndex(c => c.InviteToken).IsUnique().HasDatabaseName("ix_cohorts_invite_token");
        });

        builder.Entity<Profile>(e =>
        {
            e.ToTable("profiles");
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(p => p.CohortId).HasColumnName("cohort_id");
            e.Property(p => p.Visibility).HasColumnName("visibility").HasConversion<string>().HasMaxLength(10);
            e.Property(p => p.ApprovedRevisionId).HasColumnName("approved_revision_id");
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.HasOne<Cohort>().WithMany().HasForeignKey(p => p.CohortId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(p => p.CohortId).HasDatabaseName("ix_profiles_cohort_id");
        });

        builder.Entity<ProfileRevision>(e =>
        {
            e.ToTable("profile_revisions");
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(r => r.ProfileId).HasColumnName("profile_id");
            e.Property(r => r.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(10);
            e.Property(r => r.Name).HasColumnName("name").HasMaxLength(ProfileRevision.NameMaxLength).IsRequired();
            e.Property(r => r.ProductName).HasColumnName("product_name").HasMaxLength(ProfileRevision.ProductNameMaxLength).IsRequired();
            e.Property(r => r.ProductUrl).HasColumnName("product_url").HasMaxLength(ProfileRevision.ProductUrlMaxLength).IsRequired();
            e.Property(r => r.SubmittedAt).HasColumnName("submitted_at");
            e.Property(r => r.Description).HasColumnName("description").HasMaxLength(ProfileRevision.DescriptionMaxLength);
            e.Property(r => r.Role).HasColumnName("role").HasMaxLength(ProfileRevision.RoleMaxLength);
            e.Property(r => r.Workplace).HasColumnName("workplace").HasMaxLength(ProfileRevision.WorkplaceMaxLength);
            e.Property(r => r.IntroText).HasColumnName("intro_text").HasMaxLength(ProfileRevision.IntroMaxLength);
            e.Property(r => r.IntroAnswer1).HasColumnName("intro_answer_1").HasMaxLength(ProfileRevision.IntroMaxLength);
            e.Property(r => r.IntroAnswer2).HasColumnName("intro_answer_2").HasMaxLength(ProfileRevision.IntroMaxLength);
            e.Property(r => r.IntroAnswer3).HasColumnName("intro_answer_3").HasMaxLength(ProfileRevision.IntroMaxLength);
            e.Property(r => r.AvatarId).HasColumnName("avatar_id").HasMaxLength(ProfileRevision.AvatarIdMaxLength).IsRequired().HasDefaultValue(ProfileAvatars.Default);
            e.Property(r => r.Photo).HasColumnName("photo").HasColumnType("bytea");
            e.Property(r => r.Cv).HasColumnName("cv").HasColumnType("text");
            e.Property(r => r.PendingEmail).HasColumnName("pending_email").HasMaxLength(ParticipantPrivate.EmailMaxLength);
            e.Property(r => r.PendingPhone).HasColumnName("pending_phone").HasMaxLength(ParticipantPrivate.PhoneMaxLength);
            e.Property(r => r.ReturnNote).HasColumnName("return_note").HasMaxLength(ProfileRevision.ReturnNoteMaxLength);
            e.Property(r => r.ReturnedAt).HasColumnName("returned_at");
            e.Property(r => r.PhotoContentType).HasColumnName("photo_content_type").HasMaxLength(ProfileRevision.PhotoContentTypeMaxLength);
            e.HasOne<Profile>().WithMany().HasForeignKey(r => r.ProfileId).OnDelete(DeleteBehavior.Restrict);
            // AD-2 / AD-14: at most one pending revision per profile, enforced by the database.
            e.HasIndex(r => r.ProfileId).IsUnique().HasFilter("status = 'Pending'").HasDatabaseName("ux_profile_revisions_one_pending");
        });

        builder.Entity<AuditLogEntry>(e =>
        {
            e.ToTable("audit_log");
            e.HasKey(a => a.Id);
            e.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(a => a.ActorId).HasColumnName("actor_id");
            e.Property(a => a.At).HasColumnName("at");
            e.Property(a => a.Action).HasColumnName("action").HasMaxLength(AuditLogEntry.ActionMaxLength).IsRequired();
            e.Property(a => a.EntityType).HasColumnName("entity_type").HasMaxLength(AuditLogEntry.EntityTypeMaxLength).IsRequired();
            e.Property(a => a.EntityId).HasColumnName("entity_id");
            e.HasIndex(a => a.At).HasDatabaseName("ix_audit_log_at");
        });

        builder.Entity<RateLimitCounter>(e =>
        {
            e.ToTable("rate_limit_counters");
            e.HasKey(c => new { c.Scope, c.KeyHash, c.WindowStart });
            e.Property(c => c.Scope).HasColumnName("scope").HasMaxLength(40);
            e.Property(c => c.KeyHash).HasColumnName("key_hash").HasMaxLength(64);
            e.Property(c => c.WindowStart).HasColumnName("window_start");
            e.Property(c => c.Count).HasColumnName("count");
            e.HasIndex(c => new { c.Scope, c.WindowStart }).HasDatabaseName("ix_rate_limit_counters_scope_window");
        });

        builder.Entity<ParticipantPrivate>(e =>
        {
            e.ToTable("participant_private");
            e.HasKey(p => p.ProfileId);
            e.Property(p => p.ProfileId).HasColumnName("profile_id").ValueGeneratedNever();
            e.Property(p => p.CohortId).HasColumnName("cohort_id");
            e.Property(p => p.Email).HasColumnName("email").HasMaxLength(ParticipantPrivate.EmailMaxLength).IsRequired();
            e.Property(p => p.Phone).HasColumnName("phone").HasMaxLength(ParticipantPrivate.PhoneMaxLength).IsRequired();
            e.Property(p => p.ContactVersion).HasColumnName("contact_version").HasDefaultValue(1);
            e.HasOne<Profile>().WithOne().HasForeignKey<ParticipantPrivate>(p => p.ProfileId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Cohort>().WithMany().HasForeignKey(p => p.CohortId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(p => new { p.CohortId, p.Email }).IsUnique().HasDatabaseName("ix_participant_private_cohort_email");
            e.HasIndex(p => p.Email).HasDatabaseName("ix_participant_private_email");
        });

        builder.Entity<EmailOutboxEntry>(e =>
        {
            e.ToTable("email_outbox");
            e.HasKey(o => o.Id);
            e.Property(o => o.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(o => o.TemplateKey).HasColumnName("template_key").HasMaxLength(EmailOutboxEntry.TemplateKeyMaxLength).IsRequired();
            e.Property(o => o.ProfileId).HasColumnName("profile_id");
            e.Property(o => o.RevisionId).HasColumnName("revision_id");
            e.Property(o => o.RequestId).HasColumnName("request_id");
            e.Property(o => o.DeduplicationKey).HasColumnName("deduplication_key").HasMaxLength(EmailOutboxEntry.DeduplicationKeyMaxLength).IsRequired();
            e.Property(o => o.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(10);
            e.Property(o => o.Attempts).HasColumnName("attempts");
            e.Property(o => o.ClaimCount).HasColumnName("claim_count");
            e.Property(o => o.NextAttemptAt).HasColumnName("next_attempt_at");
            e.Property(o => o.LeaseUntil).HasColumnName("lease_until");
            e.Property(o => o.LastErrorCategory).HasColumnName("last_error_category").HasMaxLength(EmailOutboxEntry.ErrorCategoryMaxLength);
            e.Property(o => o.CreatedAt).HasColumnName("created_at");
            e.Property(o => o.SentAt).HasColumnName("sent_at");
            e.HasOne<Profile>().WithMany().HasForeignKey(o => o.ProfileId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ProfileRevision>().WithMany().HasForeignKey(o => o.RevisionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ContactRequest>().WithMany().HasForeignKey(o => o.RequestId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(o => o.DeduplicationKey).IsUnique().HasDatabaseName("ux_email_outbox_deduplication_key");
            e.HasIndex(o => new { o.Status, o.NextAttemptAt }).HasDatabaseName("ix_email_outbox_status_next_attempt");
        });

        builder.Entity<PersonalToken>(e =>
        {
            e.ToTable("personal_tokens");
            e.HasKey(t => t.Id);
            e.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(t => t.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsRequired();
            e.Property(t => t.Purpose).HasColumnName("purpose").HasMaxLength(30).IsRequired();
            e.Property(t => t.ProfileId).HasColumnName("profile_id");
            e.Property(t => t.RequestId).HasColumnName("request_id");
            e.Property(t => t.ExpiresAt).HasColumnName("expires_at");
            e.Property(t => t.UsedAt).HasColumnName("used_at");
            e.Property(t => t.RevokedAt).HasColumnName("revoked_at");
            e.Property(t => t.CreatedAt).HasColumnName("created_at");
            e.HasOne<Profile>().WithMany().HasForeignKey(t => t.ProfileId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ContactRequest>().WithMany().HasForeignKey(t => t.RequestId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(t => t.RequestId).HasDatabaseName("ix_personal_tokens_request_id");
            e.HasIndex(t => t.TokenHash).IsUnique().HasDatabaseName("ux_personal_tokens_token_hash");
            e.HasIndex(t => new { t.ProfileId, t.Purpose }).HasDatabaseName("ix_personal_tokens_profile_purpose");
        });

        builder.Entity<ContactRequest>(e =>
        {
            e.ToTable("contact_requests", t =>
            {
                t.HasCheckConstraint("ck_contact_requests_status", ContactRequestStatusNames.AllStatesSql);
                t.HasCheckConstraint("ck_contact_requests_participant_decision", ParticipantDecisionNames.AllowedSql);
                t.HasCheckConstraint("ck_contact_requests_decision_pair", ParticipantDecisionNames.DecisionPairSql);
                t.HasCheckConstraint("ck_contact_requests_manager_approval", ParticipantDecisionNames.ManagerApprovalSql);
            });
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(r => r.ProfileId).HasColumnName("profile_id");
            e.Property(r => r.VisitorName).HasColumnName("visitor_name").HasMaxLength(ContactRequest.NameMaxLength).IsRequired();
            e.Property(r => r.VisitorEmail).HasColumnName("visitor_email").HasMaxLength(ParticipantPrivate.EmailMaxLength).IsRequired();
            e.Property(r => r.Reason).HasColumnName("reason").HasMaxLength(ContactRequest.ReasonMaxLength).IsRequired();
            e.Property(r => r.Status).HasColumnName("status").HasMaxLength(30).IsRequired()
                .HasConversion(v => ContactRequestStatusNames.ToWire(v), v => ContactRequestStatusNames.Parse(v));
            e.Property(r => r.CreatedAt).HasColumnName("created_at");
            e.Property(r => r.ExpiresAt).HasColumnName("expires_at");
            e.Property(r => r.VerifiedAt).HasColumnName("verified_at");
            e.Property(r => r.ParticipantDecision).HasColumnName("participant_decision").HasMaxLength(10);
            e.Property(r => r.ParticipantDecidedAt).HasColumnName("participant_decided_at");
            e.Property(r => r.ParticipantContactVersion).HasColumnName("participant_contact_version");
            e.Property(r => r.ManagerApprovedAt).HasColumnName("manager_approved_at");
            e.HasOne<Profile>().WithMany().HasForeignKey(r => r.ProfileId).OnDelete(DeleteBehavior.Restrict);
            // One open request per (profile, visitor address): the arbiter of concurrent and repeated submissions.
            e.HasIndex(r => new { r.ProfileId, r.VisitorEmail }).IsUnique().HasFilter(ContactRequestStatusNames.OpenStatesSql)
                .HasDatabaseName("ux_contact_requests_open_profile_visitor");
            e.HasIndex(r => new { r.Status, r.ExpiresAt }).HasDatabaseName("ix_contact_requests_status_expires_at");
            // The admin queue (story 3.2): verified requests, oldest verification first; the open states get their own narrow index.
            e.HasIndex(r => new { r.VerifiedAt, r.Status, r.Id }).HasFilter("verified_at IS NOT NULL").HasDatabaseName("ix_contact_requests_verified");
            e.HasIndex(r => new { r.VerifiedAt, r.Id }).HasFilter("status IN ('submitted', 'sent_to_participant', 'participant_approved')")
                .HasDatabaseName("ix_contact_requests_queue_open");
        });
    }
}
