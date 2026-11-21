namespace PEMS.Licensing.Fingerprint;

/// <summary>Stability classes of design 3.1: S1 hardware identity, S2 changes with OS reinstall, S3 changes with a component.</summary>
public enum SignalClass { S1, S2, S3 }

public sealed record SignalSpec(string Id, int Weight, SignalClass Class);

/// <summary>Signal ids, weights and classes. Constants of the library, never read from a licence, so a licence cannot loosen the rule (design 3.2).</summary>
public static class SignalCatalog
{
    public const int Scheme = 1;

    public const string SmbiosUuid = "smbios_uuid";
    public const string BoardSerial = "board_serial";
    public const string CpuId = "cpu_id";
    public const string DiskSerial = "disk_serial";
    public const string NicMacPrefix = "nic_mac_";
    public const string OsMachineId = "os_machine_id";
    public const string TpmEk = "tpm_ek";
    public const string HypervisorId = "hypervisor_id";

    public const int MaxNics = 2;

    public static SignalSpec Spec(string id) => id switch
    {
        SmbiosUuid => new(id, 3, SignalClass.S1),
        BoardSerial => new(id, 2, SignalClass.S1),
        CpuId => new(id, 1, SignalClass.S3),
        DiskSerial => new(id, 2, SignalClass.S3),
        OsMachineId => new(id, 2, SignalClass.S2),
        TpmEk => new(id, 3, SignalClass.S1),
        HypervisorId => new(id, 2, SignalClass.S2),
        _ when id.StartsWith(NicMacPrefix, StringComparison.Ordinal) => new(id, 1, SignalClass.S3),
        _ => new(id, 1, SignalClass.S3),
    };
}
