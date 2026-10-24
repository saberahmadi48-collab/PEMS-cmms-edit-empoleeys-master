/*
  PEMS CMMS - H30: Work Orders that are Closed/Cancelled but have child records created AFTER the closure
  (for example Crew "AUDITCREW" on WO-2026-0003).

  READ-ONLY. This script only runs SELECT statements; it never inserts, updates or deletes anything.
  Run it against a copy/restore first, then against the real database with read-only intent, e.g.
      sqlcmd -S localhost\SQLEXPRESS -d <Database> -E -i Report-PostClosureChildRecords.sql -s "," -W

  Closure time = the LAST WorkOrderStatusHistories row whose ToStatus equals the Work Order's current terminal status.
  A Work Order that was formally reopened and closed again is measured from the last closure.

  Recommended action per ChildType (decide after reading the rows - nothing is changed automatically):
    Crew / CrewMember       -> test or stray data: flag with Deactivate-PostClosureCrews.ps1 after confirmation.
    RequiredSkill / WorkOrderMaterial -> planning data added after close: delete only after confirmation; no cost effect.
    WorkOrderLabor          -> changes cost: DO NOT delete. Reopen the Work Order formally (WorkOrder.Reopen + reason), correct, re-close.
    MaintenanceSpareUsage   -> changes cost AND stock: DO NOT delete. Reopen formally, return the part (ReturnSpareUsage), re-close.
*/
SET NOCOUNT ON;

;WITH Terminal AS (
    SELECT w.WorkOrderId, w.WorkOrderNumber, w.Status,
           (SELECT MAX(h.ChangedDate) FROM WorkOrderStatusHistories h
             WHERE h.WorkOrderId = w.WorkOrderId AND h.ToStatus = w.Status) AS ClosedAt
    FROM WorkOrders w
    WHERE w.Status IN (N'Closed', N'Cancelled')
),
Children AS (
    SELECT t.WorkOrderId, t.WorkOrderNumber, t.Status, t.ClosedAt, N'Crew' AS ChildType, c.CrewId AS ChildId,
           CONCAT(c.Code, N' / ', c.Name) AS Detail, c.CreatedDate AS ChildCreated, c.CreatedBy AS ChildCreatedBy, CAST(NULL AS decimal(18,2)) AS Amount
    FROM Terminal t JOIN Crews c ON c.WorkOrderId = t.WorkOrderId AND c.IsActive = 1 AND c.CreatedDate > t.ClosedAt
    UNION ALL
    SELECT t.WorkOrderId, t.WorkOrderNumber, t.Status, t.ClosedAt, N'CrewMember', cm.CrewMemberId,
           CONCAT(N'Crew ', c.Code, N', EmployeeId ', cm.EmployeeId), cm.JoinedDate, cm.CreatedBy, NULL
    FROM Terminal t JOIN Crews c ON c.WorkOrderId = t.WorkOrderId
         JOIN CrewMembers cm ON cm.CrewId = c.CrewId AND cm.IsActive = 1 AND cm.JoinedDate > t.ClosedAt
    UNION ALL
    SELECT t.WorkOrderId, t.WorkOrderNumber, t.Status, t.ClosedAt, N'RequiredSkill', rs.RequiredSkillId,
           CONCAT(N'SkillId ', rs.SkillId), rs.CreatedDate, NULL, NULL
    FROM Terminal t JOIN RequiredSkills rs ON rs.WorkOrderId = t.WorkOrderId AND rs.CreatedDate > t.ClosedAt
    UNION ALL
    SELECT t.WorkOrderId, t.WorkOrderNumber, t.Status, t.ClosedAt, N'WorkOrderMaterial', wm.WorkOrderMaterialId,
           CONCAT(N'MaterialItemId ', wm.MaterialItemId, N' x ', wm.RequiredQuantity), wm.CreatedDate, NULL, NULL
    FROM Terminal t JOIN WorkOrderMaterials wm ON wm.WorkOrderId = t.WorkOrderId AND wm.CreatedDate > t.ClosedAt
    UNION ALL
    SELECT t.WorkOrderId, t.WorkOrderNumber, t.Status, t.ClosedAt, N'WorkOrderLabor', l.WorkOrderLaborId,
           CONCAT(N'EmployeeId ', l.EmployeeId, N', hours ', l.RegularHours, N'+', l.OvertimeHours), l.CreatedDate, l.CreatedBy, l.LaborCost
    FROM Terminal t JOIN WorkOrderLabors l ON l.WorkOrderId = t.WorkOrderId AND l.IsActive = 1 AND l.CreatedDate > t.ClosedAt
    UNION ALL
    SELECT t.WorkOrderId, t.WorkOrderNumber, t.Status, t.ClosedAt, N'MaintenanceSpareUsage', s.MaintenanceSpareUsageId,
           CONCAT(N'MaterialItemId ', s.MaterialItemId, N' x ', s.Quantity), s.CreatedDate, s.CreatedBy, s.TotalCost
    FROM Terminal t JOIN MaintenanceSpareUsages s ON s.WorkOrderId = t.WorkOrderId AND s.IsActive = 1 AND s.CreatedDate > t.ClosedAt
)
SELECT WorkOrderNumber, Status, ClosedAt, ChildType, ChildId, Detail, ChildCreated, ChildCreatedBy, Amount,
       CASE ChildType WHEN N'WorkOrderLabor' THEN N'Reopen formally and correct (changes cost)'
                      WHEN N'MaintenanceSpareUsage' THEN N'Reopen formally and return part (changes cost and stock)'
                      WHEN N'Crew' THEN N'Candidate for flagging (Deactivate-PostClosureCrews.ps1)'
                      WHEN N'CrewMember' THEN N'Candidate for flagging with its Crew'
                      ELSE N'Candidate for manual removal after confirmation' END AS RecommendedAction
FROM Children
ORDER BY WorkOrderNumber, ChildCreated;

-- Summary per Work Order (how many child rows were added after closure, and the money involved)
;WITH Terminal AS (
    SELECT w.WorkOrderId, w.WorkOrderNumber, w.Status,
           (SELECT MAX(h.ChangedDate) FROM WorkOrderStatusHistories h WHERE h.WorkOrderId = w.WorkOrderId AND h.ToStatus = w.Status) AS ClosedAt
    FROM WorkOrders w WHERE w.Status IN (N'Closed', N'Cancelled')
)
SELECT t.WorkOrderNumber, t.Status, t.ClosedAt,
       (SELECT COUNT(*) FROM Crews c WHERE c.WorkOrderId = t.WorkOrderId AND c.IsActive = 1 AND c.CreatedDate > t.ClosedAt) AS CrewsAfterClose,
       (SELECT COUNT(*) FROM WorkOrderLabors l WHERE l.WorkOrderId = t.WorkOrderId AND l.IsActive = 1 AND l.CreatedDate > t.ClosedAt) AS LaborRowsAfterClose,
       (SELECT COALESCE(SUM(l.LaborCost), 0) FROM WorkOrderLabors l WHERE l.WorkOrderId = t.WorkOrderId AND l.IsActive = 1 AND l.CreatedDate > t.ClosedAt) AS LaborCostAfterClose,
       (SELECT COUNT(*) FROM MaintenanceSpareUsages s WHERE s.WorkOrderId = t.WorkOrderId AND s.IsActive = 1 AND s.CreatedDate > t.ClosedAt) AS SpareRowsAfterClose,
       (SELECT COALESCE(SUM(s.TotalCost), 0) FROM MaintenanceSpareUsages s WHERE s.WorkOrderId = t.WorkOrderId AND s.IsActive = 1 AND s.CreatedDate > t.ClosedAt) AS SpareCostAfterClose
FROM Terminal t
WHERE EXISTS (SELECT 1 FROM Crews c WHERE c.WorkOrderId = t.WorkOrderId AND c.IsActive = 1 AND c.CreatedDate > t.ClosedAt)
   OR EXISTS (SELECT 1 FROM WorkOrderLabors l WHERE l.WorkOrderId = t.WorkOrderId AND l.IsActive = 1 AND l.CreatedDate > t.ClosedAt)
   OR EXISTS (SELECT 1 FROM MaintenanceSpareUsages s WHERE s.WorkOrderId = t.WorkOrderId AND s.IsActive = 1 AND s.CreatedDate > t.ClosedAt)
   OR EXISTS (SELECT 1 FROM RequiredSkills r WHERE r.WorkOrderId = t.WorkOrderId AND r.CreatedDate > t.ClosedAt)
   OR EXISTS (SELECT 1 FROM WorkOrderMaterials m WHERE m.WorkOrderId = t.WorkOrderId AND m.CreatedDate > t.ClosedAt)
ORDER BY t.WorkOrderNumber;
