using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>
/// طبق سند Engineering Intelligence Redesign (بخش ۱): جایگزین سیستم جدای MR/MRQ.
/// جریان: Procurement Package → MR → Vendor Inquiry → Technical Clarification → MRQ →
/// Response → Approval → Closeout - همه از یک صفحه قابل مدیریت.
/// </summary>
public interface IProcurementPackageService
{
    Task<List<ProcurementPackageDto>> GetAllAsync();

    // طبق «PEMS — Global Grid Hardening» P1: GetAllAsync بالا کل جدول فعال Packages را بدون
    // Skip/Take بارگذاری می‌کند. این متد جدید و جدا صفحه‌بندی واقعی سمت SQL برای Index دارد.
    Task<PagedResult<ProcurementPackageDto>> GetPagedAsync(int page, int pageSize);

    Task<ProcurementPackageDetailDto?> GetDetailAsync(int packageId);

    Task<int> CreateAsync(string packageNumber, int projectId, int? disciplineId, string title, string? description);

    [PEMS.Application.Common.PemsOperation("ProcurementPackage", "AdvanceStage", "ProcurementPackage.Edit")]
    Task AdvanceStageAsync(int packageId, string newStage);

    [PEMS.Application.Common.PemsOperation("ProcurementPackage", "Closeout", "ProcurementPackage.Approve")]
    Task CloseoutAsync(int packageId);

    // پیوند یک MR موجود، یا ساخت یک MR جدید زیر همین Package
    [PEMS.Application.Common.PemsOperation("ProcurementPackage", "LinkMR", "ProcurementPackage.Edit")]
    Task LinkExistingMRAsync(int packageId, int mrId);

    [PEMS.Application.Common.PemsOperation("ProcurementPackage", "AddCommunication", "ProcurementPackage.Edit")]
    Task<int> AddCommunicationAsync(int packageId, string type, string subject, string body,
        int? vendorId, string? raisedByName, int? attachmentId, int? tenderId = null);

    // طبق «PEMS — Final Global Repository Audit» بند ۲۴/۳۸ (IDOR): قبلاً فقط با communicationId
    // پیدا می‌شد - هیچ بررسی نمی‌شد که این Communication واقعاً متعلق به packageId درخواستی است
    // یا نه (دقیقاً همون کلاس نقصی که WorkOrderTask.CompleteTask/ReopenTask داشت). حالا هر دو
    // Id با هم فیلتر می‌شوند.
    [PEMS.Application.Common.PemsOperation("ProcurementPackage", "RespondToCommunication", "ProcurementPackage.Edit")]
    Task RespondToCommunicationAsync(int packageId, int communicationId, string responseText, string? respondedByName);

    [PEMS.Application.Common.PemsOperation("ProcurementPackage", "SetCommunicationStatus", "ProcurementPackage.Edit")]
    Task SetCommunicationStatusAsync(int packageId, int communicationId, string actionStatus);

    [PEMS.Application.Common.PemsOperation("ProcurementPackage", "AddApproval", "ProcurementPackage.Approve")]
    Task AddApprovalAsync(int packageId, string approverName, string decision, string? comment);
}
