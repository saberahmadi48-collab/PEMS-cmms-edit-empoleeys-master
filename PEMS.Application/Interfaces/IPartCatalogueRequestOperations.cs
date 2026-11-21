using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

public sealed record PcRequestRow(int Id, string RequestNumber, string RequestType, string Status, string CurrentStep, bool Urgent, string Section, string TechnicalClassCode, string TechnicalClassDescription, string Requester, string AssignedPartNumber, DateTime CreatedDate, DateTime? SubmittedDate);

public sealed record PcRequestSummaryRow(int Total, int Draft, int InApproval, int Returned, int Completed, int Cancelled, int Urgent, int AwaitingMe);

/// <summary>The head and the part information of one request (reference fields as their labels) with what the user may do now.</summary>
public sealed record PcRequestHeadRow(int Id, string RequestNumber, string RequestType, string Status, bool Urgent, string Requester, string AssignedPartNumber, bool IsCritical, bool IsCapital, DateTime CreatedDate, DateTime? SubmittedDate, DateTime? CompletedDate,
    string Section, string TechnicalClass, string TechnicalClassDefinition, string AssetType, string CommodityGroup1, string CommodityGroup2, string ProductFamily, string Unit, string Manufacturer, bool IsNewManufacturer, string ManufacturerPartNumber,
    string Supplier, string SupplierPartNumber, string Price, string TypeDesignation, string SparePartSerialNo, string EquipmentSerialNo, string ManufacturerDrawingNo, string DocumentNo,
    string PlanningMethod, string MinimumStock, string MaxConsumptionRate, string FirstPurchaseQuantity, bool PrimaryIssuedRequired, string IssuedQty, string StockGrade, DateTime? WantedReceiptDate, string StorageVolume,
    bool IsRd, string ExistingPartNumber, string ChangesMatchInventory, string FreeFormatText, string ActiveStep, bool CanEdit, bool CanApprove, bool CanReturn, bool CanCancel, bool RequiresPartNumber, bool IsAdminOverride, bool IsOrganizationBlocked,
    string OrganizationBlockReason, bool CanRetryRouting);

public sealed record PcRequestAttributeRow(string Name, string Value);
public sealed record PcRequestStepRow(int Order, string StepKey, string Kind, string Status, string SkipReason, string AssignedTo, DateTime? DecidedDate);
public sealed record PcRequestApprovalRow(DateTime ActionDate, string StepKey, string Action, string Actor, string Comment, string ToStatus);
public sealed record PcRequestNoteRow(DateTime CreatedDate, string Author, string StepKey, string Text);
public sealed record PcRequestEquipmentRow(int Id, string Text, decimal InstalledQuantity);
public sealed record PcRequestAttachmentRow(int Id, string FileName, string Kind, long FileSize, string UploadedBy, DateTime UploadedDate);

/// <summary>
/// The Part Catalogue requests (SCRUM-662) as operations: the list with its counts, the details (head, attributes, route, history, notes, equipment, attachments), the workflow commands of the details page (a new draft, approve, return,
/// cancel, register the part number, retry the routing, add a note) and the files (an attachment, the INWH-PLA-FO-001 workbook and PDF). The same IPartCatalogueService calls as the old controller; the service checks the rule of
/// every step again. The users need an employee record, as before. The edit page (the long form) stays on the controller until a form can be loaded from an operation.
/// </summary>
public interface IPartCatalogueRequestOperations
{
    [PEMS.Application.Common.PemsOperation("PartCatalogueRequest", "List")]
    Task<List<PcRequestRow>> ListAsync(List<string> userRoles, int? employeeId, int? userId, string? search = null, string? status = null, string? requestType = null, bool? urgent = null, int? sectionId = null, DateTime? fromDate = null, DateTime? toDate = null, string? view = null);

    [PEMS.Application.Common.PemsOperation("PartCatalogueRequest", "Summary")]
    Task<List<PcRequestSummaryRow>> SummaryAsync(List<string> userRoles, int? employeeId, int? userId);

    [PEMS.Application.Common.PemsOperation("PartCatalogueRequest", "SectionChoices")]
    Task<List<ReportChoiceRow>> SectionChoicesAsync(List<string> userRoles);

    [PEMS.Application.Common.PemsOperation("PartCatalogueRequest", "Head")]
    Task<List<PcRequestHeadRow>> HeadAsync(List<string> userRoles, int? employeeId, int? userId, int id);

    [PEMS.Application.Common.PemsOperation("PartCatalogueRequest", "Attributes")]
    Task<List<PcRequestAttributeRow>> AttributesAsync(List<string> userRoles, int? employeeId, int? userId, int id);

    [PEMS.Application.Common.PemsOperation("PartCatalogueRequest", "Steps")]
    Task<List<PcRequestStepRow>> StepsAsync(List<string> userRoles, int? employeeId, int? userId, int id);

    [PEMS.Application.Common.PemsOperation("PartCatalogueRequest", "Approvals")]
    Task<List<PcRequestApprovalRow>> ApprovalsAsync(List<string> userRoles, int? employeeId, int? userId, int id);

    [PEMS.Application.Common.PemsOperation("PartCatalogueRequest", "Notes")]
    Task<List<PcRequestNoteRow>> NotesAsync(List<string> userRoles, int? employeeId, int? userId, int id);

    [PEMS.Application.Common.PemsOperation("PartCatalogueRequest", "Equipment")]
    Task<List<PcRequestEquipmentRow>> EquipmentAsync(List<string> userRoles, int? employeeId, int? userId, int id);

    [PEMS.Application.Common.PemsOperation("PartCatalogueRequest", "Attachments")]
    Task<List<PcRequestAttachmentRow>> AttachmentsAsync(List<string> userRoles, int? employeeId, int? userId, int id);

    /// <summary>A new draft of type Add or Change.</summary>
    [PEMS.Application.Common.PemsOperation("PartCatalogueRequest", "NewDraft")]
    Task<(bool Success, string? Error)> NewDraftAsync(List<string> userRoles, int? employeeId, int? userId, string requestType);

    /// <summary>action: Approve, Return or Cancel (Approve on the registration step takes the part number).</summary>
    [PEMS.Application.Common.PemsOperation("PartCatalogueRequest", "Execute")]
    Task<(bool Success, string? Error)> ExecuteAsync(List<string> userRoles, int? employeeId, int? userId, int id, string action, string? comment = null, string? partNumber = null);

    [PEMS.Application.Common.PemsOperation("PartCatalogueRequest", "RetryRouting")]
    Task<(bool Success, string? Error)> RetryRoutingAsync(List<string> userRoles, int? employeeId, int? userId, int id);

    [PEMS.Application.Common.PemsOperation("PartCatalogueRequest", "AddNote")]
    Task<(bool Success, string? Error)> AddNoteAsync(List<string> userRoles, int? employeeId, int? userId, int id, string text);

    [PEMS.Application.Common.PemsOperation("PartCatalogueRequest", "Attachment")]
    Task<PcFileResult> AttachmentAsync(List<string> userRoles, int? employeeId, int? userId, int id, int attachmentId);

    [PEMS.Application.Common.PemsOperation("PartCatalogueRequest", "ExportXlsx")]
    Task<PcFileResult> ExportXlsxAsync(List<string> userRoles, int? employeeId, int? userId, int id);

    [PEMS.Application.Common.PemsOperation("PartCatalogueRequest", "ExportPdf")]
    Task<PcFileResult> ExportPdfAsync(List<string> userRoles, int? employeeId, int? userId, int id);
}
