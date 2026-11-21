namespace PEMS.Licensing;

/// <summary>
/// Plain settings object; the web project fills it from env/*.env (<c>Licensing__*</c>, read with Required*).
/// <see cref="Enforce"/> is false by default everywhere until the follow-up rollout tasks switch it on.
/// </summary>
public sealed record LicensingSettings(
    bool Enforce,
    string SealDirectory,
    int PeriodicCheckHours,
    int HeartbeatHours,
    int ClockToleranceMinutes,
    int WarningDays,
    int RestoreGraceHours,
    string HostMachineIdPath,
    string ProductVersion,
    string BuildDate)
{
    public TimeSpan ClockTolerance => TimeSpan.FromMinutes(ClockToleranceMinutes);
    public TimeSpan Heartbeat => TimeSpan.FromHours(HeartbeatHours);
    public TimeSpan RestoreGrace => TimeSpan.FromHours(RestoreGraceHours);

    /// <summary>"auto" selects the platform default (design 4.2); anything else is an explicit directory (Docker volume).</summary>
    public string ResolveSealDirectory()
    {
        if (!string.Equals(SealDirectory, "auto", StringComparison.OrdinalIgnoreCase)) return SealDirectory;
        return OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PEMS", "seal")
            : "/var/lib/pems/seal";
    }
}
