# Licensing Design

PEMS licensing design - machine and install-time locked (on-prem, offline)

Owner need: even if a whole machine is imaged and deployed elsewhere it must not work; a file is generated on the customer server, returned to us, and we deliver the real licence; lifetime or timed. Limits: no offline scheme is unbreakable - the goal is to defeat casual copying and add contractual backing.

| Step | Stage | Description | Backlog rows |
| --- | --- | --- | --- |
| 1 | Install | The installer runs; first start creates a random Install ID and a key pair; the private key is made non-exportable with DPAPI/TPM; install time and a monotonic counter are written to the 'install seal' (DB + an independent file). | #99 |
| 2 | Request | Without a licence the system starts in 'activation required' mode and creates a .pemsreq file: hashed fingerprint, Install ID, install time, version, customer id, nonce, install-key signature. | #98 #100 #108 |
| 3 | Return to vendor | The customer gives the file to us through the website portal (WEB_ACT) or by hand; the product itself never calls out. | #161 #123 |
| 4 | Issue | The vendor tool (L_VENDOR) validates the signature and structure, selects edition/modules/limits/type (lifetime or term) and signs a .pemslic with the offline private key; the licence is bound to the nonce, Install ID and fingerprint hash of that request. | #101 #102 #106 #107 |
| 5 | Activate | The customer uploads the licence (or a zip); verification: signature, Install ID match, fingerprint, seal, monotonic counter, expiry; then an automatic restart. | #103 #108 |
| 6 | Operate | Multiple checks at startup/periodic/sensitive operations; expiry warnings for term licences; failure means restricted read-only mode, not a crash. | #103 #105 #115 |
| 7 | Renew/transfer | Renewal with a small file; hardware replacement with a new request and revocation of the old licence (re-host) with a cap on count. | #115 #116 #117 |

Threat and defense table

| # | Attack scenario | Defense | Residual risk | Rows |
| --- | --- | --- | --- | --- |
| 1 | Full machine image on different hardware | Multi-signal fingerprint plus a non-exportable install key (DPAPI/TPM); the seal cannot be opened | Low | #98 #99 |
| 2 | VM clone (same virtual hardware appearance) | VM/container policy (L_TIMES); virtual TPM / hypervisor identifiers; monotonic counter and lifecycle trail | Medium - needs a commercial decision and contract | #97 #104 #120 |
| 3 | Restoring an old snapshot on the same machine | Monotonic counter plus audit hash chain; a lower counter is suspicious; re-activation required | Medium | #104 |
| 4 | Setting the clock back for a term licence | Sealed last-seen time; a jump back causes a soft lock | Low | #105 |
| 5 | Binary patch / removing the licence check | Several independent checks, obfuscation, assembly signing, integrity checks | Medium (no software defense is absolute) | #103 #118 |
| 6 | Copying or sharing a licence file between customers | The licence is bound to Install ID + fingerprint + nonce; another install rejects it | Low | #101 |
| 7 | Leak of the private signing key | Offline key/HSM, two-person control, public-key rotation via update | Critical if it happens | #107 |
| 8 | Licence issuing tool inside the customer package | Two flags (build-time/run-time); package allowlist scan | Low | #106 #119 |
| 9 | Leftover bypass from development | No bypass flag; a signed dev licence; release test | Low | #110 |
| 10 | Recovery after hardware failure (legitimate user, new machine) | Official re-host path with vendor approval and a cap on count | Operational | #116 #171 |
