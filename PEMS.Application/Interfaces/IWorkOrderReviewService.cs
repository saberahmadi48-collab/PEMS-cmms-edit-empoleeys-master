using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

public interface IWorkOrderReviewService
{
    // ارجاع WorkOrder به چند دیسیپلین برای ثبت نظر - شروع گردش‌کار موازی
    Task RequestDisciplineReviewAsync(
        int workOrderId, IReadOnlyList<int> disciplineIds,
        int? actionByEmployeeId, string? ipAddress = null, string? userAgent = null);

    // ثبت نظر یک دیسیپلین خاص
    [PEMS.Application.Common.PemsOperation("WorkOrderReview", "Respond", "WorkOrder.View")]
    Task RespondAsync(
        int workOrderId, string decision, string? findings, string? failureModeCode, string? failureCauseCode,
        int actorEmployeeId, int actorDisciplineId, string? ipAddress = null, string? userAgent = null);

    Task<IReadOnlyList<VdrReviewStatusItemDto>> GetDisciplineReviewStatusAsync(int workOrderId);

    // آیا بازبینی چند-دیسیپلینه فعالی روی این WorkOrder در جریانه (هنوز همه پاسخ ندادن)
    Task<bool> HasPendingDisciplineReviewAsync(int workOrderId);
}
