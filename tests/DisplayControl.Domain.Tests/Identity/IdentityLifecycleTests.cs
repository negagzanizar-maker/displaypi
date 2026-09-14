using DisplayControl.Domain.Identity;

namespace DisplayControl.Domain.Tests.Identity;

public sealed class IdentityLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void InvitationIsEmailBoundAndOneUse()
    {
        var invitation = new Invitation(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "ADMIN@EXAMPLE.TEST",
            TenantRole.TenantAdmin,
            new byte[32],
            Now.AddHours(1),
            Guid.NewGuid(),
            Now);

        Assert.Throws<InvalidOperationException>(() =>
            invitation.Consume(Guid.NewGuid(), "OTHER@EXAMPLE.TEST", Now.AddMinutes(1)));

        var userId = Guid.NewGuid();
        invitation.Consume(userId, "ADMIN@EXAMPLE.TEST", Now.AddMinutes(1));

        Assert.Equal(userId, invitation.ConsumedByUserId);
        Assert.False(invitation.CanBeConsumedAt(Now.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => invitation.Revoke(Now.AddMinutes(2)));
    }

    [Fact]
    public void InvitationCannotBeConsumedBeforeCreationOrAtExpiry()
    {
        var invitation = CreateInvitation();

        Assert.False(invitation.CanBeConsumedAt(Now.AddTicks(-1)));
        Assert.False(invitation.CanBeConsumedAt(Now.AddHours(1)));
    }

    [Fact]
    public void MembershipLifecycleRejectsInvalidTransitions()
    {
        var membership = new TenantMembership(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            TenantRole.Viewer,
            Guid.NewGuid(),
            Now);

        membership.ChangeRole(TenantRole.ContentManager, Now.AddMinutes(1));
        membership.Suspend(Now.AddMinutes(2));
        Assert.Equal(TenantRole.ContentManager, membership.Role);
        Assert.Equal(MembershipState.Suspended, membership.State);
        Assert.Throws<InvalidOperationException>(() =>
            membership.ChangeRole(TenantRole.TenantAdmin, Now.AddMinutes(3)));

        membership.Reactivate(Now.AddMinutes(3));
        membership.Remove(Now.AddMinutes(4));
        Assert.Throws<InvalidOperationException>(() => membership.Remove(Now.AddMinutes(5)));
    }

    [Fact]
    public void SessionIdleExtensionNeverExceedsAbsoluteExpiry()
    {
        var session = CreateSession(idleLifetime: TimeSpan.FromMinutes(30), absoluteLifetime: TimeSpan.FromHours(2));

        session.Touch(Now.AddMinutes(20), TimeSpan.FromHours(2));

        Assert.Equal(Now.AddHours(2), session.IdleExpiresAtUtc);
        Assert.False(session.IsValidAt(Now.AddHours(2)));
    }

    [Fact]
    public void RevokedSessionCannotSatisfyMfa()
    {
        var session = CreateSession(TimeSpan.FromMinutes(30), TimeSpan.FromHours(2));
        session.Revoke("user_sign_out", Now.AddMinutes(1));

        Assert.False(session.IsValidAt(Now.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => session.MarkMfaSatisfied(Now.AddMinutes(2)));
    }

    [Fact]
    public void SessionRequiresMfaNoOlderThanTheConfiguredStepUpWindow()
    {
        var session = CreateSession(TimeSpan.FromMinutes(30), TimeSpan.FromHours(2));
        session.MarkMfaSatisfied(Now.AddMinutes(1));

        Assert.False(session.HasRecentMfaAt(Now, TimeSpan.FromMinutes(10)));
        Assert.True(session.HasRecentMfaAt(Now.AddMinutes(11), TimeSpan.FromMinutes(10)));
        Assert.False(session.HasRecentMfaAt(Now.AddMinutes(11).AddTicks(1), TimeSpan.FromMinutes(10)));
        Assert.Throws<InvalidOperationException>(() => session.MarkMfaSatisfied(Now.AddMinutes(1)));

        session.MarkMfaSatisfied(Now.AddMinutes(12));
        Assert.True(session.HasRecentMfaAt(Now.AddMinutes(22), TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void MfaSecretAndRecoveryCodeAreOneTimeLifecycleRecords()
    {
        var userId = Guid.NewGuid();
        var secret = new UserMfaSecret(userId, [1, 2, 3], "aspnet-data-protection-v1", Now);
        var recoveryCode = new UserRecoveryCode(Guid.NewGuid(), userId, new byte[32], Now);

        secret.Confirm(Now.AddMinutes(1));
        secret.AcceptTimeStep(42, Now.AddMinutes(1));
        recoveryCode.MarkUsed(Now.AddMinutes(1));

        Assert.NotNull(secret.ConfirmedAtUtc);
        Assert.NotNull(recoveryCode.UsedAtUtc);
        Assert.Throws<InvalidOperationException>(() => secret.Confirm(Now.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => secret.AcceptTimeStep(42, Now.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => recoveryCode.MarkUsed(Now.AddMinutes(2)));
    }

    [Fact]
    public void IdentityNotificationCopiesProtectedPayloadAndNeverStoresRawTokenFields()
    {
        var protectedPayload = new byte[] { 1, 2, 3 };
        var notification = new IdentityNotification(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "PasswordResetRequested",
            "USER@EXAMPLE.TEST",
            "protection-v1",
            protectedPayload,
            Now);

        protectedPayload[0] = 9;

        Assert.Equal(1, notification.ProtectedPayload[0]);
        Assert.Equal("protection-v1", notification.ProtectionScheme);
        Assert.Equal(Now, notification.NextAttemptAtUtc);
    }

    [Fact]
    public void MfaRotationInvalidatesOldCredentialWithoutExtendingSessionLifetime()
    {
        var session = CreateSession(TimeSpan.FromMinutes(30), TimeSpan.FromHours(8));
        var originalDigest = session.SessionKeyDigest.ToArray();
        var originalConcurrencyToken = session.ConcurrencyToken;
        var replacement = Enumerable.Repeat((byte)7, 32).ToArray();

        session.RotateAfterMfa(replacement, Now.AddMinutes(1));
        replacement[0] = 9;

        Assert.False(originalDigest.AsSpan().SequenceEqual(session.SessionKeyDigest));
        Assert.Equal(7, session.SessionKeyDigest[0]);
        Assert.NotEqual(originalConcurrencyToken, session.ConcurrencyToken);
        Assert.Equal(Now.AddMinutes(30), session.IdleExpiresAtUtc);
        Assert.Equal(Now.AddHours(8), session.AbsoluteExpiresAtUtc);
        Assert.True(session.HasRecentMfaAt(Now.AddMinutes(2), TimeSpan.FromMinutes(10)));
        Assert.Throws<ArgumentException>(() => session.RotateAfterMfa(session.SessionKeyDigest, Now.AddMinutes(2)));
        session.Revoke("test", Now.AddMinutes(2));
        Assert.Throws<InvalidOperationException>(() => session.RotateAfterMfa(new byte[32], Now.AddMinutes(3)));
        Assert.Equal(7, session.SessionKeyDigest[0]);
    }

    private static Invitation CreateInvitation() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "USER@EXAMPLE.TEST",
        TenantRole.Viewer,
        new byte[32],
        Now.AddHours(1),
        Guid.NewGuid(),
        Now);

    private static UserSession CreateSession(TimeSpan idleLifetime, TimeSpan absoluteLifetime) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        new byte[32],
        new byte[32],
        Now,
        idleLifetime,
        absoluteLifetime);
}
