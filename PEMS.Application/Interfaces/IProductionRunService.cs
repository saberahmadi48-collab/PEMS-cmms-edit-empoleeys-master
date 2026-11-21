using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>
/// طبق «PEMS Operations &amp; Production Architecture Spec v1» فاز ۱: گذارهای ProductionRun و
/// ثبت Input/Output/Loss/رویدادها، به‌علاوه گذار QualityStatus بچ (طبق بند «Quality Gate»،
/// Placeholder سطح دامنه - بدون IWorkflowRuntime در این فاز، دقیقاً هم‌تراز با الگوی
/// WorkOrder.Status). Post کردن به موجودی («ثبت عملیاتی» در برابر «ارسال به موجودی») طبق
/// تصمیم معماری AD-Phase1-01 دو اکشن جدا و عمدی‌اند، نه یک مرحله‌ی خودکار.
/// </summary>
public interface IProductionRunService
{
    Task<ProductionRunDto?> GetByIdAsync(int productionRunId);

    [PEMS.Application.Common.PemsOperation("ProductionRun", "Create", "Production.Create")]

    Task<(bool Success, string? Error, int? ProductionRunId)> CreateAsync(
        int productionOrderId, int? productionWorkCenterId, int? equipmentId,
        int? operatorEmployeeId, int? shiftDefinitionId, string? notes, int? createdByEmployeeId);

    [PEMS.Application.Common.PemsOperation("ProductionRun", "Start", "Production.Create")]

    Task<(bool Success, string? Error)> StartAsync(int productionRunId, int? actorEmployeeId);

    [PEMS.Application.Common.PemsOperation("ProductionRun", "Pause", "Production.Create")]

    Task<(bool Success, string? Error)> PauseAsync(int productionRunId, int? actorEmployeeId);

    [PEMS.Application.Common.PemsOperation("ProductionRun", "Resume", "Production.ReleaseHold")]

    Task<(bool Success, string? Error)> ResumeAsync(int productionRunId, int? actorEmployeeId);

    [PEMS.Application.Common.PemsOperation("ProductionRun", "Hold", "Production.Hold")]

    Task<(bool Success, string? Error)> HoldAsync(int productionRunId, string reason, int? actorEmployeeId);

    [PEMS.Application.Common.PemsOperation("ProductionRun", "Complete", "Production.Create")]

    Task<(bool Success, string? Error)> CompleteAsync(int productionRunId, int? actorEmployeeId);

    [PEMS.Application.Common.PemsOperation("ProductionRun", "Cancel", "Production.Delete")]

    Task<(bool Success, string? Error)> CancelAsync(int productionRunId, string reason, int? actorEmployeeId);

    [PEMS.Application.Common.PemsOperation("ProductionRun", "RecordInput", "Production.Create")]

    Task<(bool Success, string? Error)> RecordInputAsync(
        int productionRunId, int materialItemId, decimal quantity, int? uomId,
        int? sourceProductionBatchId, string? notes, int? actorEmployeeId);

    /// <summary>
    /// اگر productionBatchId داده نشود، یک بچ جدید ساخته می‌شود (اولین خروجی این Run برای این
    /// متریال)؛ اگر داده شود، به Quantity بچ موجود افزوده می‌شود (طبق کامنت ProductionBatch.cs).
    /// </summary>
    [PEMS.Application.Common.PemsOperation("ProductionRun", "RecordOutput", "Production.Create")]
    Task<(bool Success, string? Error, int? ProductionBatchId)> RecordOutputAsync(
        int productionRunId, int materialItemId, int? productionBatchId, decimal quantity, int? uomId,
        string outputClassification, string? notes, int? actorEmployeeId);

    [PEMS.Application.Common.PemsOperation("ProductionRun", "RecordLoss", "Production.Create")]

    Task<(bool Success, string? Error)> RecordLossAsync(
        int productionRunId, decimal quantity, int? uomId, string lossCategory,
        string? notes, int? actorEmployeeId);

    [PEMS.Application.Common.PemsOperation("ProductionRun", "PostInputToInventory", "Production.Create")]

    Task<(bool Success, string? Error)> PostInputToInventoryAsync(
        int productionInputId, int warehouseId, int? storageLocationId, int? actorEmployeeId);

    [PEMS.Application.Common.PemsOperation("ProductionRun", "PostOutputToInventory", "Production.Create")]

    Task<(bool Success, string? Error)> PostOutputToInventoryAsync(
        int productionOutputId, int warehouseId, int? storageLocationId, int? actorEmployeeId);

    Task<ProductionBatchDto?> GetBatchByIdAsync(int productionBatchId);

    [PEMS.Application.Common.PemsOperation("ProductionRun", "SubmitBatchForQc", "Production.Create")]

    Task<(bool Success, string? Error)> SubmitBatchForQcAsync(int productionBatchId, int? actorEmployeeId);

    [PEMS.Application.Common.PemsOperation("ProductionRun", "ReleaseBatch", "Production.Approve")]

    Task<(bool Success, string? Error)> ReleaseBatchAsync(int productionBatchId, int? actorEmployeeId);

    [PEMS.Application.Common.PemsOperation("ProductionRun", "BlockBatch", "Production.Hold")]

    Task<(bool Success, string? Error)> BlockBatchAsync(int productionBatchId, string reason, int? actorEmployeeId);

    [PEMS.Application.Common.PemsOperation("ProductionRun", "UnblockBatch", "Production.ReleaseHold")]

    Task<(bool Success, string? Error)> UnblockBatchAsync(int productionBatchId, int? actorEmployeeId);

    [PEMS.Application.Common.PemsOperation("ProductionRun", "RejectBatch", "Production.Approve")]

    Task<(bool Success, string? Error)> RejectBatchAsync(int productionBatchId, string reason, int? actorEmployeeId);
}
