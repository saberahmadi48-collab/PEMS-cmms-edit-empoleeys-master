<#
.SYNOPSIS
  Controlled application of the RCM class-level migration (EF migration RcmClassLevelStrategy + data migration).

.DESCRIPTION
  Default (no -Execute): prints the target, lists the migrations still pending in that database (some belong to
  other in-progress work and will be applied too), and runs the read-only dry-run report. Nothing is changed.
  With -Execute (and -ConfirmDatabase <name>): takes a COPY_ONLY backup (unless -SkipBackup), runs
  `dotnet ef database update`, then prints the post-migration verification queries.
  The data migration inside the EF migration is idempotent and never deletes rows.

.EXAMPLE
  ./scripts/rcm/Apply-RcmMigration.ps1
  ./scripts/rcm/Apply-RcmMigration.ps1 -Execute -ConfirmDatabase PEMSDB -BackupPath 'D:\Backups\PEMSDB_before_rcm.bak'
#>
[CmdletBinding()]
param(
    [string]$Server = 'localhost\SQLEXPRESS',
    [string]$Database = 'PEMSDB',
    [switch]$Execute,
    [string]$ConfirmDatabase,
    [string]$BackupPath,
    [switch]$SkipBackup
)

$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$connection = "Server=$Server;Database=$Database;Trusted_Connection=True;TrustServerCertificate=True"
$env:ConnectionStrings__PEMSConnection = $connection   # PEMSDbContextFactory honours this override and prints the resolved target

Write-Host "Target: $connection" -ForegroundColor Cyan
Push-Location $repo
try {
    Write-Host "`n--- Pending migrations in the target database ---" -ForegroundColor Yellow
    dotnet ef migrations list --project PEMS.Persistence --startup-project PEMS.Persistence --no-build 2>&1 | Select-Object -Last 15

    Write-Host "`n--- Dry-run report (read-only) ---" -ForegroundColor Yellow
    & (Join-Path $PSScriptRoot 'Invoke-RcmDryRun.ps1') -Server $Server -Database $Database

    if (-not $Execute) {
        Write-Host "`nDry-run only. Re-run with -Execute -ConfirmDatabase $Database to apply." -ForegroundColor Green
        return
    }
    if ($ConfirmDatabase -ne $Database) { throw "Refusing to execute: pass -ConfirmDatabase '$Database'." }

    if (-not $SkipBackup) {
        if (-not $BackupPath) { throw 'Pass -BackupPath <file.bak> (a folder the SQL Server service can write) or -SkipBackup.' }
        Write-Host "`nBacking up (COPY_ONLY) to $BackupPath" -ForegroundColor Yellow
        sqlcmd -S $Server -E -C -b -Q "BACKUP DATABASE [$Database] TO DISK = N'$BackupPath' WITH COPY_ONLY, INIT, COMPRESSION"
        if ($LASTEXITCODE -ne 0) { throw 'Backup failed; migration not applied.' }
    }

    Write-Host "`nApplying migrations..." -ForegroundColor Yellow
    dotnet ef database update --project PEMS.Persistence --startup-project PEMS.Persistence
    if ($LASTEXITCODE -ne 0) { throw 'dotnet ef database update failed.' }

    Write-Host "`n--- Post-migration verification ---" -ForegroundColor Yellow
    $verify = @"
SET NOCOUNT ON;
SELECT Status, COUNT(*) AS Strategies FROM MaintenanceStrategies GROUP BY Status;
SELECT 'assignments' AS Item, COUNT(*) AS N FROM MaintenanceStrategyAssignments
UNION ALL SELECT 'plans linked to an assignment', COUNT(*) FROM MaintenancePlans WHERE MaintenanceStrategyAssignmentId IS NOT NULL
UNION ALL SELECT 'plans NOT linked (needs review)', COUNT(*) FROM MaintenancePlans WHERE MaintenanceStrategyAssignmentId IS NULL
UNION ALL SELECT 'strategies without catalog failure mode', COUNT(*) FROM MaintenanceStrategies WHERE FailureModeId IS NULL AND IsActive = 1;
"@
    sqlcmd -S $Server -d $Database -E -C -W -Q $verify
}
finally {
    Pop-Location
}
