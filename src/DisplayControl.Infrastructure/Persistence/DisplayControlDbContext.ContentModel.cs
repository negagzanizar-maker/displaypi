using DisplayControl.Domain.Content;
using DisplayControl.Domain.Devices;
using DisplayControl.Domain.Licensing;
using DisplayControl.Domain.Operations;
using DisplayControl.Domain.Playlists;
using DisplayControl.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace DisplayControl.Infrastructure.Persistence;

public sealed partial class DisplayControlDbContext
{
    public DbSet<ContentAsset> ContentAssets => Set<ContentAsset>();

    public DbSet<ContentVersion> ContentVersions => Set<ContentVersion>();

    public DbSet<Playlist> Playlists => Set<Playlist>();

    public DbSet<PlaylistVersion> PlaylistVersions => Set<PlaylistVersion>();

    public DbSet<PlaylistItem> PlaylistItems => Set<PlaylistItem>();

    public DbSet<DeviceGroup> DeviceGroups => Set<DeviceGroup>();

    public DbSet<DeviceGroupMember> DeviceGroupMembers => Set<DeviceGroupMember>();

    public DbSet<DeviceAssignment> DeviceAssignments => Set<DeviceAssignment>();

    public DbSet<GroupAssignment> GroupAssignments => Set<GroupAssignment>();

    public DbSet<DesiredState> DesiredStates => Set<DesiredState>();

    public DbSet<DesiredStateAsset> DesiredStateAssets => Set<DesiredStateAsset>();

    public DbSet<LicenseEvent> LicenseEvents => Set<LicenseEvent>();

    public DbSet<SystemKeyMetadata> SystemKeys => Set<SystemKeyMetadata>();

    private void ConfigureContentModel(ModelBuilder modelBuilder)
    {
        var asset = modelBuilder.Entity<ContentAsset>();
        asset.ToTable("content_assets");
        ConfigureTenantOwned(asset, "content_assets");
        asset.Property(value => value.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        asset.Property(value => value.Description).HasColumnName("description").HasMaxLength(2000);
        asset.Property(value => value.MediaKind).HasColumnName("media_kind").HasConversion<string>().HasMaxLength(24).IsRequired();
        asset.Property(value => value.LifecycleState).HasColumnName("lifecycle_state").HasConversion<string>().HasMaxLength(24).IsRequired();
        asset.Property(value => value.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        asset.Property(value => value.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        asset.Property(value => value.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        asset.Property(value => value.ArchivedAtUtc).HasColumnName("archived_at_utc");
        asset.Property(value => value.ConcurrencyToken).HasColumnName("concurrency_token").IsConcurrencyToken();
        asset.HasIndex(value => new { value.TenantId, value.Title }).HasDatabaseName("ix_content_assets_tenant_title");
        asset.HasQueryFilter(value => CurrentTenantId.HasValue && value.TenantId == CurrentTenantId.Value);

        var version = modelBuilder.Entity<ContentVersion>();
        version.ToTable("content_versions", table =>
        {
            table.HasCheckConstraint("ck_content_versions_byte_length", "byte_length >= 0");
            table.HasCheckConstraint("ck_content_versions_sha256", "DATALENGTH(sha256) = 32");
            table.HasCheckConstraint("ck_content_versions_version", "version_number > 0");
        });
        ConfigureTenantOwned(version, "content_versions");
        version.Property(value => value.ContentAssetId).HasColumnName("content_asset_id").IsRequired();
        version.Property(value => value.VersionNumber).HasColumnName("version_number").IsRequired();
        version.Property(value => value.StorageKey).HasColumnName("storage_key").HasMaxLength(512).IsRequired();
        version.Property(value => value.ByteLength).HasColumnName("byte_length").IsRequired();
        version.Property(value => value.Sha256).HasColumnName("sha256").IsRequired();
        version.Property(value => value.DetectedMimeType).HasColumnName("detected_mime_type").HasMaxLength(128).IsRequired();
        version.Property(value => value.OriginalDisplayFileName).HasColumnName("original_display_file_name").HasMaxLength(255).IsRequired();
        version.Property(value => value.MediaMetadataJson).HasColumnName("media_metadata_json").HasColumnType("nvarchar(max)").IsRequired();
        version.Property(value => value.ScanState).HasColumnName("scan_state").HasMaxLength(32).IsRequired();
        version.Property(value => value.ScanEngineVersion).HasColumnName("scan_engine_version").HasMaxLength(128);
        version.Property(value => value.RejectionCode).HasColumnName("rejection_code").HasMaxLength(64);
        version.Property(value => value.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        version.Property(value => value.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        version.Property(value => value.ApprovedByUserId).HasColumnName("approved_by_user_id");
        version.Property(value => value.ApprovedAtUtc).HasColumnName("approved_at_utc");
        version.HasOne<ContentAsset>()
            .WithMany()
            .HasForeignKey(value => new { value.TenantId, value.ContentAssetId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_content_versions_assets_tenant");
        version.HasIndex(value => new { value.TenantId, value.ContentAssetId, value.VersionNumber })
            .IsUnique()
            .HasDatabaseName("ux_content_versions_asset_version");
        version.HasIndex(value => new { value.TenantId, value.StorageKey })
            .IsUnique()
            .HasDatabaseName("ux_content_versions_tenant_storage_key");
        version.HasQueryFilter(value => CurrentTenantId.HasValue && value.TenantId == CurrentTenantId.Value);
    }

    private void ConfigurePlaylistModel(ModelBuilder modelBuilder)
    {
        var playlist = modelBuilder.Entity<Playlist>();
        playlist.ToTable("playlists");
        ConfigureTenantOwned(playlist, "playlists");
        playlist.Property(value => value.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        playlist.Property(value => value.Description).HasColumnName("description").HasMaxLength(2000);
        playlist.Property(value => value.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        playlist.Property(value => value.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        playlist.Property(value => value.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        playlist.Property(value => value.ArchivedAtUtc).HasColumnName("archived_at_utc");
        playlist.Property(value => value.ConcurrencyToken).HasColumnName("concurrency_token").IsConcurrencyToken();
        playlist.HasIndex(value => new { value.TenantId, value.Name }).HasDatabaseName("ix_playlists_tenant_name");
        playlist.HasQueryFilter(value => CurrentTenantId.HasValue && value.TenantId == CurrentTenantId.Value);

        var version = modelBuilder.Entity<PlaylistVersion>();
        version.ToTable("playlist_versions", table =>
            table.HasCheckConstraint("ck_playlist_versions_version", "version_number > 0"));
        ConfigureTenantOwned(version, "playlist_versions");
        version.Property(value => value.PlaylistId).HasColumnName("playlist_id").IsRequired();
        version.Property(value => value.VersionNumber).HasColumnName("version_number").IsRequired();
        version.Property(value => value.NameSnapshot).HasColumnName("name_snapshot").HasMaxLength(200).IsRequired();
        version.Property(value => value.DescriptionSnapshot).HasColumnName("description_snapshot").HasMaxLength(2000);
        version.Property(value => value.PublicationState).HasColumnName("publication_state").HasMaxLength(32).IsRequired();
        version.Property(value => value.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        version.Property(value => value.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        version.Property(value => value.PublishedByUserId).HasColumnName("published_by_user_id");
        version.Property(value => value.PublishedAtUtc).HasColumnName("published_at_utc");
        version.HasOne<Playlist>()
            .WithMany()
            .HasForeignKey(value => new { value.TenantId, value.PlaylistId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_playlist_versions_playlists_tenant");
        version.HasIndex(value => new { value.TenantId, value.PlaylistId, value.VersionNumber })
            .IsUnique()
            .HasDatabaseName("ux_playlist_versions_playlist_version");
        version.HasQueryFilter(value => CurrentTenantId.HasValue && value.TenantId == CurrentTenantId.Value);

        var item = modelBuilder.Entity<PlaylistItem>();
        item.ToTable("playlist_items", table =>
        {
            table.HasCheckConstraint("ck_playlist_items_position", "position >= 0");
            table.HasCheckConstraint(
                "ck_playlist_items_duration",
                "duration_milliseconds IS NULL OR duration_milliseconds > 0");
        });
        ConfigureTenantOwned(item, "playlist_items");
        item.Property(value => value.PlaylistVersionId).HasColumnName("playlist_version_id").IsRequired();
        item.Property(value => value.ContentVersionId).HasColumnName("content_version_id").IsRequired();
        item.Property(value => value.Position).HasColumnName("position").IsRequired();
        item.Property(value => value.DurationMilliseconds).HasColumnName("duration_milliseconds");
        item.Property(value => value.LoopVideo).HasColumnName("loop_video").IsRequired();
        item.Property(value => value.PresentationJson).HasColumnName("presentation_json").HasColumnType("nvarchar(max)").IsRequired();
        item.HasOne<PlaylistVersion>()
            .WithMany()
            .HasForeignKey(value => new { value.TenantId, value.PlaylistVersionId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_playlist_items_versions_tenant");
        item.HasOne<ContentVersion>()
            .WithMany()
            .HasForeignKey(value => new { value.TenantId, value.ContentVersionId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_playlist_items_content_versions_tenant");
        item.HasIndex(value => new { value.TenantId, value.PlaylistVersionId, value.Position })
            .IsUnique()
            .HasDatabaseName("ux_playlist_items_version_position");
        item.HasQueryFilter(value => CurrentTenantId.HasValue && value.TenantId == CurrentTenantId.Value);
    }

    private void ConfigureGroupModel(ModelBuilder modelBuilder)
    {
        var group = modelBuilder.Entity<DeviceGroup>();
        group.ToTable("device_groups");
        ConfigureTenantOwned(group, "device_groups");
        group.Property(value => value.Name).HasColumnName("name").HasMaxLength(160).IsRequired();
        group.Property(value => value.Description).HasColumnName("description").HasMaxLength(2000);
        group.Property(value => value.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        group.Property(value => value.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        group.Property(value => value.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        group.Property(value => value.ConcurrencyToken).HasColumnName("concurrency_token").IsConcurrencyToken();
        group.HasIndex(value => new { value.TenantId, value.Name }).IsUnique().HasDatabaseName("ux_device_groups_tenant_name");
        group.HasQueryFilter(value => CurrentTenantId.HasValue && value.TenantId == CurrentTenantId.Value);

        var member = modelBuilder.Entity<DeviceGroupMember>();
        member.ToTable("device_group_members");
        ConfigureTenantOwned(member, "device_group_members");
        member.Property(value => value.DeviceGroupId).HasColumnName("device_group_id").IsRequired();
        member.Property(value => value.DeviceId).HasColumnName("device_id").IsRequired();
        member.Property(value => value.AddedByUserId).HasColumnName("added_by_user_id").IsRequired();
        member.Property(value => value.AddedAtUtc).HasColumnName("added_at_utc").IsRequired();
        member.HasOne<DeviceGroup>()
            .WithMany()
            .HasForeignKey(value => new { value.TenantId, value.DeviceGroupId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_device_group_members_groups_tenant");
        member.HasOne<Device>()
            .WithMany()
            .HasForeignKey(value => new { value.TenantId, value.DeviceId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_device_group_members_devices_tenant");
        member.HasIndex(value => new { value.TenantId, value.DeviceGroupId, value.DeviceId })
            .IsUnique()
            .HasDatabaseName("ux_device_group_members_group_device");
        member.HasQueryFilter(value => CurrentTenantId.HasValue && value.TenantId == CurrentTenantId.Value);
    }

    private void ConfigureAssignmentModel(ModelBuilder modelBuilder)
    {
        var device = modelBuilder.Entity<DeviceAssignment>();
        device.ToTable("device_assignments", table =>
            table.HasCheckConstraint(
                "ck_device_assignments_window",
                "starts_at_utc IS NULL OR ends_at_utc IS NULL OR ends_at_utc > starts_at_utc"));
        ConfigureTenantOwned(device, "device_assignments");
        ConfigureAssignmentColumns(device);
        device.Property(value => value.DeviceId).HasColumnName("device_id").IsRequired();
        device.HasOne<Device>()
            .WithMany()
            .HasForeignKey(value => new { value.TenantId, value.DeviceId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_device_assignments_devices_tenant");
        device.HasOne<PlaylistVersion>()
            .WithMany()
            .HasForeignKey(value => new { value.TenantId, value.PlaylistVersionId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_device_assignments_playlist_versions_tenant");
        device.HasIndex(value => new { value.TenantId, value.DeviceId, value.IsEnabled, value.Priority })
            .HasDatabaseName("ix_device_assignments_resolution");
        device.HasQueryFilter(value => CurrentTenantId.HasValue && value.TenantId == CurrentTenantId.Value);

        var group = modelBuilder.Entity<GroupAssignment>();
        group.ToTable("group_assignments", table =>
            table.HasCheckConstraint(
                "ck_group_assignments_window",
                "starts_at_utc IS NULL OR ends_at_utc IS NULL OR ends_at_utc > starts_at_utc"));
        ConfigureTenantOwned(group, "group_assignments");
        ConfigureAssignmentColumns(group);
        group.Property(value => value.DeviceGroupId).HasColumnName("device_group_id").IsRequired();
        group.HasOne<DeviceGroup>()
            .WithMany()
            .HasForeignKey(value => new { value.TenantId, value.DeviceGroupId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_group_assignments_groups_tenant");
        group.HasOne<PlaylistVersion>()
            .WithMany()
            .HasForeignKey(value => new { value.TenantId, value.PlaylistVersionId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_group_assignments_playlist_versions_tenant");
        group.HasIndex(value => new { value.TenantId, value.DeviceGroupId, value.IsEnabled, value.Priority })
            .HasDatabaseName("ix_group_assignments_resolution");
        group.HasQueryFilter(value => CurrentTenantId.HasValue && value.TenantId == CurrentTenantId.Value);
    }

    private void ConfigureDesiredStateModel(ModelBuilder modelBuilder)
    {
        var state = modelBuilder.Entity<DesiredState>();
        state.ToTable("desired_states", table =>
        {
            table.HasCheckConstraint("ck_desired_states_version", "version > 0");
            table.HasCheckConstraint("ck_desired_states_sha256", "DATALENGTH(manifest_sha256) = 32");
            table.HasCheckConstraint(
                "ck_desired_states_source",
                "(source_device_assignment_id IS NOT NULL AND source_group_assignment_id IS NULL) OR " +
                "(source_device_assignment_id IS NULL AND source_group_assignment_id IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_desired_states_window",
                "starts_at_utc IS NULL OR ends_at_utc IS NULL OR ends_at_utc > starts_at_utc");
        });
        ConfigureTenantOwned(state, "desired_states");
        state.Property(value => value.DeviceId).HasColumnName("device_id").IsRequired();
        state.Property(value => value.Version).HasColumnName("version").IsRequired();
        state.Property(value => value.SourceDeviceAssignmentId).HasColumnName("source_device_assignment_id");
        state.Property(value => value.SourceGroupAssignmentId).HasColumnName("source_group_assignment_id");
        state.Property(value => value.StartsAtUtc).HasColumnName("starts_at_utc");
        state.Property(value => value.EndsAtUtc).HasColumnName("ends_at_utc");
        state.Property(value => value.ManifestSha256).HasColumnName("manifest_sha256").IsRequired();
        state.Property(value => value.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        state.Property(value => value.PublishedAtUtc).HasColumnName("published_at_utc").IsRequired();
        state.Property(value => value.SupersededByDesiredStateId).HasColumnName("superseded_by_desired_state_id");
        state.HasOne<Device>()
            .WithMany()
            .HasForeignKey(value => new { value.TenantId, value.DeviceId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_desired_states_devices_tenant");
        state.HasOne<DeviceAssignment>()
            .WithMany()
            .HasForeignKey(value => new { value.TenantId, value.SourceDeviceAssignmentId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_desired_states_device_assignments_tenant");
        state.HasOne<GroupAssignment>()
            .WithMany()
            .HasForeignKey(value => new { value.TenantId, value.SourceGroupAssignmentId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_desired_states_group_assignments_tenant");
        state.HasOne<DesiredState>()
            .WithMany()
            .HasForeignKey(value => new { value.TenantId, value.SupersededByDesiredStateId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_desired_states_superseded_by_tenant");
        state.HasIndex(value => new { value.TenantId, value.DeviceId, value.Version })
            .IsUnique()
            .HasDatabaseName("ux_desired_states_device_version");
        state.HasQueryFilter(value => CurrentTenantId.HasValue && value.TenantId == CurrentTenantId.Value);

        var asset = modelBuilder.Entity<DesiredStateAsset>();
        asset.ToTable("desired_state_assets", table =>
        {
            table.HasCheckConstraint("ck_desired_state_assets_position", "position >= 0");
            table.HasCheckConstraint("ck_desired_state_assets_byte_length", "byte_length >= 0");
            table.HasCheckConstraint("ck_desired_state_assets_sha256", "DATALENGTH(sha256) = 32");
            table.HasCheckConstraint(
                "ck_desired_state_assets_duration",
                "duration_milliseconds IS NULL OR duration_milliseconds > 0");
        });
        ConfigureTenantOwned(asset, "desired_state_assets");
        asset.Property(value => value.DesiredStateId).HasColumnName("desired_state_id").IsRequired();
        asset.Property(value => value.ContentVersionId).HasColumnName("content_version_id").IsRequired();
        asset.Property(value => value.Position).HasColumnName("position").IsRequired();
        asset.Property(value => value.MediaKind).HasColumnName("media_kind").HasConversion<string>().HasMaxLength(24).IsRequired();
        asset.Property(value => value.ByteLength).HasColumnName("byte_length").IsRequired();
        asset.Property(value => value.Sha256).HasColumnName("sha256").IsRequired();
        asset.Property(value => value.DurationMilliseconds).HasColumnName("duration_milliseconds");
        asset.Property(value => value.LoopVideo).HasColumnName("loop_video").IsRequired();
        asset.Property(value => value.PlaybackJson).HasColumnName("playback_json").HasColumnType("nvarchar(max)").IsRequired();
        asset.HasOne<DesiredState>()
            .WithMany()
            .HasForeignKey(value => new { value.TenantId, value.DesiredStateId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_desired_state_assets_states_tenant");
        asset.HasOne<ContentVersion>()
            .WithMany()
            .HasForeignKey(value => new { value.TenantId, value.ContentVersionId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_desired_state_assets_content_versions_tenant");
        asset.HasIndex(value => new { value.TenantId, value.DesiredStateId, value.Position })
            .IsUnique()
            .HasDatabaseName("ux_desired_state_assets_state_position");
        asset.HasQueryFilter(value => CurrentTenantId.HasValue && value.TenantId == CurrentTenantId.Value);
    }

    private void ConfigureLicenseEvent(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<LicenseEvent>();
        entity.ToTable("license_events");
        ConfigureTenantOwned(entity, "license_events");
        entity.Property(value => value.LicenseId).HasColumnName("license_id").IsRequired();
        entity.Property(value => value.EventType).HasColumnName("event_type").HasMaxLength(32).IsRequired();
        entity.Property(value => value.ActorType).HasColumnName("actor_type").HasMaxLength(32).IsRequired();
        entity.Property(value => value.ActorId).HasColumnName("actor_id");
        entity.Property(value => value.Reason).HasColumnName("reason").HasMaxLength(1000).IsRequired();
        entity.Property(value => value.BeforeJson).HasColumnName("before_json").HasColumnType("nvarchar(max)").IsRequired();
        entity.Property(value => value.AfterJson).HasColumnName("after_json").HasColumnType("nvarchar(max)").IsRequired();
        entity.Property(value => value.SourceDeviceId).HasColumnName("source_device_id");
        entity.Property(value => value.DestinationDeviceId).HasColumnName("destination_device_id");
        entity.Property(value => value.OccurredAtUtc).HasColumnName("occurred_at_utc").IsRequired();
        entity.HasOne<DeviceLicense>()
            .WithMany()
            .HasForeignKey(value => new { value.TenantId, value.LicenseId })
            .HasPrincipalKey(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_license_events_licenses_tenant");
        entity.HasIndex(value => new { value.TenantId, value.LicenseId, value.OccurredAtUtc })
            .HasDatabaseName("ix_license_events_license_occurred");
        entity.HasQueryFilter(value => CurrentTenantId.HasValue && value.TenantId == CurrentTenantId.Value);
    }

    private static void ConfigureSystemKeyMetadata(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SystemKeyMetadata>();
        entity.ToTable("system_key_metadata");
        entity.HasKey(value => value.KeyId).HasName("pk_system_key_metadata");
        entity.Property(value => value.KeyId).HasColumnName("key_id").HasMaxLength(128);
        entity.Property(value => value.Algorithm).HasColumnName("algorithm").HasMaxLength(24).IsRequired();
        entity.Property(value => value.Purpose).HasColumnName("purpose").HasMaxLength(32).IsRequired();
        entity.Property(value => value.PublicKeyDer).HasColumnName("public_key_der").IsRequired();
        entity.Property(value => value.ActivatesAtUtc).HasColumnName("activates_at_utc").IsRequired();
        entity.Property(value => value.RetiresAtUtc).HasColumnName("retires_at_utc");
        entity.Property(value => value.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        entity.HasIndex(value => new { value.Purpose, value.ActivatesAtUtc }).HasDatabaseName("ix_system_keys_purpose_activation");
    }

    private static void ConfigureAssignmentColumns(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<DeviceAssignment> entity)
    {
        entity.Property(value => value.PlaylistVersionId).HasColumnName("playlist_version_id").IsRequired();
        entity.Property(value => value.IsEnabled).HasColumnName("is_enabled").IsRequired();
        entity.Property(value => value.Priority).HasColumnName("priority").IsRequired();
        entity.Property(value => value.StartsAtUtc).HasColumnName("starts_at_utc");
        entity.Property(value => value.EndsAtUtc).HasColumnName("ends_at_utc");
        entity.Property(value => value.PresentationTimeZone).HasColumnName("presentation_time_zone").HasMaxLength(80).IsRequired();
        entity.Property(value => value.PublishedByUserId).HasColumnName("published_by_user_id").IsRequired();
        entity.Property(value => value.PublishedAtUtc).HasColumnName("published_at_utc").IsRequired();
        entity.Property(value => value.ConcurrencyToken).HasColumnName("concurrency_token").IsConcurrencyToken();
    }

    private static void ConfigureAssignmentColumns(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<GroupAssignment> entity)
    {
        entity.Property(value => value.PlaylistVersionId).HasColumnName("playlist_version_id").IsRequired();
        entity.Property(value => value.IsEnabled).HasColumnName("is_enabled").IsRequired();
        entity.Property(value => value.Priority).HasColumnName("priority").IsRequired();
        entity.Property(value => value.StartsAtUtc).HasColumnName("starts_at_utc");
        entity.Property(value => value.EndsAtUtc).HasColumnName("ends_at_utc");
        entity.Property(value => value.PresentationTimeZone).HasColumnName("presentation_time_zone").HasMaxLength(80).IsRequired();
        entity.Property(value => value.PublishedByUserId).HasColumnName("published_by_user_id").IsRequired();
        entity.Property(value => value.PublishedAtUtc).HasColumnName("published_at_utc").IsRequired();
        entity.Property(value => value.ConcurrencyToken).HasColumnName("concurrency_token").IsConcurrencyToken();
    }
}
