using PEMS.Licensing.Fingerprint;
using PEMS.Licensing.Formats;

namespace PEMS.Tests.Licensing;

public class FingerprintTests
{
    private static FingerprintInfo Collect(FakeSignals s) => FingerprintCollector.Collect(s);

    [Fact]
    public void Same_machine_after_reboot_matches()
    {
        var enrolled = Collect(new FakeSignals());
        Assert.Equal(FingerprintResult.Match, FingerprintComparer.Compare(enrolled, Collect(new FakeSignals())).Result);
    }

    [Fact]
    public void One_replaced_nic_is_tolerated()
    {
        var enrolled = Collect(new FakeSignals());
        var s = new FakeSignals();
        s.Values[SignalCatalog.NicMacPrefix + "2"] = "00:00:5e:00:53:77";
        var m = FingerprintComparer.Compare(enrolled, Collect(s));
        Assert.Equal(FingerprintResult.Tolerated, m.Result);
        Assert.Single(m.ChangedSignals);
    }

    [Fact]
    public void One_replaced_disk_is_tolerated()
    {
        var enrolled = Collect(new FakeSignals());
        var s = new FakeSignals();
        s.Values[SignalCatalog.DiskSerial] = "FAKE-DISK-NEW";
        Assert.Equal(FingerprintResult.Tolerated, FingerprintComparer.Compare(enrolled, Collect(s)).Result);
    }

    [Fact]
    public void Two_changed_signals_are_degraded_and_three_invalidate()
    {
        var enrolled = Collect(new FakeSignals());
        var two = new FakeSignals();
        two.Values[SignalCatalog.NicMacPrefix + "1"] = "00:00:5e:00:53:81";
        two.Values[SignalCatalog.DiskSerial] = "FAKE-DISK-NEW";
        Assert.Equal(FingerprintResult.Degraded, FingerprintComparer.Compare(enrolled, Collect(two)).Result);

        var three = new FakeSignals();
        three.Values[SignalCatalog.NicMacPrefix + "1"] = "00:00:5e:00:53:81";
        three.Values[SignalCatalog.DiskSerial] = "FAKE-DISK-NEW";
        three.Values[SignalCatalog.CpuId] = "FAKE-CPU-NEW";
        Assert.Equal(FingerprintResult.Invalid, FingerprintComparer.Compare(enrolled, Collect(three)).Result);
    }

    [Fact]
    public void Another_machine_is_invalid()
        => Assert.Equal(FingerprintResult.Invalid, FingerprintComparer.Compare(Collect(new FakeSignals()), Collect(FakeSignals.OtherMachine())).Result);

    [Fact]
    public void A_changed_system_board_alone_cannot_pass_by_weight()
    {
        // board uuid + serial changed: two S1 signals gone, only NICs/disk/machine-id left -> no S1 anchor.
        var enrolled = Collect(new FakeSignals());
        var s = new FakeSignals();
        s.Values[SignalCatalog.SmbiosUuid] = "99999999-aaaa-bbbb-cccc-dddddddddddd";
        s.Values[SignalCatalog.BoardSerial] = "FAKE-BOARD-NEW";
        Assert.Equal(FingerprintResult.Invalid, FingerprintComparer.Compare(enrolled, Collect(s)).Result);
    }

    [Fact]
    public void Unavailable_at_enrolment_is_ignored_but_unreadable_now_counts_as_changed()
    {
        var withoutBoard = new FakeSignals();
        withoutBoard.Values[SignalCatalog.BoardSerial] = "To be filled by O.E.M.";   // placeholder = unavailable
        var enrolled = Collect(withoutBoard);
        Assert.DoesNotContain(enrolled.Signals, x => x.Id == SignalCatalog.BoardSerial);
        Assert.Equal(FingerprintResult.Match, FingerprintComparer.Compare(enrolled, Collect(new FakeSignals())).Result);

        var unreadable = new FakeSignals();
        unreadable.Values[SignalCatalog.DiskSerial] = null;
        Assert.Equal(FingerprintResult.Tolerated, FingerprintComparer.Compare(Collect(new FakeSignals()), Collect(unreadable)).Result);
    }

    [Fact]
    public void Output_is_hashed_and_never_contains_raw_values()
    {
        var info = Collect(new FakeSignals());
        var json = System.Text.Encoding.UTF8.GetString(LicenseJson.ToBytes(info));
        foreach (var raw in new FakeSignals().Values.Values)
            Assert.DoesNotContain(raw!, json);
        Assert.All(info.Signals, s => Assert.Equal(22, s.H.Length));   // 16 bytes, base64url
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("  ")] [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF")] [InlineData("Default string")] [InlineData("None")]
    public void Placeholders_are_discarded(string? raw) => Assert.Null(SignalHasher.Normalize(raw));

    [Fact]
    public void Fingerprint_hash_is_order_independent()
    {
        var a = new List<SignalEntry> { new("a", "1"), new("b", "2") };
        var b = new List<SignalEntry> { new("b", "2"), new("a", "1") };
        Assert.Equal(SignalHasher.FingerprintHash(a), SignalHasher.FingerprintHash(b));
    }

    // ---- Linux and Docker source, against a fake file tree

    private static string FakeRoot(bool withNic = true)
    {
        var root = Path.Combine(Path.GetTempPath(), "pems-root-" + Guid.NewGuid().ToString("N"));
        void W(string rel, string text) { var p = Path.Combine(root, rel); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllText(p, text); }
        W("sys/class/dmi/id/product_uuid", "4c4c4544-0042-5a10-8030-b4c04f4d3733\n");
        W("sys/class/dmi/id/board_serial", "FAKE-BOARD-0001\n");
        W("sys/class/dmi/id/sys_vendor", "Fake Vendor\n");
        W("sys/class/dmi/id/product_name", "Fake Server\n");
        W("etc/machine-id", "00000000000000000000fakemachine\n");
        W("proc/cpuinfo", "model name\t: Fake CPU\ncpu family\t: 6\nmodel\t\t: 85\nstepping\t: 7\n");
        W("proc/mounts", "/dev/nvme0n1p2 / ext4 rw 0 0\n");
        W("sys/block/nvme0n1/device/serial", "FAKE-NVME-0001\n");
        if (withNic)
        {
            W("sys/class/net/eth0/address", "00:00:5e:00:53:01\n");
            W("sys/class/net/eth0/device/vendor", "0x8086");
            W("sys/class/net/docker0/address", "02:42:00:00:00:01\n");   // virtual: no 'device' entry
            W("sys/class/net/lo/address", "00:00:00:00:00:00\n");
        }
        return root;
    }

    [Fact]
    public void Linux_source_reads_physical_signals_and_skips_virtual_interfaces()
    {
        var root = FakeRoot();
        try
        {
            var source = new LinuxSignalSource(root, "/host/etc/machine-id", container: false);
            var signals = source.Collect().ToDictionary(s => s.Id, s => s.Value);
            Assert.Equal("linux", source.Platform);
            Assert.Equal("none", source.Virtualization);
            Assert.Equal("FAKE-NVME-0001", signals[SignalCatalog.DiskSerial]);
            Assert.Equal("00:00:5e:00:53:01", signals[SignalCatalog.NicMacPrefix + "1"]);
            Assert.False(signals.ContainsKey(SignalCatalog.NicMacPrefix + "2"));   // docker0 and lo are not physical
            Assert.Contains("Fake CPU", signals[SignalCatalog.CpuId]);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Container_source_uses_the_host_machine_id_and_ignores_nics_and_disks()
    {
        var root = FakeRoot();
        var hostId = Path.Combine(root, "host-machine-id");
        File.WriteAllText(hostId, "hosthosthosthosthosthosthost0001\n");
        try
        {
            var source = new LinuxSignalSource(root, hostId, container: true);
            var signals = source.Collect().ToDictionary(s => s.Id, s => s.Value);
            Assert.Equal("container", source.Platform);
            Assert.Equal("container", source.Virtualization);
            Assert.Equal("hosthosthosthosthosthosthost0001", signals[SignalCatalog.OsMachineId]);
            Assert.DoesNotContain(signals.Keys, k => k.StartsWith(SignalCatalog.NicMacPrefix) || k == SignalCatalog.DiskSerial);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Missing_files_are_absent_signals_not_errors()
    {
        var empty = Path.Combine(Path.GetTempPath(), "pems-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try
        {
            var info = FingerprintCollector.Collect(new LinuxSignalSource(empty, "/nowhere", container: false));
            Assert.Empty(info.Signals);
        }
        finally { Directory.Delete(empty, true); }
    }
}
