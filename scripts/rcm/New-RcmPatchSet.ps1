<#
.SYNOPSIS
  Produces a reviewable patch set (one .patch file + a manifest) for the RCM rework, without touching the git index.

.DESCRIPTION
  * Modified tracked files  -> `git diff` against HEAD.
  * New files               -> `git diff --no-index` against NUL (so the index is never modified).
  The listed shared files (Program.cs, PEMSDbContext.cs, resx, WorkOrderController, WO Details view, model snapshot)
  also contain unrelated uncommitted work that already existed in the working tree; review those hunks accordingly.

.EXAMPLE
  ./scripts/rcm/New-RcmPatchSet.ps1 -OutDir ./rcm-patchset
#>
[CmdletBinding()]
param([string]$OutDir = (Join-Path (Get-Location) 'rcm-patchset'))

$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
Push-Location $repo
try {
    New-Item -ItemType Directory -Force $OutDir | Out-Null
    $manifest = Join-Path $OutDir 'manifest.txt'
    $patch = Join-Path $OutDir 'rcm-rework.patch'
    Remove-Item $patch, $manifest -ErrorAction SilentlyContinue

    $paths = @('PEMS.Domain', 'PEMS.Application', 'PEMS.Persistence', 'PEMS.Web', 'PEMS.Tests', 'scripts/rcm')
    $changed = git status --porcelain -- $paths | ForEach-Object { $_.Substring(3).Trim('"') }
    $rcmPattern = 'Rcm|MaintenanceStrategy|MaintenancePlan|StrategyAssignment|StrategyImport|StrategyCoverage|FailureModeCatalog|PMGeneration|EquipmentSearchProvider|pems-global-search|Error\.cshtml|_EquipmentIdentity|BulkDefine|RcmOperationalBridge|EquipmentReadiness|EquipmentIntelligence|ConditionMonitoringService|RcmFeedback|scripts/rcm|WorkOrderController|WorkOrder/Details|PEMSDbContext\.cs|Program\.cs|SharedResource|ModelSnapshot|RcmCodes|LegacyMaps'
    $mine = $changed | Where-Object { $_ -match $rcmPattern }

    $mine | Set-Content $manifest
    foreach ($f in $mine) {
        $tracked = git ls-files --error-unmatch -- $f 2>$null
        if ($LASTEXITCODE -eq 0 -and $tracked) { git diff HEAD -- $f | Add-Content -Encoding utf8 $patch }
        else { git diff --no-index -- NUL $f | Add-Content -Encoding utf8 $patch }
    }
    Write-Host "Patch: $patch  ($($mine.Count) files listed in $manifest)" -ForegroundColor Green
}
finally { Pop-Location }
