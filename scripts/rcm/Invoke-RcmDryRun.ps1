<#
.SYNOPSIS
  Read-only report of what the RCM class-level migration would change (run this BEFORE `dotnet ef database update`).

.DESCRIPTION
  Executes scripts/rcm/rcm-dry-run-report.sql with sqlcmd. The script only counts rows, uses only legacy columns
  (so it works on the pre-migration schema) and modifies nothing. Prints the target server/database first.

.EXAMPLE
  ./scripts/rcm/Invoke-RcmDryRun.ps1 -Server 'localhost\SQLEXPRESS' -Database PEMSDB
#>
[CmdletBinding()]
param(
    [string]$Server = 'localhost\SQLEXPRESS',
    [string]$Database = 'PEMSDB'
)

$ErrorActionPreference = 'Stop'
$sqlFile = Join-Path $PSScriptRoot 'rcm-dry-run-report.sql'
if (-not (Test-Path $sqlFile)) { throw "Missing $sqlFile" }

Write-Host "RCM migration DRY-RUN (read-only) -> server '$Server', database '$Database'" -ForegroundColor Cyan
sqlcmd -S $Server -d $Database -E -C -W -b -i $sqlFile
if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed with exit code $LASTEXITCODE" }

Write-Host "`nNothing was changed. Review the numbers, then run Apply-RcmMigration.ps1." -ForegroundColor Green
