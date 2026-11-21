using PEMS.Licensing.Crypto;
using PEMS.Licensing.Formats;

namespace PemsVendorTool.Core;

public enum CheckStatus { Pass, Warn, Fail }

public sealed record CheckItem(string Name, CheckStatus Status, string Message);

public sealed record ValidationReport(RequestPayload? Payload, byte[]? PayloadBytes, IReadOnlyList<CheckItem> Items)
{
    public bool HasFailures => Items.Any(i => i.Status == CheckStatus.Fail);
    public bool HasWarnings => Items.Any(i => i.Status == CheckStatus.Warn);
}

/// <summary>The checks of design 9.2. A fail blocks issuing; a warn needs an operator note that goes to the audit log.</summary>
public sealed class RequestValidator
{
    public const int MaxFutureMinutes = 10;
    public const int MaxAgeDays = 60;

    private readonly Ledger _ledger;
    private readonly int _transferCap;

    public RequestValidator(Ledger ledger, int transferCap)
    {
        _ledger = ledger;
        _transferCap = transferCap;
    }

    public ValidationReport Validate(string? text, DateTimeOffset now)
    {
        var items = new List<CheckItem>();
        var parsed = SignedFileCodec.Parse(text, FileFormats.Request);
        if (!parsed.Ok)
        {
            items.Add(new("envelope", CheckStatus.Fail, $"Rejected: {parsed.Failure}"));
            return new ValidationReport(null, null, items);
        }
        items.Add(new("envelope", CheckStatus.Pass, "Known format and version, size within limits."));

        RequestPayload? p;
        try { p = LicenseJson.FromBytes<RequestPayload>(parsed.Value!.PayloadBytes); }
        catch (System.Text.Json.JsonException) { p = null; }
        if (p?.Install is null || p.Fingerprint is null || p.Customer is null || p.Seal is null)
        {
            items.Add(new("payload", CheckStatus.Fail, "The payload is not a valid request."));
            return new ValidationReport(null, null, items);
        }

        var file = parsed.Value!;
        if (file.Envelope.Signature.Alg != FileFormats.AlgInstall ||
            !InstallKeyVerifier.Verify(p.Install.InstallPub, file.SignedMessage, file.SignatureBytes))
            items.Add(new("signature", CheckStatus.Fail, "The install-key signature does not verify (integrity only: anyone can produce a self-consistent request)."));
        else
            items.Add(new("signature", CheckStatus.Pass, "Install-key signature verifies (proves integrity, not the customer's identity)."));

        if (!RequestTypes.All.Contains(p.RequestType))
            items.Add(new("request_type", CheckStatus.Fail, $"Unknown request type '{p.RequestType}'."));
        if (string.IsNullOrWhiteSpace(p.Nonce) || p.Nonce.Length < 16)
            items.Add(new("nonce", CheckStatus.Fail, "The nonce is missing or too short."));
        else if (_ledger.NonceSeen(p.Nonce))
            items.Add(new("nonce", CheckStatus.Fail, "This request nonce was already used. Ask the customer for a new request."));
        else
            items.Add(new("nonce", CheckStatus.Pass, "Nonce not seen before."));

        if (p.CreatedUtc > now.AddMinutes(MaxFutureMinutes))
            items.Add(new("created", CheckStatus.Fail, "The request is dated in the future."));
        else if (p.CreatedUtc < now.AddDays(-MaxAgeDays))
            items.Add(new("created", CheckStatus.Warn, $"The request is older than {MaxAgeDays} days."));
        else
            items.Add(new("created", CheckStatus.Pass, "Creation time plausible."));

        var customer = _ledger.GetCustomer(p.Customer.CustomerRef);
        items.Add(customer is null
            ? new("customer", CheckStatus.Fail, $"Customer reference '{p.Customer.CustomerRef}' is not in the register. Add the customer after verifying identity (procedure #123).")
            : new("customer", CheckStatus.Pass, $"Customer '{customer.Name}'. Confirm the identity of the requester before issuing."));

        if (p.RequestType != RequestTypes.Activation)
        {
            var known = p.PreviousLicenceId is null ? null : _ledger.GetLicence(p.PreviousLicenceId);
            if (known is null)
                items.Add(new("previous_licence", CheckStatus.Warn, "The previous licence is not in this ledger (import it first, or this is a replacement for a lost one)."));
            else if (known.InstallId != p.Install.InstallId && p.RequestType is RequestTypes.Renewal or RequestTypes.Refresh)
                items.Add(new("previous_licence", CheckStatus.Fail, "A renewal or refresh must come from the install the previous licence was bound to."));
            else if (known.Status == "revoked")
                items.Add(new("previous_licence", CheckStatus.Fail, "The previous licence was revoked."));
            else
                items.Add(new("previous_licence", CheckStatus.Pass, $"Supersedes {known.LicenceId}."));
        }

        var witness = _ledger.WitnessFor(p.Install.InstallId);
        if (witness is not null && p.Seal.Counter < witness.MaxCounter && p.RequestType != RequestTypes.Restore)
            items.Add(new("witness", CheckStatus.Warn, $"Counter {p.Seal.Counter} is behind the highest seen for this install ({witness.MaxCounter}): possible snapshot restore. Ask the customer for a restore request."));
        else if (witness is not null && p.Seal.Counter < witness.MaxCounter)
            items.Add(new("witness", CheckStatus.Pass, $"Restore request: counter {p.Seal.Counter} is behind the ledger value {witness.MaxCounter}, as expected after a restore."));
        else
            items.Add(new("witness", CheckStatus.Pass, "Counter is consistent with the ledger."));

        var clones = _ledger.CloneCandidates(p.Install.InstallId, p.Fingerprint.FpHash, p.Customer.CustomerRef);
        items.Add(clones.Count == 0
            ? new("clone_flags", CheckStatus.Pass, "No other licence shares this fingerprint or Install ID under another customer.")
            : new("clone_flags", CheckStatus.Warn, "Possible clone: " + string.Join(", ", clones.Select(c => $"{c.LicenceId} ({c.CustomerRef})")) + "."));

        if (p.RequestType == RequestTypes.Transfer)
        {
            var count = _ledger.Licences(p.Customer.CustomerRef).Where(l => l.RequestType == RequestTypes.Transfer && l.Status != "revoked").Select(l => l.TransferCount).DefaultIfEmpty(0).Max();
            items.Add(count >= _transferCap
                ? new("transfer_cap", CheckStatus.Warn, $"Transfer count {count} has reached the cap {_transferCap}: manual approval required (operator note).")
                : new("transfer_cap", CheckStatus.Pass, $"Transfers so far {count} of {_transferCap}."));
        }

        items.Add(p.Fingerprint.Virtualization != "none"
            ? new("virtualization", CheckStatus.Warn, $"Virtualisation detected: {p.Fingerprint.Virtualization}. Apply the VM/Docker policy (extra protection, EULA clause).")
            : new("virtualization", CheckStatus.Pass, "Physical machine."));

        return new ValidationReport(p, file.PayloadBytes, items);
    }
}
