namespace DisplayControl.Application.Security;

public readonly record struct TotpVerificationResult(bool IsValid, long? TimeStep);
