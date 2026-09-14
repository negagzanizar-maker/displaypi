using DisplayControl.Domain.Tenancy;

namespace DisplayControl.Domain.Identity;

public sealed class TenantMembership : TenantOwnedEntity
{
    private TenantMembership()
    {
    }

    public TenantMembership(
        Guid id,
        Guid tenantId,
        Guid userId,
        TenantRole role,
        Guid invitedByUserId,
        DateTimeOffset acceptedAtUtc)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || userId == Guid.Empty || invitedByUserId == Guid.Empty)
        {
            throw new ArgumentException("Membership, tenant, user, and inviter identifiers cannot be empty.");
        }

        EnsureUtc(acceptedAtUtc, nameof(acceptedAtUtc));
        Id = id;
        TenantId = tenantId;
        UserId = userId;
        Role = role;
        State = MembershipState.Active;
        InvitedByUserId = invitedByUserId;
        AcceptedAtUtc = acceptedAtUtc;
        CreatedAtUtc = acceptedAtUtc;
        UpdatedAtUtc = acceptedAtUtc;
        ConcurrencyToken = Guid.NewGuid();
    }

    public Guid UserId { get; private set; }

    public TenantRole Role { get; private set; }

    public MembershipState State { get; private set; }

    public Guid InvitedByUserId { get; private set; }

    public DateTimeOffset AcceptedAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public Guid ConcurrencyToken { get; private set; }

    public void ChangeRole(TenantRole role, DateTimeOffset changedAtUtc)
    {
        EnsureActive();
        EnsureUtc(changedAtUtc, nameof(changedAtUtc));
        Touch(changedAtUtc);
        Role = role;
    }

    public void Suspend(DateTimeOffset suspendedAtUtc)
    {
        EnsureActive();
        EnsureUtc(suspendedAtUtc, nameof(suspendedAtUtc));
        Touch(suspendedAtUtc);
        State = MembershipState.Suspended;
    }

    public void Reactivate(DateTimeOffset reactivatedAtUtc)
    {
        if (State != MembershipState.Suspended)
        {
            throw new InvalidOperationException("Only a suspended membership can be reactivated.");
        }

        EnsureUtc(reactivatedAtUtc, nameof(reactivatedAtUtc));
        Touch(reactivatedAtUtc);
        State = MembershipState.Active;
    }

    public void Remove(DateTimeOffset removedAtUtc)
    {
        if (State == MembershipState.Removed)
        {
            throw new InvalidOperationException("Membership is already removed.");
        }

        EnsureUtc(removedAtUtc, nameof(removedAtUtc));
        Touch(removedAtUtc);
        State = MembershipState.Removed;
    }

    private void EnsureActive()
    {
        if (State != MembershipState.Active)
        {
            throw new InvalidOperationException("Membership must be active.");
        }
    }

    private void Touch(DateTimeOffset changedAtUtc)
    {
        if (changedAtUtc < UpdatedAtUtc)
        {
            throw new ArgumentException("Change timestamp cannot move backwards.", nameof(changedAtUtc));
        }

        UpdatedAtUtc = changedAtUtc;
        ConcurrencyToken = Guid.NewGuid();
    }

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must have a UTC offset.", parameterName);
        }
    }
}
