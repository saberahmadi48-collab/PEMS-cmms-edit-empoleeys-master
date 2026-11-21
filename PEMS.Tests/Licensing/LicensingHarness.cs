extern alias VendorTool;
using PEMS.Licensing;
using PEMS.Licensing.Crypto;
using PEMS.Licensing.Evaluation;
using PEMS.Licensing.Fingerprint;
using PEMS.Licensing.Formats;
using PEMS.Licensing.Seal;
using VendorTool::PemsVendorTool.Core;

namespace PEMS.Tests.Licensing;

public sealed class ManualTime : TimeProvider
{
    private DateTimeOffset _now;
    public ManualTime(DateTimeOffset now) => _now = now;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Set(DateTimeOffset now) => _now = now;
    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>Signals a test can change: replace a NIC, a disk, or the whole machine.</summary>
public sealed class FakeSignals : ISignalSource
{
    public Dictionary<string, string?> Values { get; } = new()
    {
        [SignalCatalog.SmbiosUuid] = "4c4c4544-0042-5a10-8030-b4c04f4d3733",
        [SignalCatalog.BoardSerial] = "FAKE-BOARD-0001",
        [SignalCatalog.CpuId] = "FAKE-CPU-0001",
        [SignalCatalog.DiskSerial] = "FAKE-DISK-0001",
        [SignalCatalog.OsMachineId] = "fake-machine-id-0001",
        [SignalCatalog.NicMacPrefix + "1"] = "00:00:5e:00:53:01",
        [SignalCatalog.NicMacPrefix + "2"] = "00:00:5e:00:53:02",
    };
    public string Platform { get; set; } = "linux";
    public string Virtualization { get; set; } = "none";
    public IReadOnlyList<RawSignal> Collect() => Values.Select(kv => new RawSignal(kv.Key, kv.Value)).ToList();

    public static FakeSignals OtherMachine() => new()
    {
        Values =
        {
            [SignalCatalog.SmbiosUuid] = "11111111-2222-3333-4444-555555555555", [SignalCatalog.BoardSerial] = "FAKE-BOARD-9999",
            [SignalCatalog.CpuId] = "FAKE-CPU-9999", [SignalCatalog.DiskSerial] = "FAKE-DISK-9999", [SignalCatalog.OsMachineId] = "fake-machine-id-9999",
            [SignalCatalog.NicMacPrefix + "1"] = "00:00:5e:00:53:91", [SignalCatalog.NicMacPrefix + "2"] = "00:00:5e:00:53:92",
        },
    };
}

/// <summary>In-memory stand-in for the database store; can be told to behave like an unreachable database.</summary>
public sealed class MemorySealStore : ISealStore
{
    public string Name => "memory-db";
    public string? Content { get; set; }
    public bool Unavailable { get; set; }
    public Task<string?> ReadAsync(CancellationToken ct = default) => Unavailable ? throw new InvalidOperationException("db down") : Task.FromResult(Content);
    public Task WriteAsync(string bundleJson, CancellationToken ct = default)
    {
        if (Unavailable) throw new InvalidOperationException("db down");
        Content = bundleJson;
        return Task.CompletedTask;
    }
}

/// <summary>One customer installation (temp folders, fake hardware, fake clock) plus helpers to play the vendor.</summary>
public sealed class CustomerInstall : IDisposable
{
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "pems-lic-" + Guid.NewGuid().ToString("N"));
    public ManualTime Time { get; }
    public MemorySealStore Db { get; } = new();
    public FileSealStore File { get; }
    public FileEventSink Events { get; }
    public FakeSignals Signals { get; set; } = new();
    public SealService Seal { get; private set; } = null!;
    public LicenseManager Manager { get; private set; } = null!;
    public TrustedKeySet Trusted { get; }
    public LicensingSettings Settings { get; }

    public CustomerInstall(TrustedKeySet trusted, DateTimeOffset? now = null, bool enforce = true)
    {
        Trusted = trusted;
        Time = new ManualTime(now ?? new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero));
        Settings = new LicensingSettings(enforce, Dir, 6, 24, 5, 30, 72, "/host/etc/machine-id", "2026.11.0", "2026-10-31");
        File = new FileSealStore(Dir);
        Events = new FileEventSink(Dir);
        Rebuild();
    }

    /// <summary>Re-creates the engine objects (a process restart) over the same stores and the same hardware.</summary>
    public void Rebuild()
    {
        var keys = new FileInstallKeyStore(Dir);
        Seal = new SealService(new ISealStore[] { Db, File }, new ILicenseEventSink[] { Events }, keys, Time);
        Manager = new LicenseManager(Settings, Seal, keys, () => Signals, Trusted);
    }

    public void Dispose()
    {
        try { Directory.Delete(Dir, true); } catch (IOException) { }
    }
}

/// <summary>The vendor side: key, ledger, validator, issuer on a temp folder.</summary>
public sealed class VendorSide : IDisposable
{
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "pems-vendor-" + Guid.NewGuid().ToString("N"));
    public ManualTime Time { get; }
    public VendorKeyStore Keys { get; }
    public Ledger Ledger { get; }
    public RequestValidator Validator { get; }
    public LicenseIssuer Issuer { get; }
    public VendorSigner Signer { get; }
    public TrustedKeySet Trusted { get; }

    public VendorSide(DateTimeOffset? now = null, int transferCap = 2)
    {
        Time = new ManualTime(now ?? new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero));
        Keys = new VendorKeyStore(Path.Combine(Dir, "keys"));
        Keys.Generate("vend-TEST0001", "test-passphrase-not-a-secret", Time.GetUtcNow());
        Signer = Keys.Unlock("vend-TEST0001", "test-passphrase-not-a-secret");
        Trusted = Keys.ToTrustedKeySet();
        Ledger = new Ledger(Path.Combine(Dir, "ledger.sqlite"));
        Validator = new RequestValidator(Ledger, transferCap);
        Issuer = new LicenseIssuer(Ledger, Validator, Time);
        AddCustomer("CUST-0001", "Example Customer Co");
    }

    public void AddCustomer(string customerRef, string name)
    {
        using var c = Ledger.Open();
        using var t = c.BeginTransaction();
        Ledger.AddCustomer(c, t, new CustomerRow(customerRef, name, null), Time.GetUtcNow());
        t.Commit();
    }

    public IssueOptions Options(string type = LicenseTypes.Lifetime, DateTimeOffset? notAfter = null, DateTimeOffset? updatesUntil = null, string? note = null, params string[] allowed)
        => new("FAKE-EDITION", new List<string> { "FAKE-MODULE-A" }, new Dictionary<string, int> { ["sites"] = 1 }, type,
            Time.GetUtcNow().AddMinutes(-1), notAfter, updatesUntil, allowed.Length == 0 ? new List<string> { "physical", "vm", "container" } : allowed.ToList(), note);

    public string Issue(string requestText, IssueOptions? options = null)
    {
        var r = Issuer.Issue(requestText, options ?? Options(), Signer, "tester");
        Assert.True(r.Issued, r.Error + " " + string.Join(";", r.Report.Items.Where(i => i.Status != CheckStatus.Pass).Select(i => i.Name + ":" + i.Message)));
        return r.FileText!;
    }

    public void Dispose()
    {
        Signer.Dispose();
        try { Directory.Delete(Dir, true); } catch (IOException) { }
    }
}
