namespace PEMS.Application.DTOs;

/// <summary>پیام اعتبارسنجی/نتیجه: Key = کلید Localization (Pc_Err_* / Pc_Warn_*)، Field = نام فیلد فرم، Args = پارامتر نمایشی.</summary>
public sealed record PcMessage(string Field, string Key, string? Arg = null);

public sealed class PcResult
{
    public bool Success => Errors.Count == 0;
    public List<PcMessage> Errors { get; } = new();
    public List<PcMessage> Warnings { get; } = new();

    public static PcResult Ok() => new();

    public static PcResult Fail(string field, string key, string? arg = null)
    {
        var r = new PcResult();
        r.Errors.Add(new PcMessage(field, key, arg));
        return r;
    }
}

public sealed class PcResult<T>
{
    public PcResult Result { get; init; } = new();
    public T? Value { get; init; }
    public bool Success => Result.Success;
}

public sealed class PartCatalogueAttributeDto
{
    public string Name { get; set; } = string.Empty;
    public string? Value { get; set; }
}

/// <summary>شکل قابل‌ویرایش درخواست (Bind از فرم). Id=0 یعنی جدید؛ RowVersion باید در هر فرم Round-trip شود.</summary>
public sealed class PartCatalogueFormDto
{
    public int Id { get; set; }
    public byte[]? RowVersion { get; set; }
    public string RequestType { get; set; } = "Add";

    public bool Urgent { get; set; }
    public int? SectionId { get; set; }
    public bool IsNewEquipment { get; set; }
    public int? TechnicalClassId { get; set; }

    public string AssetType { get; set; } = "None";
    public int? CommodityGroup1Id { get; set; }
    public int? CommodityGroup2Id { get; set; }
    public int? ProductFamilyId { get; set; }
    public int? UnitOfMeasureId { get; set; }
    public int? ManufacturerId { get; set; }
    public bool IsNewManufacturer { get; set; }
    public string? NewManufacturerName { get; set; }
    public string? ManufacturerPartNumber { get; set; }
    public int? SupplierId { get; set; }
    public string? SupplierPartNumber { get; set; }
    public decimal? PartPrice { get; set; }
    public int? CurrencyId { get; set; }
    public int? TypeDesignationId { get; set; }

    public string? SparePartSerialNo { get; set; }
    public string? EquipmentSerialNo { get; set; }
    public string? ManufacturerDrawingNo { get; set; }
    public string? DocumentNo { get; set; }

    public decimal? MinimumStock { get; set; }
    public decimal? MaxConsumptionRate { get; set; }
    public decimal? FirstPurchaseQuantity { get; set; }
    public int? PlanningMethodId { get; set; }
    public bool PrimaryIssuedRequired { get; set; }
    public decimal? IssuedQty { get; set; }
    public int? StockGradeId { get; set; }

    public DateTime? WantedReceiptDate { get; set; }
    public string? StorageVolume { get; set; }

    // Change
    public bool IsRd { get; set; }
    public string? ExistingPartNumber { get; set; }
    public bool? ChangesMatchInventory { get; set; }
    public string? OldManufacturerName { get; set; }
    public string? OldManufacturerPartNumber { get; set; }

    public List<PartCatalogueAttributeDto> Attributes { get; set; } = new();
}

/// <summary>داده‌ی حل‌شده‌ای که اعتبارسنج برای قواعد وابسته به DB نیاز دارد (سرویس پر می‌کند؛ اعتبارسنج خالص می‌ماند).</summary>
public sealed class PartCatalogueValidationContext
{
    public string RequestType { get; set; } = "Add";
    public bool HasRequestNumber { get; set; }

    public string? ManufacturerName { get; set; }
    public bool ManufacturerIsLocal { get; set; }
    public bool ManufacturerIsWrong { get; set; }
    public List<(string Name, string Normalized)> ExistingManufacturers { get; set; } = new();

    public string? SupplierName { get; set; }
    public string? CurrencyCode { get; set; }
    public string? PlanningMethodCode { get; set; }

    /// <summary>null یعنی محدودیتی نیست (همه‌ی گروه‌ها مجاز).</summary>
    public HashSet<int>? AllowedGroup1Ids { get; set; }
    public HashSet<int>? AllowedGroup2Ids { get; set; }

    public int DrawingOrDocumentAttachmentCount { get; set; }
    public int SupportAttachmentCount { get; set; }
    public int EquipmentLineCount { get; set; }

    public bool ExistingPartResolved { get; set; }
    public int? ExistingPartUnitOfMeasureId { get; set; }

    public decimal LowValueThresholdIrr { get; set; } = 250_000m;
}

public sealed class PartCatalogueListFilter
{
    public string? Search { get; set; }
    public string? Status { get; set; }
    public string? RequestType { get; set; }
    public bool? Urgent { get; set; }
    public int? SectionId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }

    /// <summary>all | mine | awaiting</summary>
    public string View { get; set; } = "all";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public sealed class PartCatalogueListItemDto
{
    public int Id { get; set; }
    public string? RequestNumber { get; set; }
    public string RequestType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? CurrentStepKey { get; set; }
    public bool Urgent { get; set; }
    public string? SectionCode { get; set; }
    public string? TechnicalClassCode { get; set; }
    public string? TechnicalClassDescription { get; set; }
    public string? RequesterName { get; set; }
    public string? AssignedPartNumber { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? SubmittedDate { get; set; }
}

public sealed class PartCatalogueSummaryDto
{
    public int Total { get; set; }
    public int Draft { get; set; }
    public int InApproval { get; set; }
    public int Returned { get; set; }
    public int Completed { get; set; }
    public int Cancelled { get; set; }
    public int Urgent { get; set; }
    public int AwaitingMe { get; set; }
}

/// <summary>گزینه‌ی Lookup برای AJAX (Select جستجوپذیر): Id + Code + متن نمایشی.</summary>
public sealed class PcLookupItem
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string? Extra { get; set; }
}

/// <summary>
/// نتیجه‌ی تطبیق FREE FORMAT TEXT با Technical Class برای UI. Status: "resolved" | "ambiguous" | "none".
/// فقط پیشنهاد است؛ سرور هنگام ذخیره TechnicalClassId را مستقلاً (وجود/فعال‌بودن) اعتبارسنجی می‌کند.
/// </summary>
public sealed class PcTechnicalClassResolution
{
    public string Status { get; set; } = "none";
    public string Reason { get; set; } = "None";
    public PcLookupItem? Resolved { get; set; }
    public List<PcLookupItem> Candidates { get; set; } = new();
}

public sealed class PartCatalogueActionState
{
    public bool CanEdit { get; set; }
    public bool CanSubmit { get; set; }
    public bool CanGenerateNumber { get; set; }
    public bool CanApprove { get; set; }
    public bool CanReturn { get; set; }
    public bool CanCancel { get; set; }
    public bool RequiresPartNumber { get; set; }
    public bool IsAdminOverride { get; set; }
    public string? ActiveStepKey { get; set; }

    /// <summary>درخواست ثبت شده ولی مدیر مستقیم از ساختار سازمانی قابل‌تعیین نیست (Organization Resolution Required).</summary>
    public bool IsOrganizationBlocked { get; set; }
    public string? OrganizationBlockReason { get; set; }
    public bool CanRetryRouting { get; set; }
}

public sealed class PcImportResult
{
    public string Kind { get; set; } = string.Empty;
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public int Skipped { get; set; }
    public List<string> Errors { get; } = new();
    public long ElapsedMs { get; set; }
}

// ---------------------------------------------------------------- Admin

public sealed record PcAdminGroupMap(int MapId, string GroupText, bool NeedsConfirmation);
public sealed record PcAdminApprover(int Id, string EmployeeName);

public sealed class PcAdminSectionRow
{
    public int SectionId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? NameFa { get; set; }
    public bool RequiresSparePartsReview { get; set; }
    public bool RoutingNeedsConfirmation { get; set; }
    public bool IsActive { get; set; }
    public List<PcAdminGroupMap> Groups { get; set; } = new();
}

public sealed class PcAdminStepRow
{
    public int StepId { get; set; }
    public string StepKey { get; set; } = string.Empty;
    public int Order { get; set; }
    public string NameEn { get; set; } = string.Empty;
    public string NameFa { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Condition { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool NeedsConfirmation { get; set; }
    public List<PcAdminApprover> Approvers { get; set; } = new();
}

public sealed record PcAdminPositionRef(int PositionId, string Title, int Priority);

public sealed class PcAdminResponsibilityRow
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? DisplayNameFa { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? RequiredAction { get; set; }
    public string? RoleName { get; set; }
    public string ScopeType { get; set; } = "None";
    public string? ScopeValue { get; set; }
    public int? ManagerLevel { get; set; }
    public string Strategy { get; set; } = "Queue";
    public bool IsActive { get; set; }
    public List<PcAdminPositionRef> Positions { get; set; } = new();
}

public sealed record PcAdminCommodityResponsible(int Id, string ResponsibilityKey, string GroupText, string? PositionTitle, string? EmployeeName, int Priority, bool IsActive);
public sealed record PcAdminWorkflowStage(string Name, int Order, string? ResponsibilityKey, string? EntryCondition);
public sealed record PcAdminFinding(string Severity, string Code, string ResponsibilityKey, string Message);

public sealed class PcResponsibilityInput
{
    public int Id { get; set; }
    public string Kind { get; set; } = "PositionScope";
    public string? RequiredAction { get; set; }
    public string? RoleName { get; set; }
    public string ScopeType { get; set; } = "None";
    public string? ScopeValue { get; set; }
    public int? ManagerLevel { get; set; }
    public string SelectionStrategy { get; set; } = "Queue";
    public bool IsActive { get; set; } = true;

    /// <summary>فهرست Idهای Position با کاما؛ ترتیب = اولویت (اولی بالاترین).</summary>
    public string? PositionIds { get; set; }
}

public sealed class PcAdminOverview
{
    public List<PcAdminResponsibilityRow> Responsibilities { get; set; } = new();
    public List<PcAdminCommodityResponsible> CommodityResponsibles { get; set; } = new();
    public List<PcLookupItem> Positions { get; set; } = new();
    public List<string> Roles { get; set; } = new();
    public List<PcAdminWorkflowStage> WorkflowStages { get; set; } = new();
    public List<PcAdminFinding> Findings { get; set; } = new();

    public List<PcAdminSectionRow> Sections { get; set; } = new();
    public List<PcAdminStepRow> Steps { get; set; } = new();
    public List<(int Id, string Parent, string Child, bool NeedsConfirmation)> Group2Maps { get; set; } = new();
    public Dictionary<string, int> Counts { get; set; } = new();
}

// ---------------------------------------------------------------- جستجوی قطعه‌ی موجود / کاندیدهای تکراری (فقط‌خواندنی، بدون تغییر Schema)

/// <summary>یک قطعه‌ی موجود (درخواست تکمیل‌شده‌ی کاتالوگ یا ردیف Material Master) که با جستجو/کاندید تکراری مطابقت دارد.</summary>
public sealed class PcExistingPartHit
{
    /// <summary>"CatalogueRequest" | "MaterialItem".</summary>
    public string Source { get; set; } = string.Empty;
    public int SourceId { get; set; }
    public string PartNumber { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ManufacturerName { get; set; }
    public string? ManufacturerPartNumber { get; set; }
    public string? SupplierPartNumber { get; set; }

    /// <summary>SCRUM-428: کد واحد سنجش قطعه (MaterialUOM.Code) برای پر کردن واحد ردیف MR.</summary>
    public string? Unit { get; set; }

    /// <summary>فیلدی که مطابقت داده شد: PartNumber | ManufacturerPartNumber | SupplierPartNumber | OldManufacturerPartNumber | Description.</summary>
    public string MatchedOn { get; set; } = string.Empty;

    /// <summary>
    /// Exact = مقدار نرمال‌شده برابر؛ Partial = شامل جستجو. برای کاندید تکراری: SameMpnSameManufacturer (قوی) |
    /// SameMpnOtherManufacturer | SameMpnManufacturerUnknown | SameSupplierPartNumber.
    /// </summary>
    public string MatchKind { get; set; } = string.Empty;
}

public sealed class PcDuplicateProbe
{
    public string? ManufacturerPartNumber { get; set; }
    public string? SupplierPartNumber { get; set; }
    public int? ManufacturerId { get; set; }
    public string? NewManufacturerName { get; set; }

    /// <summary>خودِ درخواست در حال ویرایش از نتیجه حذف می‌شود.</summary>
    public int? ExcludeRequestId { get; set; }
}

public sealed class PcDuplicateResult
{
    public List<PcExistingPartHit> Candidates { get; set; } = new();

    /// <summary>بیش از یک قطعه‌ی تأییدشده با MPN و سازنده‌ی یکسان ⇒ انتخاب خودکار ممنوع؛ تصمیم با انسان.</summary>
    public bool IsAmbiguous { get; set; }

    /// <summary>تعداد درخواست‌های «در جریان» دیگر با همان MPN (جزئیات عمداً فاش نمی‌شود).</summary>
    public int InFlightRequestsWithSameMpn { get; set; }

    /// <summary>همیشه false: این سامانه هیچ ادغام خودکاری انجام نمی‌دهد.</summary>
    public bool AutoMergeAllowed => false;
}
