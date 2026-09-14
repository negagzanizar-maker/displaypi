namespace DisplayControl.Domain.Devices;

public enum DeviceCertificateState
{
    Active = 0,
    Superseded = 1,
    Revoked = 2,
    Expired = 3
}
