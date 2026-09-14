namespace DisplayControl.Application.Security;

public static class DeviceClaimTypes
{
    public const string TenantId = "display_control:device_tenant_id";
    public const string DeviceId = "display_control:device_id";
    public const string CertificateId = "display_control:device_certificate_id";
    public const string AuthenticationMethod = "amr";
    public const string MutualTlsAuthenticationMethod = "mtls";
    public const string AuthenticationType = "DeviceCertificate";
}
