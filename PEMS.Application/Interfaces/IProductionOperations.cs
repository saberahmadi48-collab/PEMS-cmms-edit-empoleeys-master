namespace PEMS.Application.Interfaces;

public sealed record ProductionSummaryRow(int ActiveOrderCount, int ReleasedOrderCount, int ActiveRunCount, int HeldRunCount, decimal TodaysOutputQuantity, decimal TodaysLossQuantity, int UnresolvedReconciliationIssueCount);
public sealed record ProductionOrderRow(int ProductionOrderId, string OrderNumber, string MaterialCode, string MaterialDescription, decimal PlannedQuantity, string Uom, string Status, string WorkCenter, DateTime? PlannedStartDate, DateTime? PlannedFinishDate, int RunCount);
public sealed record ProductionOrderHeadRow(int ProductionOrderId, string OrderNumber, string Material, decimal PlannedQuantity, string Uom, string Status, string WorkCenter, string Equipment, string Responsible, DateTime? PlannedStartDate, DateTime? PlannedFinishDate,
    DateTime? ActualStartDate, DateTime? ActualFinishDate, string Notes, string HoldReason, string CancellationReason, string CreatedBy, DateTime CreatedDate);
public sealed record ProductionRunRow(int ProductionRunId, string OrderNumber, string RunNumber, string Status, DateTime? StartDate, DateTime? EndDate, decimal TotalOutputQuantity, decimal TotalLossQuantity);
public sealed record ProductionRunHeadRow(int ProductionRunId, int ProductionOrderId, string OrderNumber, string Material, string RunNumber, string Status, DateTime? StartDate, DateTime? EndDate, string WorkCenter, string Equipment, string Operator, string Shift, string Notes, string HoldReason,
    bool IsComparable, string ReconciliationNote, decimal TotalInput, decimal TotalOutput, decimal TotalLoss, decimal? Difference);
public sealed record ProductionInputRow(int Id, string Material, decimal Quantity, string Uom, string SourceBatch, bool PostedToInventory, string Employee, DateTime TransactionDate, string Notes);
public sealed record ProductionOutputRow(int Id, string Material, int ProductionBatchId, string BatchNumber, decimal Quantity, string Uom, string Classification, bool PostedToInventory, string Employee, DateTime TransactionDate, string Notes);
public sealed record ProductionLossRow(int Id, decimal Quantity, string Uom, string Category, string Employee, DateTime TransactionDate, string Notes);
public sealed record ProductionEventRow(int Id, string EventType, DateTime EventDate, string Employee, string Details);
public sealed record ProductionBatchRow(int ProductionBatchId, string BatchNumber, int ProductionOrderId, string OrderNumber, int ProductionRunId, string RunNumber, string Material, decimal Quantity, string Uom, DateTime ProducedDate, string QualityStatus,
    string QualityStatusReason, DateTime? QualityStatusChangedDate, string QualityStatusChangedBy, string Notes);

/// <summary>
/// The production screens (SCRUM-737, 738, 739) as read operations: the dashboard (counts, active orders, recent batches and losses), the order list and details with its runs, the run details (inputs, outputs, losses, events,
/// reconciliation) and the batch details, with the choices of the forms. The commands are the service operations (ProductionOrder and ProductionRun models). Every read needs Production.View.
/// </summary>
public interface IProductionOperations
{
    [PEMS.Application.Common.PemsOperation("Production", "Summary", "Production.View")]
    Task<List<ProductionSummaryRow>> SummaryAsync();

    [PEMS.Application.Common.PemsOperation("Production", "ActiveOrders", "Production.View")]
    Task<List<ProductionOrderRow>> ActiveOrdersAsync();

    [PEMS.Application.Common.PemsOperation("Production", "RecentBatches", "Production.View")]
    Task<List<ProductionBatchRow>> RecentBatchesAsync();

    [PEMS.Application.Common.PemsOperation("Production", "RecentLosses", "Production.View")]
    Task<List<ProductionLossRow>> RecentLossesAsync();

    [PEMS.Application.Common.PemsOperation("Production", "Orders", "Production.View")]
    Task<List<ProductionOrderRow>> OrdersAsync(string? status = null, string? search = null);

    [PEMS.Application.Common.PemsOperation("Production", "OrderHead", "Production.View")]
    Task<List<ProductionOrderHeadRow>> OrderHeadAsync(int id);

    [PEMS.Application.Common.PemsOperation("Production", "OrderRuns", "Production.View")]
    Task<List<ProductionRunRow>> OrderRunsAsync(int id);

    [PEMS.Application.Common.PemsOperation("Production", "RunHead", "Production.View")]
    Task<List<ProductionRunHeadRow>> RunHeadAsync(int id);

    [PEMS.Application.Common.PemsOperation("Production", "RunInputs", "Production.View")]
    Task<List<ProductionInputRow>> RunInputsAsync(int id);

    [PEMS.Application.Common.PemsOperation("Production", "RunOutputs", "Production.View")]
    Task<List<ProductionOutputRow>> RunOutputsAsync(int id);

    [PEMS.Application.Common.PemsOperation("Production", "RunLosses", "Production.View")]
    Task<List<ProductionLossRow>> RunLossesAsync(int id);

    [PEMS.Application.Common.PemsOperation("Production", "RunEvents", "Production.View")]
    Task<List<ProductionEventRow>> RunEventsAsync(int id);

    [PEMS.Application.Common.PemsOperation("Production", "BatchHead", "Production.View")]
    Task<List<ProductionBatchRow>> BatchHeadAsync(int id);

    [PEMS.Application.Common.PemsOperation("Production", "MaterialChoices", "Production.View")]
    Task<List<ReportChoiceRow>> MaterialChoicesAsync();

    [PEMS.Application.Common.PemsOperation("Production", "WorkCenterChoices", "Production.View")]
    Task<List<ReportChoiceRow>> WorkCenterChoicesAsync();

    [PEMS.Application.Common.PemsOperation("Production", "UomChoices", "Production.View")]
    Task<List<ReportChoiceRow>> UomChoicesAsync();

    [PEMS.Application.Common.PemsOperation("Production", "EquipmentChoices", "Production.View")]
    Task<List<ReportChoiceRow>> EquipmentChoicesAsync();

    [PEMS.Application.Common.PemsOperation("Production", "EmployeeChoices", "Production.View")]
    Task<List<ReportChoiceRow>> EmployeeChoicesAsync();

    [PEMS.Application.Common.PemsOperation("Production", "ShiftChoices", "Production.View")]
    Task<List<ReportChoiceRow>> ShiftChoicesAsync();

    [PEMS.Application.Common.PemsOperation("Production", "WarehouseChoices", "Production.View")]
    Task<List<ReportChoiceRow>> WarehouseChoicesAsync();

    [PEMS.Application.Common.PemsOperation("Production", "LossCategoryChoices", "Production.View")]
    Task<List<ReportChoiceRow>> LossCategoryChoicesAsync();
}
