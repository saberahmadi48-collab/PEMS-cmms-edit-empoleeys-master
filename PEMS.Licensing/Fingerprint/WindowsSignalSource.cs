using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace PEMS.Licensing.Fingerprint;

/// <summary>Windows signals (design 3.1): SMBIOS through WMI, MachineGuid from the registry, physical NICs only. Each signal fails independently.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSignalSource : ISignalSource
{
    private static readonly string[] VirtualNicWords =
        { "virtual", "vmware", "hyper-v", "vethernet", "vpn", "tap-", "tunnel", "loopback", "bluetooth", "docker", "wsl", "pseudo" };

    public string Platform => "windows";

    public string Virtualization
    {
        get
        {
            var model = (Wmi("SELECT Model, Manufacturer FROM Win32_ComputerSystem", "Model") + " "
                + Wmi("SELECT Model, Manufacturer FROM Win32_ComputerSystem", "Manufacturer")).ToLowerInvariant();
            if (model.Contains("vmware")) return "vmware";
            if (model.Contains("virtual machine") || model.Contains("hyper-v")) return "hyperv";
            if (model.Contains("kvm") || model.Contains("qemu")) return "kvm";
            if (model.Contains("virtualbox") || model.Contains("xen")) return "other";
            return "none";
        }
    }

    public IReadOnlyList<RawSignal> Collect()
    {
        var list = new List<RawSignal>
        {
            new(SignalCatalog.SmbiosUuid, Wmi("SELECT UUID FROM Win32_ComputerSystemProduct", "UUID")),
            new(SignalCatalog.BoardSerial, Wmi("SELECT SerialNumber FROM Win32_BaseBoard", "SerialNumber")),
            new(SignalCatalog.CpuId, Wmi("SELECT ProcessorId FROM Win32_Processor", "ProcessorId")),
            new(SignalCatalog.DiskSerial, SystemDiskSerial()),
            new(SignalCatalog.OsMachineId, MachineGuid()),
        };
        if (Virtualization != "none")
            list.Add(new RawSignal(SignalCatalog.HypervisorId,
                Wmi("SELECT Manufacturer, Model FROM Win32_ComputerSystem", "Manufacturer") + "|" + Wmi("SELECT Manufacturer, Model FROM Win32_ComputerSystem", "Model")
                + "|" + Wmi("SELECT UUID FROM Win32_ComputerSystemProduct", "UUID")));
        var i = 1;
        foreach (var mac in PhysicalMacs().Take(SignalCatalog.MaxNics))
            list.Add(new RawSignal(SignalCatalog.NicMacPrefix + i++, mac));
        return list;
    }

    private static string? MachineGuid()
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return key?.GetValue("MachineGuid") as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return null; }
    }

    private static IEnumerable<string> PhysicalMacs()
        => NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.Wireless80211)
            .Where(n => !VirtualNicWords.Any(w => (n.Description + " " + n.Name).Contains(w, StringComparison.OrdinalIgnoreCase)))
            .Select(n => n.GetPhysicalAddress().ToString())
            .Where(a => a.Length > 0)
            .OrderBy(a => SignalHasher.Hash("nic_mac", a), StringComparer.Ordinal);

    /// <summary>Serial of the physical disk holding the system volume: logical disk -> partition -> drive.</summary>
    private static string? SystemDiskSerial()
    {
        try
        {
            var drive = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").TrimEnd('\\');
            foreach (var partition in Related($"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{drive}'}} WHERE AssocClass=Win32_LogicalDiskToPartition"))
            {
                var id = partition["DeviceID"]?.ToString();
                if (id is null) continue;
                foreach (var disk in Related($"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{id}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition"))
                {
                    var serial = disk["SerialNumber"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(serial)) return serial;
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { }
        return null;
    }

    private static IEnumerable<ManagementBaseObject> Related(string query)
    {
        using var searcher = new ManagementObjectSearcher(query);
        foreach (var o in searcher.Get()) yield return o;
    }

    private static string? Wmi(string query, string property)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            foreach (var o in searcher.Get())
            {
                var v = o[property]?.ToString();
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { }
        return null;
    }
}
