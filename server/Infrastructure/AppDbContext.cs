using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ResumeApp.Domain.Cohorts;
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
            e.HasOne<Profile>().WithMany().HasForeignKey(r => r.ProfileId).OnDelete(DeleteBehavior.Restrict);
            // AD-2 / AD-14: at most one pending revision per profile, enforced by the database.
            e.HasIndex(r => r.ProfileId).IsUnique().HasFilter("status = 'Pending'").HasDatabaseName("ux_profile_revisions_one_pending");
        });

        builder.Entity<ParticipantPrivate>(e =>
        {
            e.ToTable("participant_private");
            e.HasKey(p => p.ProfileId);
            e.Property(p => p.ProfileId).HasColumnName("profile_id").ValueGeneratedNever();
            e.Property(p => p.CohortId).HasColumnName("cohort_id");
            e.Property(p => p.Email).HasColumnName("email").HasMaxLength(ParticipantPrivate.EmailMaxLength).IsRequired();
            e.Property(p => p.Phone).HasColumnName("phone").HasMaxLength(ParticipantPrivate.PhoneMaxLength).IsRequired();
            e.HasOne<Profile>().WithOne().HasForeignKey<ParticipantPrivate>(p => p.ProfileId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Cohort>().WithMany().HasForeignKey(p => p.CohortId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(p => new { p.CohortId, p.Email }).IsUnique().HasDatabaseName("ix_participant_private_cohort_email");
        });
    }
}
