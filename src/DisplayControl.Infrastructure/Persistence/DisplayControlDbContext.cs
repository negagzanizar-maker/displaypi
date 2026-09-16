using DisplayControl.Application.Tenancy;
using DisplayControl.Domain.Devices;
using DisplayControl.Domain.Licensing;
using DisplayControl.Domain.Operations;
using DisplayControl.Domain.Tenancy;
using DisplayControl.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DisplayControl.Infrastructure.Persistence;

public sealed partial class DisplayControlDbContext(
    DbContextOptions<DisplayControlDbContext> options,
    ICurrentTenant currentTenant) : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    private Guid? CurrentTenantId => currentTenant.TenantId;

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<Device> Devices => Set<Device>();

    public DbSet<DeviceLicense> Licenses => Set<DeviceLicense>();

    public DbSet<TenantAuditEvent> AuditEvents => Set<TenantAuditEvent>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public async Task<IDbContextTransaction> BeginTenantTransactionAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant identifier cannot be empty.", nameof(tenantId));
        }

        var transaction = await Database.BeginTransactionAsync(cancellationToken);

        try
        {
            await Database.ExecuteSqlRawAsync(
                "EXEC sys.sp_set_session_context @key=N'tenant_id', @value={0}, @read_only=0",
                [tenantId.ToString()],
                cancellationToken);
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    public async Task<IDbContextTransaction> BeginPlatformCatalogTransactionAsync(
        CancellationToken cancellationToken = default)
    {
        var transaction = await Database.BeginTransactionAsync(cancellationToken);

        try
        {
            await Database.ExecuteSqlRawAsync(
                "EXEC sys.sp_set_session_context @key=N'platform_catalog', @value=1, @read_only=0",
                cancellationToken);
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        base.OnModelCreating(builder);
        builder.HasDefaultSchema("app");
        ConfigureIdentityModel(builder);
        ConfigureTenant(builder);
        ConfigureDevice(builder);
        ConfigureDeviceSecurityModel(builder);
        ConfigureLicense(builder);
        ConfigureAudit(builder);
        ConfigureOutbox(builder);
        ConfigureContentModel(builder);
        ConfigurePlaylistModel(builder);
        ConfigureGroupModel(builder);
        ConfigureAssignmentModel(builder);
        ConfigureDesiredStateModel(builder);
        ConfigureLicenseEvent(builder);
        ConfigureSystemKeyMetadata(builder);
    }

    private static void ConfigureTenant(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Tenant>();
        entity.ToTable("tenants");
        entity.HasKey(value => value.Id).HasName("pk_tenants");
        entity.Property(value => value.Id).HasColumnName("id");
        entity.Property(value => value.Name).HasColumnName("name").HasMaxLength(160).IsRequired();
        entity.Property(value => value.Slug).HasColumnName("slug").HasMaxLength(80).IsRequired();
        entity.Property(value => value.TimeZone).HasColumnName("time_zone").HasMaxLength(80).IsRequired();
        entity.Property(value => value.State).HasColumnName("state").HasConversion<string>().HasMaxLength(24).IsRequired();
        entity.Property(value => value.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.Property(value => value.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        entity.Property(value => value.ConcurrencyToken).HasColumnName("concurrency_token").IsConcurrencyToken();
        entity.HasIndex(value => value.Slug).IsUnique().HasDatabaseName("ux_tenants_slug");
    }

    private void ConfigureDevice(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Device>();
        entity.ToTable("devices");
        ConfigureTenantOwned(entity, "devices");
        entity.Property(value => value.DisplayName).HasColumnName("display_name").HasMaxLength(160).IsRequired();
        entity.Property(value => value.State).HasColumnName("state").HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(value => value.SerialNumberNormalized).HasColumnName("serial_number_normalized").HasMaxLength(32);
        entity.Property(value => value.Hostname).HasColumnName("hostname").HasMaxLength(253);
        entity.Property(value => value.OsDescription).HasColumnName("os_description").HasMaxLength(256);
        entity.Property(value => value.Architecture).HasColumnName("architecture").HasMaxLength(32);
        entity.Property(value => value.AgentVersion).HasColumnName("agent_version").HasMaxLength(64);
        entity.Property(value => value.PlayerVersion).HasColumnName("player_version").HasMaxLength(64);
        entity.Property(value => value.DiskCapacityBytes).HasColumnName("disk_capacity_bytes");
        entity.Property(value => value.LastSeenUtc).HasColumnName("last_seen_utc");
        entity.Property(value => value.AppliedManifestVersion).HasColumnName("applied_manifest_version");
        entity.Property(value => value.PlaybackHealthCode).HasColumnName("playback_health_code").HasMaxLength(64);
        entity.Property(value => value.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.Property(value => value.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        entity.Property(value => value.ConcurrencyToken).HasColumnName("concurrency_token").IsConcurrencyToken();
        entity.HasIndex(value => new { value.TenantId, value.SerialNumberNormalized })
            .IsUnique()
            .HasFilter("serial_number_normalized IS NOT NULL")
            .HasDatabaseName("ux_devices_tenant_serial");
        entity.HasQueryFilter(value => CurrentTenantId.HasValue && value.TenantId == CurrentTenantId.Value);
    }

    private void ConfigureLicense(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DeviceLicense>();
        entity.ToTable("licenses", table =>
            table.HasCheckConstraint("ck_licenses_window", "expires_at_utc > valid_from_utc"));
        ConfigureTenantOwned(entity, "licenses");
        entity.Property(value => value.DeviceId).HasColumnName("device_id").IsRequired();
        entity.Property(value => value.ValidFromUtc).HasColumnName("valid_from_utc").IsRequired();
        entity.Property(value => value.ExpiresAtUtc).HasColumnName("expires_at_utc").IsRequired();
        entity.Property(value => value.ControlState).HasColumnName("control_state").HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(value => value.LatestIssuedLeaseExpiryUtc).HasColumnName("latest_issued_lease_expiry_utc");
        entity.Property(value => value.TransferDestinationDeviceId).HasColumnName("transfer_destination_device_id");
        entity.Property(value => value.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.Property(value => value.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        entity.Property(value => value.ConcurrencyToken).HasColumnName("concurrency_token").IsConcurrencyToken();
        entity.HasOne<Device>()
            .WithMany()
            .HasForeignKey(value => new { value.TenantId, value.DeviceId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_licenses_devices_tenant");
        entity.HasIndex(value => new { value.TenantId, value.DeviceId }).HasDatabaseName("ix_licenses_tenant_device");
        entity.HasQueryFilter(value => CurrentTenantId.HasValue && value.TenantId == CurrentTenantId.Value);
    }

    private void ConfigureAudit(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<TenantAuditEvent>();
        entity.ToTable("audit_events");
        ConfigureTenantOwned(entity, "audit_events");
        entity.Property(value => value.ActorType).HasColumnName("actor_type").HasMaxLength(32).IsRequired();
        entity.Property(value => value.ActorId).HasColumnName("actor_id");
        entity.Property(value => value.Action).HasColumnName("action").HasMaxLength(128).IsRequired();
        entity.Property(value => value.TargetType).HasColumnName("target_type").HasMaxLength(64).IsRequired();
        entity.Property(value => value.TargetId).HasColumnName("target_id");
        entity.Property(value => value.Outcome).HasColumnName("outcome").HasMaxLength(32).IsRequired();
        entity.Property(value => value.ReasonCode).HasColumnName("reason_code").HasMaxLength(64);
        entity.Property(value => value.CorrelationId).HasColumnName("correlation_id").IsRequired();
        entity.Property(value => value.DetailsJson).HasColumnName("details_json").HasColumnType("nvarchar(max)").IsRequired();
        entity.Property(value => value.OccurredAtUtc).HasColumnName("occurred_at_utc").IsRequired();
        entity.HasIndex(value => new { value.TenantId, value.OccurredAtUtc }).HasDatabaseName("ix_audit_events_tenant_occurred");
        entity.HasQueryFilter(value => CurrentTenantId.HasValue && value.TenantId == CurrentTenantId.Value);
    }

    private void ConfigureOutbox(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<OutboxMessage>();
        entity.ToTable("outbox_messages");
        ConfigureTenantOwned(entity, "outbox_messages");
        entity.Property(value => value.MessageType).HasColumnName("message_type").HasMaxLength(160).IsRequired();
        entity.Property(value => value.PayloadJson).HasColumnName("payload_json").HasColumnType("nvarchar(max)").IsRequired();
        entity.Property(value => value.OccurredAtUtc).HasColumnName("occurred_at_utc").IsRequired();
        entity.Property(value => value.ProcessedAtUtc).HasColumnName("processed_at_utc");
        entity.Property(value => value.AttemptCount).HasColumnName("attempt_count").IsRequired();
        entity.Property(value => value.LastSafeError).HasColumnName("last_safe_error").HasMaxLength(1024);
        entity.HasIndex(value => new { value.ProcessedAtUtc, value.OccurredAtUtc }).HasDatabaseName("ix_outbox_pending");
        entity.HasQueryFilter(value => CurrentTenantId.HasValue && value.TenantId == CurrentTenantId.Value);
    }

    private static void ConfigureTenantOwned<TEntity>(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> entity,
        string tableName)
        where TEntity : TenantOwnedEntity
    {
        entity.HasKey(value => value.Id).HasName($"pk_{tableName}");
        entity.HasAlternateKey(value => new { value.TenantId, value.Id }).HasName($"ak_{tableName}_tenant_id_id");
        entity.Property(value => value.Id).HasColumnName("id");
        entity.Property(value => value.TenantId).HasColumnName("tenant_id").IsRequired();
        entity.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(value => value.TenantId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName($"fk_{tableName}_tenants");
    }
}
