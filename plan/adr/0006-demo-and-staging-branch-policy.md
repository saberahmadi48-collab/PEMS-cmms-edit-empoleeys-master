# ADR-0006: Demo branch policy (with the later staging branch)

- **Status:** Accepted
- **Date:** 2026-10-18 (amended 2026-10-25, see Amendments)
- **Decided by:** Hamid Maleki, project owner and Member A (requirement of 2026-10-18 recorded in plan row #300; Instructions rule 18). Row #300 itself is "Confirmed by PM: No" and "Not Started": the branch rules below are the plan text, the branch model change of 2026-10-25 is recorded in the deployment README
- **Approved by:** Hamid Maleki (Member A), 2026-10-27, in the SCRUM-14 session
- **Plan rows:** #300 (branch policy), #288, #258 (dependencies), #304 (demo content), #351 (staging); written under #2 (SCRUM-14)
- **Sources:** `plan/rows/0300.md`; Instructions rule 18; [DEPLOYMENT_README.md](../../docs/deployment/DEPLOYMENT_README.md); [chapter 00 section 3](../../docs/guidelines/00-task-lifecycle.md); Supportability Matrix SM-34

## Context

Owner requirement 2026-10-18: the demo is a branch of the main product repository, while the landing website is a separate repository
(`pems-landing`). Without rules the demo branch drifts from main and becomes a fork. The landing site and the demo share one host but
nothing else (rule 18).

## Decision

1. The demo is a **long-lived `demo` branch** of the product repository. Product changes are made only on `main`; no commit from `demo` is ever merged into `main`.
2. Demo-only behaviour is a demo-mode configuration flag in the product (see DM_PERSONA, DM_SAFE), not a code edit on the demo branch.
3. The branch is protected and push rights are limited. A push to `demo` deploys the demo environment.
4. Landing website (repository `pems-landing`) and demo have separate source, pipelines, deploy accounts, secrets, environments, monitoring and rollback (rule 18); both deploy by GitHub Actions with a manual-artifact fallback.

**Planned in row #300, not implemented as of this ADR:** a separate overlay directory (demo data pack, reset scripts, workflow, configuration),
an automatic scheduled sync from `main` that merges or opens a pull request, and a check that fails any change on `demo` outside the overlay.
The row's Requirement ("the demo branch differs from main only by the overlay directory") still stands as the target.

## Alternatives considered

| Option | Why it was not chosen |
|---|---|
| Demo as part of the landing-website repository | Excluded by the owner requirement: the demo is a branch of the product repository, the landing site is separate. |
| Free editing on the demo branch | The risk the row names: the branch drifts from main and becomes a fork. |

## Consequences

- Good: demo always runs shipped product code; failure or redeploy of the demo cannot touch the landing site.
- Cost: demo-only behaviour must be built as configuration flags in the product; the overlay and sync automation are still to be built under #300.
- Deploying to `demo` or `staging` happens only when the owner asks (guideline chapter 00, section 6).

## Amendments

- 2026-10-25: the `dev` branch was retired and a `staging` branch added (Supportability Matrix SM-34, [DEPLOYMENT_README.md](../../docs/deployment/DEPLOYMENT_README.md)). `main` is the only integration branch and runs no pipeline; `staging` (staging subdomain, own database, `env/staging.env`, `STAGING_*` secrets) and `demo` (`DEMO_*`) deploy through the same reusable workflow `deploy-site.yml`. Promotion is a merge in the order working branch -> `main` -> `staging` -> `demo`. Today promotion is therefore a manual, owner-directed merge; the automatic sync of row #300 is not built. The rule that product changes originate on `main` is unchanged.
