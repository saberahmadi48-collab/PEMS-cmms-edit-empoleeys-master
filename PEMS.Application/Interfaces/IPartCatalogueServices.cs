using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>هویت اقدام‌کننده: EmployeeId (نه UserId) + نقش‌ها برای بررسی مجوز سمت سرویس.</summary>
public sealed record PcActor(int EmployeeId, int? UserId, IReadOnlyList<string> Roles);

/// <summary>تنظیمات ماژول (appsettings: "PartCatalogue"). مقادیر پیش‌فرض بدون تنظیم هم معتبرند.</summary>
public sealed class PartCatalogueOptions
{
    public const string SectionName = "PartCatalogue";

    /// <summary>آستانه‌ی Low Value Asset به ریال (دستورالعمل: کمتر از ۲۵۰,۰۰۰ ریال).</summary>
    public decimal LowValueThresholdIrr { get; set; } = 250_000m;

    public long MaxAttachmentBytes { get; set; } = 20L * 1024 * 1024;
    public int MaxAttachmentsPerRequest { get; set; } = 30;

    public string[] AllowedAttachmentExtensions { get; set; } =
        { ".pdf", ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".doc", ".docx", ".xls", ".xlsx", ".dwg", ".dxf", ".zip", ".txt" };
}

public sealed class PartCatalogueAttachmentView
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string? UploadedBy { get; set; }
    public DateTime UploadedDate { get; set; }
}

public sealed class PartCatalogueEquipmentView
{
    public int Id { get; set; }
    public int? EquipmentId { get; set; }
    public string Text { get; set; } = string.Empty;
    public decimal InstalledQuantity { get; set; }
}

public sealed class PartCatalogueStepView
{
    public string StepKey { get; set; } = string.Empty;
    public int Order { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? SkipReason { get; set; }
    public string? AssignedName { get; set; }
    public DateTime? DecidedDate { get; set; }
}

public sealed class PartCatalogueApprovalView
{
    public string? StepKey { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? ActorName { get; set; }
    public string? Comment { get; set; }
    public string? ToStatus { get; set; }
    public DateTime ActionDate { get; set; }
}

public sealed class PartCatalogueNoteView
{
    public string Text { get; set; } = string.Empty;
    public string? AuthorName { get; set; }
    public string? StepKey { get; set; }
    public DateTime CreatedDate { get; set; }
}

public sealed class PartCatalogueDetailsDto
{
    public int Id { get; set; }
    public byte[]? RowVersion { get; set; }
    public string? RequestNumber { get; set; }
    public string RequestType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? CurrentStepKey { get; set; }
    public bool Urgent { get; set; }
    public int RequesterEmployeeId { get; set; }
    public string? RequesterName { get; set; }
    public string? AssignedPartNumber { get; set; }
    public bool IsCritical { get; set; }
    public bool IsCapital { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? SubmittedDate { get; set; }
    public DateTime? CompletedDate { get; set; }

    /// <summary>مقادیر فرم (برای ویرایش و نمایش).</summary>
    public PartCatalogueFormDto Form { get; set; } = new();

    /// <summary>برچسب نمایشی فیلدهای مرجع (Section, TechnicalClass, ...): کلید = نام فیلد.</summary>
    public Dictionary<string, string> Labels { get; set; } = new();

    public string? TechnicalClassLongDescription { get; set; }
    public List<PartCatalogueEquipmentView> Equipment { get; set; } = new();
    public List<PartCatalogueAttachmentView> Attachments { get; set; } = new();
    public List<PartCatalogueNoteView> Notes { get; set; } = new();
    public List<PartCatalogueStepView> Steps { get; set; } = new();
    public List<PartCatalogueApprovalView> Approvals { get; set; } = new();
    public PartCatalogueActionState Actions { get; set; } = new();
}

/// <summary>مدل چاپ فرم INWH-PLA-FO-001 (Rev.3): همه‌ی مقادیر از قبل رشته‌ی نهایی‌اند.</summary>
public sealed class PartCataloguePrintModel
{
    public bool IsChange { get; set; }
    public string PartNumber { get; set; } = string.Empty;
    public string TechnicalClassName { get; set; } = string.Empty;
    public string Material { get; set; } = string.Empty;
    public string Attributes { get; set; } = string.Empty;
    public string EquipmentSerial { get; set; } = string.Empty;
    public string SparePartSerial { get; set; } = string.Empty;
    public string EquipmentDocNo { get; set; } = string.Empty;
    public string ManufacturerName { get; set; } = string.Empty;
    public string ManufacturerPartNo { get; set; } = string.Empty;
    public string ManufacturerDwgNo { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string SupplierPartNo { get; set; } = string.Empty;
    public string RelatedEquipmentTags { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string CommodityGroup1 { get; set; } = string.Empty;
    public string ProductFamily { get; set; } = string.Empty;
    public string MinStock { get; set; } = string.Empty;
    public string MaxConsumptionRate { get; set; } = string.Empty;
    public string InstalledQuantity { get; set; } = string.Empty;
    public DateTime? WantedReceiptDate { get; set; }
    public string StorageVolume { get; set; } = string.Empty;
    public string StockGrade { get; set; } = string.Empty;
    public bool IsCritical { get; set; }
    public string RequestNumber { get; set; } = string.Empty;
    public List<PrintSignature> Signatures { get; set; } = new();
}

/// <summary>Role: Originator | SpareSpecialist | MaintPlanning | PartCataloguer | InventoryController.</summary>
public sealed record PrintSignature(string Role, string Name, DateTime? Date, string Tel);

public interface IPartCatalogueLookupService
{
    Task<List<PcLookupItem>> SearchTechnicalClassesAsync(string? q, int take = 30);
    Task<PcLookupItem?> GetTechnicalClassAsync(int id);

    /// <summary>تطبیق قطعی متن FREE FORMAT TEXT با Technical Classهای فعال (TechnicalClassMatcher)؛ هیچ داده‌ای نمی‌نویسد.</summary>
    Task<PcTechnicalClassResolution> ResolveTechnicalClassAsync(string? freeFormatText);
    Task<List<PcLookupItem>> SearchManufacturersAsync(string? q, int take = 30);
    Task<List<PcLookupItem>> SearchSuppliersAsync(string? q, int take = 30);
    Task<List<PcLookupItem>> SearchCommodityGroupsAsync(string? q, int take = 30);
    Task<List<PcLookupItem>> SearchEquipmentAsync(string? q, int take = 20);

    Task<List<PcLookupItem>> GetSectionsAsync();
    Task<List<PcLookupItem>> GetCommodityGroup1Async(int? sectionId);
    Task<List<PcLookupItem>> GetCommodityGroup2Async(int? group1Id);
    Task<List<PcLookupItem>> GetProductFamiliesAsync();
    Task<List<PcLookupItem>> GetUnitsAsync();
    Task<List<PcLookupItem>> GetCurrenciesAsync();
    Task<List<PcLookupItem>> GetStockGradesAsync();
    Task<List<PcLookupItem>> GetPlanningMethodsAsync();

    /// <summary>Extra = "CR" / "CAP" / "CR,CAP" / "" برای نمایش مشتق‌های Is Critical/Is Capital در کلاینت.</summary>
    Task<List<PcLookupItem>> GetTypeDesignationsAsync();

    Task<(List<string> Exact, List<string> Similar)> CheckManufacturerNameAsync(string name);
}

public interface IPartCatalogueService
{
    Task<int> CreateDraftAsync(string requestType, PcActor actor);
    Task<PartCatalogueDetailsDto?> GetDetailsAsync(int id, PcActor actor);
    Task<PcResult<int>> SaveAsync(PartCatalogueFormDto form, PcActor actor);
    Task<PcResult> GenerateNumberAsync(int id, byte[]? rowVersion, PcActor actor);
    Task<PcResult> LoadExistingPartAsync(int id, string partNumber, PcActor actor);
    Task<PcResult> SubmitAsync(int id, byte[]? rowVersion, PcActor actor);
    Task<PcResult> RetryRoutingAsync(int id, byte[]? rowVersion, PcActor actor);

    /// <summary>action: Approve | Return | Cancel | (RegisterPartNumber = Approve روی گام Registration با partNumber).</summary>
    Task<PcResult> ExecuteAsync(int id, string action, string? comment, string? partNumber, byte[]? rowVersion, PcActor actor);

    Task<PcResult> AddNoteAsync(int id, string text, PcActor actor);
    Task<PcResult> AddEquipmentAsync(int id, int? equipmentId, string? text, decimal installedQuantity, PcActor actor);
    Task<PcResult> RemoveEquipmentAsync(int id, int equipmentLineId, PcActor actor);
    Task<PcResult> UploadAttachmentAsync(int id, string fileName, string? contentType, string kind, Stream content, long length, PcActor actor);
    Task<PcResult> RemoveAttachmentAsync(int id, int attachmentId, PcActor actor);
    Task<(Stream Stream, string FileName, string ContentType)?> OpenAttachmentAsync(int id, int attachmentId, PcActor actor);

    Task<PagedResult<PartCatalogueListItemDto>> ListAsync(PartCatalogueListFilter filter, PcActor actor);
    Task<PartCatalogueSummaryDto> GetSummaryAsync(PcActor actor);
    Task<PartCataloguePrintModel?> GetPrintModelAsync(int id, PcActor actor);
}

public interface IPartCatalogueAdminService
{
    Task<PcOverviewResult> GetOverviewAsync(PcActor actor);

    Task<PcImportResult> ImportTechnicalClassesAsync(Stream xlsx, PcActor actor);
    Task<PcImportResult> ImportCommodityGroupsAsync(Stream xlsx, PcActor actor);
    Task<PcImportResult> ImportProductFamiliesAsync(Stream xlsx, PcActor actor);

    /// <summary>فهرست نام (ستون اول) برای Manufacturer/Supplier؛ kind = Manufacturer | Supplier.</summary>
    Task<PcImportResult> ImportNameListAsync(string kind, Stream xlsx, PcActor actor);

    Task<PcResult> SetSectionAsync(int sectionId, bool requiresSparePartsReview, bool isActive, PcActor actor);
    Task<PcResult> AddSectionGroupAsync(int sectionId, int commodityGroupId, PcActor actor);
    Task<PcResult> RemoveSectionGroupAsync(int mapId, PcActor actor);
    Task<PcResult> AddGroupChildAsync(int parentGroupId, int childGroupId, PcActor actor);
    Task<PcResult> RemoveGroupChildAsync(int mapId, PcActor actor);
    Task<PcResult> UpdateStepAsync(int stepId, int order, bool isActive, string conditionKey, PcActor actor);
    Task<PcResult> AddApproverAsync(string stepKey, int employeeId, PcActor actor);
    Task<PcResult> RemoveApproverAsync(int assignmentId, PcActor actor);

    Task<List<PcLookupItem>> SearchEmployeesAsync(string? q, int take = 20);

    // ---- Enterprise Actor Resolution (مسئولیت‌ها، نگاشت Commodity، تشخیص)
    Task<PcResult> SaveResponsibilityAsync(PcResponsibilityInput input, PcActor actor);
    Task<PcResult> AddCommodityResponsibleAsync(string responsibilityKey, int commodityGroupId, int? positionId, int? employeeId, int priority, PcActor actor);
    Task<PcResult> RemoveCommodityResponsibleAsync(int id, PcActor actor);

    /// <summary>پیش‌نمایش تشخیصی Resolver (چه کسی واجد شرایط است و چه کسی/چرا حذف شد). فقط PartCatalogue.Admin.</summary>
    Task<PEMS.Application.ActorResolution.ActorResolutionResult?> PreviewResolutionAsync(string responsibilityKey, int requesterEmployeeId, int? commodityGroupId, PcActor actor);
}

/// <summary>Overview با کنترل مجوز؛ Overview=null یعنی دسترسی ندارد.</summary>
public sealed class PcOverviewResult
{
    public bool Allowed { get; set; }
    public PcAdminOverview? Overview { get; set; }
}

/// <summary>
/// SCRUM-478: Part Catalogue reports (who did what, equipment coverage, stages). Rows follow the request list's visibility rule.
/// <paramref name="today"/> only fixes "now" for tests.
/// </summary>
public interface IPartCatalogueReportService
{
    Task<PartCatalogueReportDto> GetReportAsync(PartCatalogueReportFilter filter, PcActor actor, DateTime? today = null);
}
