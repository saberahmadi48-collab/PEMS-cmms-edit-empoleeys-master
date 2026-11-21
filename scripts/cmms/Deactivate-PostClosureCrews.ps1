<#
.SYNOPSIS
  H30 - flag (IsActive = 0) Crews that were created on a Closed/Cancelled Work Order AFTER its closure. Nothing is ever deleted.

.DESCRIPTION
  DRY-RUN BY DEFAULT: prints what would be flagged and changes nothing. Real flagging requires ALL of:
    -Execute, -ConfirmCrewIds <ids you reviewed in Report-PostClosureChildRecords.sql>, and typing the database name in -ConfirmDatabase.
  Only Crews that still match the post-closure rule are touched (an id that no longer matches is skipped). Each flagged Crew and its active
  members are written to WorkOrderChangeLogs (Section='DataCleanup') in the same transaction, so the action is auditable and reversible by
  setting IsActive back to 1 for the logged ids. Labor and spare-part rows are NEVER touched by this script (they change cost/stock).

.EXAMPLE
  # review only
  .\Deactivate-PostClosureCrews.ps1 -Server 'localhost\SQLEXPRESS' -Database PEMS_COPY
  # flag after review (against a COPY first!)
  .\Deactivate-PostClosureCrews.ps1 -Server 'localhost\SQLEXPRESS' -Database PEMS_COPY -Execute -ConfirmCrewIds 12,13 -ConfirmDatabase PEMS_COPY
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Server,
    [Parameter(Mandatory)][string]$Database,
    [switch]$Execute,
    [int[]]$ConfirmCrewIds = @(),
    [string]$ConfirmDatabase = '',
    [string]$PerformedBy = $env:USERNAME
)
$ErrorActionPreference = 'Stop'

$candidates = @"
SET NOCOUNT ON;
SELECT c.CrewId, c.Code, c.Name, w.WorkOrderId, w.WorkOrderNumber, w.Status, c.CreatedDate,
       (SELECT MAX(h.ChangedDate) FROM WorkOrderStatusHistories h WHERE h.WorkOrderId = w.WorkOrderId AND h.ToStatus = w.Status) AS ClosedAt,
       (SELECT COUNT(*) FROM CrewMembers m WHERE m.CrewId = c.CrewId AND m.IsActive = 1) AS ActiveMembers
FROM Crews c JOIN WorkOrders w ON w.WorkOrderId = c.WorkOrderId
WHERE c.IsActive = 1 AND w.Status IN (N'Closed', N'Cancelled')
  AND c.CreatedDate > (SELECT MAX(h.ChangedDate) FROM WorkOrderStatusHistories h WHERE h.WorkOrderId = w.WorkOrderId AND h.ToStatus = w.Status)
ORDER BY w.WorkOrderNumber, c.CrewId;
"@

Write-Host "Server=$Server Database=$Database  Mode=$(if ($Execute) { 'EXECUTE' } else { 'DRY-RUN (read-only)' })"
$rows = Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $candidates -TrustServerCertificate
if (-not $rows) { Write-Host 'No post-closure crews found.'; return }
$rows | Format-Table -AutoSize

if (-not $Execute) { Write-Host 'Dry-run only. Re-run with -Execute -ConfirmCrewIds <ids> -ConfirmDatabase <name> to flag reviewed crews.'; return }
if ($ConfirmDatabase -ne $Database) { throw "Refusing to run: -ConfirmDatabase must equal -Database ('$Database')." }
if ($ConfirmCrewIds.Count -eq 0) { throw 'Refusing to run: pass the reviewed crew ids in -ConfirmCrewIds.' }

$ids = ($ConfirmCrewIds | ForEach-Object { [int]$_ }) -join ','
$by = $PerformedBy.Replace("'", "''")
$apply = @"
SET XACT_ABORT ON; SET NOCOUNT ON;
BEGIN TRAN;
DECLARE @target TABLE (CrewId int PRIMARY KEY, WorkOrderId int, Code nvarchar(450), Status nvarchar(30));
INSERT @target
SELECT c.CrewId, w.WorkOrderId, c.Code, w.Status
FROM Crews c JOIN WorkOrders w ON w.WorkOrderId = c.WorkOrderId
WHERE c.CrewId IN ($ids) AND c.IsActive = 1 AND w.Status IN (N'Closed', N'Cancelled')
  AND c.CreatedDate > (SELECT MAX(h.ChangedDate) FROM WorkOrderStatusHistories h WHERE h.WorkOrderId = w.WorkOrderId AND h.ToStatus = w.Status);

INSERT WorkOrderChangeLogs (WorkOrderId, Section, Action, RecordRef, Outcome, Code, WorkOrderStatus, ChangedByUserName, ChangedDate, Reason, BeforeValue, AfterValue, IsSecurityEvent)
SELECT t.WorkOrderId, N'DataCleanup', N'Edit', CAST(t.CrewId AS nvarchar(100)), N'Allowed', N'PostClosureCrewFlagged', t.Status, N'$by', SYSDATETIME(),
       N'H30 cleanup: crew created after the Work Order was closed', CONCAT(N'Crew ', t.Code, N' IsActive=1'), N'IsActive=0', 0
FROM @target t;

UPDATE m SET IsActive = 0, LeftDate = COALESCE(m.LeftDate, SYSDATETIME()) FROM CrewMembers m JOIN @target t ON t.CrewId = m.CrewId WHERE m.IsActive = 1;
UPDATE c SET IsActive = 0 FROM Crews c JOIN @target t ON t.CrewId = c.CrewId;
SELECT COUNT(*) AS FlaggedCrews FROM @target;
COMMIT;
"@
Invoke-Sqlcmd -ServerInstance $Server -Database $Database -Query $apply -TrustServerCertificate | Format-Table -AutoSize
