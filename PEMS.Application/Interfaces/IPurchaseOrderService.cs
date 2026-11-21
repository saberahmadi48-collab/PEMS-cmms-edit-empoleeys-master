using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

public interface IPurchaseOrderService
{
    Task<List<PurchaseOrderDto>> GetAllAsync();
    Task<PurchaseOrderDto?> GetByIdAsync(int id);
    Task<int> CreateAsync(string poNumber, int projectId, int vendorId, int? contractId,
        string? description, decimal? value);

    /// <summary>
    /// انتقال اعتبارسنجی‌شده‌ی Draft → Issued. فقط PO فعال و در وضعیت Draft قابل صدور است؛
    /// PO های قدیمی/واردشده که از قبل Issued هستند دست نمی‌خورند (خطا می‌دهد).
    /// مجوز و SoD در لایه‌ی Controller بررسی می‌شود.
    /// </summary>
    [PEMS.Application.Common.PemsOperation("PurchaseOrder", "Issue", "PurchaseOrder.Approve")]
    Task IssueAsync(int poId);

    /// <summary>
    /// فقط برای Import/Reconciliation (شماره‌ی PO از یک سند صادرشده‌ی بیرونی): اگر PO نباشد آن را با وضعیت قدیمی
    /// <c>Issued</c> می‌سازد، بدون هیچ مجوز Approve/SoD. هرگز برای ایجاد عملیاتی PO جدید استفاده نشود - آن مسیر
    /// <see cref="CreateAsync"/> (Draft) + <see cref="IssueAsync"/> است. در حال حاضر هیچ فراخوان تولیدی ندارد و تست
    /// PurchaseOrderCreationPathTests هر فراخوان تولیدی جدید را مسدود می‌کند تا بازبینی شود.
    /// </summary>
    Task<int> FindOrCreateByNumberAsync(string poNumber, int projectId, int vendorId);
}
