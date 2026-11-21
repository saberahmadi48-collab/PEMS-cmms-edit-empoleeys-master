namespace PEMS.Application.Interfaces;

/// <summary>Who acts on the warehouse intake (identity comes from the server-side principal, never from the form).</summary>
public sealed record WarehouseIntakeActor(int? EmployeeId, IReadOnlyList<string> Roles, string? UserName);

/// <summary>Outcome of a warehouse intake command. <c>Error</c> is a resource key (<c>Whi_Err_*</c>), never a raw message.</summary>
public sealed record WarehouseIntakeResult(bool Success, string? Error, int? StoreRequisitionId = null, bool IntakeCompleted = false)
{
    public static WarehouseIntakeResult Fail(string error) => new(false, error);
}

/// <summary>One work-order material line as the warehouse intake sees it.</summary>
public sealed record WarehouseIntakeLineDto(
    int WorkOrderMaterialId,
    int MaterialItemId,
    string MaterialCode,
    string? MaterialDescription,
    string? UnitName,
    string SupplyCode,
    decimal RequiredQuantity,
    decimal SourcedQuantity,
    decimal UnsourcedQuantity,
    IReadOnlyList<WarehouseIntakeRequisitionRef> Requisitions);

/// <summary>A store requisition raised for the line, with its warehouse.</summary>
public sealed record WarehouseIntakeRequisitionRef(int StoreRequisitionId, string RequisitionNumber, string Status, string WarehouseCode, decimal Quantity);

/// <summary>The intake of one work order: its lines and the state of the warehouse task.</summary>
public sealed record WarehouseIntakeDto(
    int WorkOrderId,
    string WorkOrderNumber,
    string WorkOrderStatus,
    string? IntakeStatus,
    DateTime? RequestedDate,
    IReadOnlyList<WarehouseIntakeLineDto> Lines)
{
    /// <summary>True while the task waits in the warehouse queue.</summary>
    public bool IsOpen => IntakeStatus == "Running";
}

/// <summary>A direct-purchase work-order line waiting for the procurement intake (SCRUM-471).</summary>
public sealed record DirectPurchaseDemandDto(
    int WorkOrderMaterialId,
    int WorkOrderId,
    string WorkOrderNumber,
    int MaterialItemId,
    string MaterialCode,
    string? MaterialDescription,
    decimal RequiredQuantity,
    string? UnitName);

/// <summary>
/// SCRUM-472: the warehouse intake of work-order materials. A planner requests the stock lines of a work order; the task lands
/// in the warehouse role queue of My Work; the storekeeper picks the supplying warehouse (never inferred) and the service raises
/// a store requisition whose lines are linked to the work-order lines. Direct-purchase lines skip the warehouse.
/// </summary>
public interface IWarehouseIntakeService
{
    /// <summary>Supply codes a planner may set while the line is not sourced yet.</summary>
    Task<WarehouseIntakeResult> SetSupplyCodeAsync(int workOrderMaterialId, string supplyCode, WarehouseIntakeActor actor);

    /// <summary>Starts the warehouse task for the unsourced stock lines of a released work order (no duplicate while one is open).</summary>
    Task<WarehouseIntakeResult> RequestMaterialsAsync(int workOrderId, WarehouseIntakeActor actor);

    Task<WarehouseIntakeDto?> GetAsync(int workOrderId);

    /// <summary>
    /// Raises a Draft store requisition in <paramref name="warehouseId"/> for the given quantities, each line linked to its
    /// work-order line; completes the warehouse task when no stock line is left unsourced.
    /// </summary>
    Task<WarehouseIntakeResult> SourceAsync(int workOrderId, int warehouseId, IReadOnlyDictionary<int, decimal> quantityByWorkOrderMaterialId, WarehouseIntakeActor actor);

    /// <summary>False when a live store requisition already supplies the line (removing it would orphan the requisition).</summary>
    Task<bool> CanRemoveMaterialAsync(int workOrderMaterialId);

    /// <summary>Direct-purchase lines of released work orders, for the procurement intake.</summary>
    Task<IReadOnlyList<DirectPurchaseDemandDto>> GetDirectPurchaseDemandAsync();
}
