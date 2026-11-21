using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>
/// طبق سند Inventory بخش ۹-۱۰: موجودی هرگز مستقیم تغییر نمی‌کند - هر تغییر یک
/// InventoryTransaction می‌سازد که موجودی از روی آن به‌روزرسانی می‌شود.
/// </summary>
public interface IMaterialStockService
{
    Task<List<MaterialStockRowDto>> GetLowStockAsync(int take = 50);

    // طبق «PEMS Operations & Production Architecture Spec v1» فاز ۱ (بند AD): دو پارامتر انتهاییِ
    // اختیاری برای برچسب‌گذاری تراکنش موجودی به منبع واقعی‌اش (مثلاً ProductionRun/ProductionBatch)
    // اضافه شدند - کاملاً افزایشی (Additive)؛ همه‌ی فراخوانی‌های موجود (که این دو را نمی‌دهند)
    // دقیقاً مثل قبل با ReferenceType="Manual" رفتار می‌کنند. امضای برگشتی و رفتار پیش‌فرض تغییر
    // نکرده - طبق قاعده‌ی EXTEND نه REPLACE.
    //
    // طبق «PEMS Operations Phase 2A — Foundational Hardening» بخش ۹-۱۱: یک پارامتر اختیاری
    // انتهاییِ دیگر، productionBatchId، اضافه شد - همون الگوی افزایشی بالا. وقتی صدازننده
    // می‌داند این حرکت موجودی واقعاً به یک ProductionBatch وصل است، آن را این‌جا می‌دهد تا روی
    // InventoryTransaction.ProductionBatchId ثبت شود؛ در غیر این‌صورت Null می‌ماند - هیچ حدس یا
    // بازسازی خودکار بچ انجام نمی‌شود.
    [PEMS.Application.Common.PemsOperation("MaterialStock", "Receive", "MaterialItem.Create")]
    Task<(bool Success, string? Error)> ReceiveAsync(
        int materialItemId, int warehouseId, int? storageLocationId, decimal quantity,
        string? reason, int? employeeId, string? referenceType = null, int? referenceId = null,
        int? productionBatchId = null);

    [PEMS.Application.Common.PemsOperation("MaterialStock", "Adjust", "MaterialItem.Edit")]
    Task<(bool Success, string? Error)> AdjustAsync(
        int materialItemId, int warehouseId, int? storageLocationId, decimal newOnHandQuantity,
        string? reason, int? employeeId, string? referenceType = null, int? referenceId = null,
        int? productionBatchId = null);

    /// <summary>تغییر نسبی (Delta علامت‌دار) موجودی؛ مقدار جدید روی مقدار تازه‌ی ردیف داخل هر تلاش محاسبه می‌شود (بدون Race خواندن-سپس-نوشتن).</summary>
    Task<(bool Success, string? Error)> AdjustByAsync(
        int materialItemId, int warehouseId, int? storageLocationId, decimal delta,
        string? reason, int? employeeId, string? referenceType = null, int? referenceId = null,
        int? productionBatchId = null);

    /// <summary>
    /// آخرین InventoryTransactionId ثبت‌شده برای یک (ReferenceType, ReferenceId) مشخص را برمی‌گرداند -
    /// طبق تصمیم بالا (بدون تغییر امضای برگشتی Receive/Adjust)، این‌جوری کدِ صدازننده
    /// (مثلاً ProductionRunService) می‌تواند بعد از Post کردن موفق، شناسه‌ی تراکنش را برای ذخیره
    /// روی ProductionInput/Output.InventoryTransactionId بخواند.
    /// </summary>
    Task<int?> GetLastTransactionIdAsync(string referenceType, int referenceId);

    /// <summary>
    /// طبق CMMS Production Baseline v1 بند ۱۲.۱: متریال فیزیکی به انبار برمی‌گردد - برخلاف
    /// Receive (که برای دریافت نو از تأمین‌کننده است)، این‌جا مصرف قبلی معکوس می‌شود.
    /// </summary>
    Task<(bool Success, string? Error)> ReturnAsync(
        int materialItemId, int warehouseId, int? storageLocationId, decimal quantity,
        string? reason, int? employeeId, int? productionBatchId = null);

    /// <summary>
    /// طبق CMMS Production Baseline v1 بند ۱۲.۱: انتقال بین دو انبار - دو ردیف InventoryTransaction
    /// جفت (TransferOut/TransferIn) می‌سازد چون هر ردیف فقط یک WarehouseId دارد.
    ///
    /// طبق «PEMS Operations Phase 2A» بخش ۱۱ («Batch identity must survive the inventory
    /// movement»): اگر productionBatchId داده شود، هر دو طرفِ جفتِ Transfer همان مقدار را روی
    /// ProductionBatchId می‌گیرند - هویت بچ در طول انتقال گم نمی‌شود.
    /// </summary>
    [PEMS.Application.Common.PemsOperation("MaterialStock", "Transfer", "MaterialItem.Edit")]
    Task<(bool Success, string? Error)> TransferAsync(
        int materialItemId, int fromWarehouseId, int? fromStorageLocationId,
        int toWarehouseId, int? toStorageLocationId, decimal quantity, string? reason, int? employeeId,
        int? productionBatchId = null);

    Task<(bool Success, string? Error)> SetThresholdsAsync(
        int materialStockId, decimal? minimumQuantity, decimal? maximumQuantity,
        decimal? reorderPoint, decimal? reorderQuantity);
}
