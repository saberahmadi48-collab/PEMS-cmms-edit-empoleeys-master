/*
  PEMS CMMS - C18: duplicate seeded disciplines (for example two "Electrical", "Process", "Piping", "Civil").

  Cause: the legacy HasData rows use codes EL/PR/PI/CV/... while DisciplineSeeder used to compare ONLY codes, so it added MECH/ELEC/PROC/PIP/CIV... as a
  second discipline. DisciplineSeeder now treats a default as present when an existing discipline has the same code, the same code in ImportAliases,
  or the same name (new databases no longer get duplicates). This script handles databases that ALREADY contain them.

  SAFE BY DESIGN
    * @Apply = 0 (default): READ-ONLY report. Nothing is changed.
    * @Apply = 1: sets IsActive = 0 ONLY for a duplicate that has ZERO references in every table with a foreign key to Disciplines.
      Nothing is deleted, nothing is re-pointed. A duplicate that is already referenced (employees, equipment, maintenance groups, ...) is listed
      with its reference counts and needs a deliberate manual merge (decide which row survives, update the references, then deactivate).
    * Run against a restored COPY first. Re-running is harmless (idempotent).
*/
SET NOCOUNT ON;
DECLARE @Apply bit = 0;      -- <== set to 1 only after reviewing the report

IF OBJECT_ID('tempdb..#dups') IS NOT NULL DROP TABLE #dups;
IF OBJECT_ID('tempdb..#refs') IS NOT NULL DROP TABLE #refs;

CREATE TABLE #dups (DisciplineId int PRIMARY KEY, Code nvarchar(50), Name nvarchar(200), KeepDisciplineId int, KeepCode nvarchar(50), KeepName nvarchar(200), Reason nvarchar(40));
CREATE TABLE #refs (DisciplineId int, RefTable sysname, RefColumn sysname, RefCount int);

;WITH Defaults AS (SELECT v.Code FROM (VALUES (N'MECH'),(N'ELEC'),(N'INST'),(N'PROC'),(N'PIP'),(N'CIV'),(N'STR'),(N'HSE'),(N'ARCH'),(N'UTIL'),(N'FIRE'),(N'INSP'),(N'CORR'),(N'REL')) v(Code))
INSERT #dups
SELECT d.DisciplineId, d.Code, d.Name, k.DisciplineId, k.Code, k.Name,
       CASE WHEN EXISTS (SELECT 1 FROM STRING_SPLIT(COALESCE(k.ImportAliases, N''), N',') s WHERE LTRIM(RTRIM(s.value)) = d.Code) THEN N'code is an alias of kept row' ELSE N'same name' END
FROM Disciplines d
JOIN Defaults df ON df.Code = d.Code
JOIN Disciplines k ON k.DisciplineId < d.DisciplineId AND k.IsActive = 1 AND k.DisciplineId <> d.DisciplineId
 AND (EXISTS (SELECT 1 FROM STRING_SPLIT(COALESCE(k.ImportAliases, N''), N',') s WHERE LTRIM(RTRIM(s.value)) = d.Code)
      OR UPPER(REPLACE(REPLACE(k.Name, N' ', N''), N'-', N'')) = UPPER(REPLACE(REPLACE(d.Name, N' ', N''), N'-', N'')))
WHERE d.IsActive = 1
  AND NOT EXISTS (SELECT 1 FROM Disciplines k2 WHERE k2.DisciplineId < k.DisciplineId AND k2.DisciplineId <> d.DisciplineId AND k2.IsActive = 1
        AND EXISTS (SELECT 1 FROM STRING_SPLIT(COALESCE(k2.ImportAliases, N''), N',') s2 WHERE LTRIM(RTRIM(s2.value)) = d.Code));   -- keep the OLDEST match

DECLARE @sql nvarchar(max) = N'';
SELECT @sql += N'INSERT #refs SELECT d.DisciplineId, N''' + OBJECT_NAME(fk.parent_object_id) + N''', N''' + c.name + N''', COUNT(*) FROM #dups d JOIN '
             + QUOTENAME(OBJECT_SCHEMA_NAME(fk.parent_object_id)) + N'.' + QUOTENAME(OBJECT_NAME(fk.parent_object_id)) + N' t ON t.' + QUOTENAME(c.name) + N' = d.DisciplineId GROUP BY d.DisciplineId;' + CHAR(10)
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
JOIN sys.columns c ON c.object_id = fkc.parent_object_id AND c.column_id = fkc.parent_column_id
WHERE fk.referenced_object_id = OBJECT_ID(N'Disciplines');
EXEC sp_executesql @sql;

-- 1) the report
SELECT d.DisciplineId AS DuplicateId, d.Code AS DuplicateCode, d.Name AS DuplicateName, d.KeepDisciplineId AS KeepId, d.KeepCode, d.KeepName, d.Reason,
       COALESCE((SELECT SUM(r.RefCount) FROM #refs r WHERE r.DisciplineId = d.DisciplineId), 0) AS TotalReferences,
       CASE WHEN COALESCE((SELECT SUM(r.RefCount) FROM #refs r WHERE r.DisciplineId = d.DisciplineId), 0) = 0 THEN N'unused: can be deactivated (@Apply = 1)'
            ELSE N'referenced: manual merge required' END AS Recommendation
FROM #dups d ORDER BY d.Name, d.DisciplineId;

SELECT r.DisciplineId, r.RefTable, r.RefColumn, r.RefCount FROM #refs r ORDER BY r.DisciplineId, r.RefTable;

-- 2) apply (only unused duplicates; flag only)
IF @Apply = 1
BEGIN
    BEGIN TRAN;
    UPDATE x SET IsActive = 0
    FROM Disciplines x JOIN #dups d ON d.DisciplineId = x.DisciplineId
    WHERE x.IsActive = 1 AND NOT EXISTS (SELECT 1 FROM #refs r WHERE r.DisciplineId = d.DisciplineId AND r.RefCount > 0);
    SELECT @@ROWCOUNT AS DeactivatedDuplicates;
    COMMIT;
END
ELSE PRINT N'Dry run only (@Apply = 0). No data was changed.';
