using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>
/// طبق CMMS-10a Execution Baseline بند ۱۱: گذار وضعیت Permit باید صریح و کنترل‌شده باشد -
/// نه یک انتساب آزاد به permit.Status از یک Controller نامرتبط. اعتبارسنجی گذار داخل Service
/// انجام می‌شود، همون الگوی WorkRequestService/WorkOrderLifecycleService.
/// </summary>
public interface IPermitService
{
    Task<List<PermitListDto>> GetListAsync(string? status = null);

    // مسیر معمولی (بدون pendingMyApproval) واقعاً در DB صفحه‌بندی می‌شود.
    Task<PagedResult<PermitListDto>> GetPagedListAsync(string? status, int page, int pageSize);

    // ==============================================================================
    // طبق ممیزی مستقل فاز ۶B Wave A (P1): مسیر pendingMyApproval دیگر کل جدول Permit را
    // بارگذاری نمی‌کند. IWorkflowRuntime.GetPendingTasksAsync از پیش شناسه‌های Permit در
    // انتظار تایید همین کاربر را برمی‌گرداند (صف تایید یک کاربر، نه کل جدول) - این دو متد
    // آن مجموعه‌ی کوچک و از پیش محدودشده را با فیلتر Status محدودتر می‌کنند و فقط دقیقاً
    // همان صفحه را از DB می‌خوانند. ترتیب واقعی (بر اساس AssignedDate از Workflow Runtime -
    // ستونی از DB نیست) در Controller روی همین شناسه‌های کوچک محاسبه می‌شود؛ جزئیات در
    // PermitController.Index مستند شده است.
    // ==============================================================================

    /// <summary>شناسه‌های فعال Permit از میان candidateIds که با فیلتر Status هم مطابقت دارند.</summary>
    Task<List<int>> GetActiveIdsMatchingAsync(IReadOnlyList<int> candidateIds, string? status);

    /// <summary>DTOهای کامل Permit فقط برای شناسه‌های داده‌شده (بدون هیچ فیلتر یا صفحه‌بندی اضافه).</summary>
    Task<List<PermitListDto>> GetByIdsAsync(IReadOnlyList<int> permitIds);

    Task<PermitDetailDto?> GetDetailsAsync(int permitId);

    Task<(bool Success, string? Error, int? Id)> CreateAsync(
        string permitType, string title, string? description, int requestedByEmployeeId,
        int? equipmentId, int? functionalLocationId, int? workOrderId, int? permitReceiverEmployeeId,
        string priority, DateTime? validFrom, DateTime? validTo);

    /// <summary>The floor of this operation is "Permit.View"; the definition of each action adds the permission of its step (Permit.Cancel, Permit.Revoke, Permit.Suspend).</summary>
    [PEMS.Application.Common.PemsOperation("Permit", "AdvanceStatus", "Permit.View")]
    Task<(bool Success, string? Error)> AdvanceStatusAsync(
        int permitId, string nextStatus, string? reason, int? changedByEmployeeId);

    // ==============================================================================
    // طبق CMMS-10b: زنجیره‌ی تایید واقعی چندنقشی از طریق IWorkflowRuntime موجود (نه یک
    // موتور موازی). این متدها فقط گذار Permit.Status از UnderReview→Approved/Rejected/Draft
    // را کنترل می‌کنند - بقیه‌ی گذارها (Submit/Issue/Suspend/...) دست‌نخورده باقی می‌مانند.
    // ==============================================================================

    [PEMS.Application.Common.PemsOperation("Permit", "SubmitForApproval", "Permit.Submit")]
    Task<(bool Success, string? Error)> SubmitForApprovalAsync(int permitId, int? actorEmployeeId);

    Task<(bool Success, string? Error)> RespondApprovalAsync(
        int permitId, string decision, string? comment, int? actorEmployeeId, IReadOnlyList<string> actorRoles,
        string? ipAddress, string? userAgent);

    [PEMS.Application.Common.PemsOperation("Permit", "ReturnForRework", "Permit.Reject")]
    Task<(bool Success, string? Error)> ReturnForReworkAsync(
        int permitId, string? comment, int? actorEmployeeId, IReadOnlyList<string> actorRoles,
        string? ipAddress, string? userAgent);

    Task<bool> HasPendingApprovalAsync(int permitId);
}
