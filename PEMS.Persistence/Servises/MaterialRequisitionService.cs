using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

public class MaterialRequisitionService : IMaterialRequisitionService
{
    private readonly PEMSDbContext _context;

    // طبق ممیزی مستقل فاز ۶B Wave A (P1): ChangeStatusAsync قبلاً هر رشته‌ای را بدون بررسی
    // گذار قانونی می‌پذیرفت. طبق الگوی WorkRequestService.LegalTransitions - نه یک موتور
    // گردش‌کار جدید - فقط همون توالی‌ای که خودِ View (Details.cshtml) در Dropdown نمایش
    // می‌دهد: Draft → Submitted → Under Technical Review → Approved → Issued to Procurement
    // → Closed، به‌علاوه‌ی Cancelled به‌عنوان مسیر خروج زودهنگام (قبل از ورود به Procurement -
    // بعد از آن مثل WorkRequest's "Converted" یک نقطه‌ی بی‌بازگشت است).
    private static readonly Dictionary<string, string[]> LegalTransitions = new()
    {
        ["Draft"] = new[] { "Submitted", "Cancelled" },
        ["Submitted"] = new[] { "Under Technical Review", "Cancelled" },
        ["Under Technical Review"] = new[] { "Approved", "Cancelled" },
        ["Approved"] = new[] { "Issued to Procurement", "Cancelled" },
        ["Issued to Procurement"] = new[] { "Closed" },
    };

    private readonly INumberingService _numbering;
    private readonly IPartCatalogueDuplicateService _parts;

    // SCRUM-40/428: شماره‌گذاری و جستجوی قطعه‌ی ثبت‌شده اختیاری در سازنده تا تست‌های تک‌پارامتری سازگار بمانند؛ DI آن‌ها را می‌دهد
    public MaterialRequisitionService(PEMSDbContext context, INumberingService? numbering = null, IPartCatalogueDuplicateService? parts = null)
    {
        _context = context;
        _numbering = numbering ?? new NumberingService(context);
        _parts = parts ?? new PartCatalogueDuplicateService(context);
    }

    // طبق سند «PEMS — Global Data Grid Standard» (فاز ۶B - Wave A): قبلاً کل جدول با ۳
    // Include غیرضروری (Select پایین‌تر فقط فیلد اسکالر می‌گیرد) و بدون Skip/Take بارگذاری
    // می‌شد.
    public async Task<PagedResult<MRDto>> GetAllAsync(int page = 1, int pageSize = 50)
    {
        var query = _context.MaterialRequisitions.AsQueryable();

        var totalCount = await query.CountAsync();

        page = page < 1 ? 1 : page;
        pageSize = DataGridPageSize.Normalize(pageSize);

        var items = await query
            .OrderByDescending(x => x.CreatedDate)
            .ThenByDescending(x => x.MRId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new MRDto
            {
                MRId = x.MRId,
                MRNumber = x.MRNumber,
                ProjectId = x.ProjectId,
                ProjectName = x.Project!.ProjectName,
                DisciplineId = x.DisciplineId,
                DisciplineName = x.Discipline!.Name,
                EquipmentTagNumber = x.EquipmentTagNumber,
                Description = x.Description,
                RequiredDate = x.RequiredDate,
                ResponsibleEngineerName = x.ResponsibleEngineer != null
                    ? x.ResponsibleEngineer.FirstName + " " + x.ResponsibleEngineer.LastName
                    : null,
                Status = x.Status,
                CreatedDate = x.CreatedDate,
                QuotationsCount = x.Quotations.Count
            })
            .ToListAsync();

        return new PagedResult<MRDto> { Items = items, Page = page, PageSize = pageSize, TotalCount = totalCount };
    }

    public async Task<MRDto?> GetByIdAsync(int id)
    {
        var mr = await _context.MaterialRequisitions
            .Include(x => x.Project)
            .Include(x => x.Discipline)
            .Include(x => x.ResponsibleEngineer)
            .Include(x => x.Items)
            .Include(x => x.Quotations)
            .FirstOrDefaultAsync(x => x.MRId == id);

        if (mr == null) return null;

        return new MRDto
        {
            MRId = mr.MRId,
            MRNumber = mr.MRNumber,
            ProjectId = mr.ProjectId,
            ProjectName = mr.Project?.ProjectName ?? "",
            DisciplineId = mr.DisciplineId,
            DisciplineName = mr.Discipline?.Name ?? "",
            EquipmentTagNumber = mr.EquipmentTagNumber,
            Description = mr.Description,
            RequiredDate = mr.RequiredDate,
            ResponsibleEngineerName = mr.ResponsibleEngineer != null
                ? mr.ResponsibleEngineer.FirstName + " " + mr.ResponsibleEngineer.LastName
                : null,
            Status = mr.Status,
            CreatedDate = mr.CreatedDate,
            QuotationsCount = mr.Quotations.Count,
            Items = mr.Items.Select(i => new MRItemDto
            {
                MRItemId = i.MRItemId,
                ItemDescription = i.ItemDescription,
                PartNumber = i.PartNumber,
                Quantity = i.Quantity,
                Unit = i.Unit,
                Remarks = i.Remarks,
                IsCatalogued = i.MaterialItemId != null || i.PartCatalogueRequestId != null
            }).ToList(),
            Quotations = mr.Quotations.Select(q => new MRQDto
            {
                MRQId = q.MRQId,
                MRQNumber = q.MRQNumber,
                MRId = q.MRId,
                MRNumber = mr.MRNumber,
                VendorName = q.VendorName,
                InquiryDate = q.InquiryDate,
                QuotedAmount = q.QuotedAmount,
                Currency = q.Currency,
                QuotationStatus = q.QuotationStatus,
                TechnicalEvaluation = q.TechnicalEvaluation,
                CommercialEvaluation = q.CommercialEvaluation,
                ApprovalStatus = q.ApprovalStatus,
                Notes = q.Notes
            }).ToList()
        };
    }

    // طبق ممیزی مستقل فاز ۶B Wave A (P1): قبلاً هیچ‌کدام از ProjectId/DisciplineId/
    // ResponsibleEngineerId یا یکتایی MRNumber بررسی نمی‌شدند - فقط به Exception سطح FK
    // تکیه می‌شد که پیام معنادار به کاربر نمی‌دهد.
    public async Task<(bool Success, string? Error, int? Id)> CreateAsync(string mrNumber, int projectId, int disciplineId, string? equipmentTag,
        string description, DateTime? requiredDate, int? responsibleEngineerId, int? relatedDocumentId,
        List<(string desc, string? part, double qty, string? unit)> items)
    {
        // SCRUM-40: شماره خودکار از الگوی مدیر (شماره‌ی واردشده فقط اگر الگو ورود دستی را مجاز کرده باشد)
        mrNumber = await _numbering.ResolveNumberAsync("MaterialRequisition", mrNumber, projectId, disciplineId);
        if (string.IsNullOrWhiteSpace(mrNumber))
            return (false, "شماره MR الزامی است.", null);

        if (await _context.MaterialRequisitions.AnyAsync(x => x.MRNumber == mrNumber))
            return (false, $"شماره MR «{mrNumber}» قبلاً استفاده شده است.", null);

        if (!await _context.Projects.AnyAsync(x => x.ProjectId == projectId && x.IsActive))
            return (false, "پروژه‌ی انتخاب‌شده نامعتبر یا غیرفعال است.", null);

        if (!await _context.Disciplines.AnyAsync(x => x.DisciplineId == disciplineId && x.IsActive))
            return (false, "دیسیپلین انتخاب‌شده نامعتبر یا غیرفعال است.", null);

        if (responsibleEngineerId.HasValue &&
            !await _context.Employees.AnyAsync(x => x.EmployeeId == responsibleEngineerId.Value && x.IsActive))
            return (false, "مهندس مسئول انتخاب‌شده نامعتبر یا غیرفعال است.", null);

        var mr = new MaterialRequisition
        {
            MRNumber = mrNumber,
            ProjectId = projectId,
            DisciplineId = disciplineId,
            EquipmentTagNumber = equipmentTag,
            Description = description,
            RequiredDate = requiredDate,
            ResponsibleEngineerId = responsibleEngineerId,
            RelatedDocumentId = relatedDocumentId,
            Status = "Draft",
            CreatedDate = DateTime.Now
        };

        // SCRUM-428: شماره‌ی قطعه سمت سرور با قطعه‌های ثبت‌شده (کاتالوگ قطعه / Material Master) تطبیق داده می‌شود؛ پیوند فقط از این‌جا ساخته
        // می‌شود (مرورگر نمی‌تواند آن را جعل کند). ردیفی که شرح ندارد ولی قطعه‌ی ثبت‌شده دارد، شرح و واحد را از خود قطعه می‌گیرد.
        foreach (var item in items)
        {
            var part = string.IsNullOrWhiteSpace(item.part) ? null : await _parts.FindRegisteredPartAsync(item.part);
            var itemDescription = !string.IsNullOrWhiteSpace(item.desc) ? item.desc.Trim() : part?.Description;
            if (string.IsNullOrWhiteSpace(itemDescription)) continue;

            mr.Items.Add(new MaterialRequisitionItem
            {
                ItemDescription = itemDescription,
                PartNumber = part?.PartNumber ?? (string.IsNullOrWhiteSpace(item.part) ? null : item.part.Trim()),
                Quantity = item.qty,
                Unit = !string.IsNullOrWhiteSpace(item.unit) ? item.unit.Trim() : part?.Unit,
                MaterialItemId = part?.Source == "MaterialItem" ? part.SourceId : null,
                PartCatalogueRequestId = part?.Source == "CatalogueRequest" ? part.SourceId : null,
            });
        }

        await _context.MaterialRequisitions.AddAsync(mr);

        // طبق ممیزی مستقل (P1-03): بررسی AnyAsync بالا صرفاً پیام دوستانه‌ی سریع است، نه ضامن
        // نهایی - در برابر Race Condition امن نیست (دو درخواست هم‌زمان هر دو AnyAsync==false
        // می‌بینند). ایندکس Unique روی MRNumber (ProcurementConfigurations) ضامن نهایی است؛
        // همون الگوی EngineeringDocumentsController.IsDuplicateDocumentNo.
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsDuplicateMrNumber(ex))
        {
            return (false, $"شماره MR «{mrNumber}» هم‌زمان توسط یک درخواست دیگر استفاده شد.", null);
        }

        return (true, null, mr.MRId);
    }

    private static bool IsDuplicateMrNumber(DbUpdateException ex) =>
        ex.InnerException is Microsoft.Data.SqlClient.SqlException sqlEx
            && (sqlEx.Number == 2601 || sqlEx.Number == 2627);

    // طبق ممیزی مستقل فاز ۶B Wave A (P1): قبلاً هر رشته‌ای بدون بررسی گذار قانونی پذیرفته
    // می‌شد و روی MR ناموجود بی‌صدا هیچ کاری نمی‌کرد (بدون گزارش خطا).
    public async Task<(bool Success, string? Error)> ChangeStatusAsync(int mrId, string newStatus)
    {
        var mr = await _context.MaterialRequisitions.FirstOrDefaultAsync(x => x.MRId == mrId);
        if (mr == null) return (false, "MR پیدا نشد.");

        if (!LegalTransitions.TryGetValue(mr.Status, out var allowed) || !allowed.Contains(newStatus))
            return (false, $"گذار از وضعیت «{mr.Status}» به «{newStatus}» مجاز نیست.");

        mr.Status = newStatus;
        await _context.SaveChangesAsync();

        return (true, null);
    }

    // طبق ممیزی مستقل فاز ۶B Wave A (P1): قبلاً وجود MR مرجع را قبل از ساخت Quotation بررسی
    // نمی‌کرد - فقط به Exception سطح FK تکیه می‌شد.
    public async Task<(bool Success, string? Error, int? Id)> AddQuotationAsync(int mrId, string mrqNumber, string vendorName, DateTime? inquiryDate,
        decimal? amount, string? currency, string? notes)
    {
        if (!await _context.MaterialRequisitions.AnyAsync(x => x.MRId == mrId))
            return (false, "MR پیدا نشد.", null);

        var mrq = new MaterialRequisitionQuotation
        {
            MRId = mrId,
            MRQNumber = mrqNumber,
            VendorName = vendorName,
            InquiryDate = inquiryDate,
            QuotedAmount = amount,
            Currency = currency ?? "USD",
            QuotationStatus = "Sent",
            Notes = notes,
            CreatedDate = DateTime.Now
        };

        await _context.MaterialRequisitionQuotations.AddAsync(mrq);
        await _context.SaveChangesAsync();

        return (true, null, mrq.MRQId);
    }

    public async Task UpdateQuotationEvaluationAsync(int mrqId, string technical, string commercial, string approval)
    {
        var mrq = await _context.MaterialRequisitionQuotations.FirstOrDefaultAsync(x => x.MRQId == mrqId);
        if (mrq == null) return;

        mrq.TechnicalEvaluation = technical;
        mrq.CommercialEvaluation = commercial;
        mrq.ApprovalStatus = approval;
        mrq.QuotationStatus = "Evaluated";

        await _context.SaveChangesAsync();
    }
}
