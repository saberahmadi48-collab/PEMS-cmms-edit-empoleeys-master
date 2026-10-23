SET NOCOUNT ON;
SELECT 'legacy strategies (tag-level)' AS Item, COUNT(*) AS N FROM MaintenanceStrategies WHERE EquipmentId IS NOT NULL
UNION ALL SELECT 'active strategies', COUNT(*) FROM MaintenanceStrategies WHERE IsActive = 1
UNION ALL SELECT 'StrategyType values that will be rewritten', COUNT(*) FROM MaintenanceStrategies WHERE StrategyType NOT IN (N'Preventive',N'Predictive',N'CBM',N'Corrective',N'RunToFailure',N'FailureFinding',N'Redesign')
UNION ALL SELECT 'ConsequenceCategory values that will be rewritten', COUNT(*) FROM MaintenanceStrategies WHERE ConsequenceCategory NOT IN (N'Hidden',N'Safety',N'Environmental',N'Operational',N'NonOperational')
UNION ALL SELECT 'Frequency values that will be rewritten', COUNT(*) FROM MaintenanceStrategies WHERE Frequency IS NOT NULL AND Frequency NOT IN (N'Daily',N'Weekly',N'Monthly',N'Quarterly',N'SemiAnnual',N'Yearly')
UNION ALL SELECT 'strategies with no class on equipment (stay class-less)', COUNT(*) FROM MaintenanceStrategies s JOIN Equipments e ON e.EquipmentId = s.EquipmentId WHERE e.EquipmentClassId IS NULL
UNION ALL SELECT 'strategies whose FailureModeCode matches the ISO catalog', COUNT(*) FROM MaintenanceStrategies s WHERE EXISTS (SELECT 1 FROM FailureModes m WHERE m.IsActive = 1 AND (m.Code = s.FailureModeCode OR LOWER(m.Name) = LOWER(s.FailureModeCode)))
UNION ALL SELECT 'strategies with NO catalog failure mode (need remediation)', COUNT(*) FROM MaintenanceStrategies s WHERE s.EquipmentId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM FailureModes m WHERE m.IsActive = 1 AND (m.Code = s.FailureModeCode OR LOWER(m.Name) = LOWER(s.FailureModeCode)))
UNION ALL SELECT 'assignments to be created', COUNT(*) FROM MaintenanceStrategies s WHERE s.EquipmentId IS NOT NULL
UNION ALL SELECT 'plans to be linked to an assignment', COUNT(*) FROM MaintenancePlans p JOIN MaintenanceStrategies s ON s.MaintenanceStrategyId = p.MaintenanceStrategyId WHERE s.EquipmentId = p.EquipmentId
UNION ALL SELECT 'plans NOT linkable (plan equipment <> strategy equipment)', COUNT(*) FROM MaintenancePlans p JOIN MaintenanceStrategies s ON s.MaintenanceStrategyId = p.MaintenanceStrategyId WHERE s.EquipmentId IS NOT NULL AND s.EquipmentId <> p.EquipmentId
UNION ALL SELECT 'reactive strategies on safety/env/hidden consequence (-> Draft)', COUNT(*) FROM MaintenanceStrategies s WHERE s.IsActive = 1 AND s.StrategyType IN (N'RunToFailure',N'Corrective',N'rtf',N'cm') AND s.ConsequenceCategory IN (N'Safety',N'Environmental',N'Hidden')
UNION ALL SELECT 'plans on RunToFailure/Corrective strategies (will stop generating WOs)', COUNT(*) FROM MaintenancePlans p JOIN MaintenanceStrategies s ON s.MaintenanceStrategyId = p.MaintenanceStrategyId WHERE s.StrategyType IN (N'RunToFailure',N'Corrective');
