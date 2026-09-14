namespace DisplayControl.Domain.Devices;

public enum DeviceLifecycleState
{
    PendingEnrollment,
    Active,
    Suspended,
    Quarantined,
    Retired
}
