namespace DisplayControl.DeviceAgent;

public sealed class AgentRuntimeOptions
{
    public const string SectionName = "Agent";

    public required Uri ServerBaseAddress { get; init; }

    public int HeartbeatIntervalSeconds { get; init; } = 30;

    public required string StateDirectory { get; init; }

    public string? EnrollmentCode { get; init; }

    public string? EnrollmentCodeFile { get; init; }

    public string? DevelopmentSerialNumber { get; init; }

    public bool CheckServerCertificateRevocation { get; init; } = true;

    public long MaximumCacheBytes { get; init; } = 4_294_967_296;

    public long MinimumFreeDiskBytes { get; init; } = 268_435_456;

    public static bool IsValid(AgentRuntimeOptions options) =>
        options.ServerBaseAddress.IsAbsoluteUri &&
        options.ServerBaseAddress.Scheme == Uri.UriSchemeHttps &&
        options.HeartbeatIntervalSeconds is >= 10 and <= 300 &&
        Path.IsPathFullyQualified(options.StateDirectory) &&
        options.MaximumCacheBytes is >= 67_108_864 and <= 68_719_476_736 &&
        options.MinimumFreeDiskBytes is >= 16_777_216 and <= 17_179_869_184 &&
        options.MinimumFreeDiskBytes < options.MaximumCacheBytes &&
        (options.EnrollmentCode is null || options.EnrollmentCode.Length is >= 32 and <= 160) &&
        (options.EnrollmentCodeFile is null || Path.IsPathFullyQualified(options.EnrollmentCodeFile)) &&
        !(options.EnrollmentCode is not null && options.EnrollmentCodeFile is not null) &&
        (options.DevelopmentSerialNumber is null || options.DevelopmentSerialNumber.Length is >= 1 and <= 32);
}
