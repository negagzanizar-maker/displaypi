namespace DisplayControl.Application.Security;

public static class SessionClaimTypes
{
    public const string SessionKey = "display_control:session_key";
    public const string TenantId = "display_control:tenant_id";
    public const string TenantRole = "display_control:tenant_role";
    public const string AuthenticationStage = "display_control:authentication_stage";
    public const string AuthenticationMethod = "amr";
    public const string DisplayName = "display_control:display_name";

    public const string FullStage = "full";
    public const string MfaPendingStage = "mfa_pending";
    public const string MfaEnrollmentStage = "mfa_enrollment";
}
