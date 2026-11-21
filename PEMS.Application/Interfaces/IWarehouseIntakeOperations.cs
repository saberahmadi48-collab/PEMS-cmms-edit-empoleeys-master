namespace PEMS.Application.Interfaces;

/// <summary>The head of a warehouse intake (one row): the work order, its status and the state of the intake (Open, Sourced, NotRequested).</summary>
public sealed record WarehouseIntakeHeaderRow(int WorkOrderId, string WorkOrderNumber, string WorkOrderStatus, string State, DateTime? RequestedDate, int OpenLines);

/// <summary>One material line of the work order as the storekeeper sees it.</summary>
public sealed record WarehouseIntakeLineRow(int WorkOrderMaterialId, string Material, string Supply, string Required, string Sourced, decimal Unsourced, string Requisitions);

/// <summary>
/// The warehouse intake of work-order materials as operations (SCRUM-665). The reads are the same GetAsync of IWarehouseIntakeService; the writes call SourceAsync with the same actor
/// (employee, roles, user name from the context). The per-line quantities of the old form became two commands: everything open from one warehouse, or one line with its quantity.
/// </summary>
public interface IWarehouseIntakeOperations
{
    [PEMS.Application.Common.PemsOperation("WarehouseIntake", "Header", "StoreRequisition.View")]
    Task<List<WarehouseIntakeHeaderRow>> HeaderAsync(int workOrderId);

    [PEMS.Application.Common.PemsOperation("WarehouseIntake", "Lines", "StoreRequisition.View")]
    Task<List<WarehouseIntakeLineRow>> LinesAsync(int workOrderId);

    [PEMS.Application.Common.PemsOperation("WarehouseIntake", "Warehouses", "StoreRequisition.View")]
    Task<List<ReportChoiceRow>> WarehousesAsync();

    /// <summary>Raises the Draft store requisition for every open stock line, each at its full unsourced quantity, from the chosen warehouse.</summary>
    [PEMS.Application.Common.PemsOperation("WarehouseIntake", "SourceAll", "StoreRequisition.Create")]
    Task<WarehouseIntakeResult> SourceAllAsync(int workOrderId, int warehouseId, WarehouseIntakeActor actor);

    /// <summary>Raises the Draft store requisition for one line and the quantity the storekeeper types (never more than the unsourced quantity; the service checks).</summary>
    [PEMS.Application.Common.PemsOperation("WarehouseIntake", "SourceLine", "StoreRequisition.Create")]
    Task<WarehouseIntakeResult> SourceLineAsync(int workOrderId, int workOrderMaterialId, int warehouseId, decimal quantity, WarehouseIntakeActor actor);
}
