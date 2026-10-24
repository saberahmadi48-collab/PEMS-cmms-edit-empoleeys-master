/*
  PEMS CMMS - C18: READ-ONLY report of master-data rows that look like automated-test leftovers.
  The regression suite now runs only against the isolated database PEMSDB_RegressionTest (PemsDbContextFactory refuses PEMSDB), so these rows come from
  older runs or manual experiments. This script never changes data: review the list, then deactivate rows manually in the application
  (Departments/Disciplines/Employees/Teams pages) - never bulk-delete, they may be referenced.
*/
SET NOCOUNT ON;
SELECT N'Employee' AS Kind, e.EmployeeId AS Id, CONCAT(e.FirstName, N' ', e.LastName) AS Name, e.IsActive, e.CreatedDate
FROM Employees e
WHERE (e.FirstName LIKE N'E[0-9a-f][0-9a-f][0-9a-f][0-9a-f]' AND e.LastName = N'T')      -- test base actors (E1a2b / T)
   OR e.FirstName IN (N'Conc', N'Test', N'Tester') OR e.LastName IN (N'Requester', N'Tester', N'Reviewer')
UNION ALL
SELECT N'Crew', c.CrewId, CONCAT(c.Code, N' / ', c.Name), c.IsActive, c.CreatedDate FROM Crews c
WHERE c.Code LIKE N'AUDIT%' OR c.Code LIKE N'TEST%' OR c.Code LIKE N'CR-%' OR c.Code LIKE N'STANDALONE%'
UNION ALL
SELECT N'Department', d.DepartmentId, d.Name, d.IsActive, NULL FROM Departments d WHERE d.Name LIKE N'Test%' OR d.Name LIKE N'Dept%[0-9]%' OR d.Name LIKE N'DEPT-%'
UNION ALL
SELECT N'Discipline', x.DisciplineId, CONCAT(x.Code, N' / ', x.Name), x.IsActive, NULL FROM Disciplines x WHERE x.Name LIKE N'Test%' OR x.Code LIKE N'TST%'
UNION ALL
SELECT N'Team', t.TeamId, CONCAT(t.Code, N' / ', t.Name), t.IsActive, NULL FROM Teams t WHERE t.Name LIKE N'Team' OR t.Name LIKE N'Test%' OR t.Code LIKE N'T[0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f]'
ORDER BY Kind, Id;
