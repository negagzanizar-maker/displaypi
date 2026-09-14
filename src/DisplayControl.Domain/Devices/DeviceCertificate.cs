using DisplayControl.Domain.Tenancy;

namespace DisplayControl.Domain.Devices;

public sealed class DeviceCertificate : TenantOwnedEntity
{
    private DeviceCertificate()
    {
    }

    public DeviceCertificate(
        Guid id,
        Guid tenantId,
        Guid deviceId,
        string certificateSerialNumber,
        byte[] thumbprintSha256,
        byte[] subjectPublicKeyInfoSha256,
        byte[] certificateDer,
        DateTimeOffset notBeforeUtc,
        DateTimeOffset notAfterUtc,
        DateTimeOffset issuedAtUtc,
        Guid? rotatedFromCertificateId = null)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || deviceId == Guid.Empty)
        {
            throw new ArgumentException("Certificate, tenant, and device identifiers cannot be empty.");
        }

        if (rotatedFromCertificateId == Guid.Empty)
        {
            throw new ArgumentException("A rotation source identifier cannot be empty.", nameof(rotatedFromCertificateId));
        }

        EnsureUtc(notBeforeUtc, nameof(notBeforeUtc));
        EnsureUtc(notAfterUtc, nameof(notAfterUtc));
        EnsureUtc(issuedAtUtc, nameof(issuedAtUtc));
        if (notAfterUtc <= notBeforeUtc || issuedAtUtc < notBeforeUtc || issuedAtUtc >= notAfterUtc)
        {
            throw new ArgumentException("Certificate validity and issuance timestamps are inconsistent.");
        }

        var normalizedSerial = certificateSerialNumber.Trim().ToUpperInvariant();
        if (normalizedSerial.Length is 0 or > 128)
        {
            throw new ArgumentException("Certificate serial number must contain 1-128 characters.", nameof(certificateSerialNumber));
        }

        EnsureSha256(thumbprintSha256, nameof(thumbprintSha256));
        EnsureSha256(subjectPublicKeyInfoSha256, nameof(subjectPublicKeyInfoSha256));
        ArgumentNullException.ThrowIfNull(certificateDer);
        if (certificateDer.Length is < 100 or > 16_384)
        {
            throw new ArgumentException("Certificate DER length is invalid.", nameof(certificateDer));
        }

        Id = id;
        TenantId = tenantId;
        DeviceId = deviceId;
        CertificateSerialNumber = normalizedSerial;
        ThumbprintSha256 = thumbprintSha256.ToArray();
        SubjectPublicKeyInfoSha256 = subjectPublicKeyInfoSha256.ToArray();
        CertificateDer = certificateDer.ToArray();
        NotBeforeUtc = notBeforeUtc;
        NotAfterUtc = notAfterUtc;
        State = DeviceCertificateState.Active;
        IssuedAtUtc = issuedAtUtc;
        RotatedFromCertificateId = rotatedFromCertificateId;
    }

    public Guid DeviceId { get; private set; }

    public string CertificateSerialNumber { get; private set; } = string.Empty;

    public byte[] ThumbprintSha256 { get; private set; } = [];

    public byte[] SubjectPublicKeyInfoSha256 { get; private set; } = [];

    public byte[]? CertificateDer { get; private set; }

    public DateTimeOffset NotBeforeUtc { get; private set; }

    public DateTimeOffset NotAfterUtc { get; private set; }

    public DeviceCertificateState State { get; private set; }

    public DateTimeOffset IssuedAtUtc { get; private set; }

    public Guid? RotatedFromCertificateId { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public string? RevocationReasonCode { get; private set; }

    public void Revoke(string reasonCode, DateTimeOffset revokedAtUtc)
    {
        if (State == DeviceCertificateState.Revoked)
        {
            return;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        var normalizedReason = reasonCode.Trim();
        if (normalizedReason.Length > 64)
        {
            throw new ArgumentException("Revocation reason cannot exceed 64 characters.", nameof(reasonCode));
        }

        EnsureUtc(revokedAtUtc, nameof(revokedAtUtc));
        State = DeviceCertificateState.Revoked;
        RevokedAtUtc = revokedAtUtc;
        RevocationReasonCode = normalizedReason;
    }

    public void LimitValidity(DateTimeOffset notAfterUtc)
    {
        EnsureUtc(notAfterUtc, nameof(notAfterUtc));
        if (State != DeviceCertificateState.Active || notAfterUtc <= NotBeforeUtc)
        {
            throw new InvalidOperationException("Only an active certificate can receive a valid shortened boundary.");
        }

        if (notAfterUtc < NotAfterUtc)
        {
            NotAfterUtc = notAfterUtc;
        }
    }

    private static void EnsureSha256(byte[] value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value.Length != 32)
        {
            throw new ArgumentException("SHA-256 values must contain exactly 32 bytes.", parameterName);
        }
    }

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must have a UTC offset.", parameterName);
        }
    }
}
