using PEMS.Licensing.Crypto;
using PEMS.Licensing.Evaluation;
using PEMS.Licensing.Formats;

namespace PemsVendorTool.Core;

public sealed record IssueOptions(
    string Edition,
    List<string> Modules,
    Dictionary<string, int> Limits,
    string Type,
    DateTimeOffset NotBefore,
    DateTimeOffset? NotAfter,
    DateTimeOffset? UpdatesUntil,
    List<string> AllowedDeployments,
    string? OperatorNote);

public sealed record IssueResult(bool Issued, string? FileText, string? LicenceId, ValidationReport Report, string? Error);

/// <summary>Issues a .pemslic (design 9.3). The ledger row, nonce, supersede and audit entry are one transaction with the signature.</summary>
public sealed class LicenseIssuer
{
    private readonly Ledger _ledger;
    private readonly RequestValidator _validator;
    private readonly TimeProvider _time;

    public LicenseIssuer(Ledger ledger, RequestValidator validator, TimeProvider time)
    {
        _ledger = ledger;
        _validator = validator;
        _time = time;
    }

    public IssueResult Issue(string requestText, IssueOptions options, VendorSigner signer, string operatorName)
    {
        var now = _time.GetUtcNow();
        var report = _validator.Validate(requestText, now);   // never trust what the UI showed earlier: validate again at issue time
        if (report.HasFailures || report.Payload is null) return new IssueResult(false, null, null, report, "The request has failing checks.");
        if (report.HasWarnings && string.IsNullOrWhiteSpace(options.OperatorNote))
            return new IssueResult(false, null, null, report, "The request has warnings: an operator note is required.");
        var error = ValidateOptions(options);
        if (error is not null) return new IssueResult(false, null, null, report, error);

        var req = report.Payload;
        using var c = _ledger.Open();
        using var t = c.BeginTransaction();
        var seq = _ledger.NextIssueSeq(c, t);
        var licenceId = $"LIC-{seq:000000}";

        var previous = req.PreviousLicenceId is null ? null : _ledger.GetLicence(req.PreviousLicenceId);
        var transferCount = (previous?.TransferCount ?? 0) + (req.RequestType == RequestTypes.Transfer ? 1 : 0);
        var customer = _ledger.GetCustomer(req.Customer.CustomerRef)!;

        var payload = new LicensePayload(
            licenceId, seq, now, signer.KeyId, req.Nonce, LicenseVerifier.Hash(report.PayloadBytes!),
            new LicenceCustomer(customer.CustomerRef, customer.Name),
            new BindingInfo(req.Install.InstallId, InstallKeyVerifier.PublicKeyHash(req.Install.InstallPub), req.Fingerprint,
                new DeploymentInfo(req.Fingerprint.Platform, req.Fingerprint.Virtualization, options.AllowedDeployments)),
            new GrantInfo(options.Edition, options.Modules, options.Limits),
            options.Type, new ValidityInfo(options.NotBefore, options.NotAfter), new MaintenanceInfo(options.UpdatesUntil),
            transferCount, previous?.LicenceId, new List<string>());

        var text = SignedFileCodec.Create(FileFormats.Licence, LicenseJson.ToBytes(payload), FileFormats.AlgVendor, signer.KeyId, signer.Sign);

        _ledger.InsertLicence(c, t, new LedgerRow(seq, licenceId, customer.CustomerRef, req.Install.InstallId, req.Fingerprint.FpHash, signer.KeyId,
            options.Edition, string.Join(",", options.Modules), options.Type, options.NotBefore, options.NotAfter, options.UpdatesUntil, "active", now,
            req.Nonce, req.RequestType, transferCount, req.Seal.Counter, req.Seal.AuditHead, options.OperatorNote, false), text);
        if (previous is not null && previous.Status == "active") _ledger.SetStatus(c, t, previous.LicenceId, "superseded");
        _ledger.RegisterNonce(c, t, req.Nonce, req.Install.InstallId, req.RequestType, now, req.Seal.Counter, req.Seal.AuditHead, customer.CustomerRef);
        _ledger.Audit(c, t, operatorName, "issue", licenceId,
            $"customer={customer.CustomerRef};install={req.Install.InstallId};type={options.Type};request={req.RequestType};warnings={report.HasWarnings};note={options.OperatorNote}", now);
        t.Commit();
        return new IssueResult(true, text, licenceId, report, null);
    }

    public void Revoke(string licenceId, string reason, string operatorName)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A reason is required.", nameof(reason));
        var row = _ledger.GetLicence(licenceId) ?? throw new InvalidOperationException("Unknown licence id.");
        var now = _time.GetUtcNow();
        using var c = _ledger.Open();
        using var t = c.BeginTransaction();
        _ledger.SetStatus(c, t, row.LicenceId, "revoked");
        _ledger.Audit(c, t, operatorName, "revoke", licenceId, "reason=" + reason, now);
        t.Commit();
    }

    /// <summary>Imports a licence issued before the ledger existed (plan #341): signature verified with a vendor public key, owner confirms the customer.</summary>
    public (bool Ok, string Message) Import(string licenceText, string customerRef, TrustedKeySet keys, string operatorName)
    {
        var parsed = SignedFileCodec.Parse(licenceText, FileFormats.Licence);
        if (!parsed.Ok) return (false, $"Rejected: {parsed.Failure}");
        var file = parsed.Value!;
        if (!keys.TryGet(file.Envelope.Signature.KeyId, out var key) || !Base64Url.TryDecode(key.PublicKey, out var pub) ||
            !Ed25519.Verify(pub, file.SignedMessage, file.SignatureBytes))
            return (false, "Rejected: the signature does not verify with any vendor key.");
        var p = LicenseJson.FromBytes<LicensePayload>(file.PayloadBytes);
        if (p is null) return (false, "Rejected: payload unreadable.");
        if (_ledger.GetLicence(p.LicenceId) is not null) return (false, "This licence is already in the ledger.");
        if (_ledger.GetCustomer(customerRef) is null) return (false, "Unknown customer reference.");

        var now = _time.GetUtcNow();
        using var c = _ledger.Open();
        using var t = c.BeginTransaction();
        _ledger.InsertLicence(c, t, new LedgerRow(p.IssueSeq, p.LicenceId, customerRef, p.Binding.InstallId, p.Binding.Fingerprint.FpHash, p.SigningKeyId,
            p.Grant.Edition, string.Join(",", p.Grant.Modules), p.Type, p.Validity.NotBefore, p.Validity.NotAfter, p.Maintenance.UpdatesUntil, "active",
            p.IssuedUtc, p.RequestNonce, "import", p.TransferCount, 0, "", "imported", true), licenceText);
        _ledger.Audit(c, t, operatorName, "import", p.LicenceId, $"customer={customerRef};install={p.Binding.InstallId}", now);
        t.Commit();
        return (true, "Imported.");
    }

    public static string ToCsv(IEnumerable<LedgerRow> rows)
    {
        static string Q(string? s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
        var lines = new List<string> { "issue_seq,licence_id,customer,install_id,fp_hash,key_id,edition,modules,type,not_before,not_after,updates_until,status,issued_utc,request_nonce,transfer_count" };
        lines.AddRange(rows.Select(r => string.Join(",", r.IssueSeq, Q(r.LicenceId), Q(r.CustomerRef), Q(r.InstallId), Q(r.FpHash), Q(r.SigningKeyId), Q(r.Edition),
            Q(r.Modules), Q(r.Type), Q(Ledger.Iso(r.NotBefore)), Q(r.NotAfter is null ? "" : Ledger.Iso(r.NotAfter.Value)),
            Q(r.UpdatesUntil is null ? "" : Ledger.Iso(r.UpdatesUntil.Value)), Q(r.Status), Q(Ledger.Iso(r.IssuedUtc)), Q(r.RequestNonce), r.TransferCount)));
        return string.Join("\n", lines) + "\n";
    }

    private static string? ValidateOptions(IssueOptions o)
    {
        if (!LicenseTypes.All.Contains(o.Type)) return "Unknown licence type.";
        if (string.IsNullOrWhiteSpace(o.Edition)) return "Edition is required.";
        if (o.AllowedDeployments.Count == 0 || o.AllowedDeployments.Any(d => d is not ("physical" or "vm" or "container"))) return "Choose at least one deployment kind (physical, vm, container).";
        if (o.Limits.ContainsKey("users")) return "There is no user limit in PEMS licences (decision C3).";
        if (LicenseTypes.IsTimeLimited(o.Type) && o.NotAfter is null) return "A term, trial or demo licence needs an end date.";
        if (o.Type == LicenseTypes.Lifetime && o.NotAfter is not null) return "A lifetime licence has no end date.";
        if (o.NotAfter is { } end && end <= o.NotBefore) return "The end date must be after the start date.";
        return null;
    }
}
