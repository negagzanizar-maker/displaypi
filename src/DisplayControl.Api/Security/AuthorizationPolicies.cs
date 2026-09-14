namespace DisplayControl.Api.Security;

public static class AuthorizationPolicies
{
    public const string AuthenticatedSession = "authenticated-session";
    public const string FullSession = "full-session";
    public const string MfaPending = "mfa-pending";
    public const string MfaEnrollment = "mfa-enrollment";
    public const string MfaVerifiedSession = "mfa-verified-session";
    public const string RecentMfaSession = "recent-mfa-session";
    public const string TenantViewer = "tenant-viewer";
    public const string TenantContentManager = "tenant-content-manager";
    public const string TenantAdministrator = "tenant-administrator";
    public const string TenantAdministratorRecentMfa = "tenant-administrator-recent-mfa";
    public const string PlatformAdministrator = "platform-administrator";
    public const string PlatformAdministratorRecentMfa = "platform-administrator-recent-mfa";
    public const string DeviceAuthenticated = "device-authenticated";
}
