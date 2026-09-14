using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using DisplayControl.Api.Controllers;
using DisplayControl.Api.Realtime;
using DisplayControl.Api.Security;
using DisplayControl.Application.Security;
using DisplayControl.Application.Tenancy;
using DisplayControl.Domain.Content;
using DisplayControl.Domain.Devices;
using DisplayControl.Domain.Identity;
using DisplayControl.Domain.Licensing;
using DisplayControl.Domain.Tenancy;
using DisplayControl.Infrastructure.Identity;
using DisplayControl.Infrastructure.Persistence;
using DisplayControl.Infrastructure.Security;
using DisplayControl.Infrastructure.Tenancy;
using DisplayControl.IntegrationTests.Database;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DisplayControl.IntegrationTests.Api;

public sealed class AuthenticationFlowTests : IClassFixture<AuthenticationFlowFixture>
{
    private static readonly string[] TestLocalAddresses = ["192.0.2.10", "2001:db8::10"];
    private static readonly JsonSerializerOptions ResponseJsonOptions = CreateResponseJsonOptions();
    private readonly AuthenticationFlowFixture _fixture;

    public AuthenticationFlowTests(AuthenticationFlowFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task TenantIdentityDeviceAndContentLifecycleUsesRealSqlServer2022()
    {
        using var client = _fixture.CreateClient();
        using (var readiness = await client.GetAsync("/_health/ready"))
        {
            readiness.EnsureSuccessStatusCode();
            Assert.Contains("\"status\":\"healthy\"", await readiness.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        var anonymousSession = await GetSessionAsync(client);

        using (var wrongPassword = await PostAsync(
            client,
            "/api/v1/auth/sign-in",
            new { email = AuthenticationFlowFixture.AdminEmail, password = "WrongPassword123" },
            anonymousSession.CsrfToken))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        }

        string preMfaCookie;
        using (var signIn = await PostAsync(
            client,
            "/api/v1/auth/sign-in",
            new { email = AuthenticationFlowFixture.AdminEmail, password = AuthenticationFlowFixture.AdminPassword },
            anonymousSession.CsrfToken))
        {
            Assert.Equal(HttpStatusCode.Accepted, signIn.StatusCode);
            var status = await signIn.Content.ReadFromJsonAsync<AuthenticationStatusResponse>();
            Assert.Equal("mfa_enrollment_required", status?.Status);
            preMfaCookie = signIn.Headers.GetValues("Set-Cookie")
                .Single(value => value.StartsWith("__Host-dc.session=", StringComparison.Ordinal)).Split(';')[0];
        }

        var enrollmentSession = await GetSessionAsync(client);
        Assert.True(enrollmentSession.Authenticated);
        Assert.Equal("mfa_enrollment", enrollmentSession.AuthenticationStage);

        using (var deniedBeforeMfa = await PostAsync(
            client,
            $"/api/v1/tenants/{AuthenticationFlowFixture.TenantId}/invitations",
            new { email = "viewer@example.test", role = "viewer" },
            enrollmentSession.CsrfToken))
        {
            Assert.Equal(HttpStatusCode.Forbidden, deniedBeforeMfa.StatusCode);
        }

        TotpEnrollmentResponse enrollment;
        using (var enroll = await PostAsync(
            client,
            "/api/v1/auth/mfa/totp/enroll",
            new { },
            enrollmentSession.CsrfToken))
        {
            enroll.EnsureSuccessStatusCode();
            enrollment = await enroll.Content.ReadFromJsonAsync<TotpEnrollmentResponse>()
                ?? throw new InvalidOperationException("TOTP enrollment response was empty.");
        }

        var currentCode = ComputeTotp(DecodeBase32(enrollment.Secret), _fixture.UtcNow);
        MfaCompletionResponse completion;
        using (var confirm = await PostAsync(
            client,
            "/api/v1/auth/mfa/totp/confirm",
            new { code = currentCode },
            enrollmentSession.CsrfToken))
        {
            confirm.EnsureSuccessStatusCode();
            completion = await confirm.Content.ReadFromJsonAsync<MfaCompletionResponse>()
                ?? throw new InvalidOperationException("MFA completion response was empty.");
        }

        Assert.Equal("authenticated", completion.Status);
        Assert.Equal(10, completion.RecoveryCodes?.Count);
        Assert.Equal(10, completion.RecoveryCodes?.Distinct(StringComparer.Ordinal).Count());

        var authenticatedSession = await GetSessionAsync(client);
        Assert.Equal("full", authenticatedSession.AuthenticationStage);
        Assert.True(authenticatedSession.MfaSatisfied);
        Assert.Equal("TenantAdmin", authenticatedSession.TenantRole);

        using (var staleClient = _fixture.CreateClient(handleCookies: false))
        {
            staleClient.DefaultRequestHeaders.Add("Cookie", preMfaCookie);
            using var staleResponse = await staleClient.GetAsync("/api/v1/auth/sessions");
            Assert.Equal(HttpStatusCode.Unauthorized, staleResponse.StatusCode);
        }

        var activeSessions = await client.GetFromJsonAsync<List<UserSessionResponse>>("/api/v1/auth/sessions");
        Assert.Single(activeSessions!, value => value.IsCurrent);
        using (var otherClient = _fixture.CreateClient())
        {
            var otherAnonymous = await GetSessionAsync(otherClient);
            using var otherSignIn = await PostAsync(otherClient, "/api/v1/auth/sign-in",
                new { email = AuthenticationFlowFixture.AdminEmail, password = AuthenticationFlowFixture.AdminPassword },
                otherAnonymous.CsrfToken);
            Assert.Equal(HttpStatusCode.Accepted, otherSignIn.StatusCode);
            var sessions = await client.GetFromJsonAsync<List<UserSessionResponse>>("/api/v1/auth/sessions");
            var otherSession = Assert.Single(sessions!, value => !value.IsCurrent);
            using var revoke = await PostAsync(client, $"/api/v1/auth/sessions/{otherSession.Id}/revoke",
                new { }, authenticatedSession.CsrfToken);
            Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
            using var revokedSession = await otherClient.GetAsync("/api/v1/session");
            Assert.Equal(HttpStatusCode.Unauthorized, revokedSession.StatusCode);
        }

        using (var missingSession = await PostAsync(client,
            $"/api/v1/auth/sessions/{Guid.NewGuid()}/revoke", new { }, authenticatedSession.CsrfToken))
        {
            Assert.Equal(HttpStatusCode.NotFound, missingSession.StatusCode);
        }

        InvitationCreatedResponse createdInvitation;
        using (var invitation = await PostAsync(
            client,
            $"/api/v1/tenants/{AuthenticationFlowFixture.TenantId}/invitations",
            new { email = "viewer@example.test", role = "viewer" },
            authenticatedSession.CsrfToken))
        {
            Assert.Equal(HttpStatusCode.Created, invitation.StatusCode);
            createdInvitation = await invitation.Content.ReadFromJsonAsync<InvitationCreatedResponse>(ResponseJsonOptions)
                ?? throw new InvalidOperationException("Invitation response was empty.");
        }

        Assert.StartsWith($"v1.{AuthenticationFlowFixture.TenantId:N}.", createdInvitation.Token, StringComparison.Ordinal);

        using (var viewerClient = _fixture.CreateClient())
        {
            const string viewerPassword = "DemoSecurePassphrase2026";
            var viewerAnonymousSession = await GetSessionAsync(viewerClient);
            using (var rejectCommonPassword = await PostAsync(
                viewerClient,
                "/api/v1/invitations/accept",
                new
                {
                    token = createdInvitation.Token,
                    displayName = "Tenant Viewer",
                    password = "passwordpassword"
                },
                viewerAnonymousSession.CsrfToken))
            {
                Assert.Equal(HttpStatusCode.BadRequest, rejectCommonPassword.StatusCode);
            }

            using (var acceptInvitation = await PostAsync(
                viewerClient,
                "/api/v1/invitations/accept",
                new
                {
                    token = createdInvitation.Token,
                    displayName = "Tenant Viewer",
                    password = viewerPassword
                },
                viewerAnonymousSession.CsrfToken))
            {
                Assert.Equal(HttpStatusCode.NoContent, acceptInvitation.StatusCode);
            }

            using (var viewerSignIn = await PostAsync(
                viewerClient,
                "/api/v1/auth/sign-in",
                new { email = "viewer@example.test", password = viewerPassword },
                viewerAnonymousSession.CsrfToken))
            {
                viewerSignIn.EnsureSuccessStatusCode();
            }

            var viewerSession = await GetSessionAsync(viewerClient);
            Assert.Equal("Viewer", viewerSession.TenantRole);
            using (var allowedRead = await viewerClient.GetAsync(
                $"/api/v1/tenants/{AuthenticationFlowFixture.TenantId}/devices"))
            {
                allowedRead.EnsureSuccessStatusCode();
            }

            using (var deniedWrite = await PostAsync(
                viewerClient,
                $"/api/v1/tenants/{AuthenticationFlowFixture.TenantId}/invitations",
                new { email = "denied@example.test", role = "viewer" },
                viewerSession.CsrfToken))
            {
                Assert.Equal(HttpStatusCode.Forbidden, deniedWrite.StatusCode);
            }

            using (var deniedCrossTenant = await viewerClient.GetAsync(
                $"/api/v1/tenants/{Guid.NewGuid()}/devices"))
            {
                Assert.Equal(HttpStatusCode.Forbidden, deniedCrossTenant.StatusCode);
            }
        }

        EnrollmentCodeCreatedResponse enrollmentCode;
        using (var createEnrollment = await PostAsync(
            client,
            $"/api/v1/tenants/{AuthenticationFlowFixture.TenantId}/enrollment-codes",
            new { displayName = "Lobby screen", expiresInMinutes = 15, expectedSerialNumber = "10000000abcd1234" },
            authenticatedSession.CsrfToken))
        {
            Assert.Equal(HttpStatusCode.Created, createEnrollment.StatusCode);
            enrollmentCode = await createEnrollment.Content.ReadFromJsonAsync<EnrollmentCodeCreatedResponse>()
                ?? throw new InvalidOperationException("Enrollment-code response was empty.");
        }

        Assert.StartsWith($"v1.{AuthenticationFlowFixture.TenantId:N}.", enrollmentCode.EnrollmentCode, StringComparison.Ordinal);
        var devices = await client.GetFromJsonAsync<DeviceSummaryResponse[]>(
            $"/api/v1/tenants/{AuthenticationFlowFixture.TenantId}/devices",
            ResponseJsonOptions);
        var pendingDevice = Assert.Single(devices ?? []);
        Assert.Equal(DeviceLifecycleState.PendingEnrollment, pendingDevice.State);
        Assert.Equal("Lobby screen", pendingDevice.DisplayName);

        using (var licenseBeforeEnrollment = await PostAsync(
            client,
            $"/api/v1/tenants/{AuthenticationFlowFixture.TenantId}/licenses",
            new
            {
                deviceId = enrollmentCode.DeviceId,
                validFromUtc = DateTimeOffset.UtcNow,
                expiresAtUtc = DateTimeOffset.UtcNow.AddDays(30),
                reason = "Initial subscription"
            },
            authenticatedSession.CsrfToken))
        {
            Assert.Equal(HttpStatusCode.Conflict, licenseBeforeEnrollment.StatusCode);
        }

        using var deviceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signingRequest = new CertificateRequest("CN=untrusted-device-name", deviceKey, HashAlgorithmName.SHA256);
        DeviceEnrollmentResponse enrolled;
        using (var enrollmentResponse = await PostAsync(
            client,
            "/device/v1/enrollment",
            new
            {
                enrollmentCode = enrollmentCode.EnrollmentCode,
                certificateSigningRequestPem = signingRequest.CreateSigningRequestPem(),
                serialNumber = "10000000abcd1234",
                hostname = "pi-lobby",
                osDescription = "Raspberry Pi OS 64-bit",
                architecture = "arm64",
                agentVersion = "1.0.0",
                playerVersion = "1.0.0",
                diskCapacityBytes = 64L * 1024 * 1024 * 1024,
                networkInterfaces = new[]
                {
                    new
                    {
                        interfaceName = "eth0",
                        macAddress = "02:00:00:00:00:01",
                        localAddresses = TestLocalAddresses
                    }
                }
            },
            authenticatedSession.CsrfToken))
        {
            enrollmentResponse.EnsureSuccessStatusCode();
            enrolled = await enrollmentResponse.Content.ReadFromJsonAsync<DeviceEnrollmentResponse>()
                ?? throw new InvalidOperationException("Device enrollment response was empty.");
        }

        Assert.Equal(enrollmentCode.DeviceId, enrolled.DeviceId);
        using (var issuedCertificate = X509Certificate2.CreateFromPem(enrolled.CertificatePem))
        using (var issuedPublicKey = issuedCertificate.GetECDsaPublicKey())
        {
            Assert.NotNull(issuedPublicKey);
            Assert.Equal(
                deviceKey.ExportSubjectPublicKeyInfo(),
                issuedPublicKey.ExportSubjectPublicKeyInfo());
            Assert.False(issuedCertificate.HasPrivateKey);
        }

        var realTlsProbe = await _fixture.ProbeRealKestrelMutualTlsAsync(enrolled.CertificatePem, deviceKey);
        Assert.Equal(HttpStatusCode.OK, realTlsProbe.ValidCertificateStatus);
        Assert.Equal(HttpStatusCode.Unauthorized, realTlsProbe.MissingCertificateStatus);
        Assert.Equal(HttpStatusCode.Unauthorized, realTlsProbe.UntrustedCertificateStatus);

        using (var replayEnrollment = await PostAsync(
            client,
            "/device/v1/enrollment",
            new
            {
                enrollmentCode = enrollmentCode.EnrollmentCode,
                certificateSigningRequestPem = signingRequest.CreateSigningRequestPem(),
                serialNumber = "10000000abcd1234",
                hostname = "pi-lobby",
                osDescription = "Raspberry Pi OS 64-bit",
                architecture = "arm64",
                agentVersion = "1.0.0",
                playerVersion = "1.0.0",
                diskCapacityBytes = 1L,
                networkInterfaces = Array.Empty<object>()
            },
            authenticatedSession.CsrfToken))
        {
            replayEnrollment.EnsureSuccessStatusCode();
            var replayed = await replayEnrollment.Content.ReadFromJsonAsync<DeviceEnrollmentResponse>();
            Assert.Equal(enrolled.CertificateId, replayed?.CertificateId);
            Assert.Equal(enrolled.CertificatePem, replayed?.CertificatePem);
        }

        using var wrongReplayKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var wrongReplayRequest = new CertificateRequest("CN=wrong-replay", wrongReplayKey, HashAlgorithmName.SHA256);
        using (var changedEnrollmentReplay = await PostAsync(
            client,
            "/device/v1/enrollment",
            new
            {
                enrollmentCode = enrollmentCode.EnrollmentCode,
                certificateSigningRequestPem = wrongReplayRequest.CreateSigningRequestPem(),
                serialNumber = "10000000abcd1234",
                hostname = "pi-lobby",
                osDescription = "Raspberry Pi OS 64-bit",
                architecture = "arm64",
                agentVersion = "1.0.0",
                playerVersion = "1.0.0",
                diskCapacityBytes = 1L,
                networkInterfaces = Array.Empty<object>()
            },
            authenticatedSession.CsrfToken))
        {
            Assert.Equal(HttpStatusCode.BadRequest, changedEnrollmentReplay.StatusCode);
        }

        devices = await client.GetFromJsonAsync<DeviceSummaryResponse[]>(
            $"/api/v1/tenants/{AuthenticationFlowFixture.TenantId}/devices",
            ResponseJsonOptions);
        var activeDevice = Assert.Single(devices ?? []);
        Assert.Equal(DeviceLifecycleState.Active, activeDevice.State);
        Assert.Equal("10000000ABCD1234", activeDevice.SerialNumber);
        Assert.Equal("pi-lobby", activeDevice.Hostname);
        var activeNetwork = Assert.Single(activeDevice.NetworkInterfaces);
        Assert.Equal("eth0", activeNetwork.InterfaceName);
        Assert.Equal("020000000001", activeNetwork.MacAddress);
        Assert.Equal(TestLocalAddresses, activeNetwork.LocalAddresses);

        LicenseResponse createdLicense;
        var licenseStart = DateTimeOffset.UtcNow.AddMinutes(-1);
        using (var createLicense = await PostAsync(
            client,
            $"/api/v1/tenants/{AuthenticationFlowFixture.TenantId}/licenses",
            new
            {
                deviceId = enrollmentCode.DeviceId,
                validFromUtc = licenseStart,
                expiresAtUtc = licenseStart.AddDays(30),
                reason = "Initial subscription"
            },
            authenticatedSession.CsrfToken))
        {
            Assert.Equal(HttpStatusCode.Created, createLicense.StatusCode);
            createdLicense = await createLicense.Content.ReadFromJsonAsync<LicenseResponse>(ResponseJsonOptions)
                ?? throw new InvalidOperationException("Licence response was empty.");
        }

        Assert.Equal(LicenseEffectiveState.Active, createdLicense.EffectiveState);

        _fixture.SetDeviceCertificate(enrolled.CertificatePem);
        client.DefaultRequestHeaders.Add("X-Test-Client-Certificate", "present");
        var bootId = Guid.NewGuid();
        var heartbeatRequest = new
        {
            bootId,
            sequence = 1,
            reportedSentAtUtc = DateTimeOffset.UtcNow,
            hostname = "pi-lobby",
            osDescription = "Raspberry Pi OS 64-bit",
            architecture = "arm64",
            agentVersion = "1.0.0",
            playerVersion = "1.0.0",
            diskCapacityBytes = 64L * 1024 * 1024 * 1024,
            freeDiskBytes = 48L * 1024 * 1024 * 1024,
            appliedDesiredStateVersion = (long?)null,
            playerStateCode = "licensedNoContent",
            lastErrorCode = (string?)null,
            networkInterfaces = new[]
            {
                new
                {
                    interfaceName = "eth0",
                    macAddress = "02:00:00:00:00:01",
                    localAddresses = TestLocalAddresses
                }
            }
        };
        DeviceHeartbeatResponse heartbeat;
        using (var heartbeatResponse = await PostAsync(
            client,
            "/device/v1/heartbeats",
            heartbeatRequest,
            authenticatedSession.CsrfToken))
        {
            heartbeatResponse.EnsureSuccessStatusCode();
            heartbeat = await heartbeatResponse.Content.ReadFromJsonAsync<DeviceHeartbeatResponse>()
                ?? throw new InvalidOperationException("Heartbeat response was empty.");
        }

        Assert.Equal("licensed", heartbeat.LicenseStatus);
        Assert.Equal("licensedNoContent", heartbeat.DesiredState.Status);
        Assert.NotNull(heartbeat.Lease);
        var verificationKey = new LicenseLeaseVerificationKey(
            heartbeat.Lease.KeyId,
            heartbeat.Lease.Algorithm,
            heartbeat.Lease.SubjectPublicKeyInfoPem);
        Assert.True(LicenseLeaseTokenCodec.TryValidate(
            heartbeat.Lease.Token,
            verificationKey,
            AuthenticationFlowFixture.TenantId,
            enrolled.DeviceId,
            enrolled.CertificateId,
            heartbeat.ServerTimeUtc,
            out _));

        using (var replayHeartbeat = await PostAsync(
            client,
            "/device/v1/heartbeats",
            heartbeatRequest,
            authenticatedSession.CsrfToken))
        {
            replayHeartbeat.EnsureSuccessStatusCode();
            var replayed = await replayHeartbeat.Content.ReadFromJsonAsync<DeviceHeartbeatResponse>();
            Assert.Equal(heartbeat.Lease?.Token, replayed?.Lease?.Token);
            Assert.Equal(heartbeat.ServerTimeUtc, replayed?.ServerTimeUtc);
        }

        using (var changedHeartbeatReplay = await PostAsync(
            client,
            "/device/v1/heartbeats",
            new
            {
                bootId,
                sequence = 1,
                hostname = "pi-lobby",
                osDescription = "Raspberry Pi OS 64-bit",
                architecture = "arm64",
                agentVersion = "1.0.0",
                playerVersion = "1.0.0",
                diskCapacityBytes = 1L,
                freeDiskBytes = 1L,
                appliedDesiredStateVersion = (long?)null,
                playerStateCode = "licensedNoContent",
                lastErrorCode = (string?)null,
                networkInterfaces = Array.Empty<object>()
            },
            authenticatedSession.CsrfToken))
        {
            Assert.Equal(HttpStatusCode.Conflict, changedHeartbeatReplay.StatusCode);
        }

        var delayedBootId = Guid.NewGuid();
        using (var delayedHeartbeat = await PostAsync(
            client,
            "/device/v1/heartbeats",
            new
            {
                bootId = delayedBootId,
                sequence = 2,
                reportedSentAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
                hostname = "pi-lobby",
                osDescription = "Raspberry Pi OS 64-bit",
                architecture = "arm64",
                agentVersion = "1.0.0",
                playerVersion = "1.0.0",
                diskCapacityBytes = 1L,
                freeDiskBytes = 1L,
                appliedDesiredStateVersion = (long?)null,
                playerStateCode = "licensedNoContent",
                lastErrorCode = (string?)null,
                networkInterfaces = Array.Empty<object>()
            },
            authenticatedSession.CsrfToken))
        {
            delayedHeartbeat.EnsureSuccessStatusCode();
        }

        using (var outOfOrderHeartbeat = await PostAsync(
            client,
            "/device/v1/heartbeats",
            new
            {
                bootId = delayedBootId,
                sequence = 1,
                reportedSentAtUtc = DateTimeOffset.UtcNow,
                hostname = "pi-lobby",
                osDescription = "Raspberry Pi OS 64-bit",
                architecture = "arm64",
                agentVersion = "1.0.0",
                playerVersion = "1.0.0",
                diskCapacityBytes = 1L,
                freeDiskBytes = 1L,
                appliedDesiredStateVersion = (long?)null,
                playerStateCode = "licensedNoContent",
                lastErrorCode = (string?)null,
                networkInterfaces = Array.Empty<object>()
            },
            authenticatedSession.CsrfToken))
        {
            Assert.Equal(HttpStatusCode.Conflict, outOfOrderHeartbeat.StatusCode);
        }

        var png = new byte[32];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(png, 0);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16, 4), 1920);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(20, 4), 1080);
        ContentResponse uploadedContent;
        using (var upload = await PostMultipartAsync(
            client,
            $"/api/v1/tenants/{AuthenticationFlowFixture.TenantId}/contents",
            png,
            authenticatedSession.CsrfToken))
        {
            Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
            uploadedContent = await upload.Content.ReadFromJsonAsync<ContentResponse>(ResponseJsonOptions)
                ?? throw new InvalidOperationException("Content upload response was empty.");
        }

        Assert.Equal(ContentLifecycleState.Approved, uploadedContent.LifecycleState);
        Assert.Equal("clean", uploadedContent.LatestVersion?.ScanState);
        Assert.NotNull(uploadedContent.LatestVersion?.ApprovedAtUtc);

        using (var preview = await client.GetAsync(
            $"/api/v1/tenants/{AuthenticationFlowFixture.TenantId}/contents/{uploadedContent.Id}/versions/{uploadedContent.LatestVersion!.Id}/preview"))
        {
            preview.EnsureSuccessStatusCode();
            Assert.Equal("image/png", preview.Content.Headers.ContentType?.MediaType);
            Assert.Equal(png, await preview.Content.ReadAsByteArrayAsync());
        }

        PlaylistResponse playlist;
        using (var createPlaylist = await PostAsync(
            client,
            $"/api/v1/tenants/{AuthenticationFlowFixture.TenantId}/playlists",
            new
            {
                name = "Lobby rotation",
                description = "Approved lobby media",
                publishImmediately = true,
                items = new[]
                {
                    new
                    {
                        contentVersionId = uploadedContent.LatestVersion.Id,
                        durationMilliseconds = 5000,
                        loopVideo = false
                    }
                }
            },
            authenticatedSession.CsrfToken))
        {
            Assert.Equal(HttpStatusCode.Created, createPlaylist.StatusCode);
            playlist = await createPlaylist.Content.ReadFromJsonAsync<PlaylistResponse>(ResponseJsonOptions)
                ?? throw new InvalidOperationException("Playlist response was empty.");
        }

        var playlistVersion = playlist.LatestVersion
            ?? throw new InvalidOperationException("Published playlist response had no version.");
        Assert.Equal("published", playlistVersion.PublicationState);

        DeviceGroupResponse deviceGroup;
        using (var createGroup = await PostAsync(
            client,
            $"/api/v1/tenants/{AuthenticationFlowFixture.TenantId}/device-groups",
            new { name = "Lobby displays", description = "Managed as one target" },
            authenticatedSession.CsrfToken))
        {
            Assert.Equal(HttpStatusCode.Created, createGroup.StatusCode);
            deviceGroup = await createGroup.Content.ReadFromJsonAsync<DeviceGroupResponse>()
                ?? throw new InvalidOperationException("Device-group response was empty.");
        }

        using (var addGroupMember = await PutAsync(
            client,
            $"/api/v1/tenants/{AuthenticationFlowFixture.TenantId}/device-groups/{deviceGroup.Id}/members",
            new { deviceIds = new[] { enrolled.DeviceId }, concurrencyToken = deviceGroup.ConcurrencyToken },
            authenticatedSession.CsrfToken))
        {
            addGroupMember.EnsureSuccessStatusCode();
            deviceGroup = await addGroupMember.Content.ReadFromJsonAsync<DeviceGroupResponse>()
                ?? throw new InvalidOperationException("Updated device-group response was empty.");
            Assert.Equal(enrolled.DeviceId, Assert.Single(deviceGroup.DeviceIds));
        }

        var groupEndsAtUtc = _fixture.UtcNow.AddMinutes(5);
        groupEndsAtUtc = groupEndsAtUtc.AddTicks(-(groupEndsAtUtc.Ticks % TimeSpan.TicksPerMillisecond));
        using var groupPublicationSignal = _fixture.SubscribeDeviceStateChanges(
            AuthenticationFlowFixture.TenantId,
            enrolled.DeviceId);
        GroupAssignmentPublishedResponse groupAssignment;
        using (var assignGroup = await PostAsync(
            client,
            $"/api/v1/tenants/{AuthenticationFlowFixture.TenantId}/device-groups/{deviceGroup.Id}/assignments",
            new
            {
                playlistVersionId = playlistVersion.Id,
                priority = 10,
                startsAtUtc = _fixture.UtcNow.AddMinutes(-1),
                endsAtUtc = groupEndsAtUtc,
                presentationTimeZone = "UTC"
            },
            authenticatedSession.CsrfToken))
        {
            Assert.Equal(HttpStatusCode.Created, assignGroup.StatusCode);
            groupAssignment = await assignGroup.Content.ReadFromJsonAsync<GroupAssignmentPublishedResponse>()
                ?? throw new InvalidOperationException("Group-assignment response was empty.");
        }
        Assert.True(await ReceivesStateChangeAsync(groupPublicationSignal));

        using (var equalPriorityCollision = await PostAsync(
            client,
            $"/api/v1/tenants/{AuthenticationFlowFixture.TenantId}/device-groups/{deviceGroup.Id}/assignments",
            new
            {
                playlistVersionId = playlistVersion.Id,
                priority = 10,
                startsAtUtc = _fixture.UtcNow,
                endsAtUtc = groupEndsAtUtc,
                presentationTimeZone = "UTC"
            },
            authenticatedSession.CsrfToken))
        {
            Assert.Equal(HttpStatusCode.Conflict, equalPriorityCollision.StatusCode);
        }

        DeviceHeartbeatResponse groupHeartbeat;
        using (var heartbeatWithGroupContent = await PostAsync(
            client,
            "/device/v1/heartbeats",
            new
            {
                bootId,
                sequence = 2,
                hostname = "pi-lobby",
                osDescription = "Raspberry Pi OS 64-bit",
                architecture = "arm64",
                agentVersion = "1.0.0",
                playerVersion = "1.0.0",
                diskCapacityBytes = 1L,
                freeDiskBytes = 1L,
                appliedDesiredStateVersion = (long?)null,
                playerStateCode = "synchronizing",
                lastErrorCode = (string?)null,
                networkInterfaces = Array.Empty<object>()
            },
            authenticatedSession.CsrfToken))
        {
            heartbeatWithGroupContent.EnsureSuccessStatusCode();
            groupHeartbeat = await heartbeatWithGroupContent.Content.ReadFromJsonAsync<DeviceHeartbeatResponse>()
                ?? throw new InvalidOperationException("Group heartbeat response was empty.");
        }

        var compiledGroupState = Assert.Single(groupAssignment.DesiredStates);
        Assert.Equal(compiledGroupState.DesiredStateId, groupHeartbeat.DesiredState.Id);
        Assert.True(LicenseLeaseTokenCodec.TryValidate(
            groupHeartbeat.Lease!.Token,
            verificationKey,
            AuthenticationFlowFixture.TenantId,
            enrolled.DeviceId,
            enrolled.CertificateId,
            groupHeartbeat.ServerTimeUtc,
            out var groupLeasePayload));
        Assert.Equal(groupEndsAtUtc, groupLeasePayload?.ExpiresAtUtc);

        using var groupRemovalSignal = _fixture.SubscribeDeviceStateChanges(
            AuthenticationFlowFixture.TenantId,
            enrolled.DeviceId);
        using (var removeGroupMember = await PutAsync(
            client,
            $"/api/v1/tenants/{AuthenticationFlowFixture.TenantId}/device-groups/{deviceGroup.Id}/members",
            new { deviceIds = Array.Empty<Guid>(), concurrencyToken = deviceGroup.ConcurrencyToken },
            authenticatedSession.CsrfToken))
        {
            removeGroupMember.EnsureSuccessStatusCode();
        }
        Assert.True(await ReceivesStateChangeAsync(groupRemovalSignal));

        using var devicePublicationSignal = _fixture.SubscribeDeviceStateChanges(
            AuthenticationFlowFixture.TenantId,
            enrolled.DeviceId);
        AssignmentPublishedResponse assignment;
        using (var assign = await PostAsync(
            client,
            $"/api/v1/tenants/{AuthenticationFlowFixture.TenantId}/devices/{enrolled.DeviceId}/assignments",
            new { playlistVersionId = playlistVersion.Id, priority = 0, presentationTimeZone = "UTC" },
            authenticatedSession.CsrfToken))
        {
            Assert.Equal(HttpStatusCode.Created, assign.StatusCode);
            assignment = await assign.Content.ReadFromJsonAsync<AssignmentPublishedResponse>()
                ?? throw new InvalidOperationException("Assignment response was empty.");
        }
        Assert.True(await ReceivesStateChangeAsync(devicePublicationSignal));

        DeviceHeartbeatResponse assignedHeartbeat;
        using (var heartbeatWithContent = await PostAsync(
            client,
            "/device/v1/heartbeats",
            new
            {
                bootId,
                sequence = 3,
                hostname = "pi-lobby",
                osDescription = "Raspberry Pi OS 64-bit",
                architecture = "arm64",
                agentVersion = "1.0.0",
                playerVersion = "1.0.0",
                diskCapacityBytes = 1L,
                freeDiskBytes = 1L,
                appliedDesiredStateVersion = (long?)null,
                playerStateCode = "synchronizing",
                lastErrorCode = (string?)null,
                networkInterfaces = Array.Empty<object>()
            },
            authenticatedSession.CsrfToken))
        {
            heartbeatWithContent.EnsureSuccessStatusCode();
            assignedHeartbeat = await heartbeatWithContent.Content.ReadFromJsonAsync<DeviceHeartbeatResponse>()
                ?? throw new InvalidOperationException("Assigned heartbeat response was empty.");
        }

        Assert.Equal("available", assignedHeartbeat.DesiredState.Status);
        Assert.Equal(assignment.DesiredStateId, assignedHeartbeat.DesiredState.Id);
        Assert.NotEqual(compiledGroupState.DesiredStateId, assignedHeartbeat.DesiredState.Id);
        Assert.True(LicenseLeaseTokenCodec.TryValidate(
            assignedHeartbeat.Lease!.Token,
            verificationKey,
            AuthenticationFlowFixture.TenantId,
            enrolled.DeviceId,
            enrolled.CertificateId,
            assignedHeartbeat.ServerTimeUtc,
            out var assignedLeasePayload));
        Assert.Equal(assignment.ManifestSha256, assignedLeasePayload?.DesiredStateManifestSha256);

        var manifestBytes = await client.GetByteArrayAsync(
            $"/device/v1/desired-states/{assignment.DesiredStateId}/manifest");
        Assert.Equal(assignment.ManifestSha256, Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant());
        var deliveredAsset = await client.GetByteArrayAsync(
            $"/device/v1/desired-states/{assignment.DesiredStateId}/assets/{uploadedContent.LatestVersion.Id}");
        Assert.Equal(png, deliveredAsset);

        using var rotatedDeviceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var rotationSigningRequest = new CertificateRequest(
            "CN=ignored-rotation-subject",
            rotatedDeviceKey,
            HashAlgorithmName.SHA256);
        DeviceCertificateRotationResponse rotation;
        using (var rotate = await PostAsync(
            client,
            "/device/v1/certificates/rotate",
            new { certificateSigningRequestPem = rotationSigningRequest.CreateSigningRequestPem() },
            authenticatedSession.CsrfToken))
        {
            Assert.True(rotate.IsSuccessStatusCode, await rotate.Content.ReadAsStringAsync());
            rotation = await rotate.Content.ReadFromJsonAsync<DeviceCertificateRotationResponse>()
                ?? throw new InvalidOperationException("Certificate rotation response was empty.");
        }

        Assert.True(rotation.PreviousCertificateAcceptedUntilUtc <= rotation.ServerTimeUtc.AddHours(24));
        using (var rotatedCertificate = X509Certificate2.CreateFromPem(rotation.CertificatePem))
        using (var rotatedPublicKey = rotatedCertificate.GetECDsaPublicKey())
        {
            Assert.Equal(rotatedDeviceKey.ExportSubjectPublicKeyInfo(), rotatedPublicKey?.ExportSubjectPublicKeyInfo());
        }

        using (var retryRotation = await PostAsync(
            client,
            "/device/v1/certificates/rotate",
            new { certificateSigningRequestPem = rotationSigningRequest.CreateSigningRequestPem() },
            authenticatedSession.CsrfToken))
        {
            retryRotation.EnsureSuccessStatusCode();
            var replayedRotation = await retryRotation.Content.ReadFromJsonAsync<DeviceCertificateRotationResponse>();
            Assert.Equal(rotation.CertificateId, replayedRotation?.CertificateId);
            Assert.Equal(rotation.CertificatePem, replayedRotation?.CertificatePem);
        }

        _fixture.SetDeviceCertificate(rotation.CertificatePem);
        using (var heartbeatAfterRotation = await PostAsync(
            client,
            "/device/v1/heartbeats",
            new
            {
                bootId,
                sequence = 4,
                hostname = "pi-lobby",
                osDescription = "Raspberry Pi OS 64-bit",
                architecture = "arm64",
                agentVersion = "1.0.0",
                playerVersion = "1.0.0",
                diskCapacityBytes = 1L,
                freeDiskBytes = 1L,
                appliedDesiredStateVersion = assignment.DesiredStateVersion,
                playerStateCode = "ready",
                lastErrorCode = (string?)null,
                networkInterfaces = Array.Empty<object>()
            },
            authenticatedSession.CsrfToken))
        {
            heartbeatAfterRotation.EnsureSuccessStatusCode();
        }

        EnrollmentCodeCreatedResponse replacementEnrollmentCode;
        using (var createReplacementEnrollment = await PostAsync(
            client,
            $"/api/v1/tenants/{AuthenticationFlowFixture.TenantId}/enrollment-codes",
            new { displayName = "Replacement screen", expiresInMinutes = 15, expectedSerialNumber = "10000000abcd5678" },
            authenticatedSession.CsrfToken))
        {
            createReplacementEnrollment.EnsureSuccessStatusCode();
            replacementEnrollmentCode = await createReplacementEnrollment.Content
                .ReadFromJsonAsync<EnrollmentCodeCreatedResponse>()
                ?? throw new InvalidOperationException("Replacement enrollment response was empty.");
        }

        using var replacementDeviceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var replacementSigningRequest = new CertificateRequest(
            "CN=replacement-device",
            replacementDeviceKey,
            HashAlgorithmName.SHA256);
        using (var enrollReplacement = await PostAsync(
            client,
            "/device/v1/enrollment",
            new
            {
                enrollmentCode = replacementEnrollmentCode.EnrollmentCode,
                certificateSigningRequestPem = replacementSigningRequest.CreateSigningRequestPem(),
                serialNumber = "10000000abcd5678",
                hostname = "pi-replacement",
                osDescription = "Raspberry Pi OS 64-bit",
                architecture = "arm64",
                agentVersion = "1.0.0",
                playerVersion = "1.0.0",
                diskCapacityBytes = 1L,
                networkInterfaces = Array.Empty<object>()
            },
            authenticatedSession.CsrfToken))
        {
            enrollReplacement.EnsureSuccessStatusCode();
        }

        var currentLicenses = await client.GetFromJsonAsync<LicenseResponse[]>(
            $"/api/v1/tenants/{AuthenticationFlowFixture.TenantId}/licenses",
            ResponseJsonOptions);
        var sourceLicense = Assert.Single(currentLicenses ?? []);
        LicenseTransferResponse transfer;
        using (var transferResponse = await PostAsync(
            client,
            $"/api/v1/tenants/{AuthenticationFlowFixture.TenantId}/licenses/{sourceLicense.Id}/transfer",
            new
            {
                destinationDeviceId = replacementEnrollmentCode.DeviceId,
                concurrencyToken = sourceLicense.ConcurrencyToken,
                reason = "Screen replacement"
            },
            authenticatedSession.CsrfToken))
        {
            transferResponse.EnsureSuccessStatusCode();
            transfer = await transferResponse.Content.ReadFromJsonAsync<LicenseTransferResponse>(ResponseJsonOptions)
                ?? throw new InvalidOperationException("Licence transfer response was empty.");
        }

        Assert.Equal(LicenseControlState.TransferPending, transfer.Source.ControlState);
        Assert.Equal(sourceLicense.LatestIssuedLeaseExpiryUtc, transfer.DestinationActivatesAtUtc);
        Assert.Equal(replacementEnrollmentCode.DeviceId, transfer.Destination.DeviceId);

        using (var sourceAfterTransfer = await PostAsync(
            client,
            "/device/v1/heartbeats",
            new
            {
                bootId,
                sequence = 5,
                hostname = "pi-lobby",
                osDescription = "Raspberry Pi OS 64-bit",
                architecture = "arm64",
                agentVersion = "1.0.0",
                playerVersion = "1.0.0",
                diskCapacityBytes = 1L,
                freeDiskBytes = 1L,
                appliedDesiredStateVersion = assignment.DesiredStateVersion,
                playerStateCode = "ready",
                lastErrorCode = (string?)null,
                networkInterfaces = Array.Empty<object>()
            },
            authenticatedSession.CsrfToken))
        {
            sourceAfterTransfer.EnsureSuccessStatusCode();
            var denied = await sourceAfterTransfer.Content.ReadFromJsonAsync<DeviceHeartbeatResponse>();
            Assert.Equal("notLicensed", denied?.LicenseStatus);
        }

        using var revocationSignal = _fixture.SubscribeDeviceStateChanges(
            AuthenticationFlowFixture.TenantId,
            transfer.Destination.DeviceId);
        using (var revokeDestination = await PostAsync(
            client,
            $"/api/v1/tenants/{AuthenticationFlowFixture.TenantId}/licenses/{transfer.Destination.Id}/revoke",
            new
            {
                concurrencyToken = transfer.Destination.ConcurrencyToken,
                reason = "Real-time revocation regression"
            },
            authenticatedSession.CsrfToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, revokeDestination.StatusCode);
        }
        Assert.True(await ReceivesStateChangeAsync(revocationSignal));

        var replacementLicenseStart = DateTimeOffset.UtcNow.AddMinutes(-1);
        LicenseResponse replacementActiveLicense;
        using (var createReplacementLicense = await PostAsync(
            client,
            $"/api/v1/tenants/{AuthenticationFlowFixture.TenantId}/licenses",
            new
            {
                deviceId = replacementEnrollmentCode.DeviceId,
                validFromUtc = replacementLicenseStart,
                expiresAtUtc = replacementLicenseStart.AddHours(12),
                reason = "Replacement after retaining revoked history"
            },
            authenticatedSession.CsrfToken))
        {
            Assert.Equal(HttpStatusCode.Created, createReplacementLicense.StatusCode);
            replacementActiveLicense = await createReplacementLicense.Content
                .ReadFromJsonAsync<LicenseResponse>(ResponseJsonOptions)
                ?? throw new InvalidOperationException("Replacement licence response was empty.");
        }

        devices = await client.GetFromJsonAsync<DeviceSummaryResponse[]>(
            $"/api/v1/tenants/{AuthenticationFlowFixture.TenantId}/devices",
            ResponseJsonOptions);
        var replacementDevice = Assert.Single(
            devices ?? [],
            value => value.Id == replacementEnrollmentCode.DeviceId);
        Assert.Equal(LicenseEffectiveState.Active, replacementDevice.LicenseState);
        Assert.NotNull(replacementDevice.LicenseExpiresAtUtc);
        Assert.InRange(
            (replacementActiveLicense.ExpiresAtUtc - replacementDevice.LicenseExpiresAtUtc.Value).Duration(),
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(1));

        var replacementDeviceDetail = await client.GetFromJsonAsync<DeviceDetailResponse>(
            $"/api/v1/tenants/{AuthenticationFlowFixture.TenantId}/devices/{replacementEnrollmentCode.DeviceId}",
            ResponseJsonOptions);
        Assert.Equal(LicenseEffectiveState.Active, replacementDeviceDetail?.License?.State);
        Assert.Equal(replacementActiveLicense.Id, replacementDeviceDetail?.License?.Id);

        var members = await client.GetFromJsonAsync<TenantMemberResponse[]>(
            $"/api/v1/tenants/{AuthenticationFlowFixture.TenantId}/members",
            ResponseJsonOptions);
        Assert.Equal(2, members?.Length);
        var administrator = Assert.Single(members ?? [], value => value.Role == TenantRole.TenantAdmin);
        Assert.Equal(TenantRole.TenantAdmin, administrator.Role);
        using (var selfSuspend = await PostAsync(
            client,
            $"/api/v1/tenants/{AuthenticationFlowFixture.TenantId}/members/{administrator.MembershipId}/suspend",
            new { concurrencyToken = administrator.ConcurrencyToken, reason = "Self lockout attempt" },
            authenticatedSession.CsrfToken))
        {
            Assert.Equal(HttpStatusCode.Conflict, selfSuspend.StatusCode);
        }

        var auditEvents = await client.GetFromJsonAsync<AuditEventResponse[]>(
            $"/api/v1/tenants/{AuthenticationFlowFixture.TenantId}/audit-events");
        Assert.Contains(auditEvents ?? [], value => value.Action == "content.automatically_approved");
        Assert.Contains(auditEvents ?? [], value => value.Action == "assignment.published");
        Assert.Contains(auditEvents ?? [], value => value.Action == "identity.authentication.sign_in");
        Assert.Contains(auditEvents ?? [], value => value.Action == "identity.mfa.enrollment_confirmed");
        Assert.Contains(auditEvents ?? [], value => value.Action == "identity.invitation.created");
        Assert.Contains(auditEvents ?? [], value => value.Action == "identity.invitation.accepted");

        using (var crossTenant = await PostAsync(
            client,
            $"/api/v1/tenants/{Guid.NewGuid()}/invitations",
            new { email = "other@example.test", role = "viewer" },
            authenticatedSession.CsrfToken))
        {
            Assert.Equal(HttpStatusCode.Forbidden, crossTenant.StatusCode);
        }

        _fixture.Advance(TimeSpan.FromMinutes(11));
        using (var staleStepUp = await PostAsync(
            client,
            "/api/v1/auth/mfa/recovery/regenerate",
            new { },
            authenticatedSession.CsrfToken))
        {
            Assert.Equal(HttpStatusCode.Forbidden, staleStepUp.StatusCode);
        }

        var stepUpCode = ComputeTotp(DecodeBase32(enrollment.Secret), _fixture.UtcNow);
        using (var stepUp = await PostAsync(
            client,
            "/api/v1/auth/mfa/step-up",
            new { code = stepUpCode },
            authenticatedSession.CsrfToken))
        {
            stepUp.EnsureSuccessStatusCode();
            var stepUpResult = await stepUp.Content.ReadFromJsonAsync<MfaCompletionResponse>();
            Assert.Equal("recent_mfa_confirmed", stepUpResult?.Status);
        }

        var reauthenticatedSession = await GetSessionAsync(client);
        using (var regenerateRecoveryCodes = await PostAsync(
            client,
            "/api/v1/auth/mfa/recovery/regenerate",
            new { },
            reauthenticatedSession.CsrfToken))
        {
            regenerateRecoveryCodes.EnsureSuccessStatusCode();
            var regenerated = await regenerateRecoveryCodes.Content.ReadFromJsonAsync<MfaCompletionResponse>();
            Assert.Equal(10, regenerated?.RecoveryCodes?.Count);
        }

        using (var signOut = await PostAsync(
            client,
            "/api/v1/auth/sign-out",
            new { },
            reauthenticatedSession.CsrfToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, signOut.StatusCode);
        }

        Assert.False((await GetSessionAsync(client)).Authenticated);

    }

    [Fact]
    public async Task DevelopmentPasswordOnlyModeSignsPrivilegedUserInWithoutMfa()
    {
        using var factory = _fixture.CreatePasswordOnlyFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        var anonymousSession = await GetSessionAsync(client);
        Assert.False(anonymousSession.MfaRequired);

        using (var signIn = await PostAsync(
            client,
            "/api/v1/auth/sign-in",
            new
            {
                email = AuthenticationFlowFixture.AdminEmail,
                password = AuthenticationFlowFixture.AdminPassword
            },
            anonymousSession.CsrfToken))
        {
            Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
            var status = await signIn.Content.ReadFromJsonAsync<AuthenticationStatusResponse>();
            Assert.Equal("authenticated", status?.Status);
        }

        var authenticatedSession = await GetSessionAsync(client);
        Assert.True(authenticatedSession.Authenticated);
        Assert.Equal(SessionClaimTypes.FullStage, authenticatedSession.AuthenticationStage);
        Assert.False(authenticatedSession.MfaSatisfied);
        Assert.False(authenticatedSession.MfaRequired);

        using var privilegedRead = await client.GetAsync(
            $"/api/v1/tenants/{AuthenticationFlowFixture.TenantId}/members");
        privilegedRead.EnsureSuccessStatusCode();
    }

    private static async Task<bool> ReceivesStateChangeAsync(
        DeviceStateChangeBroker.DeviceStateChangeSubscription subscription)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        return await subscription.WaitAsync(timeout.Token);
    }

    [Fact]
    public async Task PlatformBootstrapAndTenantLifecycleUsesRealSqlServer2022()
    {
        using var platformClient = _fixture.CreateClient();
        var platformAnonymousSession = await GetSessionAsync(platformClient);
        Assert.True(_fixture.VerifyPlatformBootstrapToken(AuthenticationFlowFixture.PlatformBootstrapToken));
        using (var rejectedBootstrap = await PostBootstrapAsync(
            platformClient,
            new
            {
                email = AuthenticationFlowFixture.PlatformAdminEmail,
                displayName = "Platform Admin Test",
                password = AuthenticationFlowFixture.PlatformAdminPassword
            },
            platformAnonymousSession.CsrfToken,
            "wrong-bootstrap-token"))
        {
            Assert.Equal(HttpStatusCode.NotFound, rejectedBootstrap.StatusCode);
        }

        using (var bootstrap = await PostBootstrapAsync(
            platformClient,
            new
            {
                email = AuthenticationFlowFixture.PlatformAdminEmail,
                displayName = "Platform Admin Test",
                password = AuthenticationFlowFixture.PlatformAdminPassword
            },
            platformAnonymousSession.CsrfToken,
            AuthenticationFlowFixture.PlatformBootstrapToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, bootstrap.StatusCode);
        }

        using (var platformSignIn = await PostAsync(
            platformClient,
            "/api/v1/auth/sign-in",
            new
            {
                email = AuthenticationFlowFixture.PlatformAdminEmail,
                password = AuthenticationFlowFixture.PlatformAdminPassword
            },
            platformAnonymousSession.CsrfToken))
        {
            Assert.Equal(HttpStatusCode.Accepted, platformSignIn.StatusCode);
        }

        var platformEnrollmentSession = await GetSessionAsync(platformClient);
        Assert.Null(platformEnrollmentSession.TenantId);
        TotpEnrollmentResponse platformEnrollment;
        using (var enrollPlatformMfa = await PostAsync(
            platformClient,
            "/api/v1/auth/mfa/totp/enroll",
            new { },
            platformEnrollmentSession.CsrfToken))
        {
            enrollPlatformMfa.EnsureSuccessStatusCode();
            platformEnrollment = await enrollPlatformMfa.Content.ReadFromJsonAsync<TotpEnrollmentResponse>()
                ?? throw new InvalidOperationException("Platform TOTP enrollment response was empty.");
        }

        using (var confirmPlatformMfa = await PostAsync(
            platformClient,
            "/api/v1/auth/mfa/totp/confirm",
            new { code = ComputeTotp(DecodeBase32(platformEnrollment.Secret), _fixture.UtcNow) },
            platformEnrollmentSession.CsrfToken))
        {
            confirmPlatformMfa.EnsureSuccessStatusCode();
        }

        var platformSession = await GetSessionAsync(platformClient);
        PlatformTenantCreatedResponse createdTenant;
        using (var createTenant = await PostAsync(
            platformClient,
            "/api/v1/platform/tenants",
            new
            {
                name = "Customer B",
                slug = "customer-b",
                timeZone = "Africa/Casablanca",
                initialAdministratorEmail = "customer-admin@example.test"
            },
            platformSession.CsrfToken))
        {
            Assert.Equal(HttpStatusCode.Created, createTenant.StatusCode);
            createdTenant = await createTenant.Content.ReadFromJsonAsync<PlatformTenantCreatedResponse>(ResponseJsonOptions)
                ?? throw new InvalidOperationException("Platform tenant response was empty.");
        }

        Assert.StartsWith($"v1.{createdTenant.Tenant.Id:N}.", createdTenant.InvitationToken, StringComparison.Ordinal);
        var platformTenants = await platformClient.GetFromJsonAsync<PlatformTenantResponse[]>(
            "/api/v1/platform/tenants",
            ResponseJsonOptions);
        Assert.Contains(platformTenants ?? [], value => value.Id == createdTenant.Tenant.Id);

        using (var suspendTenant = await PatchAsync(
            platformClient,
            $"/api/v1/platform/tenants/{createdTenant.Tenant.Id}/state",
            new
            {
                state = "suspended",
                concurrencyToken = createdTenant.Tenant.ConcurrencyToken,
                reason = "Integration test suspension"
            },
            platformSession.CsrfToken))
        {
            suspendTenant.EnsureSuccessStatusCode();
            var suspended = await suspendTenant.Content.ReadFromJsonAsync<PlatformTenantResponse>(ResponseJsonOptions);
            Assert.Equal(TenantState.Suspended, suspended?.State);
        }
    }

    private static async Task<SessionResponse> GetSessionAsync(HttpClient client) =>
        await client.GetFromJsonAsync<SessionResponse>("/api/v1/session")
        ?? throw new InvalidOperationException("Session response was empty.");

    private static Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string path,
        object payload,
        string csrfToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("X-CSRF-TOKEN", csrfToken);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> PutAsync(
        HttpClient client,
        string path,
        object payload,
        string csrfToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, path)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("X-CSRF-TOKEN", csrfToken);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> PatchAsync(
        HttpClient client,
        string path,
        object payload,
        string csrfToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, path)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("X-CSRF-TOKEN", csrfToken);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> PostBootstrapAsync(
        HttpClient client,
        object payload,
        string csrfToken,
        string bootstrapToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/bootstrap/platform-administrator")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("X-CSRF-TOKEN", csrfToken);
        request.Headers.Add("X-Platform-Bootstrap-Token", bootstrapToken);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> PostMultipartAsync(
        HttpClient client,
        string path,
        byte[] fileBytes,
        string csrfToken)
    {
        var multipart = new MultipartFormDataContent
        {
            { new StringContent("Lobby visual"), "Title" },
            { new ByteArrayContent(fileBytes), "File", "lobby.png" }
        };
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = multipart };
        request.Headers.Add("X-CSRF-TOKEN", csrfToken);
        return client.SendAsync(request);
    }

    [SuppressMessage(
        "Security",
        "CA5350:Do Not Use Weak Cryptographic Algorithms",
        Justification = "RFC 6238 interoperability requires keyed HMAC-SHA1 for the configured TOTP profile; this is not a password or unkeyed digest.")]
    private static string ComputeTotp(byte[] secret, DateTimeOffset nowUtc)
    {
        var step = nowUtc.ToUnixTimeSeconds() / 30;
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
        var digest = HMACSHA1.HashData(secret, counter);
        var offset = digest[^1] & 0x0F;
        var binaryCode = BinaryPrimitives.ReadInt32BigEndian(digest.AsSpan(offset, 4)) & 0x7FFFFFFF;
        return (binaryCode % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static byte[] DecodeBase32(string value)
    {
        var output = new List<byte>();
        var buffer = 0;
        var bits = 0;
        foreach (var character in value)
        {
            var index = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".IndexOf(character, StringComparison.Ordinal);
            if (index < 0)
            {
                throw new InvalidOperationException("Enrollment secret was not Base32.");
            }

            buffer = (buffer << 5) | index;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                output.Add((byte)(buffer >> bits));
                buffer &= (1 << bits) - 1;
            }
        }

        return output.ToArray();
    }

    private static JsonSerializerOptions CreateResponseJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "xUnit invokes IAsyncLifetime.DisposeAsync after the fixture completes.")]
public sealed class AuthenticationFlowFixture : IAsyncLifetime
{
    public const string AdminEmail = "admin@example.test";
    public const string AdminPassword = "StrongPassword123";
    public const string PlatformAdminEmail = "platform@example.test";
    public const string PlatformAdminPassword = "ControlPlanePassphrase2026";
    public const string PlatformBootstrapToken = "integration-bootstrap-token-with-strong-entropy";
    public static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private const string RuntimeLogin = "display_control_runtime_test";
    private const string RuntimePassword = "SqlServer_Auth_Runtime_Test_123!";
    private readonly SqlServerTestDatabase _database = new("display_control_auth_tests");
    private WebApplicationFactory<Program>? _factory;
    private readonly TestClientCertificateStore _clientCertificateStore = new();
    private readonly AdjustableTimeProvider _timeProvider = new();
    private readonly DeviceCertificateIssuer _deviceCertificateIssuer =
        DeviceCertificateIssuerFactory.CreateEphemeralForTesting(DateTimeOffset.UtcNow, TimeSpan.FromDays(29));
    private string? _runtimeConnection;

    public DateTimeOffset UtcNow => _timeProvider.GetUtcNow();

    public void Advance(TimeSpan duration) => _timeProvider.Advance(duration);

    public async Task InitializeAsync()
    {
        await _database.StartAsync();
        var ownerOptions = _database.CreateOwnerOptions();
        await using (var context = new DisplayControlDbContext(ownerOptions, NullTenant.Instance))
        {
            await context.Database.MigrateAsync();
            await SeedAdminAsync(context);
        }

        var runtimeConnection = await _database.ProvisionRuntimeLoginAsync(RuntimeLogin, RuntimePassword);
        _runtimeConnection = runtimeConnection;
        _factory = CreateFactory(runtimeConnection, requireMfa: true);
    }

    public void SetDeviceCertificate(string certificatePem)
    {
        var previous = _clientCertificateStore.Certificate;
        _clientCertificateStore.Certificate = X509Certificate2.CreateFromPem(certificatePem);
        previous?.Dispose();
    }

    public HttpClient CreateClient(bool handleCookies = true) => (_factory ?? throw new InvalidOperationException("Fixture is not initialized."))
        .CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = handleCookies,
            AllowAutoRedirect = false
        });

    public WebApplicationFactory<Program> CreatePasswordOnlyFactory() => CreateFactory(
        _runtimeConnection ?? throw new InvalidOperationException("Fixture is not initialized."),
        requireMfa: false);

    private WebApplicationFactory<Program> CreateFactory(string runtimeConnection, bool requireMfa) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Database:Provider", "SqlServer");
            builder.UseSetting("ConnectionStrings:Database", runtimeConnection);
            builder.UseSetting("Security:HumanAuthentication:RequireMfa", requireMfa.ToString());
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Database"] = runtimeConnection,
                    ["Database:Provider"] = "SqlServer",
                    ["Security:DeviceCertificateAuthority:IssuedLifetimeDays"] = "29",
                    ["Security:PlatformBootstrapTokenSha256Base64"] = Convert.ToBase64String(
                        SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(PlatformBootstrapToken)))
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(_timeProvider);
                services.RemoveAll<IDeviceCertificateIssuer>();
                services.AddSingleton<IDeviceCertificateIssuer>(_deviceCertificateIssuer);
                services.RemoveAll<PlatformBootstrapCredential>();
                services.AddSingleton(new PlatformBootstrapCredential(Convert.ToBase64String(
                    SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(PlatformBootstrapToken)))));
                services.AddSingleton<IStartupFilter>(new TestClientCertificateStartupFilter(_clientCertificateStore));
            });
        });

    public DeviceStateChangeBroker.DeviceStateChangeSubscription SubscribeDeviceStateChanges(
        Guid tenantId,
        Guid deviceId) => (_factory ?? throw new InvalidOperationException("Fixture is not initialized."))
        .Services.GetRequiredService<DeviceStateChangeBroker>()
        .Subscribe(tenantId, deviceId);

    public bool VerifyPlatformBootstrapToken(string token) =>
        (_factory ?? throw new InvalidOperationException("Fixture is not initialized."))
            .Services.GetRequiredService<PlatformBootstrapCredential>()
            .Verify(token);

    [SuppressMessage(
        "Security",
        "CA5359:Do Not Disable Certificate Validation",
        Justification = "The loopback integration test pins behavior above a generated ephemeral server certificate; production clients do not disable server validation.")]
    public async Task<RealTlsProbeResult> ProbeRealKestrelMutualTlsAsync(
        string certificatePem,
        ECDsa privateKey)
    {
        var runtimeConnection = _runtimeConnection
            ?? throw new InvalidOperationException("The fixture has not initialized its runtime database.");
        using var serverCertificate = CreateLoopbackServerCertificate();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.ConfigureKestrel(options => options.Listen(
            IPAddress.Loopback,
            0,
            listen => listen.UseHttps(new HttpsConnectionAdapterOptions
            {
                ServerCertificate = serverCertificate,
                ClientCertificateMode = ClientCertificateMode.AllowCertificate,
                ClientCertificateValidation = static (_, _, _) => true
            })));
        builder.Services.AddDbContext<DisplayControlDbContext>(options => options.UseSqlServer(
            runtimeConnection,
            sqlServer => sqlServer.MigrationsAssembly(SqlServerTestDatabase.MigrationsAssembly)));
        builder.Services.AddScoped<ScopedTenantContext>();
        builder.Services.AddScoped<ICurrentTenant>(services => services.GetRequiredService<ScopedTenantContext>());
        builder.Services.AddSingleton<IDeviceCertificateIssuer>(_deviceCertificateIssuer);
        builder.Services.AddSingleton<TimeProvider>(_timeProvider);
        await using var application = builder.Build();
        application.UseMiddleware<DisplayControl.Api.Security.DeviceCertificateAuthenticationMiddleware>();
        application.MapGet("/device/v1/probe", (HttpContext context) => Results.Ok(new
        {
            deviceId = context.User.FindFirstValue(DeviceClaimTypes.DeviceId),
            tenantId = context.User.FindFirstValue(DeviceClaimTypes.TenantId)
        }));
        await application.StartAsync();
        try
        {
            var address = application.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()?.Addresses.Single()
                ?? throw new InvalidOperationException("Kestrel did not publish its loopback address.");
            using var missingHandler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
            using var missingClient = new HttpClient(missingHandler) { BaseAddress = new Uri(address) };
            using var missingResponse = await missingClient.GetAsync("/device/v1/probe");

            using var publicCertificate = X509Certificate2.CreateFromPem(certificatePem);
            using var combinedClientCertificate = publicCertificate.CopyWithPrivateKey(privateKey);
            using var clientCertificate = ReloadWithPlatformKeyStore(combinedClientCertificate);
            using var validHandler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
            validHandler.ClientCertificates.Add(clientCertificate);
            using var validClient = new HttpClient(validHandler) { BaseAddress = new Uri(address) };
            using var validResponse = await validClient.GetAsync("/device/v1/probe");

            using var untrustedKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var untrustedRequest = new CertificateRequest(
                "CN=untrusted-device",
                untrustedKey,
                HashAlgorithmName.SHA256);
            untrustedRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            untrustedRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature,
                true));
            using var createdUntrustedCertificate = untrustedRequest.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddHours(1));
            using var untrustedCertificate = ReloadWithPlatformKeyStore(createdUntrustedCertificate);
            using var untrustedHandler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
            untrustedHandler.ClientCertificates.Add(untrustedCertificate);
            using var untrustedClient = new HttpClient(untrustedHandler) { BaseAddress = new Uri(address) };
            using var untrustedResponse = await untrustedClient.GetAsync("/device/v1/probe");
            return new RealTlsProbeResult(
                validResponse.StatusCode,
                missingResponse.StatusCode,
                untrustedResponse.StatusCode);
        }
        finally
        {
            await application.StopAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        _clientCertificateStore.Certificate?.Dispose();

        await _database.DisposeAsync();
    }

    private static async Task SeedAdminAsync(DisplayControlDbContext context)
    {
        var nowUtc = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
        await using var transaction = await context.BeginTenantTransactionAsync(TenantId);
        var user = new ApplicationUser
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            UserName = AdminEmail,
            NormalizedUserName = AdminEmail.ToUpperInvariant(),
            Email = AdminEmail,
            NormalizedEmail = AdminEmail.ToUpperInvariant(),
            EmailConfirmed = true,
            DisplayName = "Admin Test",
            AccountState = AccountState.Active,
            HomeTenantId = TenantId,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            LastPasswordChangedAtUtc = nowUtc,
            LockoutEnabled = true
        };
        user.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(user, AdminPassword);
        context.Tenants.Add(new Tenant(TenantId, "Tenant A", "tenant-a", "UTC", nowUtc));
        context.Users.Add(user);
        context.TenantMemberships.Add(new TenantMembership(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            TenantId,
            user.Id,
            TenantRole.TenantAdmin,
            user.Id,
            nowUtc));
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static X509Certificate2 CreateLoopbackServerCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=localhost",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new("1.3.6.1.5.5.7.3.1") },
            true));
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        using var created = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1));
        return ReloadWithPlatformKeyStore(created);
    }

    private static X509Certificate2 ReloadWithPlatformKeyStore(X509Certificate2 certificate)
    {
        return X509CertificateLoader.LoadPkcs12(
            certificate.Export(X509ContentType.Pkcs12),
            password: null,
            X509KeyStorageFlags.Exportable);
    }

    private sealed class NullTenant : ICurrentTenant
    {
        public static NullTenant Instance { get; } = new();
        public Guid? TenantId => null;
    }
}

public sealed record RealTlsProbeResult(
    HttpStatusCode ValidCertificateStatus,
    HttpStatusCode MissingCertificateStatus,
    HttpStatusCode UntrustedCertificateStatus);

internal sealed class AdjustableTimeProvider : TimeProvider
{
    private long _offsetTicks;

    public override DateTimeOffset GetUtcNow() =>
        DateTimeOffset.UtcNow.AddTicks(Interlocked.Read(ref _offsetTicks));

    public void Advance(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Advance duration must be positive.");
        }

        Interlocked.Add(ref _offsetTicks, duration.Ticks);
    }
}

internal sealed class TestClientCertificateStore
{
    public X509Certificate2? Certificate { get; set; }
}

internal sealed class TestClientCertificateStartupFilter(TestClientCertificateStore store) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => application =>
    {
        application.Use(async (context, continuation) =>
        {
            if (context.Request.Headers.ContainsKey("X-Test-Client-Certificate") && store.Certificate is not null)
            {
                context.Connection.ClientCertificate = X509CertificateLoader.LoadCertificate(store.Certificate.RawData);
            }

            await continuation(context);
        });
        next(application);
    };
}
