namespace PEMS.Application.Interfaces;

/// <summary>Who acts on the pre-procurement queue (identity from the server-side principal).</summary>
public sealed record PreProcurementActor(int? EmployeeId, IReadOnlyList<string> Roles, string? UserName);

/// <summary>Outcome of a pre-procurement command. <c>Error</c> is a resource key (<c>Ppr_Err_*</c>).</summary>
public sealed record PreProcurementResult(bool Success, string? Error, int? Id = null, int Count = 0)
{
    public static PreProcurementResult Fail(string error) => new(false, error);
}

/// <summary>One row of the procurement queue.</summary>
public sealed record ProcurementDemandRow(
    int ProcurementDemandId,
    string SourceType,
    int SourceId,
    string? SourceNumber,
    string? MaterialCode,
    string Description,
    decimal Quantity,
    string? UnitName,
    string? WarehouseCode,
    string Status,
    DateTime RaisedAt,
    string? RaisedBy,
    int? MaterialRequisitionId,
    string? MaterialRequisitionNumber);

/// <summary>A demand with what the buyer needs to decide it.</summary>
public sealed record ProcurementDemandDetails(
    ProcurementDemandRow Row,
    int? MaterialItemId,
    int? WorkOrderId,
    int? ProjectId,
    string? DecisionReason,
    DateTime? DecidedAt,
    string? DecidedBy,
    // Draft MRs the line can be added to
    IReadOnlyList<(int Id, string Number, string Description)> DraftRequisitions);

/// <summary>Data for a new MR the buyer raises from a demand.</summary>
public sealed record NewRequisitionInput(string? MrNumber, int ProjectId, int DisciplineId, string? Description, DateTime? RequiredDate);

/// <summary>
/// SCRUM-471: the pre-procurement intake. Every purchase enters the procurement queue of My Work as a <c>ProcurementDemand</c>
/// (owner decision 2026-10-29) and the buyer decides it. A live demand exists at most once per source.
/// </summary>
public interface IPreProcurementService
{
    /// <summary>The warehouse hands an Open replenishment recommendation to procurement; the recommendation becomes Converted.</summary>
    [PEMS.Application.Common.PemsOperation("PreProcurement", "SendRecommendation")]
    Task<PreProcurementResult> SendRecommendationAsync(int recommendationId, PreProcurementActor actor);

    /// <summary>Raises the direct-purchase lines of a released work order that are not raised yet (<c>Count</c> = new items).</summary>
    Task<PreProcurementResult> RaiseDirectPurchaseLinesAsync(int workOrderId, PreProcurementActor actor);

    /// <summary>Raises an engineering MR that was just issued to procurement (called by the MR status change).</summary>
    Task<PreProcurementResult> RaiseMaterialRequisitionAsync(int materialRequisitionId, PreProcurementActor actor);

    Task<IReadOnlyList<ProcurementDemandRow>> GetQueueAsync(string? status, string? sourceType);

    Task<ProcurementDemandDetails?> GetAsync(int procurementDemandId);

    /// <summary>Source ids that already have a live demand (for showing "sent" on the source pages).</summary>
    Task<IReadOnlySet<int>> GetLiveSourceIdsAsync(string sourceType, IReadOnlyCollection<int> sourceIds);

    /// <summary>Adds a material demand as a line to an existing Draft MR (<paramref name="existingMrId"/>) or to a new MR.</summary>
    [PEMS.Application.Common.PemsOperation("PreProcurement", "Convert", "PreProcurement.Edit")]
    Task<PreProcurementResult> ConvertToRequisitionAsync(int procurementDemandId, int? existingMrId, NewRequisitionInput? newMr, PreProcurementActor actor);

    /// <summary>Accepts an engineering MR into procurement.</summary>
    [PEMS.Application.Common.PemsOperation("PreProcurement", "Accept", "PreProcurement.Edit")]
    Task<PreProcurementResult> AcceptAsync(int procurementDemandId, PreProcurementActor actor);

    /// <summary>Dismisses a material demand, or returns an engineering MR to engineering (status back to Approved). A reason is required.</summary>
    [PEMS.Application.Common.PemsOperation("PreProcurement", "Dismiss", "PreProcurement.Edit")]
    Task<PreProcurementResult> DismissAsync(int procurementDemandId, string reason, PreProcurementActor actor);
}

/// <summary>
/// SCRUM-471 / net-demand design §6.1 (PlannedSupply): quantity already handed to procurement for a stock shortage, so the next
/// replenishment run does not recommend it again. Ends when the demand is dismissed or its MR is closed or cancelled
/// (until PO lines and receipts exist, SCRUM-470/473).
/// </summary>
public interface IProcurementPipeline
{
    Task<IReadOnlyDictionary<(int MaterialItemId, int WarehouseId), decimal>> GetPlannedSupplyAsync(IReadOnlyCollection<int>? materialItemIds, CancellationToken ct = default);
}
