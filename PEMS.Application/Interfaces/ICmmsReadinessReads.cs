using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>The totals of the operational readiness summary (one row, Id 1).</summary>
public sealed record CmmsReadinessTotalsRow(int Id, int TotalActiveEquipment, int ClassifiedEquipment, int UnclassifiedEquipment, int CriticalityReadyEquipment, int StrategyReadyEquipment,
    int PmPlanReadyEquipment, int JobPlanReadyEquipment, int SafetyReadyEquipment, int ExecutionReadyEquipment);

/// <summary>One class of the activation backlog; <c>Missing</c> lists what the class still lacks.</summary>
public sealed record CmmsClassBacklogRow(int EquipmentClassId, string Code, string Name, int EquipmentCount, int CriticalityReadyCount, int StrategyReadyCount, int PmPlanReadyCount, int ExecutionReadyCount, string Missing);

/// <summary>One equipment with its readiness state per requirement (SCRUM-674).</summary>
public sealed record CmmsEquipmentReadinessRow(int EquipmentId, string TagNumber, string EquipmentClassName, string Classification, string Criticality, string Strategy, string PmPlan, string JobPlan, string Safety, bool ExecutionReady);

/// <summary>The CMMS readiness pages as read operations over <see cref="IEquipmentReadinessService"/> (SCRUM-674); the permission is the one the controller checked.</summary>
public interface ICmmsReadinessReads
{
    [PEMS.Application.Common.PemsOperation("CmmsReadiness", "Totals", "MaintenanceMasterData.View")]
    Task<CmmsReadinessTotalsRow> TotalsAsync();

    [PEMS.Application.Common.PemsOperation("CmmsReadiness", "ClassBacklog", "MaintenanceMasterData.View")]
    Task<List<CmmsClassBacklogRow>> ClassBacklogAsync();

    [PEMS.Application.Common.PemsOperation("CmmsReadiness", "Equipment", "MaintenanceMasterData.View")]
    Task<List<CmmsEquipmentReadinessRow>> EquipmentAsync();
}
