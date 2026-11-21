# ADR-0005: Licensing threat model and VM/container policy

- **Status:** Accepted
- **Date:** 2026-10-18
- **Decided by:** Hamid Maleki, project owner and Member A (plan rows #96 and #97, both "Confirmed by PM: Yes"; the timed-licence rule of 2026-10-15 is quoted in row #97)
- **Approved by:** Hamid Maleki (Member A), 2026-10-27, in the SCRUM-14 session
- **Plan rows:** #96 (threat model), #97 (VM/container policy); implemented by L_FP, L_CLONE, L_EULA and the licensing chain #98-#120; written under #2 (SCRUM-14)
- **Sources:** `plan/rows/0096.md` and `plan/rows/0097.md`; plan page *Licensing Design* (`plan/sheets/licensing-design.md`) (threat and defense table); Supportability Matrix SM-02, SM-03, SM-07; Instructions rules 10, 11, 20

## Context

PEMS is sold on-premise and runs offline. The owner goal: lock a licence to the machine and install time so that even a full machine image
redeployed elsewhere does not work. No offline scheme is unbreakable. Timed licences are a time period with a start and an end date;
count-based licences are not offered (owner decision 2026-10-15). A VM or container fingerprint can be cloned, so the sales policy on
VMs and Docker limits how strong the anti-clone defense can be.

## Decision

1. **Protection level:** defeat casual copying and back the rest with a contract. The defenses are a machine fingerprint, an install seal, a signed licence, a restore-detection counter, code obfuscation and a strong EULA. **No hardware token.**
2. **VMs and Docker are allowed with extra protection:** a virtual TPM or hypervisor identifier where available, the restore-detection counter (L_CLONE) and a no-cloning EULA clause (L_EULA).
3. The scenario x defense x residual-risk table lives in the *Licensing Design* page (`plan/sheets/licensing-design.md`) (ten scenarios: image on other hardware, VM clone, snapshot restore, clock change, binary patch, licence file reuse, signing-key leak, issuing tool in the customer package, development bypass, legitimate re-host). It is the Requirement of row #96 and is not copied here, to keep one source.
4. Standing rules that belong to this decision: the licence-signing and reset private keys exist only on the owners' computers and never in the repo, a CI or shared server (rule 10); the licence is active in every environment with no bypass flag and free surfaces only from an allowlist (rule 11); licensing operations are completable in a UI (rule 20); the product never phones home (rule 1).

## Alternatives considered

| Option | Why it was not chosen |
|---|---|
| Hardware token (dongle) | Excluded by the owner: no hardware token (row #96). |
| Forbid VMs and containers | Not chosen; VMs and Docker are accepted with the conditions above (row #97). |
| Count-based licences | Not offered (owner decision 2026-10-15, quoted in row #97). |

## Consequences

- Good: a licence is bound to Install ID, fingerprint and nonce, so a copied file or imaged machine is rejected; VM and Docker customers are supported.
- Residual risk (from the *Licensing Design* page): VM clone and snapshot restore stay at Medium and need the EULA; binary patching stays at Medium; a leak of the private signing key is Critical.
- Supportability Matrix lists VMs (SM-02) and Docker (SM-03) as supported with these conditions; row #97 requires L_FP, L_CLONE and L_EULA to implement the policy.
- Follow-up: licensing chain rows #98-#120, EULA, restore-detection counter.
