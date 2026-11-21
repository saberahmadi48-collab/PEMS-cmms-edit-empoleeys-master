using PEMS.Licensing;
using PEMS.Licensing.Evaluation;
using PEMS.Licensing.Fingerprint;
using PEMS.Licensing.Formats;
using PEMS.Licensing.Seal;

namespace PEMS.Tests.Licensing;

/// <summary>The sequences of design section 10 end to end: customer install, vendor tool, back again.</summary>
public class ActivationFlowTests
{
    private static async Task<(CustomerInstall Install, VendorSide Vendor, string LicenceText)> Activated(
        string type = LicenseTypes.Lifetime, DateTimeOffset? notAfter = null, DateTimeOffset? updatesUntil = null, bool enforce = true)
    {
        var vendor = new VendorSide();
        var install = new CustomerInstall(vendor.Trusted, enforce: enforce);
        var request = await install.Manager.CreateRequestAsync(RequestTypes.Activation, "CUST-0001", "ORD-0001", null);
        var licence = vendor.Issue(request.FileText, vendor.Options(type, notAfter, updatesUntil));
        var outcome = await install.Manager.ApplyLicenceAsync(System.Text.Encoding.UTF8.GetBytes(licence));
        Assert.True(outcome.Applied, outcome.Failure + " " + outcome.Detail);
        return (install, vendor, licence);
    }

    [Fact]
    public async Task First_activation_request_issue_upload_makes_the_install_active()
    {
        var (install, vendor, _) = await Activated();
        using var _1 = install; using var _2 = vendor;
        var status = await install.Manager.CheckAsync(CheckReason.Startup);
        Assert.Equal(LicenseState.Active, status.State);
        Assert.Equal("lifetime", status.LicenceType);
        Assert.Null(status.ExpiresUtc);
        Assert.Single(vendor.Ledger.Licences());
        Assert.True(vendor.Ledger.VerifyAuditChain());
    }

    [Fact]
    public async Task No_licence_means_activation_required()
    {
        using var vendor = new VendorSide();
        using var install = new CustomerInstall(vendor.Trusted);
        Assert.Equal(LicenseState.ActivationRequired, (await install.Manager.CheckAsync(CheckReason.Startup)).State);
    }

    [Fact]
    public async Task Every_request_has_a_unique_nonce_and_the_pending_list_is_sealed()
    {
        using var vendor = new VendorSide();
        using var install = new CustomerInstall(vendor.Trusted);
        var a = await install.Manager.CreateRequestAsync(RequestTypes.Activation, "CUST-0001", "ORD-1", null);
        var b = await install.Manager.CreateRequestAsync(RequestTypes.Activation, "CUST-0001", "ORD-1", null);
        Assert.NotEqual(a.Payload.Nonce, b.Payload.Nonce);
        Assert.Equal(2, (await install.Seal.LoadAsync()).Record!.PendingRequests.Count);
    }

    [Fact]
    public async Task A_licence_issued_for_request_A_is_not_valid_on_install_B()
    {
        var (a, vendor, licence) = await Activated();
        using var _1 = a; using var _2 = vendor;
        using var b = new CustomerInstall(vendor.Trusted) { Signals = FakeSignals.OtherMachine() };
        await b.Manager.CreateRequestAsync(RequestTypes.Activation, "CUST-0001", "ORD-2", null);   // B has its own pending request
        var outcome = await b.Manager.ApplyLicenceAsync(System.Text.Encoding.UTF8.GetBytes(licence));
        Assert.False(outcome.Applied);
        Assert.Equal(LicenseFailure.InstallMismatch, outcome.Failure);
    }

    [Fact]
    public async Task A_tampered_signature_is_rejected_with_BadSignature()
    {
        using var vendor = new VendorSide();
        using var install = new CustomerInstall(vendor.Trusted);
        var request = await install.Manager.CreateRequestAsync(RequestTypes.Activation, "CUST-0001", "ORD-1", null);
        var licence = vendor.Issue(request.FileText);
        var tampered = FlipSignature(licence);
        var outcome = await install.Manager.ApplyLicenceAsync(System.Text.Encoding.UTF8.GetBytes(tampered));
        Assert.Equal(LicenseFailure.BadSignature, outcome.Failure);
        Assert.Equal(LicenseState.ActivationRequired, (await install.Manager.InspectAsync()).State);
    }

    [Fact]
    public async Task A_licence_without_a_pending_request_is_rejected()
    {
        using var vendor = new VendorSide();
        using var other = new CustomerInstall(vendor.Trusted);
        var request = await other.Manager.CreateRequestAsync(RequestTypes.Activation, "CUST-0001", "ORD-1", null);
        var licence = vendor.Issue(request.FileText);
        await other.Manager.ApplyLicenceAsync(System.Text.Encoding.UTF8.GetBytes(licence));
        // Replaying the same licence after it was applied: its request is no longer pending.
        var replay = await other.Manager.ApplyLicenceAsync(System.Text.Encoding.UTF8.GetBytes(licence));
        Assert.False(replay.Applied);
        Assert.Contains(replay.Failure, new[] { LicenseFailure.NonceUnknown, LicenseFailure.NonceMismatch });
    }

    [Fact]
    public async Task Term_licence_expires_at_the_end_of_the_jalali_day_and_restricts()
    {
        var end = LicenseDates.EndOfJalaliDayUtc(1405, 8, 30);   // valid through 30 Aban 1405 (Tehran)
        var (install, vendor, _) = await Activated(LicenseTypes.Term, end);
        using var _1 = install; using var _2 = vendor;

        install.Time.Set(end.AddSeconds(-1));
        Assert.Equal(LicenseState.ActiveWarning, (await install.Manager.CheckAsync(CheckReason.Periodic)).State);   // inside the warning window
        install.Time.Set(end);
        var expired = await install.Manager.CheckAsync(CheckReason.Periodic);
        Assert.Equal(LicenseState.Restricted, expired.State);
        Assert.Contains(LicenseReasons.Expired, expired.Reasons);
        Assert.False(expired.WritesAllowed);
    }

    [Fact]
    public async Task Jalali_end_of_day_is_tehran_midnight()
    {
        var end = LicenseDates.EndOfJalaliDayUtc(1405, 8, 30);
        var tehran = TimeZoneInfo.ConvertTime(end, LicenseDates.Tehran());
        Assert.Equal(TimeSpan.Zero, tehran.TimeOfDay);
        Assert.Equal("1405/08/30", LicenseDates.ToJalaliString(end.AddSeconds(-1)));
        Assert.Equal("1405/09/01", LicenseDates.ToJalaliString(end));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Lifetime_shows_no_expiry_warning_even_years_later()
    {
        var (install, vendor, _) = await Activated();
        using var _1 = install; using var _2 = vendor;
        install.Time.Advance(TimeSpan.FromDays(3650));
        var status = await install.Manager.CheckAsync(CheckReason.Periodic);
        Assert.Equal(LicenseState.Active, status.State);
        Assert.DoesNotContain(LicenseReasons.Expiring, status.Reasons);
    }

    [Fact]
    public async Task Setting_the_clock_back_30_days_does_not_extend_a_term_licence()
    {
        var end = new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero);
        var (install, vendor, _) = await Activated(LicenseTypes.Term, end);
        using var _1 = install; using var _2 = vendor;

        install.Time.Set(end.AddDays(1));                                   // expired
        Assert.Equal(LicenseState.Restricted, (await install.Manager.CheckAsync(CheckReason.Periodic)).State);

        install.Time.Set(end.AddDays(1).AddDays(-30));                      // the customer sets the clock back
        install.Rebuild();                                                  // and restarts the product
        var status = await install.Manager.CheckAsync(CheckReason.Startup);
        Assert.Equal(LicenseState.Restricted, status.State);
        Assert.Contains(LicenseReasons.Expired, status.Reasons);
        Assert.Contains(LicenseReasons.ClockBackward, status.Reasons);
    }

    [Fact]
    public async Task Renewal_before_expiry_replaces_the_licence_and_after_expiry_restores_it()
    {
        var end = new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);
        var (install, vendor, _) = await Activated(LicenseTypes.Term, end);
        using var _1 = install; using var _2 = vendor;

        install.Time.Set(end.AddDays(5));                                   // expired: Restricted, but the licence page can still renew
        Assert.Equal(LicenseState.Restricted, (await install.Manager.CheckAsync(CheckReason.Periodic)).State);
        var request = await install.Manager.CreateRequestAsync(RequestTypes.Renewal, "CUST-0001", "ORD-1", null);
        Assert.NotNull(request.Payload.PreviousLicenceId);

        vendor.Time.Set(install.Time.GetUtcNow());
        var renewal = vendor.Issue(request.FileText, vendor.Options(LicenseTypes.Term, end.AddDays(365)));
        var outcome = await install.Manager.ApplyLicenceAsync(System.Text.Encoding.UTF8.GetBytes(renewal));
        Assert.True(outcome.Applied, outcome.Failure.ToString());
        Assert.Equal(LicenseState.Active, (await install.Manager.CheckAsync(CheckReason.Periodic)).State);
        Assert.Equal(new[] { "superseded", "active" }, vendor.Ledger.Licences().Select(l => l.Status));
    }

    [Fact]
    public async Task An_older_licence_cannot_replace_a_newer_one()
    {
        var (install, vendor, firstLicence) = await Activated();
        using var _1 = install; using var _2 = vendor;
        var request = await install.Manager.CreateRequestAsync(RequestTypes.Renewal, "CUST-0001", "ORD-1", null);
        vendor.Issue(request.FileText);
        await install.Manager.ApplyLicenceAsync(System.Text.Encoding.UTF8.GetBytes(vendor.Ledger.GetLicenceText("LIC-000002")!));
        var again = await install.Manager.ApplyLicenceAsync(System.Text.Encoding.UTF8.GetBytes(firstLicence));
        Assert.False(again.Applied);
    }

    [Fact]
    public async Task One_replaced_nic_keeps_the_install_active_with_a_drift_warning()
    {
        var (install, vendor, _) = await Activated();
        using var _1 = install; using var _2 = vendor;
        install.Signals.Values[SignalCatalog.NicMacPrefix + "2"] = "00:00:5e:00:53:55";
        var status = await install.Manager.CheckAsync(CheckReason.Startup);
        Assert.Equal(LicenseState.ActiveWarning, status.State);
        Assert.Contains(LicenseReasons.FingerprintDrift, status.Reasons);
        Assert.True(status.WritesAllowed);
    }

    [Fact]
    public async Task Image_booted_on_other_hardware_is_restricted_and_a_transfer_restores_it()
    {
        var (install, vendor, _) = await Activated();
        using var _1 = install; using var _2 = vendor;

        install.Signals = FakeSignals.OtherMachine();                       // same disk image, different machine
        install.Rebuild();
        var status = await install.Manager.CheckAsync(CheckReason.Startup);
        Assert.Equal(LicenseState.Restricted, status.State);
        Assert.Contains(LicenseReasons.FingerprintInvalid, status.Reasons);
        Assert.False(status.WritesAllowed);

        var request = await install.Manager.CreateRequestAsync(RequestTypes.Transfer, "CUST-0001", "ORD-1", "new server");
        var licence = vendor.Issue(request.FileText);                       // transfer count 1 of cap 2: no warning
        var outcome = await install.Manager.ApplyLicenceAsync(System.Text.Encoding.UTF8.GetBytes(licence));
        Assert.True(outcome.Applied, outcome.Failure.ToString());
        Assert.Equal(LicenseState.Active, (await install.Manager.CheckAsync(CheckReason.Periodic)).State);
        Assert.Equal(1, vendor.Ledger.Licences().Last().TransferCount);
    }

    [Fact]
    public async Task Restore_of_an_old_snapshot_warns_then_restricts_and_re_activation_clears_it()
    {
        var (install, vendor, _) = await Activated();
        using var _1 = install; using var _2 = vendor;

        var snapshotDb = install.Db.Content;
        var snapshotFile = await File.ReadAllTextAsync(Path.Combine(install.Dir, "install.seal"));
        for (var n = 0; n < 4; n++) { install.Rebuild(); await install.Manager.CheckAsync(CheckReason.Startup); }   // normal life: counter rises
        install.Db.Content = snapshotDb;                                    // a partial restore (database only)
        install.Rebuild();

        var warned = await install.Manager.CheckAsync(CheckReason.Startup);
        Assert.Equal(LicenseState.ActiveWarning, warned.State);
        Assert.Contains(LicenseReasons.RestoreSuspected, warned.Reasons);
        Assert.Contains(install.Events.ReadAll(), e => e.Type == "restore_suspected");

        install.Time.Advance(TimeSpan.FromHours(73));                       // grace (72 h) passes without re-activation
        var restricted = await install.Manager.CheckAsync(CheckReason.Periodic);
        Assert.Equal(LicenseState.Restricted, restricted.State);
        Assert.Contains(LicenseReasons.RestoreGraceExpired, restricted.Reasons);

        var request = await install.Manager.CreateRequestAsync(RequestTypes.Restore, "CUST-0001", "ORD-1", "restored from backup");
        vendor.Time.Set(install.Time.GetUtcNow());
        var licence = vendor.Issue(request.FileText, vendor.Options(note: "restore confirmed by phone call-back"));   // witness: counter is behind the ledger? no, ahead
        Assert.True((await install.Manager.ApplyLicenceAsync(System.Text.Encoding.UTF8.GetBytes(licence))).Applied);
        var after = await install.Manager.CheckAsync(CheckReason.Periodic);
        Assert.Equal(LicenseState.Active, after.State);
        Assert.DoesNotContain(LicenseReasons.RestoreSuspected, after.Reasons);
        _ = snapshotFile;
    }

    [Fact]
    public async Task Zip_bundle_with_several_licences_activates_the_matching_one()
    {
        using var vendor = new VendorSide();
        using var install = new CustomerInstall(vendor.Trusted);
        var request = await install.Manager.CreateRequestAsync(RequestTypes.Activation, "CUST-0001", "ORD-1", null);
        var good = vendor.Issue(request.FileText);
        using var other = new CustomerInstall(vendor.Trusted) { Signals = FakeSignals.OtherMachine() };
        var otherRequest = await other.Manager.CreateRequestAsync(RequestTypes.Activation, "CUST-0001", "ORD-2", null);
        var foreign = vendor.Issue(otherRequest.FileText);

        var zip = Zip(("a.pemslic", foreign), ("b.pemslic", good), ("manifest.json", "{}"));
        var outcome = await install.Manager.ApplyLicenceAsync(zip);
        Assert.True(outcome.Applied, outcome.Failure.ToString());
    }

    [Theory]
    [InlineData("../evil.pemslic", LicenseFailure.UnsafeArchive)]
    [InlineData("/abs.pemslic", LicenseFailure.UnsafeArchive)]
    [InlineData("sub/x.pemslic", LicenseFailure.UnsafeArchive)]
    [InlineData("readme.txt", LicenseFailure.NoLicenceInArchive)]
    public async Task Hostile_or_empty_zip_is_rejected_before_any_parsing(string entryName, LicenseFailure expected)
    {
        using var vendor = new VendorSide();
        using var install = new CustomerInstall(vendor.Trusted);
        await install.Manager.CreateRequestAsync(RequestTypes.Activation, "CUST-0001", "ORD-1", null);
        var outcome = await install.Manager.ApplyLicenceAsync(Zip((entryName, "{}")));
        Assert.Equal(expected, outcome.Failure);
    }

    [Fact]
    public async Task Enforce_false_changes_nothing_on_a_developer_machine()
    {
        using var vendor = new VendorSide();
        using var install = new CustomerInstall(vendor.Trusted, enforce: false);
        var status = await install.Manager.CheckAsync(CheckReason.Startup);
        Assert.Equal(LicenseState.NotEnforced, status.State);
        Assert.True(status.WritesAllowed);
        Assert.True(await install.Manager.IsBuildEntitledAsync(DateTimeOffset.UtcNow));
        Assert.True(await install.Manager.IsModuleGrantedAsync("ANYTHING"));
        Assert.Null(install.Db.Content);                                    // no seal, no key, no event, no file
        Assert.False(Directory.Exists(install.Dir) && Directory.EnumerateFileSystemEntries(install.Dir).Any());
    }

    [Fact]
    public async Task Update_entitlement_follows_updates_until()
    {
        var until = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var (install, vendor, _) = await Activated(LicenseTypes.Lifetime, null, until);
        using var _1 = install; using var _2 = vendor;
        Assert.True(await install.Manager.IsBuildEntitledAsync(until.AddDays(-1)));
        Assert.False(await install.Manager.IsBuildEntitledAsync(until.AddDays(1)));
        Assert.True(await install.Manager.IsModuleGrantedAsync("FAKE-MODULE-A"));
        Assert.False(await install.Manager.IsModuleGrantedAsync("FAKE-MODULE-Z"));
    }

    [Fact]
    public async Task Deployment_policy_rejects_a_vm_on_a_physical_only_licence()
    {
        using var vendor = new VendorSide();
        using var install = new CustomerInstall(vendor.Trusted);
        var request = await install.Manager.CreateRequestAsync(RequestTypes.Activation, "CUST-0001", "ORD-1", null);
        var licence = vendor.Issue(request.FileText, vendor.Options(allowed: "physical"));
        install.Signals.Virtualization = "vmware";
        var outcome = await install.Manager.ApplyLicenceAsync(System.Text.Encoding.UTF8.GetBytes(licence));
        Assert.Equal(LicenseFailure.DeploymentNotAllowed, outcome.Failure);
    }

    private static byte[] Zip(params (string Name, string Content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
            foreach (var (name, content) in entries)
                using (var w = new StreamWriter(zip.CreateEntry(name).Open())) w.Write(content);
        return ms.ToArray();
    }

    internal static string FlipSignature(string fileText)
    {
        var m = System.Text.RegularExpressions.Regex.Match(fileText, "\"value\": \"(.)");
        var c = m.Groups[1].Value == "A" ? "B" : "A";
        return fileText.Remove(m.Groups[1].Index, 1).Insert(m.Groups[1].Index, c);
    }
}
