namespace PEMS.Application.Interfaces;

public interface IMOCService
{
    [PEMS.Application.Common.PemsOperation("MOC", "Raise")]
    Task<int> RaiseAsync(
        string title, string description, string? reason, string changeType, string riskLevel,
        int projectId, int disciplineId, int? relatedDocumentId, int? relatedEquipmentId,
        DateTime? temporaryExpiryDate, bool trainingRequired,
        int? raisedByEmployeeId, IReadOnlyList<string> actorRoles,
        string? ipAddress = null, string? userAgent = null);

    [PEMS.Application.Common.PemsOperation("MOC", "AssessRisk")]
    Task AssessRiskAsync(
        int mocId, string riskAssessmentText, int? actorEmployeeId, int actorDisciplineId,
        string? ipAddress = null, string? userAgent = null);

    // ==============================================================================
    // اگر ریسک High باشد، خودکار مرحله‌ی تایید ارشد (SeniorApproval) هم فعال می‌شود -
    // کاربر این را انتخاب نمی‌کند، از روی RiskLevel ثبت‌شده تعیین می‌شود.
    // ==============================================================================
    [PEMS.Application.Common.PemsOperation("MOC", "Approve")]
    Task ApproveAsync(
        int mocId, int? actorEmployeeId, IReadOnlyList<string> actorRoles,
        string? ipAddress = null, string? userAgent = null);

    [PEMS.Application.Common.PemsOperation("MOC", "Reject")]
    Task RejectAsync(
        int mocId, string reason, int? actorEmployeeId, IReadOnlyList<string> actorRoles,
        string? ipAddress = null, string? userAgent = null);

    [PEMS.Application.Common.PemsOperation("MOC", "RecordImplementation")]
    Task RecordImplementationAsync(
        int mocId, string implementationPlan, int actorEmployeeId,
        string? ipAddress = null, string? userAgent = null);

    [PEMS.Application.Common.PemsOperation("MOC", "MarkDocumentsUpdated")]
    Task MarkDocumentsUpdatedAsync(int mocId, string? note, int actorEmployeeId);

    [PEMS.Application.Common.PemsOperation("MOC", "MarkTrainingCompleted")]
    Task MarkTrainingCompletedAsync(int mocId, int actorEmployeeId);

    // طبق رویه‌ی استاندارد MOC: بستن نهایی فقط وقتی چک‌لیست (به‌روزرسانی مدارک + آموزش در
    // صورت نیاز) کامل باشد ممکن است - وگرنه خطای صریح می‌دهد
    [PEMS.Application.Common.PemsOperation("MOC", "Close")]
    Task CloseAsync(
        int mocId, int? actorEmployeeId, IReadOnlyList<string> actorRoles,
        string? ipAddress = null, string? userAgent = null);
}
