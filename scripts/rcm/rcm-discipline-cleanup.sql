/*
  PEMS - Discipline cleanup (junk test rows + unreferenced duplicates)
  --------------------------------------------------------------------
  DRY-RUN BY DEFAULT.  Pass the sqlcmd variable  -v Apply=0 (report only) | -v Apply=1 (apply).

  What it does (never deletes anything; "apply" only sets IsActive = 0, which is reversible):
    1. Reports every discipline grouped as: test-junk / duplicate-name / real, with the number of rows
       that reference it (all foreign keys to dbo.Disciplines are discovered from sys.foreign_keys).
    2. APPLY=1  ->  deactivates
         a) test-junk disciplines created by automated tests that once ran against the shared database
            (Name LIKE 'RQ Disc %' | 'TQMOC Disc %' | 'FPA Test Disc %' | 'Regression Baseline%');
            rows that reference them are left untouched (no data loss);
         b) ACTIVE duplicates that have NO references at all while a same-named, referenced sibling exists
            (e.g. "Process"/PROC with 0 references next to "Process"/PR).
    3. Duplicates that are referenced on both sides (e.g. "Piping" PI vs PIP, the many "Other" rows) are only
       REPORTED - merging them re-points foreign keys and needs a human decision.

  Run via scripts/rcm/Invoke-RcmDisciplineCleanup.ps1 (adds the confirm-by-name guard).
*/
SET NOCOUNT ON;

IF OBJECT_ID('tempdb..#refs') IS NOT NULL DROP TABLE #refs;
CREATE TABLE #refs (DisciplineId int NOT NULL, Refs int NOT NULL);

DECLARE @sql nvarchar(max) = N'';
SELECT @sql = @sql + N'INSERT #refs SELECT ' + QUOTENAME(c.name) + N', COUNT(*) FROM ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name)
            + N' WHERE ' + QUOTENAME(c.name) + N' IS NOT NULL GROUP BY ' + QUOTENAME(c.name) + N';' + CHAR(10)
FROM sys.foreign_key_columns fkc
JOIN sys.tables  t ON t.object_id = fkc.parent_object_id
JOIN sys.schemas s ON s.schema_id = t.schema_id
JOIN sys.columns c ON c.object_id = fkc.parent_object_id AND c.column_id = fkc.parent_column_id
WHERE fkc.referenced_object_id = OBJECT_ID('dbo.Disciplines');
EXEC sp_executesql @sql;

IF OBJECT_ID('tempdb..#d') IS NOT NULL DROP TABLE #d;
SELECT d.DisciplineId, d.Code, d.Name, d.IsActive,
       ISNULL(r.Refs, 0) AS Refs,
       CASE WHEN d.Name LIKE N'RQ Disc %' OR d.Name LIKE N'TQMOC Disc %' OR d.Name LIKE N'FPA Test Disc %'
                 OR d.Name LIKE N'Regression Baseline%' THEN 1 ELSE 0 END AS IsTestJunk,
       LOWER(LTRIM(RTRIM(d.Name))) AS NormName
INTO #d
FROM dbo.Disciplines d
LEFT JOIN (SELECT DisciplineId, SUM(Refs) AS Refs FROM #refs GROUP BY DisciplineId) r ON r.DisciplineId = d.DisciplineId;

PRINT '=== Summary ===';
SELECT CASE WHEN IsTestJunk = 1 THEN 'test-junk' ELSE 'other' END AS Kind,
       COUNT(*) AS Total, SUM(CASE WHEN IsActive = 1 THEN 1 ELSE 0 END) AS Active, SUM(CASE WHEN Refs > 0 THEN 1 ELSE 0 END) AS Referenced
FROM #d GROUP BY CASE WHEN IsTestJunk = 1 THEN 'test-junk' ELSE 'other' END;

-- Duplicate names among real (non-junk) active disciplines
IF OBJECT_ID('tempdb..#dup') IS NOT NULL DROP TABLE #dup;
SELECT x.*,
       CASE WHEN x.Refs = 0 AND EXISTS (SELECT 1 FROM #d y WHERE y.NormName = x.NormName AND y.IsActive = 1 AND y.Refs > 0 AND y.DisciplineId <> x.DisciplineId)
            THEN 'deactivate (unreferenced duplicate)' ELSE 'REPORT ONLY (merge needs a human decision)' END AS Action
INTO #dup
FROM #d x
WHERE x.IsTestJunk = 0 AND x.IsActive = 1
  AND (SELECT COUNT(*) FROM #d y WHERE y.NormName = x.NormName AND y.IsActive = 1 AND y.IsTestJunk = 0) > 1;

PRINT '=== Duplicate active disciplines (non-junk) ===';
SELECT DisciplineId, Code, Name, Refs, Action FROM #dup ORDER BY NormName, Refs DESC, DisciplineId;

PRINT '=== Test-junk disciplines that would be deactivated ===';
SELECT TOP (500) DisciplineId, Code, Name, IsActive, Refs FROM #d WHERE IsTestJunk = 1 AND IsActive = 1 ORDER BY Name;

DECLARE @junk int = (SELECT COUNT(*) FROM #d WHERE IsTestJunk = 1 AND IsActive = 1);
DECLARE @dups int = (SELECT COUNT(*) FROM #dup WHERE Action LIKE 'deactivate%');
PRINT CONCAT('Would deactivate: ', @junk, ' test-junk + ', @dups, ' unreferenced duplicate(s).');

IF $(Apply) = 1
BEGIN
    BEGIN TRAN;
    UPDATE d SET IsActive = 0 FROM dbo.Disciplines d JOIN #d j ON j.DisciplineId = d.DisciplineId WHERE j.IsTestJunk = 1 AND d.IsActive = 1;
    DECLARE @a int = @@ROWCOUNT;
    UPDATE d SET IsActive = 0 FROM dbo.Disciplines d JOIN #dup u ON u.DisciplineId = d.DisciplineId WHERE u.Action LIKE 'deactivate%' AND d.IsActive = 1;
    DECLARE @b int = @@ROWCOUNT;
    COMMIT;
    PRINT CONCAT('APPLIED: deactivated ', @a, ' test-junk and ', @b, ' unreferenced duplicate discipline(s). Nothing was deleted.');
END
ELSE PRINT 'DRY-RUN: nothing changed. Re-run with Apply=1 to apply.';
