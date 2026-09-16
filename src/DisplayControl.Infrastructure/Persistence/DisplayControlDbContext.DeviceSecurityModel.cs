using DisplayControl.Domain.Devices;
using Microsoft.EntityFrameworkCore;

namespace DisplayControl.Infrastructure.Persistence;

public sealed partial class DisplayControlDbContext
{
    public DbSet<EnrollmentToken> EnrollmentTokens => Set<EnrollmentToken>();

    public DbSet<DeviceCertificate> DeviceCertificates => Set<DeviceCertificate>();

    public DbSet<DeviceNetworkInterface> DeviceNetworkInterfaces => Set<DeviceNetworkInterface>();

    public DbSet<DeviceHeartbeat> DeviceHeartbeats => Set<DeviceHeartbeat>();

    public DbSet<DeviceSynchronizationEvent> DeviceSynchronizationEvents => Set<DeviceSynchronizationEvent>();

    private void ConfigureDeviceSecurityModel(ModelBuilder modelBuilder)
    {
        ConfigureEnrollmentToken(modelBuilder);
        ConfigureDeviceCertificate(modelBuilder);
        ConfigureDeviceNetworkInterface(modelBuilder);
        ConfigureDeviceHeartbeat(modelBuilder);
        ConfigureDeviceSynchronizationEvent(modelBuilder);
    }

    private void ConfigureEnrollmentToken(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<EnrollmentToken>();
        entity.ToTable("enrollment_tokens", table =>
        {
            table.HasCheckConstraint("ck_enrollment_tokens_digest", "DATALENGTH(token_digest) = 32");
            table.HasCheckConstraint("ck_enrollment_tokens_expiry", "expires_at_utc > created_at_utc");
            table.HasCheckConstraint(
                "ck_enrollment_tokens_failed_attempts",
                $"failed_attempt_count >= 0 AND failed_attempt_count <= {EnrollmentToken.MaximumFailedAttempts}");
            table.HasCheckConstraint(
                "ck_enrollment_tokens_consumption",
                "(consumed_at_utc IS NULL AND consumed_by_device_id IS NULL) OR " +
                "(consumed_at_utc IS NOT NULL AND consumed_by_device_id IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_enrollment_tokens_terminal_state",
                "NOT (consumed_at_utc IS NOT NULL AND revoked_at_utc IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_enrollment_tokens_expected_device",
                "expected_device_id IS NULL OR consumed_by_device_id IS NULL OR expected_device_id = consumed_by_device_id");
        });
        ConfigureTenantOwned(entity, "enrollment_tokens");
        entity.Property(value => value.TokenDigest).HasColumnName("token_digest").IsRequired();
        entity.Property(value => value.ExpectedDeviceId).HasColumnName("expected_device_id");
        entity.Property(value => value.ExpectedSerialNumberNormalized)
            .HasColumnName("expected_serial_number_normalized")
            .HasMaxLength(64);
        entity.Property(value => value.ExpiresAtUtc).HasColumnName("expires_at_utc").IsRequired();
        entity.Property(value => value.ConsumedAtUtc).HasColumnName("consumed_at_utc");
        entity.Property(value => value.ConsumedByDeviceId).HasColumnName("consumed_by_device_id");
        entity.Property(value => value.RevokedAtUtc).HasColumnName("revoked_at_utc");
        entity.Property(value => value.FailedAttemptCount).HasColumnName("failed_attempt_count").IsRequired();
        entity.Property(value => value.LastFailedAttemptAtUtc).HasColumnName("last_failed_attempt_at_utc");
        entity.Property(value => value.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        entity.Property(value => value.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.Property(value => value.ConcurrencyToken).HasColumnName("concurrency_token").IsConcurrencyToken();
        entity.HasOne<Device>()
            .WithMany()
            .HasForeignKey(value => new { value.TenantId, value.ExpectedDeviceId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_enrollment_tokens_expected_devices_tenant");
        entity.HasOne<Device>()
            .WithMany()
            .HasForeignKey(value => new { value.TenantId, value.ConsumedByDeviceId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_enrollment_tokens_consuming_devices_tenant");
        entity.HasIndex(value => value.TokenDigest).IsUnique().HasDatabaseName("ux_enrollment_tokens_digest");
        entity.HasIndex(value => new { value.TenantId, value.ExpiresAtUtc })
            .HasDatabaseName("ix_enrollment_tokens_tenant_expiry");
        entity.HasQueryFilter(value => CurrentTenantId.HasValue && value.TenantId == CurrentTenantId.Value);
    }

    private void ConfigureDeviceCertificate(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DeviceCertificate>();
        entity.ToTable("device_certificates", table =>
        {
            table.HasCheckConstraint("ck_device_certificates_thumbprint", "DATALENGTH(thumbprint_sha256) = 32");
            table.HasCheckConstraint("ck_device_certificates_spki", "DATALENGTH(subject_public_key_info_sha256) = 32");
            table.HasCheckConstraint(
                "ck_device_certificates_der",
                "certificate_der IS NULL OR (DATALENGTH(certificate_der) >= 100 AND DATALENGTH(certificate_der) <= 16384)");
            table.HasCheckConstraint("ck_device_certificates_validity", "not_after_utc > not_before_utc");
            table.HasCheckConstraint(
                "ck_device_certificates_issuance",
                "issued_at_utc >= not_before_utc AND issued_at_utc < not_after_utc");
            table.HasCheckConstraint(
                "ck_device_certificates_revocation",
                "state <> 'Revoked' OR (revoked_at_utc IS NOT NULL AND revocation_reason_code IS NOT NULL)");
        });
        ConfigureTenantOwned(entity, "device_certificates");
        entity.Property(value => value.DeviceId).HasColumnName("device_id").IsRequired();
        entity.Property(value => value.CertificateSerialNumber)
            .HasColumnName("certificate_serial_number")
            .HasMaxLength(128)
            .IsRequired();
        entity.Property(value => value.ThumbprintSha256).HasColumnName("thumbprint_sha256").IsRequired();
        entity.Property(value => value.SubjectPublicKeyInfoSha256)
            .HasColumnName("subject_public_key_info_sha256")
            .IsRequired();
        entity.Property(value => value.CertificateDer).HasColumnName("certificate_der");
        entity.Property(value => value.NotBeforeUtc).HasColumnName("not_before_utc").IsRequired();
        entity.Property(value => value.NotAfterUtc).HasColumnName("not_after_utc").IsRequired();
        entity.Property(value => value.State).HasColumnName("state").HasConversion<string>().HasMaxLength(24).IsRequired();
        entity.Property(value => value.IssuedAtUtc).HasColumnName("issued_at_utc").IsRequired();
        entity.Property(value => value.RotatedFromCertificateId).HasColumnName("rotated_from_certificate_id");
        entity.Property(value => value.RevokedAtUtc).HasColumnName("revoked_at_utc");
        entity.Property(value => value.RevocationReasonCode).HasColumnName("revocation_reason_code").HasMaxLength(64);
        entity.HasOne<Device>()
            .WithMany()
            .HasForeignKey(value => new { value.TenantId, value.DeviceId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_device_certificates_devices_tenant");
        entity.HasOne<DeviceCertificate>()
            .WithMany()
            .HasForeignKey(value => new { value.TenantId, value.RotatedFromCertificateId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_device_certificates_rotation_tenant");
        entity.HasIndex(value => value.ThumbprintSha256).IsUnique().HasDatabaseName("ux_device_certificates_thumbprint");
        entity.HasIndex(value => value.CertificateSerialNumber)
            .IsUnique()
            .HasDatabaseName("ux_device_certificates_serial");
        entity.HasIndex(value => new { value.TenantId, value.DeviceId, value.State })
            .HasDatabaseName("ix_device_certificates_device_state");
        entity.HasQueryFilter(value => CurrentTenantId.HasValue && value.TenantId == CurrentTenantId.Value);
    }

    private void ConfigureDeviceNetworkInterface(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DeviceNetworkInterface>();
        entity.ToTable("device_network_interfaces");
        ConfigureTenantOwned(entity, "device_network_interfaces");
        entity.Property(value => value.DeviceId).HasColumnName("device_id").IsRequired();
        entity.Property(value => value.InterfaceName).HasColumnName("interface_name").HasMaxLength(64).IsRequired();
        entity.Property(value => value.MacAddressNormalized).HasColumnName("mac_address_normalized").HasMaxLength(12);
        entity.Property(value => value.LocalAddressesJson)
            .HasColumnName("local_addresses_json")
            .HasColumnType("nvarchar(max)")
            .IsRequired();
        entity.Property(value => value.ObservedAtUtc).HasColumnName("observed_at_utc").IsRequired();
        entity.HasOne<Device>()
            .WithMany()
            .HasForeignKey(value => new { value.TenantId, value.DeviceId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_device_network_interfaces_devices_tenant");
        entity.HasIndex(value => new { value.TenantId, value.DeviceId, value.InterfaceName })
            .IsUnique()
            .HasDatabaseName("ux_device_network_interfaces_device_name");
        entity.HasQueryFilter(value => CurrentTenantId.HasValue && value.TenantId == CurrentTenantId.Value);
    }

    private void ConfigureDeviceHeartbeat(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DeviceHeartbeat>();
        entity.ToTable("device_heartbeats", table =>
        {
            table.HasCheckConstraint("ck_device_heartbeats_sequence", "sequence > 0");
            table.HasCheckConstraint("ck_device_heartbeats_free_disk", "free_disk_bytes IS NULL OR free_disk_bytes >= 0");
        });
        ConfigureTenantOwned(entity, "device_heartbeats");
        entity.Property(value => value.DeviceId).HasColumnName("device_id").IsRequired();
        entity.Property(value => value.BootId).HasColumnName("boot_id").IsRequired();
        entity.Property(value => value.Sequence).HasColumnName("sequence").IsRequired();
        entity.Property(value => value.RequestSha256).HasColumnName("request_sha256").IsRequired();
        entity.Property(value => value.ResponseJson).HasColumnName("response_json").HasColumnType("nvarchar(max)");
        entity.Property(value => value.ReportedSentAtUtc).HasColumnName("reported_sent_at_utc");
        entity.Property(value => value.ReceivedAtUtc).HasColumnName("received_at_utc").IsRequired();
        entity.Property(value => value.ServerObservedIp).HasColumnName("server_observed_ip").HasMaxLength(64).IsRequired();
        entity.Property(value => value.InventoryJson).HasColumnName("inventory_json").HasColumnType("nvarchar(max)").IsRequired();
        entity.Property(value => value.AppliedDesiredStateVersion).HasColumnName("applied_desired_state_version");
        entity.Property(value => value.PlayerStateCode).HasColumnName("player_state_code").HasMaxLength(64).IsRequired();
        entity.Property(value => value.FreeDiskBytes).HasColumnName("free_disk_bytes");
        entity.Property(value => value.LastErrorCode).HasColumnName("last_error_code").HasMaxLength(64);
        entity.Property(value => value.CorrelationId).HasColumnName("correlation_id").IsRequired();
        entity.HasOne<Device>()
            .WithMany()
            .HasForeignKey(value => new { value.TenantId, value.DeviceId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_device_heartbeats_devices_tenant");
        entity.HasIndex(value => new { value.TenantId, value.DeviceId, value.BootId, value.Sequence })
            .IsUnique()
            .HasDatabaseName("ux_device_heartbeats_device_boot_sequence");
        entity.HasIndex(value => new { value.TenantId, value.DeviceId, value.ReceivedAtUtc })
            .HasDatabaseName("ix_device_heartbeats_device_received");
        entity.HasQueryFilter(value => CurrentTenantId.HasValue && value.TenantId == CurrentTenantId.Value);
    }

    private void ConfigureDeviceSynchronizationEvent(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DeviceSynchronizationEvent>();
        entity.ToTable("device_synchronization_events", table =>
            table.HasCheckConstraint(
                "ck_device_synchronization_events_version",
                "desired_state_version IS NULL OR desired_state_version > 0"));
        ConfigureTenantOwned(entity, "device_synchronization_events");
        entity.Property(value => value.DeviceId).HasColumnName("device_id").IsRequired();
        entity.Property(value => value.DesiredStateVersion).HasColumnName("desired_state_version");
        entity.Property(value => value.EventType).HasColumnName("event_type").HasMaxLength(64).IsRequired();
        entity.Property(value => value.ResultCode).HasColumnName("result_code").HasMaxLength(64).IsRequired();
        entity.Property(value => value.SafeDetailsJson).HasColumnName("safe_details_json").HasColumnType("nvarchar(max)").IsRequired();
        entity.Property(value => value.ReportedAtUtc).HasColumnName("reported_at_utc");
        entity.Property(value => value.ReceivedAtUtc).HasColumnName("received_at_utc").IsRequired();
        entity.Property(value => value.CorrelationId).HasColumnName("correlation_id").IsRequired();
        entity.HasOne<Device>()
            .WithMany()
            .HasForeignKey(value => new { value.TenantId, value.DeviceId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_device_synchronization_events_devices_tenant");
        entity.HasIndex(value => new { value.TenantId, value.DeviceId, value.ReceivedAtUtc })
            .HasDatabaseName("ix_device_synchronization_events_device_received");
        entity.HasQueryFilter(value => CurrentTenantId.HasValue && value.TenantId == CurrentTenantId.Value);
    }
}
