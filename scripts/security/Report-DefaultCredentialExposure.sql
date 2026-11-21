/*
  PEMS SCRUM-17 / #317: READ-ONLY report of accounts that may still carry a credential that was once built into the product
  (default administrator password until 2026-10-23, fixed password of users created from the employee form, load-test accounts).
  It never changes data and never reads or prints a password or a hash. Run it on every database (demo, staging, uat, production, developer),
  then follow docs/deployment/CREDENTIAL_ROTATION_RUNBOOK.md. Password resets are done in the application (Admin > Users), not in SQL.
*/
SET NOCOUNT ON;

-- 1. Administrators (the seeded 'admin' is one of them): who they are and when they last signed in
SELECT N'1 SystemAdmin' AS Section, u.Id, u.UserName, u.Email, u.IsActive, u.MustChangePassword, u.CreatedDate, u.LastLoginDate
FROM AspNetUsers u
JOIN AspNetUserRoles ur ON ur.UserId = u.Id
JOIN AspNetRoles r ON r.Id = ur.RoleId AND r.Name = N'SystemAdmin'
ORDER BY u.CreatedDate;

-- 2. Accounts created with a one-time password that nobody has used to sign in yet, or that must still be changed
--    (users created from the employee form before the random one-time password change had the same fixed password)
SELECT N'2 Never changed / never signed in' AS Section, u.Id, u.UserName, u.Email, u.IsActive, u.MustChangePassword, u.CreatedDate, u.LastLoginDate
FROM AspNetUsers u
WHERE u.MustChangePassword = 1 OR u.LastLoginDate IS NULL
ORDER BY u.CreatedDate;

-- 3. Load-test accounts (tools/LoadTestSeed): loadtest001..200 and loadtestadmin01..10 (SystemAdmin) - deactivate or delete
SELECT N'3 Load-test accounts' AS Section, u.Id, u.UserName, u.IsActive, u.CreatedDate, u.LastLoginDate
FROM AspNetUsers u
WHERE u.UserName LIKE N'loadtest%'
ORDER BY u.UserName;

-- 4. Everything created before the built-in administrator password was removed from the code (2026-10-23)
SELECT N'4 Created before 2026-10-23' AS Section, COUNT(*) AS Users, SUM(CASE WHEN u.IsActive = 1 THEN 1 ELSE 0 END) AS ActiveUsers
FROM AspNetUsers u
WHERE u.CreatedDate < '2026-10-23';
