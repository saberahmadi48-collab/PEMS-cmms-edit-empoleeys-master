using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.ActorResolution;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Application.PartCatalogue;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Workflow;
using PEMS.WorkflowPlatform.Entities;
using PEMS.WorkflowPlatform.Interfaces;
using PEMS.WorkflowPlatform.Services;

namespace PEMS.Persistence.Services;

/// <summary>
/// سرویس «درخواست کاتالوگ قطعه» (Warehouse). همه‌ی منطق کسب‌وکار این‌جاست؛ Controller فقط ورودی را به PcActor/DTO تبدیل می‌کند.
/// مسیر تأیید داده‌محور است (PartCatalogueWorkflowStep + Planner خالص)، هر درخواست Snapshot مسیر خودش را دارد و تاریخچه
/// Append-only است (PartCatalogueRequestApproval). مرز امنیتی (مجوز/مالکیت/گیرنده‌ی گام) در همین کلاس اعمال می‌شود.
/// </summary>
public class PartCatalogueService : IPartCatalogueService
{
    public const string Module = "PartCatalogue";

    private readonly PEMSDbContext _ctx;
    private readonly INumberingService _numbering;
    private readonly INotificationService _notifications;
    private readonly IFileStorageService _files;
    private readonly IRoleModuleActionService _actions;
    private readonly PartCatalogueOptions _options;
    private readonly IWorkflowRuntime _runtime;
    private readonly IWorkflowInstanceControl _control;
    private readonly IWorkflowBindingService _bindings;
    private readonly IEnterpriseActorResolver _resolver;

    public PartCatalogueService(PEMSDbContext ctx, INumberingService numbering, INotificationService notifications,
        IFileStorageService files, IRoleModuleActionService actions, PartCatalogueOptions options,
        IWorkflowRuntime runtime, IWorkflowInstanceControl control, IWorkflowBindingService bindings, IEnterpriseActorResolver resolver)
    {
        _runtime = runtime;
        _control = control;
        _bindings = bindings;
        _resolver = resolver;
        _ctx = ctx;
        _numbering = numbering;
        _notifications = notifications;
        _files = files;
        _actions = actions;
        _options = options;
    }

    // ------------------------------------------------------------------ مجوز / دید

    private Task<bool> HasAsync(PcActor a, string action) => _actions.HasActionAsync(a.Roles, Module, action);

    private static string PermissionForKind(string kind) =>
        kind == PartCatalogueStepKinds.Approval ? "Approve" : "Review";

    private async Task<bool> CanSeeAllAsync(PcActor a) =>
        await HasAsync(a, "Admin") || await HasAsync(a, "Review") || await HasAsync(a, "Approve");

    private async Task<HashSet<string>> MyPoolKeysAsync(int employeeId) =>
        (await _ctx.PartCatalogueApproverAssignments
            .Where(x => x.EmployeeId == employeeId && x.IsActive)
            .Select(x => x.StepKey).ToListAsync()).ToHashSet();

    private static bool Editable(PartCatalogueRequest r) =>
        r.Status == PartCatalogueStatuses.Draft || r.Status == PartCatalogueStatuses.Returned;

    private async Task<bool> CanViewAsync(PartCatalogueRequest r, PcActor a)
    {
        if (r.RequesterEmployeeId == a.EmployeeId) return true;
        if (!await HasAsync(a, "View") && !await HasAsync(a, "Review") && !await HasAsync(a, "Approve") && !await HasAsync(a, "Admin"))
            return false;
        if (await CanSeeAllAsync(a)) return true;

        if (r.WorkflowInstanceId.HasValue)
            return await _ctx.WorkflowInstanceStageCandidates.AsNoTracking().AnyAsync(c => c.EmployeeId == a.EmployeeId && c.InstanceStage!.WorkflowInstanceId == r.WorkflowInstanceId);

        var pool = await MyPoolKeysAsync(a.EmployeeId);
        return await _ctx.PartCatalogueRequestSteps.AnyAsync(s =>
            s.PartCatalogueRequestId == r.PartCatalogueRequestId
            && (s.AssignedEmployeeId == a.EmployeeId || pool.Contains(s.StepKey)));
    }

    // ------------------------------------------------------------------ پیش‌نویس / ذخیره

    public async Task<int> CreateDraftAsync(string requestType, PcActor actor)
    {
        if (!await HasAsync(actor, "Create")) throw new UnauthorizedAccessException("PartCatalogue.Create required");

        var type = string.Equals(requestType, PartCatalogueRequestTypes.Change, StringComparison.OrdinalIgnoreCase)
            ? PartCatalogueRequestTypes.Change : PartCatalogueRequestTypes.Add;

        var entity = new PartCatalogueRequest
        {
            RequestType = type,
            Status = PartCatalogueStatuses.Draft,
            RequesterEmployeeId = actor.EmployeeId
        };
        // FREE FORMAT TEXT اجباری است - از ابتدا یک ردیف با مقدار «یک Space» (دستورالعمل صفحه ۲)
        entity.Attributes.Add(new PartCatalogueRequestAttribute { Name = PartCatalogueRequestAttribute.FreeFormatText, Value = " ", SortOrder = 0 });

        _ctx.PartCatalogueRequests.Add(entity);
        await _ctx.SaveChangesAsync();
        return entity.PartCatalogueRequestId;
    }

    private async Task<PartCatalogueRequest?> LoadTrackedAsync(int id, bool withSteps = false) =>
        withSteps
            ? await _ctx.PartCatalogueRequests.Include(r => r.Attributes).Include(r => r.Steps).FirstOrDefaultAsync(r => r.PartCatalogueRequestId == id && r.IsActive)
            : await _ctx.PartCatalogueRequests.Include(r => r.Attributes).FirstOrDefaultAsync(r => r.PartCatalogueRequestId == id && r.IsActive);

    private static PartCatalogueFormDto ToForm(PartCatalogueRequest r) => new()
    {
        Id = r.PartCatalogueRequestId,
        RowVersion = r.RowVersion,
        RequestType = r.RequestType,
        Urgent = r.Urgent,
        SectionId = r.SectionId,
        IsNewEquipment = r.IsNewEquipment,
        TechnicalClassId = r.TechnicalClassId,
        AssetType = r.AssetType,
        CommodityGroup1Id = r.CommodityGroup1Id,
        CommodityGroup2Id = r.CommodityGroup2Id,
        ProductFamilyId = r.ProductFamilyId,
        UnitOfMeasureId = r.UnitOfMeasureId,
        ManufacturerId = r.ManufacturerId,
        IsNewManufacturer = r.IsNewManufacturer,
        NewManufacturerName = r.NewManufacturerName,
        ManufacturerPartNumber = r.ManufacturerPartNumber,
        SupplierId = r.SupplierId,
        SupplierPartNumber = r.SupplierPartNumber,
        PartPrice = r.PartPrice,
        CurrencyId = r.CurrencyId,
        TypeDesignationId = r.TypeDesignationId,
        SparePartSerialNo = r.SparePartSerialNo,
        EquipmentSerialNo = r.EquipmentSerialNo,
        ManufacturerDrawingNo = r.ManufacturerDrawingNo,
        DocumentNo = r.DocumentNo,
        MinimumStock = r.MinimumStock,
        MaxConsumptionRate = r.MaxConsumptionRate,
        FirstPurchaseQuantity = r.FirstPurchaseQuantity,
        PlanningMethodId = r.PlanningMethodId,
        PrimaryIssuedRequired = r.PrimaryIssuedRequired,
        IssuedQty = r.IssuedQty,
        StockGradeId = r.StockGradeId,
        WantedReceiptDate = r.WantedReceiptDate,
        StorageVolume = r.StorageVolume,
        IsRd = r.IsRd,
        ExistingPartNumber = r.ExistingPartNumber,
        ChangesMatchInventory = r.ChangesMatchInventory,
        OldManufacturerName = r.OldManufacturerName,
        OldManufacturerPartNumber = r.OldManufacturerPartNumber,
        Attributes = r.Attributes.OrderBy(a => a.SortOrder).ThenBy(a => a.RequestAttributeId)
            .Select(a => new PartCatalogueAttributeDto { Name = a.Name, Value = a.Value }).ToList()
    };

    /// <summary>داده‌های DB که اعتبارسنج خالص نیاز دارد.</summary>
    private async Task<PartCatalogueValidationContext> BuildContextAsync(PartCatalogueRequest entity, PartCatalogueFormDto form)
    {
        var ctx = new PartCatalogueValidationContext
        {
            RequestType = entity.RequestType,
            HasRequestNumber = !string.IsNullOrEmpty(entity.RequestNumber),
            LowValueThresholdIrr = _options.LowValueThresholdIrr
        };

        if (form.ManufacturerId.HasValue)
        {
            var m = await _ctx.PartCatalogueManufacturers.Where(x => x.ManufacturerId == form.ManufacturerId)
                .Select(x => new { x.Name, x.IsLocal, x.IsWrong }).FirstOrDefaultAsync();
            if (m != null) { ctx.ManufacturerName = m.Name; ctx.ManufacturerIsLocal = m.IsLocal; ctx.ManufacturerIsWrong = m.IsWrong; }
        }

        if (form.IsNewManufacturer)
            ctx.ExistingManufacturers = (await _ctx.PartCatalogueManufacturers.Where(x => x.IsActive && !x.IsWrong)
                .Select(x => new { x.Name, x.NormalizedName }).ToListAsync())
                .Select(x => (x.Name, x.NormalizedName)).ToList();

        if (form.SupplierId.HasValue)
            ctx.SupplierName = await _ctx.PartCatalogueSuppliers.Where(x => x.SupplierId == form.SupplierId).Select(x => x.Name).FirstOrDefaultAsync();
        if (form.CurrencyId.HasValue)
            ctx.CurrencyCode = await _ctx.PartCatalogueCurrencies.Where(x => x.CurrencyId == form.CurrencyId).Select(x => x.Code).FirstOrDefaultAsync();
        if (form.PlanningMethodId.HasValue)
            ctx.PlanningMethodCode = await _ctx.PartCataloguePlanningMethods.Where(x => x.PlanningMethodId == form.PlanningMethodId).Select(x => x.Code).FirstOrDefaultAsync();

        if (form.SectionId.HasValue)
        {
            var g1 = await _ctx.PartCatalogueSectionCommodityGroups.Where(x => x.SectionId == form.SectionId)
                .Select(x => x.CommodityGroupId).ToListAsync();
            if (g1.Count > 0) ctx.AllowedGroup1Ids = g1.ToHashSet();
        }
        if (form.CommodityGroup1Id.HasValue)
        {
            var g2 = await _ctx.PartCatalogueCommodityGroupChildren.Where(x => x.ParentGroupId == form.CommodityGroup1Id)
                .Select(x => x.ChildGroupId).ToListAsync();
            if (g2.Count > 0) ctx.AllowedGroup2Ids = g2.ToHashSet();
        }

        if (entity.PartCatalogueRequestId > 0)
        {
            var att = await _ctx.PartCatalogueRequestAttachments
                .Where(x => x.PartCatalogueRequestId == entity.PartCatalogueRequestId && x.IsActive)
                .GroupBy(x => x.Kind).Select(g => new { Kind = g.Key, C = g.Count() }).ToListAsync();
            ctx.DrawingOrDocumentAttachmentCount = att.Where(a => a.Kind == "Drawing" || a.Kind == "Document").Sum(a => a.C);
            ctx.SupportAttachmentCount = att.Where(a => a.Kind == "Support").Sum(a => a.C);
            ctx.EquipmentLineCount = await _ctx.PartCatalogueRequestEquipment.CountAsync(x => x.PartCatalogueRequestId == entity.PartCatalogueRequestId);
        }

        if (!string.IsNullOrWhiteSpace(form.ExistingPartNumber))
        {
            var src = await ResolveExistingPartAsync(form.ExistingPartNumber.Trim());
            ctx.ExistingPartResolved = src != null;
            ctx.ExistingPartUnitOfMeasureId = src?.UnitOfMeasureId;
        }

        return ctx;
    }

    public async Task<PcResult<int>> SaveAsync(PartCatalogueFormDto form, PcActor actor)
    {
        var entity = await LoadTrackedAsync(form.Id);
        if (entity == null) return Fail<int>("Id", "Pc_Err_NotFound");
        if (entity.RequesterEmployeeId != actor.EmployeeId || !Editable(entity) || !await HasAsync(actor, "Create"))
            return Fail<int>("Id", "Pc_Err_NotEditable");

        form.RequestType = entity.RequestType;   // نوع (Add/Change) پس از ایجاد قابل تغییر نیست
        var ctx = await BuildContextAsync(entity, form);

        // قواعد خودکار: LOCAL ⇒ NO_PART_NR ؛ Planning A ⇒ صفر
        form.ManufacturerPartNumber = PartCatalogueRules.ApplyManufacturerPartNumber(ctx.ManufacturerIsLocal, form.ManufacturerPartNumber);
        (form.MinimumStock, form.MaxConsumptionRate) = PartCatalogueRules.ApplyPlanningMethod(ctx.PlanningMethodCode, form.MinimumStock, form.MaxConsumptionRate);
        if (form.IsNewManufacturer)
        {
            form.ManufacturerId = null;
            form.NewManufacturerName = PartCatalogueRules.NormalizeNewManufacturerName(form.NewManufacturerName);
        }
        else form.NewManufacturerName = null;

        // FREE FORMAT TEXT: تک‌خطی + مقدار همیشه موجود؛ Change ⇒ افزودن سازنده/کد قبلی
        var fft = form.Attributes.FirstOrDefault(a => string.Equals(a.Name, PartCatalogueRequestAttribute.FreeFormatText, StringComparison.OrdinalIgnoreCase));
        if (fft == null)
        {
            fft = new PartCatalogueAttributeDto { Name = PartCatalogueRequestAttribute.FreeFormatText, Value = " " };
            form.Attributes.Insert(0, fft);
        }
        fft.Name = PartCatalogueRequestAttribute.FreeFormatText;
        fft.Value = PartCatalogueRules.NormalizeFreeFormatText(fft.Value);

        if (entity.RequestType == PartCatalogueRequestTypes.Change)
        {
            var currentName = form.IsNewManufacturer ? form.NewManufacturerName : ctx.ManufacturerName;
            var nameChanged = !string.IsNullOrWhiteSpace(entity.OldManufacturerName)
                && PartCatalogueRules.NormalizeName(entity.OldManufacturerName) != PartCatalogueRules.NormalizeName(currentName);
            var codeChanged = !string.IsNullOrWhiteSpace(entity.OldManufacturerPartNumber)
                && PartCatalogueRules.NormalizeName(entity.OldManufacturerPartNumber) != PartCatalogueRules.NormalizeName(form.ManufacturerPartNumber);
            fft.Value = PartCatalogueRules.AppendOldVendorInfo(fft.Value, entity.OldManufacturerName, entity.OldManufacturerPartNumber, nameChanged, codeChanged);
        }

        var validation = PartCatalogueValidator.Validate(form, ctx, forSubmit: false);
        if (!validation.Success) return new PcResult<int> { Result = validation, Value = entity.PartCatalogueRequestId };

        var badRef = await FindInvalidReferenceAsync(form);
        if (badRef != null) return Fail<int>(badRef, "Pc_Err_InvalidReference");

        if (form.RowVersion is { Length: > 0 })
            _ctx.Entry(entity).Property(e => e.RowVersion).OriginalValue = form.RowVersion;

        Apply(entity, form);

        // مشتق از Type Designation (فقط‌خواندنی در UI)
        var td = form.TypeDesignationId.HasValue
            ? await _ctx.PartCatalogueTypeDesignations.Where(x => x.TypeDesignationId == form.TypeDesignationId).Select(x => new { x.IsCritical, x.IsCapital }).FirstOrDefaultAsync()
            : null;
        entity.IsCritical = td?.IsCritical ?? false;
        entity.IsCapital = td?.IsCapital ?? false;

        ApplyAttributes(entity, form.Attributes);

        try { await _ctx.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException)
        {
            _ctx.ChangeTracker.Clear();
            return Fail<int>("RowVersion", "Pc_Err_Concurrency");
        }

        return new PcResult<int> { Result = validation, Value = entity.PartCatalogueRequestId };
    }

    private static void Apply(PartCatalogueRequest e, PartCatalogueFormDto f)
    {
        e.Urgent = f.Urgent;
        e.SectionId = f.SectionId;
        e.IsNewEquipment = f.IsNewEquipment;
        e.TechnicalClassId = f.TechnicalClassId;
        e.AssetType = f.AssetType is "FixedAsset" or "LowValueAsset" ? f.AssetType : PartCatalogueAssetTypes.None;
        e.CommodityGroup1Id = f.CommodityGroup1Id;
        e.CommodityGroup2Id = f.CommodityGroup2Id;
        e.ProductFamilyId = f.ProductFamilyId;
        e.UnitOfMeasureId = f.UnitOfMeasureId;
        e.ManufacturerId = f.ManufacturerId;
        e.IsNewManufacturer = f.IsNewManufacturer;
        e.NewManufacturerName = f.NewManufacturerName;
        e.ManufacturerPartNumber = f.ManufacturerPartNumber;
        e.SupplierId = f.SupplierId;
        e.SupplierPartNumber = Trim(f.SupplierPartNumber);
        e.PartPrice = f.PartPrice;
        e.CurrencyId = f.CurrencyId;
        e.TypeDesignationId = f.TypeDesignationId;
        e.SparePartSerialNo = Trim(f.SparePartSerialNo);
        e.EquipmentSerialNo = Trim(f.EquipmentSerialNo);
        e.ManufacturerDrawingNo = Trim(f.ManufacturerDrawingNo);
        e.DocumentNo = Trim(f.DocumentNo);
        e.MinimumStock = f.MinimumStock;
        e.MaxConsumptionRate = f.MaxConsumptionRate;
        e.FirstPurchaseQuantity = f.FirstPurchaseQuantity;
        e.PlanningMethodId = f.PlanningMethodId;
        e.PrimaryIssuedRequired = f.PrimaryIssuedRequired;
        e.IssuedQty = f.IssuedQty;
        e.StockGradeId = f.StockGradeId;
        e.WantedReceiptDate = f.WantedReceiptDate;
        e.StorageVolume = Trim(f.StorageVolume);
        e.IsRd = f.IsRd;
        e.ExistingPartNumber = Trim(f.ExistingPartNumber);
        e.ChangesMatchInventory = f.ChangesMatchInventory;
        // OldManufacturer* فقط توسط «Load» پر می‌شود (مقدار منبع)، نه از فرم
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private void ApplyAttributes(PartCatalogueRequest entity, List<PartCatalogueAttributeDto> rows)
    {
        var wanted = new List<PartCatalogueAttributeDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var name = (row.Name ?? string.Empty).Trim();
            if (name.Length == 0 || !seen.Add(name)) continue;
            wanted.Add(new PartCatalogueAttributeDto { Name = name, Value = row.Value });
        }

        foreach (var existing in entity.Attributes.ToList())
            if (!wanted.Any(w => string.Equals(w.Name, existing.Name, StringComparison.OrdinalIgnoreCase)))
                _ctx.PartCatalogueRequestAttributes.Remove(existing);

        var order = 0;
        foreach (var w in wanted)
        {
            var hit = entity.Attributes.FirstOrDefault(a => string.Equals(a.Name, w.Name, StringComparison.OrdinalIgnoreCase));
            if (hit == null)
                entity.Attributes.Add(new PartCatalogueRequestAttribute { Name = w.Name, Value = w.Value, SortOrder = order });
            else { hit.Value = w.Value; hit.SortOrder = order; }
            order++;
        }
    }

    /// <summary>وجود مرجع‌ها را قبل از ذخیره بررسی می‌کند (پیام تمیز به‌جای خطای FK).</summary>
    private async Task<string?> FindInvalidReferenceAsync(PartCatalogueFormDto f)
    {
        async Task<bool> Bad(int? id, Func<int, Task<bool>> exists) => id.HasValue && !await exists(id.Value);

        if (await Bad(f.SectionId, id => _ctx.PartCatalogueSections.AnyAsync(x => x.SectionId == id && x.IsActive))) return nameof(f.SectionId);
        if (await Bad(f.TechnicalClassId, id => _ctx.PartCatalogueTechnicalClasses.AnyAsync(x => x.TechnicalClassId == id && x.IsActive))) return nameof(f.TechnicalClassId);
        if (await Bad(f.CommodityGroup1Id, id => _ctx.PartCatalogueCommodityGroups.AnyAsync(x => x.CommodityGroupId == id && x.IsActive))) return nameof(f.CommodityGroup1Id);
        if (await Bad(f.CommodityGroup2Id, id => _ctx.PartCatalogueCommodityGroups.AnyAsync(x => x.CommodityGroupId == id && x.IsActive))) return nameof(f.CommodityGroup2Id);
        if (await Bad(f.ProductFamilyId, id => _ctx.PartCatalogueProductFamilies.AnyAsync(x => x.ProductFamilyId == id && x.IsActive))) return nameof(f.ProductFamilyId);
        if (await Bad(f.UnitOfMeasureId, id => _ctx.MaterialUOMs.AnyAsync(x => x.MaterialUOMId == id && x.IsActive))) return nameof(f.UnitOfMeasureId);
        if (await Bad(f.ManufacturerId, id => _ctx.PartCatalogueManufacturers.AnyAsync(x => x.ManufacturerId == id && x.IsActive))) return nameof(f.ManufacturerId);
        if (await Bad(f.SupplierId, id => _ctx.PartCatalogueSuppliers.AnyAsync(x => x.SupplierId == id && x.IsActive))) return nameof(f.SupplierId);
        if (await Bad(f.CurrencyId, id => _ctx.PartCatalogueCurrencies.AnyAsync(x => x.CurrencyId == id && x.IsActive))) return nameof(f.CurrencyId);
        if (await Bad(f.TypeDesignationId, id => _ctx.PartCatalogueTypeDesignations.AnyAsync(x => x.TypeDesignationId == id && x.IsActive))) return nameof(f.TypeDesignationId);
        if (await Bad(f.PlanningMethodId, id => _ctx.PartCataloguePlanningMethods.AnyAsync(x => x.PlanningMethodId == id && x.IsActive))) return nameof(f.PlanningMethodId);
        if (await Bad(f.StockGradeId, id => _ctx.PartCatalogueStockGrades.AnyAsync(x => x.StockGradeId == id && x.IsActive))) return nameof(f.StockGradeId);
        return null;
    }

    private static PcResult<T> Fail<T>(string field, string key, string? arg = null) =>
        new() { Result = PcResult.Fail(field, key, arg) };

    // ------------------------------------------------------------------ شماره‌دهی

    public async Task<PcResult> GenerateNumberAsync(int id, byte[]? rowVersion, PcActor actor)
    {
        var e = await _ctx.PartCatalogueRequests.FirstOrDefaultAsync(r => r.PartCatalogueRequestId == id && r.IsActive);
        if (e == null) return PcResult.Fail("Id", "Pc_Err_NotFound");
        if (e.RequesterEmployeeId != actor.EmployeeId || !Editable(e) || !await HasAsync(actor, "Create"))
            return PcResult.Fail("Id", "Pc_Err_NotEditable");
        if (!string.IsNullOrEmpty(e.RequestNumber)) return PcResult.Fail("RequestNumber", "Pc_Err_NumberExists");

        if (rowVersion is { Length: > 0 }) _ctx.Entry(e).Property(x => x.RowVersion).OriginalValue = rowVersion;

        e.RequestNumber = await _numbering.GetNextNumberAsync("PartCatalogueRequest");
        try { await _ctx.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { _ctx.ChangeTracker.Clear(); return PcResult.Fail("RowVersion", "Pc_Err_Concurrency"); }
        catch (DbUpdateException) { _ctx.ChangeTracker.Clear(); return PcResult.Fail("RequestNumber", "Pc_Err_NumberConflict"); }
        return PcResult.Ok();
    }

    // ------------------------------------------------------------------ Change: Load

    private sealed record ExistingPart(
        int? UnitOfMeasureId, int? ManufacturerId, string? ManufacturerName, string? ManufacturerPartNumber,
        PartCatalogueRequest? Source, MaterialItem? Material);

    private async Task<ExistingPart?> ResolveExistingPartAsync(string partNumber)
    {
        // ۱) آخرین درخواست «تکمیل‌شده»ی کاتالوگ با همین شماره‌ی قطعه‌ی ثبت‌شده
        var src = await _ctx.PartCatalogueRequests.AsNoTracking()
            .Include(r => r.Attributes).Include(r => r.Manufacturer)
            .Where(r => r.IsActive && r.Status == PartCatalogueStatuses.Completed && r.AssignedPartNumber == partNumber)
            .OrderByDescending(r => r.CompletedDate).FirstOrDefaultAsync();
        if (src != null)
            return new ExistingPart(src.UnitOfMeasureId, src.ManufacturerId,
                src.Manufacturer?.Name ?? src.NewManufacturerName, src.ManufacturerPartNumber, src, null);

        // ۲) Material Master موجود (MRS) با همین MaterialCode
        var mat = await _ctx.MaterialItems.AsNoTracking().Include(m => m.Uom)
            .FirstOrDefaultAsync(m => m.MaterialCode == partNumber);
        if (mat != null)
            return new ExistingPart(mat.UomId, null, mat.Manufacturer, mat.ManufacturerPartNumber, null, mat);

        return null;
    }

    public async Task<PcResult> LoadExistingPartAsync(int id, string partNumber, PcActor actor)
    {
        var e = await LoadTrackedAsync(id);
        if (e == null) return PcResult.Fail("Id", "Pc_Err_NotFound");
        if (e.RequesterEmployeeId != actor.EmployeeId || !Editable(e) || !await HasAsync(actor, "Create"))
            return PcResult.Fail("Id", "Pc_Err_NotEditable");
        if (e.RequestType != PartCatalogueRequestTypes.Change) return PcResult.Fail("Id", "Pc_Err_NotChangeRequest");

        partNumber = (partNumber ?? string.Empty).Trim();
        if (partNumber.Length == 0) return PcResult.Fail(nameof(PartCatalogueFormDto.ExistingPartNumber), "Pc_Err_Required");

        var part = await ResolveExistingPartAsync(partNumber);
        if (part == null) return PcResult.Fail(nameof(PartCatalogueFormDto.ExistingPartNumber), "Pc_Err_ChangePartNotFound");

        e.ExistingPartNumber = partNumber;
        e.UnitOfMeasureId = part.UnitOfMeasureId;
        e.OldManufacturerName = part.ManufacturerName;
        e.OldManufacturerPartNumber = part.ManufacturerPartNumber;

        var src = part.Source;
        if (src != null)
        {
            e.SectionId = src.SectionId; e.TechnicalClassId = src.TechnicalClassId; e.AssetType = src.AssetType;
            e.CommodityGroup1Id = src.CommodityGroup1Id; e.CommodityGroup2Id = src.CommodityGroup2Id; e.ProductFamilyId = src.ProductFamilyId;
            e.ManufacturerId = src.ManufacturerId; e.IsNewManufacturer = false; e.NewManufacturerName = null;
            e.ManufacturerPartNumber = src.ManufacturerPartNumber; e.SupplierId = src.SupplierId; e.SupplierPartNumber = src.SupplierPartNumber;
            e.PartPrice = src.PartPrice; e.CurrencyId = src.CurrencyId; e.TypeDesignationId = src.TypeDesignationId;
            e.IsCritical = src.IsCritical; e.IsCapital = src.IsCapital;
            e.SparePartSerialNo = src.SparePartSerialNo; e.EquipmentSerialNo = src.EquipmentSerialNo;
            e.ManufacturerDrawingNo = src.ManufacturerDrawingNo; e.DocumentNo = src.DocumentNo;
            e.MinimumStock = src.MinimumStock; e.MaxConsumptionRate = src.MaxConsumptionRate; e.FirstPurchaseQuantity = src.FirstPurchaseQuantity;
            e.PlanningMethodId = src.PlanningMethodId; e.PrimaryIssuedRequired = src.PrimaryIssuedRequired; e.IssuedQty = src.IssuedQty;
            e.StockGradeId = src.StockGradeId; e.StorageVolume = src.StorageVolume;

            ApplyAttributes(e, src.Attributes.OrderBy(a => a.SortOrder).Select(a => new PartCatalogueAttributeDto { Name = a.Name, Value = a.Value }).ToList());
        }
        else if (part.Material != null)
        {
            // Material Master فقط تعدادی از فیلدها را دارد: شرح → FREE FORMAT TEXT، سازنده (اگر در لیست باشد)
            var mfrKey = PartCatalogueRules.NormalizeName(part.Material.Manufacturer);
            if (mfrKey.Length > 0)
            {
                var mfr = await _ctx.PartCatalogueManufacturers.FirstOrDefaultAsync(m => m.NormalizedName == mfrKey && m.IsActive && !m.IsWrong);
                if (mfr != null) { e.ManufacturerId = mfr.ManufacturerId; e.IsNewManufacturer = false; }
            }
            e.ManufacturerPartNumber = part.Material.ManufacturerPartNumber;
            var text = PartCatalogueRules.NormalizeFreeFormatText(
                string.Join(" ", new[] { part.Material.Description, part.Material.LongDescription }.Where(s => !string.IsNullOrWhiteSpace(s))));
            ApplyAttributes(e, new List<PartCatalogueAttributeDto> { new() { Name = PartCatalogueRequestAttribute.FreeFormatText, Value = text } });
        }

        try { await _ctx.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { _ctx.ChangeTracker.Clear(); return PcResult.Fail("RowVersion", "Pc_Err_Concurrency"); }
        return PcResult.Ok();
    }

    // ------------------------------------------------------------------ Submit

    public async Task<PcResult> SubmitAsync(int id, byte[]? rowVersion, PcActor actor)
    {
        var e = await LoadTrackedAsync(id, withSteps: true);
        if (e == null) return PcResult.Fail("Id", "Pc_Err_NotFound");
        if (e.RequesterEmployeeId != actor.EmployeeId || !Editable(e) || !await HasAsync(actor, "Create"))
            return PcResult.Fail("Id", "Pc_Err_NotEditable");

        var form = ToForm(e);
        var ctx = await BuildContextAsync(e, form);
        var validation = PartCatalogueValidator.Validate(form, ctx, forSubmit: true);
        if (!validation.Success) return validation;

        if (rowVersion is { Length: > 0 }) _ctx.Entry(e).Property(x => x.RowVersion).OriginalValue = rowVersion;
        var started = await StartRoutingAsync(e, actor, validation);
        return started;
    }

    /// <summary>
    /// تلاش مجدد مسیریابی پس از اصلاح ساختار سازمانی (وضعیت OrgResolution). فقط درخواست‌دهنده یا Admin ماژول؛ مدیر هرگز
    /// تأییدکننده/مدیر را انتخاب نمی‌کند، فقط Resolver مرکزی دوباره اجرا می‌شود. بازیگر Workflow همچنان درخواست‌دهنده‌ی اصلی است.
    /// </summary>
    public async Task<PcResult> RetryRoutingAsync(int id, byte[]? rowVersion, PcActor actor)
    {
        var e = await LoadTrackedAsync(id, withSteps: true);
        if (e == null) return PcResult.Fail("Id", "Pc_Err_NotFound");
        if (e.Status != PartCatalogueStatuses.OrganizationResolutionRequired) return PcResult.Fail("Id", "Pc_Err_NotActionable");
        var isOwner = e.RequesterEmployeeId == actor.EmployeeId;
        if (!(isOwner || await HasAsync(actor, "Admin"))) return PcResult.Fail("Id", "Pc_Err_NotAuthorized");

        if (rowVersion is { Length: > 0 }) _ctx.Entry(e).Property(x => x.RowVersion).OriginalValue = rowVersion;
        return await StartRoutingAsync(e, actor, PcResult.Ok());
    }

    /// <summary>مسیریابی مشترک Submit/Retry. خطای صرفاً سازمانی ⇒ درخواست حفظ و به OrgResolution می‌رود؛ سایر خطاهای پیکربندی ⇒ رد.</summary>
    private async Task<PcResult> StartRoutingAsync(PartCatalogueRequest e, PcActor actor, PcResult validation)
    {
        var fromStatus = e.Status;
        var isResubmit = fromStatus == PartCatalogueStatuses.Returned;
        var isRetry = fromStatus == PartCatalogueStatuses.OrganizationResolutionRequired;

        var (route, orgIssue) = await PreviewRouteAsync(e, actor);
        if (!route.Success) return route;

        if (orgIssue != null)
        {
            return await BlockForOrganizationAsync(e, actor, fromStatus, orgIssue, isRetry, validation);
        }

        e.SubmittedDate ??= DateTime.Now;
        e.Status = PartCatalogueStatuses.InApproval;   // با ذخیره‌ی Instance (همان DbContext) اتمیک ثبت می‌شود
        e.RoutingBlockReason = null;

        int instanceId;
        try
        {
            instanceId = await _runtime.StartInstanceAsync(PartCatalogueWorkflow.Module, PartCatalogueWorkflow.EntityType, e.PartCatalogueRequestId,
                PartCatalogueWorkflow.Context, null, e.RequesterEmployeeId);
        }
        catch (WorkflowActorResolutionException ex)
        {
            e.Status = fromStatus;
            if (ex.State == ActorResolutionStates.OrganizationResolutionRequired)
                return await BlockForOrganizationAsync(e, actor, fromStatus, ex.State, isRetry, validation);
            var r = new PcResult();
            r.Errors.Add(new PcMessage("Route", RouteKey(ex.State, await HasAsync(actor, "Admin")), "Pc_Step_" + ex.StageName));
            return r;
        }
        catch (DbUpdateConcurrencyException) { _ctx.ChangeTracker.Clear(); return PcResult.Fail("RowVersion", "Pc_Err_Concurrency"); }
        catch (InvalidOperationException)
        {
            e.Status = fromStatus;
            return PcResult.Fail("Route", "Pc_Err_WorkflowNotConfigured");
        }

        e.WorkflowInstanceId = instanceId;
        var inst = await LatestInstanceAsync(e.PartCatalogueRequestId);
        e.Status = StatusFromInstance(inst?.Status ?? "Running", PartCatalogueStatuses.InApproval);
        if (e.Status == PartCatalogueStatuses.Completed) e.CompletedDate = DateTime.Now;
        await SyncProjectionAsync(e);

        e.Approvals.Add(new PartCatalogueRequestApproval
        {
            StepKey = null, Action = isRetry ? PartCatalogueActions.RetryRouting : isResubmit ? PartCatalogueActions.Resubmit : PartCatalogueActions.Submit,
            ActorEmployeeId = actor.EmployeeId, FromStatus = fromStatus, ToStatus = e.Status
        });

        try { await _ctx.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { _ctx.ChangeTracker.Clear(); return PcResult.Fail("RowVersion", "Pc_Err_Concurrency"); }

        await NotifyAfterTransitionAsync(e, e.Steps.FirstOrDefault(s => s.Status == PartCatalogueRequestStep.Active), actor.EmployeeId);
        return validation;
    }

    private async Task<PcResult> BlockForOrganizationAsync(PartCatalogueRequest e, PcActor actor, string fromStatus, string issue, bool isRetry, PcResult validation)
    {
        if (isRetry)
        {
            // هنوز حل نشده: وضعیت و سابقه‌ی قبلی بدون تغییر؛ دلیل فعلی به‌روز و پیام روشن برگردانده می‌شود
            e.RoutingBlockReason = issue;
            try { await _ctx.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException) { _ctx.ChangeTracker.Clear(); return PcResult.Fail("RowVersion", "Pc_Err_Concurrency"); }
            var again = new PcResult();
            again.Errors.Add(new PcMessage("Route", "Pc_Route_State_" + ActorResolutionStates.OrganizationResolutionRequired, issue));
            return again;
        }

        e.SubmittedDate ??= DateTime.Now;
        e.Status = PartCatalogueStatuses.OrganizationResolutionRequired;
        e.RoutingBlockReason = issue;
        e.Approvals.Add(new PartCatalogueRequestApproval
        {
            StepKey = null, Action = PartCatalogueActions.OrgResolutionRequired, Comment = issue,
            ActorEmployeeId = actor.EmployeeId, FromStatus = fromStatus, ToStatus = e.Status
        });
        try { await _ctx.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { _ctx.ChangeTracker.Clear(); return PcResult.Fail("RowVersion", "Pc_Err_Concurrency"); }
        validation.Warnings.Add(new PcMessage("Route", "Pc_Route_State_" + ActorResolutionStates.OrganizationResolutionRequired, issue));
        return validation;
    }

    /// <summary>اولین گام Pending را Active می‌کند (null = مسیر تمام است).</summary>
    private static PartCatalogueRequestStep? ActivateNext(PartCatalogueRequest r)
    {
        var next = r.Steps.Where(s => s.Status == PartCatalogueRequestStep.Pending).OrderBy(s => s.StepOrder).FirstOrDefault();
        if (next != null) { next.Status = PartCatalogueRequestStep.Active; next.ActivatedDate = DateTime.Now; }
        return next;
    }


    // ====================================================================== مسیر تأیید روی PEMS.WorkflowPlatform
    // مرجع نهایی مسیر: IWorkflowRuntime. جدول‌های PartCatalogueRequestStep/Approval فقط Projection خواندنی برای UI و چاپ هستند.
    // مسیر قدیمی (PartCatalogueRoutingPlanner + ExecuteLegacy...) فقط برای درخواست‌های Legacy (WorkflowInstanceId = null) باقی مانده و DEPRECATED است.

    private static string RouteKey(string state, bool admin) => admin ? "Pc_Route_State_" + state : "Pc_Route_NoEligibleApprover";

    private Task<WorkflowInstance?> LatestInstanceAsync(int requestId) =>
        _ctx.WorkflowInstances.AsNoTracking()
            .Where(i => i.Module == PartCatalogueWorkflow.Module && i.BusinessObjectType == PartCatalogueWorkflow.EntityType && i.BusinessObjectId == requestId)
            .OrderByDescending(i => i.WorkflowInstanceId).FirstOrDefaultAsync();

    private Task<bool> IsCandidateAsync(int instanceId, int employeeId) =>
        _ctx.WorkflowInstanceStages.AsNoTracking().AnyAsync(s => s.WorkflowInstanceId == instanceId && s.Status == "Pending"
            && s.Candidates.Any(c => c.IsSelected && c.EmployeeId == employeeId));

    /// <summary>پیش‌بینی مسیر قبل از Submit: همه‌ی مراحل قابل‌اجرا باید بازیگر واجد شرایط داشته باشند، وگرنه Submit با دلیل تشخیصی رد می‌شود.</summary>
    private async Task<(PcResult Errors, string? OrgIssue)> PreviewRouteAsync(PartCatalogueRequest e, PcActor actor)
    {
        var errors = new PcResult();
        string? orgIssue = null;
        var binding = await _bindings.ResolveAsync(PartCatalogueWorkflow.Module, PartCatalogueWorkflow.EntityType, PartCatalogueWorkflow.Context);
        if (binding == null) { errors.Errors.Add(new PcMessage("Route", "Pc_Err_WorkflowNotConfigured")); return (errors, null); }

        var stages = await _ctx.WorkflowStages.AsNoTracking().Where(s => s.WorkflowVersionId == binding.Version.WorkflowVersionId).OrderBy(s => s.StageOrder).ToListAsync();
        var facts = new Dictionary<string, string?>(await new PartCatalogueWorkflowContextProvider(_ctx)
            .GetFactsAsync(PartCatalogueWorkflow.Module, PartCatalogueWorkflow.EntityType, e.PartCatalogueRequestId), StringComparer.Ordinal);
        var conditions = new WorkflowConditionEvaluator(PartCatalogueWorkflow.Fields);
        var admin = await HasAsync(actor, "Admin");

        foreach (var st in stages.Where(s => !string.IsNullOrWhiteSpace(s.ResponsibilityKey)))
        {
            if (!string.IsNullOrWhiteSpace(st.EntryCondition) && !conditions.Evaluate(st.EntryCondition, facts)) continue;
            var r = await _resolver.ResolveAsync(new ActorResolutionRequest(st.ResponsibilityKey!, e.RequesterEmployeeId, facts));
            if (r.State == ActorResolutionStates.OrganizationResolutionRequired) { orgIssue ??= r.OrganizationIssue ?? r.State; continue; }
            if (!r.IsUsable && r.State != ActorResolutionStates.NotApplicable)
                errors.Errors.Add(new PcMessage("Route", RouteKey(r.State, admin), "Pc_Step_" + st.Name));
        }
        return (errors, orgIssue);
    }

    /// <summary>Projection: وضعیت گام‌ها را از Instance/InstanceStage/History موتور گردش‌کار به PartCatalogueRequestStep منتقل می‌کند.</summary>
    private async Task SyncProjectionAsync(PartCatalogueRequest e)
    {
        var inst = await LatestInstanceAsync(e.PartCatalogueRequestId);
        if (inst == null) return;

        var stages = await _ctx.WorkflowStages.AsNoTracking().Where(s => s.WorkflowVersionId == inst.WorkflowVersionId).OrderBy(s => s.StageOrder).ToListAsync();
        var instStages = await _ctx.WorkflowInstanceStages.AsNoTracking().Include(x => x.Candidates).Where(x => x.WorkflowInstanceId == inst.WorkflowInstanceId).ToListAsync();
        var skips = await _ctx.WorkflowHistories.AsNoTracking().Where(h => h.WorkflowInstanceId == inst.WorkflowInstanceId && h.ActionType == "AutoSkip").ToListAsync();
        var conds = await _ctx.PartCatalogueWorkflowSteps.AsNoTracking().ToDictionaryAsync(s => s.StepKey, s => s.ConditionKey);

        // پیش‌نمایش شرط‌ها: مراحل هنوز واردنشده با EntryCondition نادرست از همین حالا «Skipped» نمایش داده می‌شوند (اجرای واقعی Skip هنگام ورود به مرحله است)
        var facts = new Dictionary<string, string?>(await new PartCatalogueWorkflowContextProvider(_ctx)
            .GetFactsAsync(PartCatalogueWorkflow.Module, PartCatalogueWorkflow.EntityType, e.PartCatalogueRequestId), StringComparer.Ordinal);
        var evaluator = new WorkflowConditionEvaluator(PartCatalogueWorkflow.Fields);

        _ctx.PartCatalogueRequestSteps.RemoveRange(e.Steps.ToList());
        e.Steps.Clear();

        foreach (var st in stages)
        {
            var step = new PartCatalogueRequestStep { StepKey = st.Name, StepOrder = st.StageOrder, Status = PartCatalogueRequestStep.Pending };
            var instStage = instStages.Where(x => x.WorkflowStageId == st.WorkflowStageId).OrderByDescending(x => x.WorkflowInstanceStageId).FirstOrDefault();
            var skip = skips.FirstOrDefault(h => h.FromStageId == st.WorkflowStageId);

            if (inst.Status == "Running" && inst.CurrentStageId == st.WorkflowStageId)
            {
                step.Status = PartCatalogueRequestStep.Active;
                step.ActivatedDate = instStage?.AssignedDate;
            }
            else if (instStage != null && instStage.Status == "Approve")
            {
                step.Status = PartCatalogueRequestStep.Approved;
                step.DecidedDate = instStage.ActionDate;
            }
            else if (skip != null)
            {
                step.Status = PartCatalogueRequestStep.Skipped;
                step.SkipReason = skip.Comment == "EntryCondition"
                    ? "Condition:" + (conds.TryGetValue(st.Name, out var ck) ? ck : "Always")
                    : skip.Comment;
            }
            else if (instStage == null && !string.IsNullOrWhiteSpace(st.EntryCondition) && !evaluator.Evaluate(st.EntryCondition, facts))
            {
                step.Status = PartCatalogueRequestStep.Skipped;
                step.SkipReason = "Condition:" + (conds.TryGetValue(st.Name, out var ck2) ? ck2 : "Always");
            }

            var selected = instStage?.Candidates.Where(c => c.IsSelected).Select(c => c.EmployeeId).ToList();
            if (selected is { Count: 1 }) step.AssignedEmployeeId = selected[0];
            e.Steps.Add(step);
        }

        e.CurrentStepKey = inst.Status == "Running" ? stages.FirstOrDefault(s => s.WorkflowStageId == inst.CurrentStageId)?.Name : null;
    }

    private static string StatusFromInstance(string instanceStatus, string current) => instanceStatus switch
    {
        "Running" => PartCatalogueStatuses.InApproval,
        "Completed" => PartCatalogueStatuses.Completed,
        "Returned" => PartCatalogueStatuses.Returned,
        "Cancelled" => PartCatalogueStatuses.Cancelled,
        "Rejected" => PartCatalogueStatuses.Cancelled,
        _ => current
    };

    private async Task<PcResult> ExecuteRuntimeAsync(PartCatalogueRequest e, string action, string? comment, string? partNumber, PcActor actor)
    {
        var fromStatus = e.Status;
        var isAdmin = await HasAsync(actor, "Admin");
        var inst = await LatestInstanceAsync(e.PartCatalogueRequestId);
        var running = inst is { Status: "Running" };
        var activeKey = e.CurrentStepKey;

        if (string.Equals(action, "Cancel", StringComparison.OrdinalIgnoreCase))
        {
            if (e.Status == PartCatalogueStatuses.Completed || e.Status == PartCatalogueStatuses.Cancelled)
                return PcResult.Fail("Id", "Pc_Err_NotActionable");

            // لغو فقط توسط درخواست‌دهنده (تصمیم مالک محصول)؛ نه گیرنده‌ی گام جاری و نه Admin.
            if (e.RequesterEmployeeId != actor.EmployeeId) return PcResult.Fail("Id", "Pc_Err_NotAuthorized");

            if (running) await _control.CancelInstanceAsync(inst!.WorkflowInstanceId, actor.EmployeeId, comment ?? "Cancelled by requester");

            e.Status = PartCatalogueStatuses.Cancelled;
            e.CompletedDate = DateTime.Now;
            AddApproval(e, activeKey, PartCatalogueActions.Cancel, actor, comment, fromStatus, e.Status, null);
            await SyncProjectionAsync(e);
            e.CurrentStepKey = null;
        }
        else
        {
            if (e.Status != PartCatalogueStatuses.InApproval || !running || inst!.CurrentStageId == null)
                return PcResult.Fail("Id", "Pc_Err_NotActionable");
            if (e.RequesterEmployeeId == actor.EmployeeId) return PcResult.Fail("Id", "Pc_Err_SelfApproval");
            if (!await IsCandidateAsync(inst.WorkflowInstanceId, actor.EmployeeId)) return PcResult.Fail("Id", "Pc_Err_NotAuthorized");

            var isReturn = string.Equals(action, "Return", StringComparison.OrdinalIgnoreCase);
            var isApprove = string.Equals(action, "Approve", StringComparison.OrdinalIgnoreCase);
            if (!isReturn && !isApprove) return PcResult.Fail("Id", "Pc_Err_UnknownAction");
            if (isReturn && comment == null) return PcResult.Fail("Comment", "Pc_Err_CommentRequired");

            var stageName = await _ctx.WorkflowStages.AsNoTracking().Where(s => s.WorkflowStageId == inst.CurrentStageId).Select(s => s.Name).FirstAsync();
            var kind = await _ctx.PartCatalogueWorkflowSteps.AsNoTracking().Where(s => s.StepKey == stageName).Select(s => s.Kind).FirstOrDefaultAsync() ?? PartCatalogueStepKinds.Review;
            var oldPartNumber = e.AssignedPartNumber;

            if (isApprove && kind == PartCatalogueStepKinds.Registration)
            {
                var pn = Trim(partNumber) ?? (e.RequestType == PartCatalogueRequestTypes.Change ? e.ExistingPartNumber : null);
                if (pn == null) return PcResult.Fail("PartNumber", "Pc_Err_PartNumberRequired");
                if (pn.Length > 50) return PcResult.Fail("PartNumber", "Pc_Err_PartNumberTooLong");
                if (e.RequestType == PartCatalogueRequestTypes.Add
                    && await _ctx.PartCatalogueRequests.AnyAsync(r => r.PartCatalogueRequestId != e.PartCatalogueRequestId
                        && r.IsActive && r.Status != PartCatalogueStatuses.Cancelled && r.AssignedPartNumber == pn))
                    return PcResult.Fail("PartNumber", "Pc_Err_PartNumberUsed");
                e.AssignedPartNumber = pn;   // با ذخیره‌ی خودِ موتور (همان DbContext) هم‌زمان ثبت می‌شود
            }

            try
            {
                await _runtime.ExecuteActionAsync(inst.WorkflowInstanceId, isReturn ? "Return" : "Approve", actor.EmployeeId, actor.Roles, comment, null, null);
            }
            catch (UnauthorizedAccessException)
            {
                e.AssignedPartNumber = oldPartNumber;
                return PcResult.Fail("Id", "Pc_Err_NotAuthorized");
            }
            catch (WorkflowActorResolutionException ex)
            {
                e.AssignedPartNumber = oldPartNumber;
                var r = new PcResult();
                r.Errors.Add(new PcMessage("Route", RouteKey(ex.State, isAdmin), "Pc_Step_" + ex.StageName));
                return r;
            }
            catch (InvalidOperationException)
            {
                e.AssignedPartNumber = oldPartNumber;
                _ctx.ChangeTracker.Clear();
                return PcResult.Fail("RowVersion", "Pc_Err_Concurrency");
            }

            var after = await LatestInstanceAsync(e.PartCatalogueRequestId);
            e.Status = StatusFromInstance(after?.Status ?? "Running", e.Status);
            if (e.Status == PartCatalogueStatuses.Completed) e.CompletedDate = DateTime.Now;

            AddApproval(e, stageName, isReturn ? PartCatalogueActions.Return : kind == PartCatalogueStepKinds.Registration ? PartCatalogueActions.RegisterPartNumber : PartCatalogueActions.Approve,
                actor, comment, fromStatus, e.Status, null);
            if (isReturn) e.Notes.Add(new PartCatalogueRequestNote { Text = comment!, AuthorEmployeeId = actor.EmployeeId, StepKey = stageName });
            await SyncProjectionAsync(e);
        }

        try { await _ctx.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { _ctx.ChangeTracker.Clear(); return PcResult.Fail("RowVersion", "Pc_Err_Concurrency"); }

        var nextActive = e.Steps.FirstOrDefault(s => s.Status == PartCatalogueRequestStep.Active);
        await NotifyAfterTransitionAsync(e, nextActive, actor.EmployeeId);
        return PcResult.Ok();
    }

    // ------------------------------------------------------------------ Approve / Return / Cancel

    public async Task<PcResult> ExecuteAsync(int id, string action, string? comment, string? partNumber, byte[]? rowVersion, PcActor actor)
    {
        var e = await LoadTrackedAsync(id, withSteps: true);
        if (e == null) return PcResult.Fail("Id", "Pc_Err_NotFound");

        comment = Trim(comment);
        if (rowVersion is { Length: > 0 }) _ctx.Entry(e).Property(x => x.RowVersion).OriginalValue = rowVersion;
        if (e.WorkflowInstanceId.HasValue) return await ExecuteRuntimeAsync(e, action, comment, partNumber, actor);

        // ---- Legacy (DEPRECATED): درخواست‌های قدیمی که قبل از انتقال به موتور گردش‌کار ثبت شده‌اند
        var isAdmin = await HasAsync(actor, "Admin");
        var active = e.Steps.FirstOrDefault(s => s.Status == PartCatalogueRequestStep.Active);
        var fromStatus = e.Status;

        if (rowVersion is { Length: > 0 }) _ctx.Entry(e).Property(x => x.RowVersion).OriginalValue = rowVersion;

        // ---- Cancel: فقط درخواست‌دهنده (Draft/Returned/InApproval/OrgResolution)؛ نه گیرنده‌ی گام جاری و نه Admin
        if (string.Equals(action, "Cancel", StringComparison.OrdinalIgnoreCase))
        {
            if (e.Status == PartCatalogueStatuses.Completed || e.Status == PartCatalogueStatuses.Cancelled)
                return PcResult.Fail("Id", "Pc_Err_NotActionable");

            if (e.RequesterEmployeeId != actor.EmployeeId) return PcResult.Fail("Id", "Pc_Err_NotAuthorized");

            e.Status = PartCatalogueStatuses.Cancelled;
            e.CurrentStepKey = null;
            e.CompletedDate = DateTime.Now;
            AddApproval(e, active?.StepKey, PartCatalogueActions.Cancel, actor, comment, fromStatus, e.Status, null);
        }
        else
        {
            if (e.Status != PartCatalogueStatuses.InApproval || active == null)
                return PcResult.Fail("Id", "Pc_Err_NotActionable");

            var eligible = await IsEligibleAsync(active, actor);
            if (!eligible && !isAdmin) return PcResult.Fail("Id", "Pc_Err_NotAuthorized");
            if (e.RequesterEmployeeId == actor.EmployeeId) return PcResult.Fail("Id", "Pc_Err_SelfApproval");
            var overrideUsed = !eligible;
            if (overrideUsed && comment == null) return PcResult.Fail("Comment", "Pc_Err_CommentRequired");

            if (string.Equals(action, "Return", StringComparison.OrdinalIgnoreCase))
            {
                if (comment == null) return PcResult.Fail("Comment", "Pc_Err_CommentRequired");   // اصلاح ⇒ توضیح الزامی

                e.Status = PartCatalogueStatuses.Returned;
                e.CurrentStepKey = null;
                foreach (var s in e.Steps.Where(s => s.Status is PartCatalogueRequestStep.Active or PartCatalogueRequestStep.Pending))
                    s.Status = PartCatalogueRequestStep.Pending;
                AddApproval(e, active.StepKey, overrideUsed ? PartCatalogueActions.AdminOverride : PartCatalogueActions.Return,
                    actor, comment, fromStatus, e.Status, overrideUsed ? "Return" : null);
                e.Notes.Add(new PartCatalogueRequestNote { Text = comment!, AuthorEmployeeId = actor.EmployeeId, StepKey = active.StepKey });
            }
            else if (string.Equals(action, "Approve", StringComparison.OrdinalIgnoreCase))
            {
                var def = await _ctx.PartCatalogueWorkflowSteps.Where(s => s.StepKey == active.StepKey).Select(s => new { s.Kind }).FirstOrDefaultAsync();
                var kind = def?.Kind ?? PartCatalogueStepKinds.Review;

                if (kind == PartCatalogueStepKinds.Registration)
                {
                    // Part Cataloguer پس از ثبت در IFS، شماره‌ی قطعه را وارد می‌کند (Add)؛ Change از ExistingPartNumber می‌آید
                    var pn = Trim(partNumber) ?? (e.RequestType == PartCatalogueRequestTypes.Change ? e.ExistingPartNumber : null);
                    if (pn == null) return PcResult.Fail("PartNumber", "Pc_Err_PartNumberRequired");
                    if (pn.Length > 50) return PcResult.Fail("PartNumber", "Pc_Err_PartNumberTooLong");
                    if (e.RequestType == PartCatalogueRequestTypes.Add
                        && await _ctx.PartCatalogueRequests.AnyAsync(r => r.PartCatalogueRequestId != e.PartCatalogueRequestId
                            && r.IsActive && r.Status != PartCatalogueStatuses.Cancelled && r.AssignedPartNumber == pn))
                        return PcResult.Fail("PartNumber", "Pc_Err_PartNumberUsed");
                    e.AssignedPartNumber = pn;
                }

                active.Status = PartCatalogueRequestStep.Approved;
                active.DecidedDate = DateTime.Now;
                var next = ActivateNext(e);
                e.Status = next != null ? PartCatalogueStatuses.InApproval : PartCatalogueStatuses.Completed;
                e.CurrentStepKey = next?.StepKey;
                if (next == null) e.CompletedDate = DateTime.Now;

                AddApproval(e, active.StepKey,
                    overrideUsed ? PartCatalogueActions.AdminOverride
                        : kind == PartCatalogueStepKinds.Registration ? PartCatalogueActions.RegisterPartNumber : PartCatalogueActions.Approve,
                    actor, comment, fromStatus, e.Status, overrideUsed ? "Approve" : null);
            }
            else return PcResult.Fail("Id", "Pc_Err_UnknownAction");
        }

        try { await _ctx.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { _ctx.ChangeTracker.Clear(); return PcResult.Fail("RowVersion", "Pc_Err_Concurrency"); }

        var nextActive = e.Steps.FirstOrDefault(s => s.Status == PartCatalogueRequestStep.Active);
        await NotifyAfterTransitionAsync(e, nextActive, actor.EmployeeId);
        return PcResult.Ok();
    }

    private static void AddApproval(PartCatalogueRequest e, string? stepKey, string action, PcActor actor, string? comment,
        string fromStatus, string toStatus, string? overriddenAction)
    {
        e.Approvals.Add(new PartCatalogueRequestApproval
        {
            StepKey = stepKey, Action = action, ActorEmployeeId = actor.EmployeeId,
            Comment = overriddenAction == null ? comment : $"[{overriddenAction}] {comment}",
            FromStatus = fromStatus, ToStatus = toStatus
        });
    }

    /// <summary>گیرنده‌ی گام: مدیر حل‌شده یا عضو Pool - و دارای مجوز Review/Approve متناسب با نوع گام.</summary>
    private async Task<bool> IsEligibleAsync(PartCatalogueRequestStep step, PcActor actor)
    {
        var def = await _ctx.PartCatalogueWorkflowSteps.Where(s => s.StepKey == step.StepKey).Select(s => new { s.Kind, s.ApproverSource }).FirstOrDefaultAsync();
        if (def == null) return false;

        bool isRecipient;
        if (def.ApproverSource == PartCatalogueApproverSources.AssignedPool)
            isRecipient = await _ctx.PartCatalogueApproverAssignments.AnyAsync(a => a.IsActive && a.StepKey == step.StepKey && a.EmployeeId == actor.EmployeeId);
        else
            isRecipient = step.AssignedEmployeeId == actor.EmployeeId;

        return isRecipient && await HasAsync(actor, PermissionForKind(def.Kind));
    }

    // ------------------------------------------------------------------ اعلان‌ها

    private async Task<List<int>> UserIdsOfAsync(IEnumerable<int> employeeIds)
    {
        var ids = employeeIds.Distinct().ToList();
        if (ids.Count == 0) return new List<int>();
        return await _ctx.Users.Where(u => u.EmployeeId != null && ids.Contains(u.EmployeeId.Value)).Select(u => u.Id).ToListAsync();
    }

    private async Task NotifyAfterTransitionAsync(PartCatalogueRequest e, PartCatalogueRequestStep? nextActive, int actorEmployeeId)
    {
        var link = $"/Warehouse/PartCatalogueRequest/Details/{e.PartCatalogueRequestId}";
        var label = e.RequestNumber ?? $"#{e.PartCatalogueRequestId}";

        try
        {
            // اعلان‌های قبلیِ همین درخواست (برای گیرنده‌ی گام قبل) بسته می‌شود؛ سپس اعلان تازه
            await _notifications.ResolveRelatedAsync(null, "PartCatalogueRequest", e.PartCatalogueRequestId);

            if (e.Status == PartCatalogueStatuses.InApproval && nextActive != null)
            {
                var recipients = await ResolveRecipientsAsync(e, nextActive);
                foreach (var uid in await UserIdsOfAsync(recipients.Where(r => r != actorEmployeeId)))
                    await _notifications.CreateAsync(uid, "درخواست کاتالوگ قطعه - نیازمند اقدام / Part catalogue request - action required",
                        label, link, "PartCatalogueRequest", e.PartCatalogueRequestId);
            }
            else
            {
                var title = e.Status switch
                {
                    PartCatalogueStatuses.Returned => "درخواست کاتالوگ قطعه برای اصلاح برگشت خورد / Part catalogue request returned for correction",
                    PartCatalogueStatuses.Cancelled => "درخواست کاتالوگ قطعه لغو شد / Part catalogue request cancelled",
                    PartCatalogueStatuses.Completed => "درخواست کاتالوگ قطعه تکمیل شد / Part catalogue request completed",
                    _ => null
                };
                if (title != null && e.RequesterEmployeeId != actorEmployeeId)
                    foreach (var uid in await UserIdsOfAsync(new[] { e.RequesterEmployeeId }))
                        await _notifications.CreateAsync(uid, title, label + (e.AssignedPartNumber != null ? " - " + e.AssignedPartNumber : ""),
                            link, "PartCatalogueRequest", e.PartCatalogueRequestId);
            }

            // ثبت در IFS انجام شد (پایان گام Registration) ⇒ اطلاع به درخواست‌دهنده حتی اگر درخواست هنوز ادامه دارد
            if (e.Status == PartCatalogueStatuses.InApproval && e.AssignedPartNumber != null && e.RequesterEmployeeId != actorEmployeeId)
                foreach (var uid in await UserIdsOfAsync(new[] { e.RequesterEmployeeId }))
                    await _notifications.CreateAsync(uid, "قطعه در IFS ثبت شد / Part registered in IFS",
                        $"{label} - {e.AssignedPartNumber}", link, "PartCatalogueRequest", e.PartCatalogueRequestId);
        }
        catch
        {
            // اعلان‌ها تکمیلی‌اند - شکست آن‌ها نباید تصمیم ثبت‌شده را خراب کند
        }
    }

    private async Task<List<int>> ResolveRecipientsAsync(PartCatalogueRequest e, PartCatalogueRequestStep step)
    {
        if (e.WorkflowInstanceId.HasValue)
        {
            var inst = await LatestInstanceAsync(e.PartCatalogueRequestId);
            if (inst == null) return new List<int>();
            return await _ctx.WorkflowInstanceStageCandidates.AsNoTracking()
                .Where(c => c.IsSelected && c.InstanceStage!.WorkflowInstanceId == inst.WorkflowInstanceId && c.InstanceStage.Status == "Pending")
                .Select(c => c.EmployeeId).Distinct().ToListAsync();
        }

        if (step.AssignedEmployeeId.HasValue) return new List<int> { step.AssignedEmployeeId.Value };
        return await _ctx.PartCatalogueApproverAssignments.Where(a => a.IsActive && a.StepKey == step.StepKey).Select(a => a.EmployeeId).ToListAsync();
    }

    // ------------------------------------------------------------------ یادداشت / تجهیز / پیوست

    public async Task<PcResult> AddNoteAsync(int id, string text, PcActor actor)
    {
        text = (text ?? string.Empty).Trim();
        if (text.Length == 0) return PcResult.Fail("Text", "Pc_Err_Required");
        if (text.Length > 2000) return PcResult.Fail("Text", "Pc_Err_TooLong");

        var e = await _ctx.PartCatalogueRequests.Include(r => r.Steps).FirstOrDefaultAsync(r => r.PartCatalogueRequestId == id && r.IsActive);
        if (e == null) return PcResult.Fail("Id", "Pc_Err_NotFound");
        if (e.Status is PartCatalogueStatuses.Completed or PartCatalogueStatuses.Cancelled) return PcResult.Fail("Id", "Pc_Err_NotActionable");

        var active = e.Steps.FirstOrDefault(s => s.Status == PartCatalogueRequestStep.Active);
        var allowed = e.RequesterEmployeeId == actor.EmployeeId
                      || (active != null && await IsEligibleAsync(active, actor))
                      || await HasAsync(actor, "Admin");
        if (!allowed) return PcResult.Fail("Id", "Pc_Err_NotAuthorized");

        _ctx.PartCatalogueRequestNotes.Add(new PartCatalogueRequestNote
        {
            PartCatalogueRequestId = id, Text = text, AuthorEmployeeId = actor.EmployeeId, StepKey = e.CurrentStepKey
        });
        await _ctx.SaveChangesAsync();
        return PcResult.Ok();
    }

    private async Task<PartCatalogueRequest?> EditableOwnedAsync(int id, PcActor actor)
    {
        var e = await _ctx.PartCatalogueRequests.FirstOrDefaultAsync(r => r.PartCatalogueRequestId == id && r.IsActive);
        if (e == null || e.RequesterEmployeeId != actor.EmployeeId || !Editable(e) || !await HasAsync(actor, "Create")) return null;
        return e;
    }

    public async Task<PcResult> AddEquipmentAsync(int id, int? equipmentId, string? text, decimal installedQuantity, PcActor actor)
    {
        var e = await EditableOwnedAsync(id, actor);
        if (e == null) return PcResult.Fail("Id", "Pc_Err_NotEditable");

        text = Trim(text);
        if (installedQuantity < 0) return PcResult.Fail("InstalledQuantity", "Pc_Err_Negative");

        if (e.IsNewEquipment)
        {
            if (text == null) return PcResult.Fail("EquipmentText", "Pc_Err_Required");
            equipmentId = null;
        }
        else
        {
            if (!equipmentId.HasValue) return PcResult.Fail("EquipmentId", "Pc_Err_Required");
            if (!await _ctx.Equipments.AnyAsync(q => q.EquipmentId == equipmentId.Value)) return PcResult.Fail("EquipmentId", "Pc_Err_InvalidReference");
            text = null;
        }

        _ctx.PartCatalogueRequestEquipment.Add(new PartCatalogueRequestEquipment
        {
            PartCatalogueRequestId = id, EquipmentId = equipmentId, EquipmentText = text, InstalledQuantity = installedQuantity
        });
        await _ctx.SaveChangesAsync();
        return PcResult.Ok();
    }

    public async Task<PcResult> RemoveEquipmentAsync(int id, int equipmentLineId, PcActor actor)
    {
        if (await EditableOwnedAsync(id, actor) == null) return PcResult.Fail("Id", "Pc_Err_NotEditable");
        var line = await _ctx.PartCatalogueRequestEquipment.FirstOrDefaultAsync(x => x.RequestEquipmentId == equipmentLineId && x.PartCatalogueRequestId == id);
        if (line == null) return PcResult.Fail("Id", "Pc_Err_NotFound");
        _ctx.PartCatalogueRequestEquipment.Remove(line);
        await _ctx.SaveChangesAsync();
        return PcResult.Ok();
    }

    public async Task<PcResult> UploadAttachmentAsync(int id, string fileName, string? contentType, string kind, Stream content, long length, PcActor actor)
    {
        if (await EditableOwnedAsync(id, actor) == null) return PcResult.Fail("Id", "Pc_Err_NotEditable");

        var name = Path.GetFileName(fileName ?? string.Empty);
        var ext = Path.GetExtension(name).ToLowerInvariant();
        if (name.Length == 0 || !_options.AllowedAttachmentExtensions.Contains(ext)) return PcResult.Fail("File", "Pc_Err_FileType", ext);
        if (length <= 0) return PcResult.Fail("File", "Pc_Err_Required");
        if (length > _options.MaxAttachmentBytes) return PcResult.Fail("File", "Pc_Err_FileTooLarge");
        if (await _ctx.PartCatalogueRequestAttachments.CountAsync(a => a.PartCatalogueRequestId == id && a.IsActive) >= _options.MaxAttachmentsPerRequest)
            return PcResult.Fail("File", "Pc_Err_TooManyFiles");

        kind = kind is "Drawing" or "Support" ? kind : "Document";

        // هش محتوا (SHA-256) و ذخیره؛ ابتدا در حافظه‌ی محدود (≤ MaxAttachmentBytes) تا هم هش و هم ذخیره از یک خوانش باشد
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms);
        if (ms.Length > _options.MaxAttachmentBytes) return PcResult.Fail("File", "Pc_Err_FileTooLarge");
        var hash = Convert.ToHexString(SHA256.HashData(ms.ToArray()));
        ms.Position = 0;
        var key = await _files.SaveAsync("part-catalogue", name, ms);

        _ctx.PartCatalogueRequestAttachments.Add(new PartCatalogueRequestAttachment
        {
            PartCatalogueRequestId = id, FileName = name, StorageKey = key, ContentType = contentType,
            FileSize = ms.Length, FileHash = hash, Kind = kind, UploadedByEmployeeId = actor.EmployeeId
        });
        await _ctx.SaveChangesAsync();
        return PcResult.Ok();
    }

    public async Task<PcResult> RemoveAttachmentAsync(int id, int attachmentId, PcActor actor)
    {
        if (await EditableOwnedAsync(id, actor) == null) return PcResult.Fail("Id", "Pc_Err_NotEditable");
        var a = await _ctx.PartCatalogueRequestAttachments.FirstOrDefaultAsync(x => x.RequestAttachmentId == attachmentId && x.PartCatalogueRequestId == id && x.IsActive);
        if (a == null) return PcResult.Fail("Id", "Pc_Err_NotFound");
        a.IsActive = false;   // حذف نرم؛ فایل فیزیکی برای ردیابی باقی می‌ماند
        await _ctx.SaveChangesAsync();
        return PcResult.Ok();
    }

    public async Task<(Stream Stream, string FileName, string ContentType)?> OpenAttachmentAsync(int id, int attachmentId, PcActor actor)
    {
        var e = await _ctx.PartCatalogueRequests.AsNoTracking().FirstOrDefaultAsync(r => r.PartCatalogueRequestId == id && r.IsActive);
        if (e == null || !await CanViewAsync(e, actor)) return null;

        var a = await _ctx.PartCatalogueRequestAttachments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.RequestAttachmentId == attachmentId && x.PartCatalogueRequestId == id && x.IsActive);
        if (a == null) return null;

        var stream = await _files.OpenReadAsync(a.StorageKey);
        return (stream, a.FileName, a.ContentType ?? "application/octet-stream");
    }

    // ------------------------------------------------------------------ نمایش / لیست

    private string EmpName(int? id, Dictionary<int, string> names) => id.HasValue && names.TryGetValue(id.Value, out var n) ? n : string.Empty;

    private async Task<Dictionary<int, string>> EmployeeNamesAsync(IEnumerable<int> ids)
    {
        var list = ids.Distinct().ToList();
        return (await _ctx.Employees.Where(x => list.Contains(x.EmployeeId))
            .Select(x => new { x.EmployeeId, x.FirstName, x.LastName }).ToListAsync())
            .ToDictionary(x => x.EmployeeId, x => (x.FirstName + " " + x.LastName).Trim());
    }

    public async Task<PartCatalogueDetailsDto?> GetDetailsAsync(int id, PcActor actor)
    {
        var e = await _ctx.PartCatalogueRequests.AsNoTracking()
            .Include(r => r.Attributes).Include(r => r.Steps).Include(r => r.Approvals).Include(r => r.Notes)
            .Include(r => r.Equipment).ThenInclude(q => q.Equipment)
            .Include(r => r.Attachments)
            .Include(r => r.Section).Include(r => r.TechnicalClass).Include(r => r.CommodityGroup1).Include(r => r.CommodityGroup2)
            .Include(r => r.ProductFamily).Include(r => r.UnitOfMeasure).Include(r => r.Manufacturer).Include(r => r.Supplier)
            .Include(r => r.Currency).Include(r => r.TypeDesignation).Include(r => r.PlanningMethod).Include(r => r.StockGrade)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.PartCatalogueRequestId == id && r.IsActive);
        if (e == null || !await CanViewAsync(e, actor)) return null;

        var empIds = new List<int> { e.RequesterEmployeeId };
        empIds.AddRange(e.Approvals.Select(a => a.ActorEmployeeId));
        empIds.AddRange(e.Notes.Select(n => n.AuthorEmployeeId));
        empIds.AddRange(e.Attachments.Select(a => a.UploadedByEmployeeId));
        empIds.AddRange(e.Steps.Where(s => s.AssignedEmployeeId.HasValue).Select(s => s.AssignedEmployeeId!.Value));
        var names = await EmployeeNamesAsync(empIds);

        var dto = new PartCatalogueDetailsDto
        {
            Id = e.PartCatalogueRequestId, RowVersion = e.RowVersion, RequestNumber = e.RequestNumber, RequestType = e.RequestType,
            Status = e.Status, CurrentStepKey = e.CurrentStepKey, Urgent = e.Urgent, RequesterEmployeeId = e.RequesterEmployeeId,
            RequesterName = EmpName(e.RequesterEmployeeId, names), AssignedPartNumber = e.AssignedPartNumber,
            IsCritical = e.IsCritical, IsCapital = e.IsCapital, CreatedDate = e.CreatedDate, SubmittedDate = e.SubmittedDate, CompletedDate = e.CompletedDate,
            Form = ToForm(e), TechnicalClassLongDescription = e.TechnicalClass?.LongDescription
        };

        dto.Labels["SectionId"] = e.Section?.Code ?? "";
        dto.Labels["TechnicalClassId"] = e.TechnicalClass == null ? "" : $"{e.TechnicalClass.Code} - {e.TechnicalClass.Description}";
        dto.Labels["CommodityGroup1Id"] = e.CommodityGroup1 == null ? "" : $"{e.CommodityGroup1.Description}-{e.CommodityGroup1.Code}";
        dto.Labels["CommodityGroup2Id"] = e.CommodityGroup2 == null ? "" : $"{e.CommodityGroup2.Description}-{e.CommodityGroup2.Code}";
        dto.Labels["ProductFamilyId"] = e.ProductFamily == null ? "" : $"{e.ProductFamily.Description}-{e.ProductFamily.Code}";
        dto.Labels["UnitOfMeasureId"] = e.UnitOfMeasure?.Code ?? "";
        dto.Labels["ManufacturerId"] = e.IsNewManufacturer ? (e.NewManufacturerName ?? "") : (e.Manufacturer?.Name ?? "");
        dto.Labels["SupplierId"] = e.Supplier?.Name ?? "";
        dto.Labels["CurrencyId"] = e.Currency?.Code ?? "";
        dto.Labels["TypeDesignationId"] = e.TypeDesignation?.Code ?? "";
        dto.Labels["PlanningMethodId"] = e.PlanningMethod?.Code ?? "";
        dto.Labels["StockGradeId"] = e.StockGrade == null ? "" : $"{e.StockGrade.Grade} - {e.StockGrade.Description}";

        dto.Equipment = e.Equipment.Select(q => new PartCatalogueEquipmentView
        {
            Id = q.RequestEquipmentId, EquipmentId = q.EquipmentId,
            Text = q.Equipment != null ? $"{q.Equipment.TagNumber} - {q.Equipment.Name}" : (q.EquipmentText ?? ""),
            InstalledQuantity = q.InstalledQuantity
        }).ToList();
        dto.Attachments = e.Attachments.Where(a => a.IsActive).Select(a => new PartCatalogueAttachmentView
        {
            Id = a.RequestAttachmentId, FileName = a.FileName, Kind = a.Kind, FileSize = a.FileSize,
            UploadedBy = EmpName(a.UploadedByEmployeeId, names), UploadedDate = a.UploadedDate
        }).ToList();
        dto.Notes = e.Notes.OrderBy(n => n.CreatedDate).Select(n => new PartCatalogueNoteView
        {
            Text = n.Text, AuthorName = EmpName(n.AuthorEmployeeId, names), StepKey = n.StepKey, CreatedDate = n.CreatedDate
        }).ToList();
        dto.Steps = e.Steps.OrderBy(s => s.StepOrder).Select(s => new PartCatalogueStepView
        {
            StepKey = s.StepKey, Order = s.StepOrder, Status = s.Status, SkipReason = s.SkipReason,
            AssignedName = s.AssignedEmployeeId.HasValue ? EmpName(s.AssignedEmployeeId, names) : null, DecidedDate = s.DecidedDate
        }).ToList();
        var kinds = await _ctx.PartCatalogueWorkflowSteps.AsNoTracking().ToDictionaryAsync(s => s.StepKey, s => s.Kind);
        foreach (var s in dto.Steps) s.Kind = kinds.TryGetValue(s.StepKey, out var k) ? k : "";

        if (e.WorkflowInstanceId.HasValue && e.Status == PartCatalogueStatuses.InApproval)
        {
            var activeView = dto.Steps.FirstOrDefault(x => x.Status == PartCatalogueRequestStep.Active);
            var inst = await LatestInstanceAsync(e.PartCatalogueRequestId);
            if (activeView != null && inst != null)
            {
                var ids = await _ctx.WorkflowInstanceStageCandidates.AsNoTracking()
                    .Where(c => c.IsSelected && c.InstanceStage!.WorkflowInstanceId == inst.WorkflowInstanceId && c.InstanceStage.Status == "Pending")
                    .Select(c => c.EmployeeId).ToListAsync();
                var nm = await EmployeeNamesAsync(ids);
                activeView.AssignedName = string.Join(", ", ids.Select(i => EmpName(i, nm)));
            }
        }
        dto.Approvals = e.Approvals.OrderBy(a => a.ActionDate).ThenBy(a => a.RequestApprovalId).Select(a => new PartCatalogueApprovalView
        {
            StepKey = a.StepKey, Action = a.Action, ActorName = EmpName(a.ActorEmployeeId, names), Comment = a.Comment,
            ToStatus = a.ToStatus, ActionDate = a.ActionDate
        }).ToList();

        dto.Actions = await ComputeActionsAsync(e, actor, kinds);
        return dto;
    }

    private async Task<PartCatalogueActionState> ComputeActionsAsync(PartCatalogueRequest e, PcActor actor, Dictionary<string, string> kinds)
    {
        var s = new PartCatalogueActionState();
        var isOwner = e.RequesterEmployeeId == actor.EmployeeId;
        var canCreate = await HasAsync(actor, "Create");
        s.CanEdit = isOwner && canCreate && Editable(e);
        s.CanGenerateNumber = s.CanEdit && string.IsNullOrEmpty(e.RequestNumber);
        s.CanSubmit = s.CanEdit && !string.IsNullOrEmpty(e.RequestNumber);
        s.IsOrganizationBlocked = e.Status == PartCatalogueStatuses.OrganizationResolutionRequired;
        s.OrganizationBlockReason = s.IsOrganizationBlocked ? e.RoutingBlockReason : null;
        s.CanRetryRouting = s.IsOrganizationBlocked && (isOwner || await HasAsync(actor, "Admin"));

        var active = e.Steps.FirstOrDefault(x => x.Status == PartCatalogueRequestStep.Active);
        s.ActiveStepKey = active?.StepKey;
        var live = e.Status == PartCatalogueStatuses.InApproval && active != null;

        if (e.WorkflowInstanceId.HasValue)
        {
            // مسیر موتور گردش‌کار: فقط کاندیدهای Snapshot (بدون Bypass مدیر برای تأیید/بازگشت). لغو فقط برای درخواست‌دهنده است (پایین).
            if (live && !isOwner)
            {
                var inst = await LatestInstanceAsync(e.PartCatalogueRequestId);
                var cand = inst != null && await IsCandidateAsync(inst.WorkflowInstanceId, actor.EmployeeId);
                s.CanApprove = cand;
                s.CanReturn = cand;
                s.RequiresPartNumber = cand && kinds.TryGetValue(active!.StepKey, out var rk) && rk == PartCatalogueStepKinds.Registration && e.RequestType == PartCatalogueRequestTypes.Add;
            }
            if (isOwner && (Editable(e) || e.Status == PartCatalogueStatuses.InApproval || s.IsOrganizationBlocked)) s.CanCancel = true;
            return s;
        }

        if (live && !isOwner)
        {
            var eligible = await IsEligibleAsync(active!, actor);
            var admin = await HasAsync(actor, "Admin");
            s.CanApprove = eligible || admin;
            s.CanReturn = eligible || admin;
            s.IsAdminOverride = !eligible && admin;
            s.RequiresPartNumber = live && kinds.TryGetValue(active!.StepKey, out var kind) && kind == PartCatalogueStepKinds.Registration
                                   && e.RequestType == PartCatalogueRequestTypes.Add;
        }
        if (isOwner && (Editable(e) || e.Status == PartCatalogueStatuses.InApproval || s.IsOrganizationBlocked)) s.CanCancel = true;
        return s;
    }

    public async Task<PagedResult<PartCatalogueListItemDto>> ListAsync(PartCatalogueListFilter f, PcActor actor)
    {
        var q = ScopedQuery(await CanSeeAllAsync(actor), actor, await MyPoolKeysAsync(actor.EmployeeId));

        if (!string.IsNullOrWhiteSpace(f.Status)) q = q.Where(r => r.Status == f.Status);
        if (!string.IsNullOrWhiteSpace(f.RequestType)) q = q.Where(r => r.RequestType == f.RequestType);
        if (f.Urgent.HasValue) q = q.Where(r => r.Urgent == f.Urgent.Value);
        if (f.SectionId.HasValue) q = q.Where(r => r.SectionId == f.SectionId);
        if (f.FromDate.HasValue) q = q.Where(r => r.CreatedDate >= f.FromDate.Value.Date);
        if (f.ToDate.HasValue) q = q.Where(r => r.CreatedDate < f.ToDate.Value.Date.AddDays(1));
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim();
            q = q.Where(r => (r.RequestNumber != null && r.RequestNumber.Contains(s))
                             || (r.AssignedPartNumber != null && r.AssignedPartNumber.Contains(s))
                             || (r.ExistingPartNumber != null && r.ExistingPartNumber.Contains(s))
                             || (r.TechnicalClass != null && (r.TechnicalClass.Code.Contains(s) || r.TechnicalClass.Description.Contains(s))));
        }

        var pool = await MyPoolKeysAsync(actor.EmployeeId);
        if (f.View == "mine") q = q.Where(r => r.RequesterEmployeeId == actor.EmployeeId);
        else if (f.View == "awaiting")
            q = q.Where(r => r.RequesterEmployeeId != actor.EmployeeId && r.Status == PartCatalogueStatuses.InApproval
                && ((r.WorkflowInstanceId == null && r.Steps.Any(st => st.Status == PartCatalogueRequestStep.Active
                        && (st.AssignedEmployeeId == actor.EmployeeId || pool.Contains(st.StepKey))))
                    || (r.WorkflowInstanceId != null && _ctx.WorkflowInstanceStageCandidates.Any(c => c.EmployeeId == actor.EmployeeId && c.IsSelected
                        && c.InstanceStage!.Status == "Pending" && c.InstanceStage.WorkflowInstanceId == r.WorkflowInstanceId))));

        var page = f.Page < 1 ? 1 : f.Page;
        var size = PEMS.Application.Common.DataGridPageSize.Normalize(f.PageSize);
        var total = await q.CountAsync();

        // فوری‌ها فقط در درخواست‌های «باز» اول می‌آیند؛ سپس جدیدترین
        var items = await q
            .OrderByDescending(r => r.Urgent && (r.Status == PartCatalogueStatuses.InApproval || r.Status == PartCatalogueStatuses.Draft || r.Status == PartCatalogueStatuses.Returned))
            .ThenByDescending(r => r.SubmittedDate ?? r.CreatedDate)
            .ThenByDescending(r => r.PartCatalogueRequestId)
            .Skip((page - 1) * size).Take(size)
            .Select(r => new PartCatalogueListItemDto
            {
                Id = r.PartCatalogueRequestId, RequestNumber = r.RequestNumber, RequestType = r.RequestType, Status = r.Status,
                CurrentStepKey = r.CurrentStepKey, Urgent = r.Urgent, SectionCode = r.Section != null ? r.Section.Code : null,
                TechnicalClassCode = r.TechnicalClass != null ? r.TechnicalClass.Code : null,
                TechnicalClassDescription = r.TechnicalClass != null ? r.TechnicalClass.Description : null,
                RequesterName = r.Requester != null ? r.Requester.FirstName + " " + r.Requester.LastName : null,
                AssignedPartNumber = r.AssignedPartNumber, CreatedDate = r.CreatedDate, SubmittedDate = r.SubmittedDate
            }).ToListAsync();

        return new PagedResult<PartCatalogueListItemDto> { Items = items, Page = page, PageSize = size, TotalCount = total };
    }

    private IQueryable<PartCatalogueRequest> ScopedQuery(bool seeAll, PcActor actor, HashSet<string> pool)
    {
        var q = _ctx.PartCatalogueRequests.AsNoTracking().Where(r => r.IsActive);
        if (seeAll) return q;
        return q.Where(r => r.RequesterEmployeeId == actor.EmployeeId
                            || (r.WorkflowInstanceId == null && r.Steps.Any(st => st.AssignedEmployeeId == actor.EmployeeId || pool.Contains(st.StepKey)))
                            || (r.WorkflowInstanceId != null && _ctx.WorkflowInstanceStageCandidates.Any(c => c.EmployeeId == actor.EmployeeId && c.InstanceStage!.WorkflowInstanceId == r.WorkflowInstanceId)));
    }

    public async Task<PartCatalogueSummaryDto> GetSummaryAsync(PcActor actor)
    {
        var pool = await MyPoolKeysAsync(actor.EmployeeId);
        var q = ScopedQuery(await CanSeeAllAsync(actor), actor, pool);
        var byStatus = await q.GroupBy(r => r.Status).Select(g => new { Status = g.Key, C = g.Count() }).ToListAsync();
        int Get(string s) => byStatus.FirstOrDefault(x => x.Status == s)?.C ?? 0;

        return new PartCatalogueSummaryDto
        {
            Total = byStatus.Sum(x => x.C), Draft = Get(PartCatalogueStatuses.Draft), InApproval = Get(PartCatalogueStatuses.InApproval),
            Returned = Get(PartCatalogueStatuses.Returned), Completed = Get(PartCatalogueStatuses.Completed), Cancelled = Get(PartCatalogueStatuses.Cancelled),
            Urgent = await q.CountAsync(r => r.Urgent && (r.Status == PartCatalogueStatuses.InApproval || r.Status == PartCatalogueStatuses.Returned || r.Status == PartCatalogueStatuses.Draft)),
            AwaitingMe = await _ctx.PartCatalogueRequests.AsNoTracking().CountAsync(r => r.IsActive && r.Status == PartCatalogueStatuses.InApproval
                && r.RequesterEmployeeId != actor.EmployeeId
                && ((r.WorkflowInstanceId == null && r.Steps.Any(st => st.Status == PartCatalogueRequestStep.Active && (st.AssignedEmployeeId == actor.EmployeeId || pool.Contains(st.StepKey))))
                    || (r.WorkflowInstanceId != null && _ctx.WorkflowInstanceStageCandidates.Any(c => c.EmployeeId == actor.EmployeeId && c.IsSelected
                        && c.InstanceStage!.Status == "Pending" && c.InstanceStage.WorkflowInstanceId == r.WorkflowInstanceId))))
        };
    }

    // ------------------------------------------------------------------ چاپ

    public async Task<PartCataloguePrintModel?> GetPrintModelAsync(int id, PcActor actor)
    {
        var d = await GetDetailsAsync(id, actor);
        if (d == null) return null;

        var tel = (await _ctx.Employees.AsNoTracking().Where(x => x.EmployeeId == d.RequesterEmployeeId).Select(x => x.Phone).FirstOrDefaultAsync()) ?? "";
        string L(string field) => d.Labels.TryGetValue(field, out var v) ? v : "";
        var f = d.Form;
        var fft = f.Attributes.FirstOrDefault(a => string.Equals(a.Name, PartCatalogueRequestAttribute.FreeFormatText, StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ?? "";
        var material = f.Attributes.FirstOrDefault(a => string.Equals(a.Name, "MATERIAL", StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ?? "";
        var otherAttrs = f.Attributes.Where(a => !string.Equals(a.Name, PartCatalogueRequestAttribute.FreeFormatText, StringComparison.OrdinalIgnoreCase)
                                                 && !string.Equals(a.Name, "MATERIAL", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(a.Value))
            .Select(a => $"{a.Name}: {a.Value}");
        var tc = L("TechnicalClassId");

        PrintSignature Sig(string role, string stepKey)
        {
            var step = d.Approvals.LastOrDefault(a => a.StepKey == stepKey && (a.Action is PartCatalogueActions.Approve or PartCatalogueActions.RegisterPartNumber or PartCatalogueActions.AdminOverride));
            return new PrintSignature(role, step?.ActorName ?? "", step?.ActionDate, "");
        }

        return new PartCataloguePrintModel
        {
            IsChange = d.RequestType == PartCatalogueRequestTypes.Change,
            PartNumber = d.AssignedPartNumber ?? f.ExistingPartNumber ?? "",
            TechnicalClassName = tc.Contains(" - ") ? tc.Split(" - ", 2)[1] + "(" + tc.Split(" - ", 2)[0] + ")" : tc,
            Material = material,
            Attributes = string.Join(" ", new[] { fft }.Concat(otherAttrs).Where(s => s.Length > 0)),
            EquipmentSerial = string.IsNullOrWhiteSpace(f.EquipmentSerialNo) ? "N/A" : f.EquipmentSerialNo,
            SparePartSerial = string.IsNullOrWhiteSpace(f.SparePartSerialNo) ? "N/A" : f.SparePartSerialNo,
            EquipmentDocNo = f.DocumentNo ?? "",
            ManufacturerName = L("ManufacturerId"),
            ManufacturerPartNo = string.IsNullOrWhiteSpace(f.ManufacturerPartNumber) ? "N/A" : f.ManufacturerPartNumber,
            ManufacturerDwgNo = string.IsNullOrWhiteSpace(f.ManufacturerDrawingNo) ? "N/A" : f.ManufacturerDrawingNo,
            SupplierName = L("SupplierId"),
            SupplierPartNo = string.IsNullOrWhiteSpace(f.SupplierPartNumber) ? "N/A" : f.SupplierPartNumber,
            RelatedEquipmentTags = string.Join(", ", d.Equipment.Select(q => q.Text)),
            Unit = L("UnitOfMeasureId"),
            CommodityGroup1 = d.Form.CommodityGroup1Id.HasValue ? L("CommodityGroup1Id").Split('-').LastOrDefault() ?? "" : "",
            ProductFamily = d.Form.ProductFamilyId.HasValue ? L("ProductFamilyId").Split('-').LastOrDefault() ?? "" : "",
            MinStock = f.MinimumStock?.ToString("0.####") ?? "",
            MaxConsumptionRate = f.MaxConsumptionRate?.ToString("0.####") ?? "",
            InstalledQuantity = string.Join(", ", d.Equipment.Select(q => q.InstalledQuantity.ToString("0.####"))),
            WantedReceiptDate = f.WantedReceiptDate,
            StorageVolume = f.StorageVolume ?? "",
            StockGrade = L("StockGradeId").Split(' ').FirstOrDefault() ?? "",
            IsCritical = d.IsCritical,
            RequestNumber = d.RequestNumber ?? "",
            Signatures = new List<PrintSignature>
            {
                new("Originator", d.RequesterName ?? "", d.SubmittedDate, tel),
                Sig("SpareSpecialist", "SparePartsSpecialist"),
                Sig("MaintPlanning", "MaintenanceSupervisor"),
                Sig("PartCataloguer", "PartCataloguer"),
                Sig("InventoryController", "InventoryControl")
            }
        };
    }
}
