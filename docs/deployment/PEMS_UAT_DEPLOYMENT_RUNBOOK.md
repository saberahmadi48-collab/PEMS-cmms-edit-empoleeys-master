# PEMS UAT Deployment Runbook (self-service, Azure Container Apps + Azure SQL Database)

Follow these steps yourself in your own terminal with your own Azure login. **Never paste any password, connection string, or Azure CLI JSON output into this chat** — I don't need to see them, and keeping them out of the conversation transcript is safer. Where a step produces a secret, the instruction tells you exactly where it goes instead (a GitHub secret or an `az` command you run locally).

This runbook assumes the code-side prerequisites from `docs/audits/PEMS_UAT_DEPLOYMENT_PHASE1-3_REPORT.md` (already done): `appsettings.UAT.json`, the forwarded-headers/`/health` hardening in `Program.cs`, and `.github/workflows/deploy-uat.yml` all exist in the working tree already (uncommitted — commit them first, see Step 0).

---

## Step 0 — Commit the UAT-enabling changes

```powershell
git add PEMS.Web/Program.cs PEMS.Web/appsettings.UAT.json .github/workflows/deploy-uat.yml docs/audits/PEMS_UAT_DEPLOYMENT_PHASE1-3_REPORT.md docs/deployment/PEMS_UAT_DEPLOYMENT_RUNBOOK.md
git commit -m "Add UAT deployment hardening: health check, forwarded headers, UAT config, deploy workflow"
git push origin main
```
(Your other uncommitted Letter/Correspondence changes are unrelated to this — commit or stash them separately, your call; they don't need to be part of this commit.)

## Step 1 — Install the Azure CLI and sign in

```powershell
winget install Microsoft.AzureCLI
az login
az account show   # confirm the right subscription is active; az account set --subscription "<name>" if not
```

## Step 2 — Create a resource group for UAT

```powershell
az group create --name pems-uat-rg --location eastus
```

## Step 3 — Create the UAT Azure SQL Database (free offer)

Generate a strong password yourself (e.g. a password manager) — do not send it to me.

```powershell
az sql server create `
  --name pems-uat-sqlserver `
  --resource-group pems-uat-rg `
  --location eastus `
  --admin-user pemsuatadmin `
  --admin-password "<YOUR-OWN-STRONG-PASSWORD>"

az sql server firewall-rule create `
  --resource-group pems-uat-rg `
  --server pems-uat-sqlserver `
  --name AllowAzureServices `
  --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0

az sql db create `
  --resource-group pems-uat-rg `
  --server pems-uat-sqlserver `
  --name PEMSDB-UAT `
  --use-free-limit `
  --free-limit-exhaustion-behavior AutoPause
```

Build the connection string in this exact ADO.NET format (Azure SQL requires `Encrypt=True`, unlike the local dev connection string):
```
Server=tcp:pems-uat-sqlserver.database.windows.net,1433;Database=PEMSDB-UAT;User ID=pemsuatadmin;Password=<YOUR-OWN-STRONG-PASSWORD>;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;
```
Keep this string somewhere private for the next two steps — it goes into **(a)** your own terminal in Step 4 and **(b)** a GitHub secret in Step 6. It never goes into any file in this repository.

## Step 4 — Apply EF Core migrations to the new UAT database

From the repo root, in your own terminal (this uses the `PEMSDbContextFactory` env-var override that was fixed earlier this session — verify it prints "PEMSDB-UAT" before it applies anything):

```powershell
$env:ConnectionStrings__PEMSConnection = "Server=tcp:pems-uat-sqlserver.database.windows.net,1433;Database=PEMSDB-UAT;User ID=pemsuatadmin;Password=<YOUR-OWN-STRONG-PASSWORD>;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"

dotnet ef dbcontext info --project PEMS.Migrations --startup-project PEMS.Web
# ^ MUST print "Database name: PEMSDB-UAT" — if it still says PEMSDB, stop, do not proceed

dotnet ef database update --project PEMS.Migrations --startup-project PEMS.Web

Remove-Item Env:\ConnectionStrings__PEMSConnection
```
This creates the full schema (Identity tables, all PEMS entities, Hangfire tables get created automatically by the app on first run). No seed data exists yet — the app's own startup seeders (roles, permissions, departments, disciplines, etc.) will run automatically the first time the container starts against this database.

## Step 5 — Create the Container Apps environment

```powershell
az extension add --name containerapp --upgrade
az provider register --namespace Microsoft.App
az provider register --namespace Microsoft.OperationalInsights

az containerapp env create `
  --name pems-uat-env `
  --resource-group pems-uat-rg `
  --location eastus
```

## Step 6 — Create a service principal for GitHub Actions and set the 4 repo secrets

```powershell
az ad sp create-for-rbac --name "pems-uat-github-deploy" --role contributor `
  --scopes /subscriptions/<YOUR-SUBSCRIPTION-ID>/resourceGroups/pems-uat-rg `
  --sdk-auth
```
This prints a JSON blob. Go straight to GitHub — do not paste it here:

**GitHub → this repository → Settings → Secrets and variables → Actions → New repository secret**, create these 4:

| Secret name | Value |
|---|---|
| `AZURE_CREDENTIALS` | the entire JSON blob from the command above |
| `AZURE_RESOURCE_GROUP` | `pems-uat-rg` |
| `AZURE_CONTAINERAPPS_ENV` | `pems-uat-env` |
| `UAT_SQL_CONNECTION_STRING` | the connection string from Step 3 |

## Step 7 — Trigger the deploy

**GitHub → Actions tab → "PEMS UAT Deploy (manual only)" → Run workflow**, type `UAT` in the confirmation box, run it. Watch the job — it restores, builds, **runs the full test suite (will fail the deploy if any test fails)**, then builds the existing `Dockerfile` and deploys it to Container Apps.

## Step 8 — Get the live URL and fix Fido2

```powershell
az containerapp show --name pems-uat --resource-group pems-uat-rg --query properties.configuration.ingress.fqdn -o tsv
```
This prints something like `pems-uat.<random>.eastus.azurecontainerapps.io`. Then:

```powershell
az containerapp update --name pems-uat --resource-group pems-uat-rg `
  --set-env-vars `
    "Fido2__ServerDomain=pems-uat.<random>.eastus.azurecontainerapps.io" `
    "Fido2__Origins__0=https://pems-uat.<random>.eastus.azurecontainerapps.io"
```

## Step 9 — Verify (I can help with this part once you have the URL)

Send me the `https://...azurecontainerapps.io` URL (it's not a secret) and I can verify from here:
- `curl -I https://<url>/health` → expect `200`/`Healthy`
- `curl -I https://<url>/Letter/Index` → expect a `302` to `/Account/Login` (proves the app booted and the auth gate works, same check performed earlier this session against the local dev server)
- TLS certificate validity

I still cannot log in (no credentials), so Step 10 below is on you.

## Step 10 — Create the 9 UAT users (reuses the existing Admin UI, no new mechanism)

Log in as the default seeded SystemAdmin account first (the app's `IdentitySeeder` creates one on first startup — check `PEMS.Web/Security/IdentitySeeder.cs` in your own terminal for what it seeds, since I won't print any password here either), then in the app itself:

**Admin → Users → Create User**, once per row in the UAT User Matrix in `docs/audits/PEMS_UAT_DEPLOYMENT_PHASE1-3_REPORT.md` §3.2 — set each user's role via the existing role-assignment UI, and set `DisciplineId` for the three `DisciplineEngineer` accounts (Mechanical/Electrical/Instrumentation) via **Admin → Users → Edit**.

Force a password change on first login if the Admin UI's "must change password" flag is available (`ApplicationUser.MustChangePassword` exists in the entity — reuse it, don't invent a new mechanism), and hand out credentials to your colleagues outside of any chat or ticketing system that logs plaintext (a password manager's share feature, or verbally).

---

## What happens next

Once you've done Steps 0-10 and sent me the URL, I'll pick back up at Phase 9 (functional UAT: create a Work Request, approve it, create a Letter, upload a file) to whatever extent is testable via HTTP without me holding real login credentials, and Phase 11 (security verification: headers, HTTPS, unauthorized-access checks) — and I'll tell you plainly which parts still need you and your colleagues at the keyboard (Phase 10's real concurrent multi-user test, in particular, needs real people, not me).

## Rollback

```powershell
# Roll the Container App back to the previous revision without a new deploy
az containerapp revision list --name pems-uat --resource-group pems-uat-rg -o table
az containerapp ingress traffic set --name pems-uat --resource-group pems-uat-rg --revision-weight <previous-revision-name>=100

# Full teardown if needed (irreversible - only if you want to scrap UAT entirely)
az group delete --name pems-uat-rg --yes --no-wait
```
