# ADR-0012: Main host platform: Windows/IIS for the shared main host, Linux and Docker as the second supported install, one artifact

- **Status:** Proposed (for owner approval; nothing below is decided until the "Approved by" line is filled)
- **Date:** 2026-11-03 (proposal; the decision date is the approval date)
- **Decided by:** not yet. Proposed by Claude for Hamid Maleki (owner, Member A) on his answer of 2026-11-03: "both of them", asking whether supporting both is possible and worthwhile
- **Approved by:** not yet
- **Plan rows:** #287 (SCRUM-305); related #295, #296, #299 (demo pipeline, staging, runbook), #294 (landing pipeline)
- **Sources:** [DEPLOYMENT_README.md](../../docs/deployment/DEPLOYMENT_README.md), [RUNBOOK_DEPLOY_SITES.md](../../docs/deployment/RUNBOOK_DEPLOY_SITES.md), [LINUX_VM_DEPLOYMENT.md](../../docs/deployment/LINUX_VM_DEPLOYMENT.md), [ADR-0006](0006-demo-and-staging-branch-policy.md), [ADR-0005](0005-licensing-threat-model-and-vm-policy.md), `deploy/`, `.github/workflows/deploy-site.yml`, Supportability Matrix rows SM-01 (physical Windows Server), SM-02 (virtual machines), SM-03 (Docker containers)

## Context

Row #287 left four things open: host OS and web server, whether each site runs as a service or a container, and where SQL Server runs for the demo. The deploy channel is decided (GitHub Actions deploys when the host is reachable, otherwise the artifact is uploaded for manual deployment). The repository already runs **both** families, so the question is which one plays which role:

| | Shared main host (today) | Ubuntu VM kit (today) |
|---|---|---|
| Used for | `demo.parmisata.ir`, `staging.parmisata.ir` (landing: manual, #294) | the internal VM `pems-server` (UAT and customer-style install) |
| OS and web server | Windows host with Plesk and IIS, ASP.NET Core Module | Ubuntu 24.04, Caddy reverse proxy (TLS internal CA) |
| Run model | self-contained `win-x64` build uploaded by FTP, `app_offline.htm` during a deploy | containers from Docker Compose: `pems-web`, `pems-sqlserver`, `pems-caddy`; `deploy\deploy.ps1` over SSH, release image built offline on the VM |
| SQL Server | a Microsoft SQL Server database per site created in Plesk (empty, the application installs the schema), separate database per environment | `pems-sqlserver` container with a persistent volume |
| Pipeline | `deploy-site.yml` called by `deploy-staging.yml` and `deploy-demo.yml` | `deploy/deploy.ps1` (manual), `deploy-uat.yml` (manual, Azure UAT) |

Facts that are **not recorded** anywhere in the repository: the Windows version of the shared host, whether the host can run Node.js (the Next.js landing site is a separate repository, `pems-landing`), whether Docker may be installed on the shared host.

## Decision (proposed)

1. **One artifact, two supported installs.** The product is one .NET application; it is published as a `win-x64` package (IIS / ASP.NET Core Module) and as a `linux-x64` container image. Both come from the same commit, the same migrator and the same `env/<name>.env` files (ADR-0003, ADR-0004). No code is written for one platform only.
2. **The shared main host stays Windows with Plesk and IIS** for `demo` and `staging`. It works today, the pipeline is built for it and moving it would cost a new pipeline for no customer benefit. SQL Server stays on the host's SQL Server, one database per environment, created in the panel, never shared.
3. **Linux with Docker Compose and Caddy is the supported install for customers and for UAT who prefer Linux or offline VMs** (ADR-0005 allows VMs and Docker). It keeps using `deploy/deploy.ps1`; the image is built offline from the base image.
4. **The landing website** keeps its own repository and pipeline (rule 18). Its runtime is decided in #294 once two facts are known: can the host run Node.js (then Next.js as a service behind IIS), or is a static export enough (then plain IIS files, no Node). Until then it is deployed manually.
5. **Guard against drift:** every release is built and smoke-tested for both targets in CI (publish for `win-x64` and `linux-x64`, container image builds, `GET /health/database` after start). A third platform (for example Windows Server with Docker, or Kubernetes) is not supported until a customer needs it and the Supportability Matrix row is written.

## Is supporting both possible and worthwhile?

- **Possible:** yes, and it already exists. .NET and the migrator run unchanged on both; the only platform code is the deploy scripts. Nothing in the product depends on IIS or on Linux.
- **Worthwhile:** for customers, yes, because industrial customers split between Windows servers and Linux or VM installs; it costs one extra CI smoke job and one extra runbook. For the **main host** it is not worthwhile: we operate that host ourselves, one platform is enough, and a second would double the pipeline and the failure modes of the demo.
- **Cost to watch:** two runbooks, two failure histories (the FTP and `ECONNRESET` issues of the Windows host versus the offline image build of the VM), and the Supportability Matrix must name exactly what is supported (OS version, SQL Server version) instead of "Windows and Linux".

## Alternatives considered

| Option | Why it was not chosen |
|---|---|
| Move the main host to Linux with nginx and containers | A new pipeline and a new host to run the demo, with no customer benefit; the Windows host and its FTP deploy work today. The Linux path is kept for customers. |
| Windows only (drop the Linux kit) | Customers with Linux or offline VMs exist (ADR-0005, the VM kit) and the kit is already written and tested. |
| Linux only | The shared host that serves the demo today is Windows; dropping it means paying for and migrating a host the owner already has. |
| IIS with Docker on the same host | Not recorded as available on the shared host; adds a third run model. |

## Consequences

- Good: no migration of the working demo host; customers get both install paths from one artifact; the decision answers #287 in one place.
- Cost or risk: two supported installs must stay tested; the landing runtime stays open until #294; the facts listed as not recorded must be checked before the approval (host Windows version, Node.js and Docker on the host).
- Follow-up work (plan rows): #294 (landing pipeline and runtime), Supportability Matrix rows for the two installs (names and versions), a CI job that builds and smoke-tests both targets (new task if the existing build workflow does not already cover it), #351 (staging copy stays Windows).

## Amendments

None yet.
