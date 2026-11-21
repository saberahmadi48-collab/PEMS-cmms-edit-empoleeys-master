<#
.SYNOPSIS
  Dry-run (default) or apply the cleanup of junk test disciplines and unreferenced duplicate disciplines.

.DESCRIPTION
  Wraps scripts/rcm/rcm-discipline-cleanup.sql. Without -Apply nothing is changed. With -Apply you must also pass
  -ConfirmDatabase <name> equal to -Database (typing the target database name is the confirmation). The script only
  sets IsActive = 0 (reversible); it never deletes rows.

.EXAMPLE
  ./scripts/rcm/Invoke-RcmDisciplineCleanup.ps1                                   # dry-run on PEMSDB
  ./scripts/rcm/Invoke-RcmDisciplineCleanup.ps1 -Apply -ConfirmDatabase PEMSDB    # apply
#>
[CmdletBinding()]
param(
    [string]$Server = 'localhost\SQLEXPRESS',
    [string]$Database = 'PEMSDB',
    [switch]$Apply,
    [string]$ConfirmDatabase
)

$ErrorActionPreference = 'Stop'
$sqlFile = Join-Path $PSScriptRoot 'rcm-discipline-cleanup.sql'
if (-not (Test-Path $sqlFile)) { throw "Missing $sqlFile" }

if ($Apply -and $ConfirmDatabase -ne $Database) {
    throw "Refusing to apply: pass -ConfirmDatabase '$Database' to confirm the target database."
}

$mode = if ($Apply) { 1 } else { 0 }
Write-Host ("Discipline cleanup {0} -> server '{1}', database '{2}'" -f ($(if ($Apply) { 'APPLY' } else { 'DRY-RUN' })), $Server, $Database) -ForegroundColor Cyan
sqlcmd -S $Server -d $Database -E -C -W -b -v Apply=$mode -i $sqlFile
if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed with exit code $LASTEXITCODE" }
