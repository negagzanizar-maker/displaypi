using DisplayControl.Domain.Identity;
using DisplayControl.Domain.Tenancy;
using DisplayControl.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DisplayControl.Infrastructure.Persistence;

public sealed partial class DisplayControlDbContext
{
    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();

    public DbSet<Invitation> Invitations => Set<Invitation>();

    public DbSet<UserSession> UserSessions => Set<UserSession>();

    public DbSet<UserMfaSecret> UserMfaSecrets => Set<UserMfaSecret>();

    public DbSet<UserRecoveryCode> UserRecoveryCodes => Set<UserRecoveryCode>();

    public DbSet<IdentityNotification> IdentityNotifications => Set<IdentityNotification>();

    private void ConfigureIdentityModel(ModelBuilder modelBuilder)
    {
        ConfigureIdentityFrameworkTables(modelBuilder);
        ConfigureTenantMembership(modelBuilder);
        ConfigureInvitation(modelBuilder);
        ConfigureUserSession(modelBuilder);
        ConfigureUserMfaSecret(modelBuilder);
        ConfigureUserRecoveryCode(modelBuilder);
        ConfigureIdentityNotification(modelBuilder);
    }

    private static void ConfigureIdentityFrameworkTables(ModelBuilder modelBuilder)
    {
        var user = modelBuilder.Entity<ApplicationUser>();
        user.ToTable("identity_users");
        user.Property(value => value.Id).HasColumnName("id");
        user.Property(value => value.UserName).HasColumnName("user_name").HasMaxLength(256);
        user.Property(value => value.NormalizedUserName).HasColumnName("normalized_user_name").HasMaxLength(256);
        user.Property(value => value.Email).HasColumnName("email").HasMaxLength(320);
        user.Property(value => value.NormalizedEmail).HasColumnName("normalized_email").HasMaxLength(320);
        user.Property(value => value.EmailConfirmed).HasColumnName("email_confirmed");
        user.Property(value => value.PasswordHash).HasColumnName("password_hash");
        user.Property(value => value.SecurityStamp).HasColumnName("security_stamp");
        user.Property(value => value.ConcurrencyStamp).HasColumnName("concurrency_stamp");
        user.Property(value => value.PhoneNumber).HasColumnName("phone_number").HasMaxLength(32);
        user.Property(value => value.PhoneNumberConfirmed).HasColumnName("phone_number_confirmed");
        user.Property(value => value.TwoFactorEnabled).HasColumnName("two_factor_enabled");
        user.Property(value => value.LockoutEnd).HasColumnName("lockout_end_utc");
        user.Property(value => value.LockoutEnabled).HasColumnName("lockout_enabled");
        user.Property(value => value.AccessFailedCount).HasColumnName("access_failed_count");
        user.Property(value => value.DisplayName).HasColumnName("display_name").HasMaxLength(160).IsRequired();
        user.Property(value => value.AccountState)
            .HasColumnName("account_state")
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsRequired();
        user.Property(value => value.HomeTenantId).HasColumnName("home_tenant_id");
        user.Property(value => value.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        user.Property(value => value.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        user.Property(value => value.LastPasswordChangedAtUtc).HasColumnName("last_password_changed_at_utc");
        user.HasIndex(value => value.NormalizedUserName)
            .IsUnique()
            .HasDatabaseName("ux_identity_users_normalized_user_name");
        user.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(value => value.HomeTenantId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_identity_users_home_tenants");
        user.HasIndex(value => value.NormalizedEmail)
            .IsUnique()
            .HasDatabaseName("ux_identity_users_normalized_email");
        user.HasIndex(value => value.HomeTenantId).HasDatabaseName("ix_identity_users_home_tenant");

        var role = modelBuilder.Entity<IdentityRole<Guid>>();
        role.ToTable("identity_roles");
        role.Property(value => value.Id).HasColumnName("id");
        role.Property(value => value.Name).HasColumnName("name").HasMaxLength(256);
        role.Property(value => value.NormalizedName).HasColumnName("normalized_name").HasMaxLength(256);
        role.Property(value => value.ConcurrencyStamp).HasColumnName("concurrency_stamp");
        role.HasIndex(value => value.NormalizedName)
            .IsUnique()
            .HasDatabaseName("ux_identity_roles_normalized_name");

        var userClaim = modelBuilder.Entity<IdentityUserClaim<Guid>>();
        userClaim.ToTable("identity_user_claims");
        userClaim.Property(value => value.Id).HasColumnName("id");
        userClaim.Property(value => value.UserId).HasColumnName("user_id");
        userClaim.Property(value => value.ClaimType).HasColumnName("claim_type");
        userClaim.Property(value => value.ClaimValue).HasColumnName("claim_value");

        var roleClaim = modelBuilder.Entity<IdentityRoleClaim<Guid>>();
        roleClaim.ToTable("identity_role_claims");
        roleClaim.Property(value => value.Id).HasColumnName("id");
        roleClaim.Property(value => value.RoleId).HasColumnName("role_id");
        roleClaim.Property(value => value.ClaimType).HasColumnName("claim_type");
        roleClaim.Property(value => value.ClaimValue).HasColumnName("claim_value");

        var userLogin = modelBuilder.Entity<IdentityUserLogin<Guid>>();
        userLogin.ToTable("identity_user_logins");
        userLogin.Property(value => value.LoginProvider).HasColumnName("login_provider").HasMaxLength(128);
        userLogin.Property(value => value.ProviderKey).HasColumnName("provider_key").HasMaxLength(256);
        userLogin.Property(value => value.ProviderDisplayName).HasColumnName("provider_display_name");
        userLogin.Property(value => value.UserId).HasColumnName("user_id");

        var userRole = modelBuilder.Entity<IdentityUserRole<Guid>>();
        userRole.ToTable("identity_user_roles");
        userRole.Property(value => value.UserId).HasColumnName("user_id");
        userRole.Property(value => value.RoleId).HasColumnName("role_id");

        var userToken = modelBuilder.Entity<IdentityUserToken<Guid>>();
        userToken.ToTable("identity_user_tokens");
        userToken.Property(value => value.UserId).HasColumnName("user_id");
        userToken.Property(value => value.LoginProvider).HasColumnName("login_provider").HasMaxLength(128);
        userToken.Property(value => value.Name).HasColumnName("name").HasMaxLength(128);
        userToken.Property(value => value.Value).HasColumnName("value");
    }

    private void ConfigureTenantMembership(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<TenantMembership>();
        entity.ToTable("tenant_memberships");
        ConfigureTenantOwned(entity, "tenant_memberships");
        entity.Property(value => value.UserId).HasColumnName("user_id").IsRequired();
        entity.Property(value => value.Role).HasColumnName("role").HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(value => value.State).HasColumnName("state").HasConversion<string>().HasMaxLength(24).IsRequired();
        entity.Property(value => value.InvitedByUserId).HasColumnName("invited_by_user_id").IsRequired();
        entity.Property(value => value.AcceptedAtUtc).HasColumnName("accepted_at_utc").IsRequired();
        entity.Property(value => value.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.Property(value => value.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        entity.Property(value => value.ConcurrencyToken).HasColumnName("concurrency_token").IsConcurrencyToken();
        entity.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(value => value.UserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_tenant_memberships_users");
        entity.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(value => value.InvitedByUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_tenant_memberships_inviters");
        entity.HasIndex(value => new { value.TenantId, value.UserId })
            .IsUnique()
            .HasDatabaseName("ux_tenant_memberships_tenant_user");
        entity.HasIndex(value => value.UserId)
            .IsUnique()
            .HasFilter("state <> 'Removed'")
            .HasDatabaseName("ux_tenant_memberships_single_current_tenant");
        entity.HasQueryFilter(value => CurrentTenantId.HasValue && value.TenantId == CurrentTenantId.Value);
    }

    private void ConfigureInvitation(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Invitation>();
        entity.ToTable("invitations", table =>
        {
            table.HasCheckConstraint("ck_invitations_digest", "DATALENGTH(token_digest) = 32");
            table.HasCheckConstraint("ck_invitations_expiry", "expires_at_utc > created_at_utc");
            table.HasCheckConstraint(
                "ck_invitations_consumption",
                "(consumed_at_utc IS NULL AND consumed_by_user_id IS NULL) OR " +
                "(consumed_at_utc IS NOT NULL AND consumed_by_user_id IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_invitations_terminal_state",
                "NOT (consumed_at_utc IS NOT NULL AND revoked_at_utc IS NOT NULL)");
        });
        ConfigureTenantOwned(entity, "invitations");
        entity.Property(value => value.NormalizedEmail).HasColumnName("normalized_email").HasMaxLength(320).IsRequired();
        entity.Property(value => value.IntendedRole)
            .HasColumnName("intended_role")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        entity.Property(value => value.TokenDigest).HasColumnName("token_digest").IsRequired();
        entity.Property(value => value.ExpiresAtUtc).HasColumnName("expires_at_utc").IsRequired();
        entity.Property(value => value.ConsumedAtUtc).HasColumnName("consumed_at_utc");
        entity.Property(value => value.ConsumedByUserId).HasColumnName("consumed_by_user_id");
        entity.Property(value => value.RevokedAtUtc).HasColumnName("revoked_at_utc");
        entity.Property(value => value.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        entity.Property(value => value.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.Property(value => value.ConcurrencyToken).HasColumnName("concurrency_token").IsConcurrencyToken();
        entity.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(value => value.ConsumedByUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_invitations_consuming_users");
        entity.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(value => value.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_invitations_creators");
        entity.HasIndex(value => value.TokenDigest).IsUnique().HasDatabaseName("ux_invitations_digest");
        entity.HasIndex(value => new { value.TenantId, value.NormalizedEmail })
            .IsUnique()
            .HasFilter("consumed_at_utc IS NULL AND revoked_at_utc IS NULL")
            .HasDatabaseName("ux_invitations_tenant_pending_email");
        entity.HasQueryFilter(value => CurrentTenantId.HasValue && value.TenantId == CurrentTenantId.Value);
    }

    private static void ConfigureUserSession(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<UserSession>();
        entity.ToTable("user_sessions", table =>
        {
            table.HasCheckConstraint("ck_user_sessions_key_digest", "DATALENGTH(session_key_digest) = 32");
            table.HasCheckConstraint("ck_user_sessions_stamp_digest", "DATALENGTH(security_stamp_digest) = 32");
            table.HasCheckConstraint(
                "ck_user_sessions_user_agent_digest",
                "user_agent_digest IS NULL OR DATALENGTH(user_agent_digest) = 32");
            table.HasCheckConstraint(
                "ck_user_sessions_source_address_digest",
                "source_address_digest IS NULL OR DATALENGTH(source_address_digest) = 32");
            table.HasCheckConstraint(
                "ck_user_sessions_expiry",
                "idle_expires_at_utc > created_at_utc AND absolute_expires_at_utc >= idle_expires_at_utc");
            table.HasCheckConstraint(
                "ck_user_sessions_mfa",
                "(mfa_satisfied = 0 AND mfa_satisfied_at_utc IS NULL) OR " +
                "(mfa_satisfied = 1 AND mfa_satisfied_at_utc IS NOT NULL)");
        });
        entity.HasKey(value => value.Id).HasName("pk_user_sessions");
        entity.Property(value => value.Id).HasColumnName("id");
        entity.Property(value => value.UserId).HasColumnName("user_id").IsRequired();
        entity.Property(value => value.SessionKeyDigest).HasColumnName("session_key_digest").IsRequired();
        entity.Property(value => value.SelectedTenantId).HasColumnName("selected_tenant_id");
        entity.Property(value => value.SecurityStampDigest).HasColumnName("security_stamp_digest").IsRequired();
        entity.Property(value => value.MfaSatisfied).HasColumnName("mfa_satisfied").IsRequired();
        entity.Property(value => value.MfaSatisfiedAtUtc).HasColumnName("mfa_satisfied_at_utc");
        entity.Property(value => value.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.Property(value => value.LastSeenAtUtc).HasColumnName("last_seen_at_utc").IsRequired();
        entity.Property(value => value.IdleExpiresAtUtc).HasColumnName("idle_expires_at_utc").IsRequired();
        entity.Property(value => value.AbsoluteExpiresAtUtc).HasColumnName("absolute_expires_at_utc").IsRequired();
        entity.Property(value => value.RevokedAtUtc).HasColumnName("revoked_at_utc");
        entity.Property(value => value.RevocationReasonCode).HasColumnName("revocation_reason_code").HasMaxLength(64);
        entity.Property(value => value.UserAgentDigest).HasColumnName("user_agent_digest");
        entity.Property(value => value.SourceAddressDigest).HasColumnName("source_address_digest");
        entity.Property(value => value.ConcurrencyToken).HasColumnName("concurrency_token").IsConcurrencyToken();
        entity.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(value => value.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_user_sessions_users");
        entity.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(value => value.SelectedTenantId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_user_sessions_selected_tenants");
        entity.HasIndex(value => value.SessionKeyDigest).IsUnique().HasDatabaseName("ux_user_sessions_key_digest");
        entity.HasIndex(value => new { value.UserId, value.RevokedAtUtc, value.AbsoluteExpiresAtUtc })
            .HasDatabaseName("ix_user_sessions_user_active");
    }

    private static void ConfigureUserMfaSecret(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<UserMfaSecret>();
        entity.ToTable("user_mfa_secrets", table =>
        {
            table.HasCheckConstraint("ck_user_mfa_secrets_payload", "DATALENGTH(protected_secret) > 0");
            table.HasCheckConstraint(
                "ck_user_mfa_secrets_last_step",
                "last_accepted_time_step IS NULL OR last_accepted_time_step >= 0");
        });
        entity.HasKey(value => value.UserId).HasName("pk_user_mfa_secrets");
        entity.Property(value => value.UserId).HasColumnName("user_id");
        entity.Property(value => value.ProtectedSecret).HasColumnName("protected_secret").IsRequired();
        entity.Property(value => value.ProtectionScheme).HasColumnName("protection_scheme").HasMaxLength(128).IsRequired();
        entity.Property(value => value.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.Property(value => value.ConfirmedAtUtc).HasColumnName("confirmed_at_utc");
        entity.Property(value => value.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        entity.Property(value => value.LastAcceptedTimeStep).HasColumnName("last_accepted_time_step");
        entity.Property(value => value.ConcurrencyToken).HasColumnName("concurrency_token").IsConcurrencyToken();
        entity.HasOne<ApplicationUser>()
            .WithOne()
            .HasForeignKey<UserMfaSecret>(value => value.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_user_mfa_secrets_users");
    }

    private static void ConfigureUserRecoveryCode(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<UserRecoveryCode>();
        entity.ToTable("user_recovery_codes", table =>
            table.HasCheckConstraint("ck_user_recovery_codes_digest", "DATALENGTH(code_digest) = 32"));
        entity.HasKey(value => value.Id).HasName("pk_user_recovery_codes");
        entity.Property(value => value.Id).HasColumnName("id");
        entity.Property(value => value.UserId).HasColumnName("user_id").IsRequired();
        entity.Property(value => value.CodeDigest).HasColumnName("code_digest").IsRequired();
        entity.Property(value => value.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.Property(value => value.UsedAtUtc).HasColumnName("used_at_utc");
        entity.Property(value => value.ConcurrencyToken).HasColumnName("concurrency_token").IsConcurrencyToken();
        entity.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(value => value.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_user_recovery_codes_users");
        entity.HasIndex(value => new { value.UserId, value.CodeDigest })
            .IsUnique()
            .HasDatabaseName("ux_user_recovery_codes_user_digest");
    }

    private static void ConfigureIdentityNotification(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<IdentityNotification>();
        entity.ToTable("identity_notifications", table =>
        {
            table.HasCheckConstraint(
                "ck_identity_notifications_payload",
                $"DATALENGTH(protected_payload) > 0 AND DATALENGTH(protected_payload) <= {IdentityNotification.MaximumProtectedPayloadBytes}");
            table.HasCheckConstraint("ck_identity_notifications_attempts", "attempt_count >= 0");
        });
        entity.HasKey(value => value.Id).HasName("pk_identity_notifications");
        entity.Property(value => value.Id).HasColumnName("id");
        entity.Property(value => value.UserId).HasColumnName("user_id");
        entity.Property(value => value.TenantId).HasColumnName("tenant_id");
        entity.Property(value => value.NotificationType).HasColumnName("notification_type").HasMaxLength(64).IsRequired();
        entity.Property(value => value.NormalizedRecipientEmail)
            .HasColumnName("normalized_recipient_email")
            .HasMaxLength(320)
            .IsRequired();
        entity.Property(value => value.ProtectionScheme).HasColumnName("protection_scheme").HasMaxLength(128).IsRequired();
        entity.Property(value => value.ProtectedPayload).HasColumnName("protected_payload").IsRequired();
        entity.Property(value => value.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.Property(value => value.NextAttemptAtUtc).HasColumnName("next_attempt_at_utc").IsRequired();
        entity.Property(value => value.ProcessedAtUtc).HasColumnName("processed_at_utc");
        entity.Property(value => value.FailedAtUtc).HasColumnName("failed_at_utc");
        entity.Property(value => value.AttemptCount).HasColumnName("attempt_count").IsRequired();
        entity.Property(value => value.LastSafeErrorCode).HasColumnName("last_safe_error_code").HasMaxLength(64);
        entity.Property(value => value.ConcurrencyToken).HasColumnName("concurrency_token").IsConcurrencyToken();
        entity.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(value => value.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_identity_notifications_users");
        entity.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(value => value.TenantId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_identity_notifications_tenants");
        entity.HasIndex(value => new { value.ProcessedAtUtc, value.FailedAtUtc, value.NextAttemptAtUtc })
            .HasDatabaseName("ix_identity_notifications_pending");
    }
}
