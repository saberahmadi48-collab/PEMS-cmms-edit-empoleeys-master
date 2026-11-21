using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>
/// طبق «PEMS Operations &amp; Production Architecture Spec v1» فاز ۱: گذارهای وضعیتِ
/// ProductionOrder («چه چیزی باید تولید شود») - جدا از ProductionRun («چه اتفاقی افتاد»،
/// در IProductionRunService). همان الگوی WorkOrderLifecycleService (دیکشنری گذارهای مجاز،
/// نه یک Controller که هر nextStatus ای را بی‌چک اعمال کند).
/// </summary>
public interface IProductionOrderService
{
    Task<PagedResult<ProductionOrderListDto>> GetListAsync(int page, int pageSize, string? statusFilter = null, string? search = null);

    Task<ProductionOrderDto?> GetByIdAsync(int productionOrderId);

    Task<ProductionDashboardDto> GetDashboardAsync();

    [PEMS.Application.Common.PemsOperation("ProductionOrder", "Create", "Production.Create")]
    Task<(bool Success, string? Error, int? ProductionOrderId)> CreateAsync(
        int materialItemId, decimal plannedQuantity, int? uomId, int productionWorkCenterId,
        DateTime? plannedStartDate, DateTime? plannedFinishDate, int? equipmentId,
        int? responsibleEmployeeId, string? notes, int? createdByEmployeeId);

    [PEMS.Application.Common.PemsOperation("ProductionOrder", "Plan", "Production.Edit")]
    Task<(bool Success, string? Error)> PlanAsync(int productionOrderId, int? actorEmployeeId);

    [PEMS.Application.Common.PemsOperation("ProductionOrder", "Release", "Production.Approve")]
    Task<(bool Success, string? Error)> ReleaseAsync(int productionOrderId, int? actorEmployeeId);

    [PEMS.Application.Common.PemsOperation("ProductionOrder", "Hold", "Production.Hold")]
    Task<(bool Success, string? Error)> HoldAsync(int productionOrderId, string reason, int? actorEmployeeId);

    [PEMS.Application.Common.PemsOperation("ProductionOrder", "Resume", "Production.ReleaseHold")]
    Task<(bool Success, string? Error)> ResumeAsync(int productionOrderId, int? actorEmployeeId);

    [PEMS.Application.Common.PemsOperation("ProductionOrder", "Cancel", "Production.Delete")]
    Task<(bool Success, string? Error)> CancelAsync(int productionOrderId, string reason, int? actorEmployeeId);

    [PEMS.Application.Common.PemsOperation("ProductionOrder", "Complete", "Production.Approve")]
    Task<(bool Success, string? Error)> CompleteAsync(int productionOrderId, int? actorEmployeeId);

    [PEMS.Application.Common.PemsOperation("ProductionOrder", "Close", "Production.Approve")]
    Task<(bool Success, string? Error)> CloseAsync(int productionOrderId, int? actorEmployeeId);

    /// <summary>
    /// فقط توسط IProductionRunService وقتی اولین Run واقعاً Start می‌شود صدا زده می‌شود -
    /// هیچ اکشن مستقیم کاربری این را فراخوانی نمی‌کند (طبق کامنت ProductionOrder.cs).
    /// </summary>
    Task AdvanceToInProgressIfNeededAsync(int productionOrderId);
}
