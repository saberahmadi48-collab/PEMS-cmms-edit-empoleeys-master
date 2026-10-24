using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>
/// طبق CMMS Production Baseline v1 بند P0-05: مسیر رسمی قبل از ساخت WorkOrder اجرایی -
/// Draft → Submitted → UnderReview → Approved → {Rejected | Planned → Converted}.
/// </summary>
public interface IWorkRequestService
{
    Task<PagedResult<WorkRequestListDto>> GetListAsync(int page = 1, int pageSize = 50, string? status = null);

    Task<WorkRequestDetailDto?> GetDetailsAsync(int workRequestId);

    Task<(bool Success, string? Error, int? Id)> CreateAsync(
        string title, string? description, int requestedById, int? equipmentId, int? assetId,
        int? functionalLocationId, string? reportedCondition, string priority, int? suggestedDisciplineId);

    /// <summary>ثبت کامل (A2: تجهیز/Asset/Functional Location الزامی؛ A4: ریسک/HSE/SLA؛ A5: اطلاعات خرابی). اعلان HSE برای HSE/اضطراری.</summary>
    Task<(bool Success, string? Error, int? Id)> CreateAsync(WorkRequestCreateRequest request);

    /// <summary>
    /// گذار وضعیت را طبق جدول داخلی مجاز بودن اعتبارسنجی می‌کند - قبل از هرگونه تغییر، نه
    /// فقط با اعتماد به مقدار ارسالی از Controller (طبق بند ۹.۳ سند Baseline).
    /// </summary>
    Task<(bool Success, string? Error)> AdvanceStatusAsync(
        int workRequestId, string nextStatus, string? reason, int? changedByEmployeeId);

    /// <summary>
    /// اکشن کسب‌وکاری (نه اکشن عمومی گردش‌کار). برای Work Request تحت گردش‌کار مرکزی، پس از اعتبارسنجی‌های
    /// خاص Work Request، اکشن «Complete» گردش‌کار را هم در همان SaveChanges اجرا می‌کند؛ در این حالت
    /// <paramref name="actor"/> الزامی است. برای رکورد Legacy (بدون Instance) همان رفتار قبلی حفظ شده است.
    /// </summary>
    Task<(bool Success, string? Error, int? WorkOrderId)> ConvertToWorkOrderAsync(
        int workRequestId, string workOrderType, string? currentUserName, WorkRequestActor? actor = null, int? equipmentId = null);

    /// <summary>LEGACY: وضعیت بعدی برای رکوردهای بدون Instance گردش‌کار (تنها منبع؛ Controller نقشه‌ی جداگانه ندارد). null = وضعیت نهایی.</summary>
    string? GetLegacyNextStatus(string currentStatus);

    // ---------------- Phase 2: گردش‌کار مرکزی ----------------

    /// <summary>Draft → شروع Instance گردش‌کار مرکزی از طریق Binding صریح. بدون Binding خطای شفاف می‌دهد (Fallback ندارد).</summary>
    Task<(bool Success, string? Error)> SubmitAsync(int workRequestId, WorkRequestActor actor);

    /// <summary>
    /// اجرای یک اکشن گردش‌کار. مجوز سمت‌سرور از مدل مرکزی (شرکت‌کننده + RequiredPermission + SoD + شرط‌ها) گرفته می‌شود؛
    /// UI هرگز مرجع امنیت نیست. اکشن «Complete» فقط از مسیر ConvertToWorkOrder مجاز است.
    /// </summary>
    Task<(bool Success, string? Error)> ExecuteWorkflowActionAsync(int workRequestId, string actionType, string? comment, WorkRequestActor actor);

    /// <summary>مدل خواندنی مرکزی؛ null اگر Instance ندارد.</summary>
    Task<PEMS.WorkflowPlatform.Interfaces.WorkflowRuntimeViewModel?> GetWorkflowViewAsync(int workRequestId, WorkRequestActor actor);
}
