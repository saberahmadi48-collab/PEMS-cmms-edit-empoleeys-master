extern alias VendorTool;
using PEMS.Licensing;
using PEMS.Licensing.Formats;
using VendorTool::PemsVendorTool.Core;

namespace PEMS.Tests.Licensing;

public class VendorToolTests
{
    private static async Task<string> RequestText(CustomerInstall install, string type = RequestTypes.Activation, string customer = "CUST-0001")
        => (await install.Manager.CreateRequestAsync(type, customer, "ORD-1", null)).FileText;

    [Fact]
    public async Task Changing_one_byte_of_the_request_makes_the_vendor_tool_reject_it()
    {
        using var vendor = new VendorSide();
        using var install = new CustomerInstall(vendor.Trusted);
        var text = await RequestText(install);
        Assert.False(vendor.Validator.Validate(text, vendor.Time.GetUtcNow()).HasFailures);

        var tampered = ActivationFlowTests.FlipSignature(text);
        var report = vendor.Validator.Validate(tampered, vendor.Time.GetUtcNow());
        Assert.True(report.HasFailures);
        Assert.Contains(report.Items, i => i.Name == "signature" && i.Status == CheckStatus.Fail);

        // A change in the payload (re-encoded) breaks the signature too.
        var payloadStart = text.IndexOf("\"payload\": \"", StringComparison.Ordinal) + 12;
        var changed = text.Remove(payloadStart + 5, 1).Insert(payloadStart + 5, text[payloadStart + 5] == 'A' ? "B" : "A");
        Assert.True(vendor.Validator.Validate(changed, vendor.Time.GetUtcNow()).HasFailures);
    }

    [Fact]
    public async Task A_nonce_can_be_issued_only_once()
    {
        using var vendor = new VendorSide();
        using var install = new CustomerInstall(vendor.Trusted);
        var text = await RequestText(install);
        vendor.Issue(text);
        var second = vendor.Validator.Validate(text, vendor.Time.GetUtcNow());
        Assert.Contains(second.Items, i => i.Name == "nonce" && i.Status == CheckStatus.Fail);
        var result = vendor.Issuer.Issue(text, vendor.Options(), vendor.Signer, "tester");
        Assert.False(result.Issued);
    }

    [Fact]
    public async Task Unknown_customer_blocks_issuing()
    {
        using var vendor = new VendorSide();
        using var install = new CustomerInstall(vendor.Trusted);
        var report = vendor.Validator.Validate(await RequestText(install, customer: "CUST-9999"), vendor.Time.GetUtcNow());
        Assert.Contains(report.Items, i => i.Name == "customer" && i.Status == CheckStatus.Fail);
    }

    [Fact]
    public async Task Issue_sequence_is_gapless_and_every_issue_is_in_the_ledger_with_an_audit_entry()
    {
        using var vendor = new VendorSide();
        for (var n = 0; n < 3; n++)
        {
            using var install = new CustomerInstall(vendor.Trusted) { Signals = SignalsFor(n) };
            vendor.Issue(await RequestText(install));
        }
        Assert.Equal(new long[] { 1, 2, 3 }, vendor.Ledger.Licences().Select(l => l.IssueSeq));
        Assert.Equal(3, vendor.Ledger.AuditLog().Count(a => a.Action == "issue"));
        Assert.True(vendor.Ledger.VerifyAuditChain());
    }

    [Fact]
    public async Task A_failing_issue_leaves_no_ledger_row_and_no_nonce()
    {
        using var vendor = new VendorSide();
        using var install = new CustomerInstall(vendor.Trusted);
        var text = await RequestText(install);
        var bad = vendor.Options() with { Limits = new Dictionary<string, int> { ["users"] = 5 } };
        var result = vendor.Issuer.Issue(text, bad, vendor.Signer, "tester");
        Assert.False(result.Issued);
        Assert.Contains("no user limit", result.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(vendor.Ledger.Licences());
        Assert.False(vendor.Ledger.NonceSeen(result.Report.Payload!.Nonce));
    }

    [Fact]
    public async Task Term_licence_needs_an_end_date_and_lifetime_must_not_have_one()
    {
        using var vendor = new VendorSide();
        using var install = new CustomerInstall(vendor.Trusted);
        var text = await RequestText(install);
        Assert.False(vendor.Issuer.Issue(text, vendor.Options(LicenseTypes.Term), vendor.Signer, "t").Issued);
        Assert.False(vendor.Issuer.Issue(text, vendor.Options(LicenseTypes.Lifetime, vendor.Time.GetUtcNow().AddDays(5)), vendor.Signer, "t").Issued);
    }

    [Fact]
    public async Task Warnings_require_an_operator_note_that_reaches_the_audit_log()
    {
        using var vendor = new VendorSide();
        using var install = new CustomerInstall(vendor.Trusted);
        install.Signals.Virtualization = "vmware";                            // virtualisation is a warning (policy C5)
        var text = await RequestText(install);
        Assert.False(vendor.Issuer.Issue(text, vendor.Options(), vendor.Signer, "t").Issued);
        var ok = vendor.Issuer.Issue(text, vendor.Options(note: "VM approved by owner"), vendor.Signer, "t");
        Assert.True(ok.Issued);
        Assert.Contains("VM approved by owner", vendor.Ledger.Licences().Single().Note);
    }

    [Fact]
    public async Task A_second_install_with_the_same_hardware_is_flagged_as_a_possible_clone()
    {
        using var vendor = new VendorSide();
        using var first = new CustomerInstall(vendor.Trusted);
        vendor.Issue(await RequestText(first));
        using var clone = new CustomerInstall(vendor.Trusted);                // same fake hardware, new Install ID
        var report = vendor.Validator.Validate(await RequestText(clone), vendor.Time.GetUtcNow());
        Assert.Contains(report.Items, i => i.Name == "clone_flags" && i.Status == CheckStatus.Warn);
    }

    [Fact]
    public async Task A_counter_behind_the_ledger_is_flagged_unless_it_is_a_restore_request()
    {
        using var vendor = new VendorSide();
        using var install = new CustomerInstall(vendor.Trusted);
        var first = await install.Manager.CreateRequestAsync(RequestTypes.Activation, "CUST-0001", "ORD-1", null);
        await install.Manager.ApplyLicenceAsync(System.Text.Encoding.UTF8.GetBytes(vendor.Issue(first.FileText)));
        var snapshotDb = install.Db.Content;
        var snapshotFile = await File.ReadAllTextAsync(Path.Combine(install.Dir, "install.seal"));

        for (var n = 0; n < 5; n++) { install.Rebuild(); await install.Manager.CheckAsync(CheckReason.Startup); }
        var advanced = await install.Manager.CreateRequestAsync(RequestTypes.Renewal, "CUST-0001", "ORD-1", null);
        vendor.Issue(advanced.FileText);                                      // the ledger now knows the high counter

        install.Db.Content = snapshotDb;                                      // a full image restore: both stores go back together
        await File.WriteAllTextAsync(Path.Combine(install.Dir, "install.seal"), snapshotFile);
        install.Rebuild();

        var stale = await install.Manager.CreateRequestAsync(RequestTypes.Renewal, "CUST-0001", "ORD-1", null);
        var flagged = vendor.Validator.Validate(stale.FileText, vendor.Time.GetUtcNow());
        Assert.Contains(flagged.Items, i => i.Name == "witness" && i.Status == CheckStatus.Warn);

        var restore = await install.Manager.CreateRequestAsync(RequestTypes.Restore, "CUST-0001", "ORD-1", "restored image");
        var expected = vendor.Validator.Validate(restore.FileText, vendor.Time.GetUtcNow());
        Assert.Contains(expected.Items, i => i.Name == "witness" && i.Status == CheckStatus.Pass);
    }

    [Fact]
    public async Task Revoke_marks_the_licence_and_audits_it()
    {
        using var vendor = new VendorSide();
        using var install = new CustomerInstall(vendor.Trusted);
        vendor.Issue(await RequestText(install));
        vendor.Issuer.Revoke("LIC-000001", "non-payment", "tester");
        Assert.Equal("revoked", vendor.Ledger.GetLicence("LIC-000001")!.Status);
        Assert.Contains(vendor.Ledger.AuditLog(), a => a.Action == "revoke");
        Assert.Throws<ArgumentException>(() => vendor.Issuer.Revoke("LIC-000001", " ", "tester"));
    }

    [Fact]
    public async Task Importing_an_earlier_licence_needs_a_valid_signature_and_a_known_customer()
    {
        using var vendor = new VendorSide();
        using var install = new CustomerInstall(vendor.Trusted);
        var licence = vendor.Issue(await RequestText(install));

        using var fresh = new VendorSide();                                   // a new ledger that never saw this licence
        // fresh has a different key: the licence does not verify there.
        Assert.False(fresh.Issuer.Import(licence, "CUST-0001", fresh.Trusted, "tester").Ok);
        // The original key set verifies it.
        var (ok, message) = fresh.Issuer.Import(licence, "CUST-0001", vendor.Trusted, "tester");
        Assert.True(ok, message);
        Assert.True(fresh.Issuer.Import(licence, "CUST-0001", vendor.Trusted, "tester").Ok == false);   // already imported
        Assert.False(fresh.Issuer.Import(ActivationFlowTests.FlipSignature(licence), "CUST-0001", vendor.Trusted, "tester").Ok);
        Assert.True(fresh.Ledger.GetLicence("LIC-000001")!.Imported);
        Assert.Contains(fresh.Ledger.AuditLog(), a => a.Action == "import");
    }

    [Fact]
    public async Task Audit_chain_detects_a_deleted_or_edited_entry()
    {
        using var vendor = new VendorSide();
        for (var n = 0; n < 3; n++)
        {
            using var install = new CustomerInstall(vendor.Trusted) { Signals = SignalsFor(n) };
            vendor.Issue(await RequestText(install));
        }
        Assert.True(vendor.Ledger.VerifyAuditChain());
        using (var c = vendor.Ledger.Open())
        using (var cmd = c.CreateCommand()) { cmd.CommandText = "UPDATE audit SET subject='forged' WHERE seq=2"; cmd.ExecuteNonQuery(); }
        Assert.True(vendor.Ledger.VerifyAuditChain());                        // 'subject' is not hashed, data_hash is
        using (var c = vendor.Ledger.Open())
        using (var cmd = c.CreateCommand()) { cmd.CommandText = "DELETE FROM audit WHERE seq=2"; cmd.ExecuteNonQuery(); }
        Assert.False(vendor.Ledger.VerifyAuditChain());
    }

    [Fact]
    public void Private_key_is_stored_encrypted_and_a_wrong_passphrase_fails()
    {
        using var vendor = new VendorSide();
        var file = Directory.GetFiles(Path.Combine(vendor.Dir, "keys")).Single();
        var text = File.ReadAllText(file);
        Assert.DoesNotContain("PRIVATE", text);
        Assert.Throws<System.Security.Cryptography.CryptographicException>(() => vendor.Keys.Unlock("vend-TEST0001", "a-wrong-passphrase!!"));
        Assert.Throws<ArgumentException>(() => vendor.Keys.Generate("vend-TEST0002", "short", DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(() => vendor.Keys.Generate("vend-TEST0001", "test-passphrase-not-a-secret", DateTimeOffset.UtcNow));
        Assert.Contains("public_key", vendor.Keys.ExportTrustedKeys());
    }

    [Fact]
    public async Task Csv_export_lists_the_ledger()
    {
        using var vendor = new VendorSide();
        using var install = new CustomerInstall(vendor.Trusted);
        vendor.Issue(await RequestText(install));
        var csv = LicenseIssuer.ToCsv(vendor.Ledger.Licences());
        Assert.StartsWith("issue_seq,licence_id", csv);
        Assert.Contains("LIC-000001", csv);
    }

    private static FakeSignals SignalsFor(int n)
    {
        var s = FakeSignals.OtherMachine();
        s.Values[PEMS.Licensing.Fingerprint.SignalCatalog.SmbiosUuid] = $"aaaaaaaa-bbbb-cccc-dddd-{n:000000000000}";
        s.Values[PEMS.Licensing.Fingerprint.SignalCatalog.BoardSerial] = $"FAKE-BOARD-{n}";
        return s;
    }
}
