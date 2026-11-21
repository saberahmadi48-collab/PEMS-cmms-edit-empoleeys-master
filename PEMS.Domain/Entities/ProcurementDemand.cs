using PEMS.Domain.Common;

namespace PEMS.Domain.Entities;

/// <summary>
/// SCRUM-471: a pre-procurement item in the procurement cartable. Every purchase enters here first (owner decision 2026-10-29):
/// a replenishment recommendation sent by the warehouse, a direct-purchase work-order line (SCRUM-472) or an engineering MR
/// issued to procurement. The buyer decides: add it to an MR, accept the MR, or dismiss / return it with a reason.
/// A live item exists at most once per source (filtered unique index), so the same need is never raised twice.
/// </summary>
public class ProcurementDemand : BaseEntity
{
    public int ProcurementDemandId { get; set; }

    /// <summary><see cref="ProcurementDemandSources"/>.</summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary>Recommendation id, work-order material id or MR id, by <see cref="SourceType"/>.</summary>
    public int SourceId { get; set; }

    public int? MaterialItemId { get; set; }
    public MaterialItem? MaterialItem { get; set; }

    public string Description { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    public int? UomId { get; set; }
    public MaterialUOM? Uom { get; set; }

    /// <summary>The warehouse the shortage belongs to (replenishment only).</summary>
    public int? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public int? WorkOrderId { get; set; }

    public int? ProjectId { get; set; }

    /// <summary><see cref="ProcurementDemandStatuses"/>.</summary>
    public string Status { get; set; } = ProcurementDemandStatuses.Open;

    /// <summary>The buyer's reason for a dismissal or a return; optional note on a conversion.</summary>
    public string? DecisionReason { get; set; }

    /// <summary>The MR the item went to (converted) or came from (engineering MR).</summary>
    public int? MaterialRequisitionId { get; set; }
    public MaterialRequisition? MaterialRequisition { get; set; }

    public int? RaisedByEmployeeId { get; set; }
    public string? RaisedByUserName { get; set; }
    public DateTime RaisedAt { get; set; } = DateTime.Now;

    public int? DecidedByEmployeeId { get; set; }
    public string? DecidedByUserName { get; set; }
    public DateTime? DecidedAt { get; set; }
}

public static class ProcurementDemandSources
{
    public const string Replenishment = "Replenishment";
    public const string WorkOrderDirectPurchase = "WorkOrderDirectPurchase";
    public const string MaterialRequisition = "MaterialRequisition";
}

public static class ProcurementDemandStatuses
{
    /// <summary>Waiting for the buyer in the procurement queue.</summary>
    public const string Open = "Open";
    /// <summary>Added as a line to an MR.</summary>
    public const string Converted = "Converted";
    /// <summary>An engineering MR accepted into procurement.</summary>
    public const string Accepted = "Accepted";
    /// <summary>Dismissed (material) or returned to engineering (MR), with a reason.</summary>
    public const string Dismissed = "Dismissed";

    /// <summary>Statuses that hold the source: a second item for the same source is refused while one of these exists.</summary>
    public static readonly string[] Live = { Open, Converted, Accepted };
}
