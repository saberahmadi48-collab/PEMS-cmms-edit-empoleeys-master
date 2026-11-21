# PEMS vendor tool (licence issuing)

Plan rows #106 and #341, design: [LICENSING_TECHNICAL_DESIGN.md](../../docs/design/LICENSING_TECHNICAL_DESIGN.md) section 9.
A separate web app that runs **only on an owner computer** (Hamid's or Saber's own), on the loopback address. It is never part of a customer package and never runs on a server or CI.

## Run

```bash
cp tools/PemsVendorTool/vendor.env.example vendor.env      # edit DataDirectory and OperatorName
VENDOR_ENV_FILE=./vendor.env dotnet run --project tools/PemsVendorTool
# open http://127.0.0.1:5290
```

Two flags keep it out of customer builds: the `PEMS_VENDOR_TOOL` compile constant (set only in this project) and `VendorTool__Mode=true` at run time. `PEMS.Tests` fails if `PEMS.Web` or any product project references this tool.

## Pages (every vendor step is a UI step, rule 20)

| Page | Does |
|---|---|
| Customers | register a customer reference after checking identity (SOP #123) |
| Keys | generate an Ed25519 signing key (stored encrypted, passphrase typed per session, never exported); show the public `trusted-keys.json` content to commit |
| Issue | upload a `.pemsreq`, see the checks (signature, nonce, customer, witness counter, clone flags, transfer cap, virtualisation), choose edition / modules / type / Jalali dates / deployment, sign and download the `.pemslic` |
| Ledger | list and filter issued licences, download, revoke, export CSV |
| Import | add a licence issued before the ledger existed (signature must verify) |
| Audit | hash-chained audit log and its integrity check |

Data lives in `VendorTool__DataDirectory`: `ledger.sqlite` and `keys/vendor-key-<id>.json` (encrypted). Back both up as the key procedure (plan row #107, `docs/process/KEY_MANAGEMENT.md`) says; never commit them (`vendor-data/` is ignored).
