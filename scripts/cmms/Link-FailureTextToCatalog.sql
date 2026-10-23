/*
  PEMS CMMS - B9: safe migration of free-text failure mode/cause to the ISO 14224 catalog (FailureModes / FailureCauses).

  New completions are validated against the catalog by WorkOrderLifecycleService. Historic Work Orders / FailureRecords keep their text. This script only
  fills the optional reference column (FailureModeId / FailureCauseId) where the text matches EXACTLY ONE active catalog row by Code or Name
  (case-insensitive, trimmed). It never rewrites the original text, never guesses a fuzzy match and never touches rows that already have a reference.

  @Apply = 0 (default): READ-ONLY report (linkable / ambiguous / unmatched with counts) so the catalog owner can add missing catalog rows first.
  @Apply = 1: sets only the NULL reference columns for uniquely matched rows, in one transaction. Idempotent. Run on a restored copy first.
*/
SET NOCOUNT ON;
DECLARE @Apply bit = 0;

IF OBJECT_ID('tempdb..#link') IS NOT NULL DROP TABLE #link;
CREATE TABLE #link (SourceTable sysname, Kind nvarchar(10), RowId int, RawText nvarchar(400), CatalogId int NULL, Matches int);

-- Work Orders
INSERT #link SELECT N'WorkOrders', N'Mode', w.WorkOrderId, w.FailureModeCode, MIN(fm.FailureModeId), COUNT(fm.FailureModeId)
FROM WorkOrders w LEFT JOIN FailureModes fm ON fm.IsActive = 1 AND (UPPER(LTRIM(RTRIM(w.FailureModeCode))) = UPPER(fm.Code) OR UPPER(LTRIM(RTRIM(w.FailureModeCode))) = UPPER(fm.Name))
WHERE w.FailureModeId IS NULL AND NULLIF(LTRIM(RTRIM(w.FailureModeCode)), N'') IS NOT NULL GROUP BY w.WorkOrderId, w.FailureModeCode;
INSERT #link SELECT N'WorkOrders', N'Cause', w.WorkOrderId, w.FailureCauseCode, MIN(fc.FailureCauseId), COUNT(fc.FailureCauseId)
FROM WorkOrders w LEFT JOIN FailureCauses fc ON fc.IsActive = 1 AND (UPPER(LTRIM(RTRIM(w.FailureCauseCode))) = UPPER(fc.Code) OR UPPER(LTRIM(RTRIM(w.FailureCauseCode))) = UPPER(fc.Name))
WHERE w.FailureCauseId IS NULL AND NULLIF(LTRIM(RTRIM(w.FailureCauseCode)), N'') IS NOT NULL GROUP BY w.WorkOrderId, w.FailureCauseCode;
-- Failure records
INSERT #link SELECT N'FailureRecords', N'Mode', r.FailureRecordId, r.FailureMode, MIN(fm.FailureModeId), COUNT(fm.FailureModeId)
FROM FailureRecords r LEFT JOIN FailureModes fm ON fm.IsActive = 1 AND (UPPER(LTRIM(RTRIM(r.FailureMode))) = UPPER(fm.Code) OR UPPER(LTRIM(RTRIM(r.FailureMode))) = UPPER(fm.Name))
WHERE r.FailureModeId IS NULL AND NULLIF(LTRIM(RTRIM(r.FailureMode)), N'') IS NOT NULL GROUP BY r.FailureRecordId, r.FailureMode;
INSERT #link SELECT N'FailureRecords', N'Cause', r.FailureRecordId, r.FailureCause, MIN(fc.FailureCauseId), COUNT(fc.FailureCauseId)
FROM FailureRecords r LEFT JOIN FailureCauses fc ON fc.IsActive = 1 AND (UPPER(LTRIM(RTRIM(r.FailureCause))) = UPPER(fc.Code) OR UPPER(LTRIM(RTRIM(r.FailureCause))) = UPPER(fc.Name))
WHERE r.FailureCauseId IS NULL AND NULLIF(LTRIM(RTRIM(r.FailureCause)), N'') IS NOT NULL GROUP BY r.FailureRecordId, r.FailureCause;

-- 1) report
SELECT SourceTable, Kind,
       SUM(CASE WHEN Matches = 1 THEN 1 ELSE 0 END) AS Linkable,
       SUM(CASE WHEN Matches > 1 THEN 1 ELSE 0 END) AS Ambiguous,
       SUM(CASE WHEN Matches = 0 THEN 1 ELSE 0 END) AS Unmatched
FROM #link GROUP BY SourceTable, Kind ORDER BY SourceTable, Kind;

SELECT SourceTable, Kind, RawText, COUNT(*) AS Occurrences, MAX(Matches) AS CatalogMatches
FROM #link WHERE Matches <> 1 GROUP BY SourceTable, Kind, RawText ORDER BY Occurrences DESC;   -- add these to the catalog (or fix the text) before applying

-- 2) apply (unique matches only; reference columns only)
IF @Apply = 1
BEGIN
    BEGIN TRAN;
    UPDATE w SET FailureModeId = l.CatalogId FROM WorkOrders w JOIN #link l ON l.SourceTable = N'WorkOrders' AND l.Kind = N'Mode' AND l.RowId = w.WorkOrderId AND l.Matches = 1 WHERE w.FailureModeId IS NULL;
    UPDATE w SET FailureCauseId = l.CatalogId FROM WorkOrders w JOIN #link l ON l.SourceTable = N'WorkOrders' AND l.Kind = N'Cause' AND l.RowId = w.WorkOrderId AND l.Matches = 1 WHERE w.FailureCauseId IS NULL;
    UPDATE r SET FailureModeId = l.CatalogId FROM FailureRecords r JOIN #link l ON l.SourceTable = N'FailureRecords' AND l.Kind = N'Mode' AND l.RowId = r.FailureRecordId AND l.Matches = 1 WHERE r.FailureModeId IS NULL;
    UPDATE r SET FailureCauseId = l.CatalogId FROM FailureRecords r JOIN #link l ON l.SourceTable = N'FailureRecords' AND l.Kind = N'Cause' AND l.RowId = r.FailureRecordId AND l.Matches = 1 WHERE r.FailureCauseId IS NULL;
    COMMIT;
    PRINT N'Applied: reference columns filled for uniquely matched rows.';
END
ELSE PRINT N'Dry run only (@Apply = 0). No data was changed.';
