using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>A name and a count (a status, a maintenance type); the name is in the language of the user.</summary>
public sealed record CmmsCountRow(string Name, int Count);

/// <summary>The four numbers on top of the CMMS operations dashboard (one row, Id 1).</summary>
public sealed record CmmsTotalsRow(int Id, int WorkRequestTotal, int WorkRequestPendingAction, int WorkOrderTotal, int WorkOrderOverdue);

/// <summary>PM compliance as the dashboard shows it (one row, Id 1); the percent is text ("87.5%" or "N/A").</summary>
public sealed record CmmsPmComplianceRow(int Id, string DataStatus, string CompliancePercent, int CompliantCount, int CompletedLateCount, int OverdueCount, int NotYetDueCount);

/// <summary>The status and the emergency ratio of the maintenance type breakdown (one row, Id 1).</summary>
public sealed record CmmsTypeSummaryRow(int Id, string DataStatus, string EmergencyWorkRatio);

/// <summary>The CMMS operations dashboard and the maintenance backlog as read operations (SCRUM-673); the numbers come from <see cref="ICmmsOperationsService"/> unchanged.</summary>
public interface ICmmsOperationsReads
{
    [PEMS.Application.Common.PemsOperation("CmmsOperations", "Totals", "WorkOrder.View")]
    Task<CmmsTotalsRow> TotalsAsync();

    [PEMS.Application.Common.PemsOperation("CmmsOperations", "WorkRequestsByStatus", "WorkOrder.View")]
    Task<List<CmmsCountRow>> WorkRequestsByStatusAsync(string? culture = null);

    [PEMS.Application.Common.PemsOperation("CmmsOperations", "WorkOrdersByStatus", "WorkOrder.View")]
    Task<List<CmmsCountRow>> WorkOrdersByStatusAsync(string? culture = null);

    [PEMS.Application.Common.PemsOperation("CmmsOperations", "PmCompliance", "WorkOrder.View")]
    Task<CmmsPmComplianceRow> PmComplianceAsync();

    [PEMS.Application.Common.PemsOperation("CmmsOperations", "TypeBreakdown", "WorkOrder.View")]
    Task<List<CmmsCountRow>> TypeBreakdownAsync();

    [PEMS.Application.Common.PemsOperation("CmmsOperations", "TypeSummary", "WorkOrder.View")]
    Task<CmmsTypeSummaryRow> TypeSummaryAsync();

    [PEMS.Application.Common.PemsOperation("CmmsOperations", "Backlog", "WorkOrder.View")]
    Task<List<MaintenanceBacklogItemDto>> BacklogAsync(string? status = null, string? priority = null);
}
