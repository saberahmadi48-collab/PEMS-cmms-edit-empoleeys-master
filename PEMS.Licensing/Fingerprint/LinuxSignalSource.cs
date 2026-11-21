using PEMS.Licensing.Formats;

namespace PEMS.Licensing.Fingerprint;

/// <summary>
/// Linux and Docker signals (design 3.1, 3.4). <paramref name="root"/> is "/" in production and a temp folder in tests.
/// In a container the host machine-id comes from a read-only bind mount (<paramref name="hostMachineIdPath"/>),
/// and the veth interfaces and the container's own machine-id are ignored.
/// </summary>
public sealed class LinuxSignalSource : ISignalSource
{
    private readonly string _root;
    private readonly string _hostMachineIdPath;
    private readonly bool _container;

    public LinuxSignalSource(string root, string hostMachineIdPath, bool? container = null)
    {
        _root = root;
        _hostMachineIdPath = hostMachineIdPath;
        _container = container ?? DetectContainer(root);
    }

    public string Platform => _container ? "container" : "linux";

    public string Virtualization
    {
        get
        {
            if (_container) return "container";
            var text = (Read("sys/class/dmi/id/sys_vendor") + " " + Read("sys/class/dmi/id/product_name")).ToLowerInvariant();
            if (text.Contains("vmware")) return "vmware";
            if (text.Contains("microsoft") && text.Contains("virtual")) return "hyperv";
            if (text.Contains("qemu") || text.Contains("kvm") || text.Contains("bochs")) return "kvm";
            if (text.Contains("virtualbox") || text.Contains("xen") || text.Contains("innotek")) return "other";
            return (Read("proc/cpuinfo") ?? "").Contains("hypervisor", StringComparison.Ordinal) ? "other" : "none";
        }
    }

    public IReadOnlyList<RawSignal> Collect()
    {
        var list = new List<RawSignal>
        {
            new(SignalCatalog.SmbiosUuid, Read("sys/class/dmi/id/product_uuid")),
            new(SignalCatalog.BoardSerial, Read("sys/class/dmi/id/board_serial")),
            new(SignalCatalog.CpuId, CpuModel()),
            new(SignalCatalog.OsMachineId, MachineId()),
        };

        if (Virtualization is not "none")
            list.Add(new RawSignal(SignalCatalog.HypervisorId,
                string.Join("|", Read("sys/class/dmi/id/sys_vendor"), Read("sys/class/dmi/id/product_name"), Read("sys/class/dmi/id/product_serial"))));

        if (!_container)
        {
            list.Add(new RawSignal(SignalCatalog.DiskSerial, RootDiskSerial()));
            var i = 1;
            foreach (var mac in PhysicalMacs().Take(SignalCatalog.MaxNics))
                list.Add(new RawSignal(SignalCatalog.NicMacPrefix + i++, mac));
        }
        return list;
    }

    private string? MachineId()
    {
        if (_container)
            return ReadAbsolute(_hostMachineIdPath);   // the container's own id changes with the image, so it is not used
        return Read("etc/machine-id") ?? Read("var/lib/dbus/machine-id");
    }

    private string? CpuModel()
    {
        var text = Read("proc/cpuinfo");
        if (text is null) return null;
        var parts = new[] { "model name", "cpu family", "model", "stepping" }
            .Select(k => text.Split('\n').FirstOrDefault(l => l.StartsWith(k, StringComparison.Ordinal) && l.Contains(':'))?.Split(':', 2)[1].Trim())
            .Where(v => v is not null);
        var joined = string.Join("|", parts);
        return joined.Length == 0 ? null : joined;
    }

    private IEnumerable<string> PhysicalMacs()
    {
        var net = Path.Combine(_root, "sys/class/net");
        if (!Directory.Exists(net)) yield break;
        var macs = new List<string>();
        foreach (var dir in Directory.GetDirectories(net).OrderBy(d => d, StringComparer.Ordinal))
        {
            // Physical interfaces have a 'device' entry; bridges, veth, docker0 and tun do not.
            if (!Directory.Exists(Path.Combine(dir, "device")) && !File.Exists(Path.Combine(dir, "device"))) continue;
            var mac = ReadFile(Path.Combine(dir, "address"));
            if (mac is not null) macs.Add(mac);
        }
        foreach (var mac in macs.OrderBy(m => SignalHasher.Hash("nic_mac", m), StringComparer.Ordinal))
            yield return mac;
    }

    private string? RootDiskSerial()
    {
        var mounts = Read("proc/mounts");
        string? device = null;
        if (mounts is not null)
        {
            var line = mounts.Split('\n').FirstOrDefault(l => l.Split(' ').Length > 1 && l.Split(' ')[1] == "/");
            device = line?.Split(' ')[0];
        }
        var name = device is null ? null : Path.GetFileName(device);
        if (name is not null)
        {
            // nvme0n1p2 -> nvme0n1, sda1 -> sda
            var disk = System.Text.RegularExpressions.Regex.Replace(name, @"(?<=nvme\d+n\d+)p\d+$|(?<=[a-z]{2,3})\d+$", "");
            var serial = Read($"sys/block/{disk}/device/serial");
            if (serial is not null) return serial;
        }
        var blockDir = Path.Combine(_root, "sys/block");
        if (!Directory.Exists(blockDir)) return null;
        foreach (var d in Directory.GetDirectories(blockDir).OrderBy(x => x, StringComparer.Ordinal))
        {
            var n = Path.GetFileName(d);
            if (n.StartsWith("loop", StringComparison.Ordinal) || n.StartsWith("ram", StringComparison.Ordinal) || n.StartsWith("dm-", StringComparison.Ordinal)) continue;
            var serial = ReadFile(Path.Combine(d, "device", "serial"));
            if (serial is not null) return serial;
        }
        return null;
    }

    private static bool DetectContainer(string root)
    {
        if (File.Exists(Path.Combine(root, ".dockerenv"))) return true;
        if (string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase)) return true;
        var cgroup = ReadFile(Path.Combine(root, "proc/1/cgroup"));
        return cgroup is not null && (cgroup.Contains("docker") || cgroup.Contains("kubepods") || cgroup.Contains("containerd"));
    }

    private string? Read(string relative) => ReadFile(Path.Combine(_root, relative));

    private static string? ReadAbsolute(string path) => ReadFile(path);

    private static string? ReadFile(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path).Trim() : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }   // unreadable = absent (design 3.2)
    }
}
