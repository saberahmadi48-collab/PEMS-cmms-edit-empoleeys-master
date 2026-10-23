<#
.SYNOPSIS
  Writes a patch set (git diff + new files) containing ONLY the files of the CMMS audit-fix work, so it can be reviewed or applied elsewhere
  with `git apply`. Nothing in the repository is changed; nothing is committed.

.EXAMPLE
  .\Export-CmmsAuditPatch.ps1 -OutDir C:\temp\cmms-audit-patch
  git apply --check C:\temp\cmms-audit-patch\cmms-audit.patch   # then: git apply ...
#>
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutDir)
$ErrorActionPreference = 'Continue'   # git writes CRLF warnings to stderr
$repo = (git rev-parse --show-toplevel).Trim()
Set-Location $repo
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# Files owned by this work (existing files that were modified + new files)
$paths = @(
  'PEMS.Application/Common/LaborRateMissingException.cs','PEMS.Application/Common/WorkOrderSectionPolicy.cs','PEMS.Application/Common/WorkRequestPolicyOptions.cs',
  'PEMS.Application/Common/WorkRequestRiskAssessor.cs','PEMS.Application/Common/WorkflowActionRules.cs','PEMS.Application/DTOs/FailureOption.cs','PEMS.Application/DTOs/WorkRequestDto.cs',
  'PEMS.Application/Interfaces/IWorkOrderSectionGuard.cs','PEMS.Application/Interfaces/IWorkOrderLaborService.cs','PEMS.Application/Interfaces/IWorkOrderLifecycleService.cs','PEMS.Application/Interfaces/IWorkRequestService.cs',
  'PEMS.Domain/Entities/WorkOrder.cs','PEMS.Domain/Entities/WorkRequest.cs','PEMS.Domain/Entities/WorkOrderChangeLog.cs',
  'PEMS.Persistence/Configurations/WorkOrderChangeLogConfiguration.cs','PEMS.Persistence/Context/PEMSDbContext.cs','PEMS.Persistence/Migrations',
  'PEMS.Persistence/Seed/DisciplineSeeder.cs','PEMS.Persistence/Seed/MaintenanceGroupSeeder.cs','PEMS.Persistence/Seed/WorkOrderReopenPermissionSeeder.cs',
  'PEMS.Persistence/Servises/CrewService.cs','PEMS.Persistence/Servises/NumberingService.cs','PEMS.Persistence/Servises/WorkOrderLaborService.cs','PEMS.Persistence/Servises/WorkOrderLifecycleService.cs',
  'PEMS.Persistence/Servises/WorkOrderSectionGuard.cs','PEMS.Persistence/Servises/WorkRequestService.cs',
  'PEMS.WorkflowPlatform/Interfaces/WorkflowPlatformContracts.cs','PEMS.WorkflowPlatform/Services/WorkflowRuntimeService.cs',
  'PEMS.Web/Areas/Admin/Controllers/UsersController.cs','PEMS.Web/Controllers/CrewController.cs','PEMS.Web/Controllers/HomeController.cs','PEMS.Web/Controllers/WorkOrderController.cs','PEMS.Web/Controllers/WorkRequestController.cs',
  'PEMS.Web/Models/DashboardDefinitions.cs','PEMS.Web/Models/DashboardViewModel.cs','PEMS.Web/Models/KpiCardViewModel.cs',
  'PEMS.Web/Resources/SharedResource.fa.resx','PEMS.Web/Views/Home/Index.cshtml','PEMS.Web/Views/Shared/_KpiCard.cshtml','PEMS.Web/Views/WorkOrder/Details.cshtml','PEMS.Web/Views/WorkRequest',
  'PEMS.Tests/Cmms','PEMS.Tests/Security/CmmsEndpointSecurityTests.cs','PEMS.Tests/Security/SecurityAuditLogTests.cs','PEMS.Tests/TestSupport/RazorViewRenderer.cs',
  'PEMS.Tests/Web/LocalizationKeyCoverageTests.cs','PEMS.Tests/Authorization/Phase8AuthorizationGapTests.cs','PEMS.Tests/Authorization/WorkOrderTaskActionsAuthorizationTests.cs',
  'PEMS.Tests/Organization/EmployeeRoleDisciplineManagementTests.cs','scripts/cmms','docs/cmms'
)
# PEMS.Web/Program.cs also holds unrelated procurement edits: it is exported separately for manual merge (see below)
# tracked files: normal diff; new (untracked) files: --no-index diff against nothing. The index is never touched.
$out = Join-Path $OutDir 'cmms-audit.patch'
git -c core.safecrlf=false diff --binary -- $paths 2>$null | Set-Content -Encoding utf8 $out
$untracked = git ls-files --others --exclude-standard -- $paths
foreach ($u in $untracked) { git -c core.safecrlf=false diff --no-index --binary -- NUL $u 2>$null | Add-Content -Encoding utf8 $out }
git -c core.safecrlf=false diff -- PEMS.Web/Program.cs 2>$null | Set-Content -Encoding utf8 (Join-Path $OutDir 'Program.cs.full-diff.patch')
Write-Host "Patch written to $OutDir. Program.cs contains other pending edits; apply only these hunks by hand: IWorkOrderSectionGuard DI, WorkRequestPolicyOptions registration, WorkOrderReopenPermissionSeeder call."
