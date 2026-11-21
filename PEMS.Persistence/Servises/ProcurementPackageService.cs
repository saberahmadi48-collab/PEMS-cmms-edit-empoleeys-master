using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>
/// پیاده‌سازی Procurement Package طبق سند Engineering Intelligence Redesign: MR، MRQ،
/// Vendor Inquiry/Technical Clarification/Engineering Question/Response، و Approval History
/// همه زیر یک Entity والد یکپارچه مدیریت می‌شوند.
/// </summary>
public class ProcurementPackageService : IProcurementPackageService
{
    private readonly PEMSDbContext _context;

    private static readonly string[] StageOrder =
        { "MR", "VendorInquiry", "TechnicalClarification", "MRQ", "Response", "Approval", "Closeout" };

    private readonly INumberingService _numbering;

    // SCRUM-40: شماره‌گذاری اختیاری در سازنده تا تست‌های تک‌پارامتری سازگار بمانند؛ DI آن را می‌دهد
    public ProcurementPackageService(PEMSDbContext context, INumberingService? numbering = null)
    {
        _context = context;
        _numbering = numbering ?? new NumberingService(context);
    }

    public async Task<List<ProcurementPackageDto>> GetAllAsync()
    {
        return await _context.ProcurementPackages
            .Include(x => x.Project)
            .Include(x => x.Discipline)
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.CreatedDate)
            .Select(x => new ProcurementPackageDto
            {
                ProcurementPackageId = x.ProcurementPackageId,
                PackageNumber = x.PackageNumber,
                ProjectName = x.Project!.ProjectName,
                DisciplineName = x.Discipline != null ? x.Discipline.Name : null,
                Title = x.Title,
                Stage = x.Stage,
                Status = x.Status,
                MRCount = x.MaterialRequisitions.Count,
                MRQCount = x.Quotations.Count,
                OpenCommunicationsCount = x.Communications.Count(c => c.ActionStatus != "Closed"),
                CreatedDate = x.CreatedDate
            })
            .ToListAsync();
    }

    // طبق «PEMS — Global Grid Hardening» P1: جایگزین GetAllAsync برای Index - شمارش/مرتب‌سازی/
    // صفحه‌بندی روی IQueryable قبل از Select.
    public async Task<PagedResult<ProcurementPackageDto>> GetPagedAsync(int page, int pageSize)
    {
        var query = _context.ProcurementPackages.AsNoTracking().Where(x => x.IsActive).AsQueryable();

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(x => x.CreatedDate)
            .ThenByDescending(x => x.ProcurementPackageId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new ProcurementPackageDto
            {
                ProcurementPackageId = x.ProcurementPackageId,
                PackageNumber = x.PackageNumber,
                ProjectName = x.Project!.ProjectName,
                DisciplineName = x.Discipline != null ? x.Discipline.Name : null,
                Title = x.Title,
                Stage = x.Stage,
                Status = x.Status,
                MRCount = x.MaterialRequisitions.Count,
                MRQCount = x.Quotations.Count,
                OpenCommunicationsCount = x.Communications.Count(c => c.ActionStatus != "Closed"),
                CreatedDate = x.CreatedDate
            })
            .ToListAsync();

        return new PagedResult<ProcurementPackageDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public async Task<ProcurementPackageDetailDto?> GetDetailAsync(int packageId)
    {
        var package = await _context.ProcurementPackages
            .Include(x => x.Project)
            .Include(x => x.Discipline)
            .FirstOrDefaultAsync(x => x.ProcurementPackageId == packageId);

        if (package == null) return null;

        var mrs = await _context.MaterialRequisitions
            .Include(x => x.Project)
            .Include(x => x.Discipline)
            .Include(x => x.ResponsibleEngineer)
            .Where(x => x.ProcurementPackageId == packageId)
            .ToListAsync();

        var quotations = await _context.MaterialRequisitionQuotations
            .Where(x => x.ProcurementPackageId == packageId)
            .ToListAsync();

        var communications = await _context.ProcurementCommunications
            .Include(x => x.Vendor)
            .Where(x => x.ProcurementPackageId == packageId)
            .OrderByDescending(x => x.RaisedDate)
            .ToListAsync();

        var approvals = await _context.ProcurementApprovals
            .Where(x => x.ProcurementPackageId == packageId)
            .OrderByDescending(x => x.ApprovalDate)
            .ToListAsync();

        return new ProcurementPackageDetailDto
        {
            Summary = new ProcurementPackageDto
            {
                ProcurementPackageId = package.ProcurementPackageId,
                PackageNumber = package.PackageNumber,
                ProjectName = package.Project?.ProjectName ?? "",
                DisciplineName = package.Discipline?.Name,
                Title = package.Title,
                Stage = package.Stage,
                Status = package.Status,
                MRCount = mrs.Count,
                MRQCount = quotations.Count,
                OpenCommunicationsCount = communications.Count(c => c.ActionStatus != "Closed"),
                CreatedDate = package.CreatedDate
            },
            MaterialRequisitions = mrs.Select(m => new MRDto
            {
                MRId = m.MRId,
                MRNumber = m.MRNumber,
                ProjectName = m.Project?.ProjectName ?? "",
                DisciplineName = m.Discipline?.Name ?? "",
                EquipmentTagNumber = m.EquipmentTagNumber,
                Description = m.Description,
                ResponsibleEngineerName = m.ResponsibleEngineer != null
                    ? m.ResponsibleEngineer.FirstName + " " + m.ResponsibleEngineer.LastName
                    : null,
                Status = m.Status,
                CreatedDate = m.CreatedDate
            }).ToList(),
            Quotations = quotations.Select(q => new MRQDto
            {
                MRQId = q.MRQId,
                MRQNumber = q.MRQNumber,
                MRId = q.MRId,
                VendorName = q.VendorName,
                InquiryDate = q.InquiryDate,
                QuotedAmount = q.QuotedAmount,
                Currency = q.Currency,
                QuotationStatus = q.QuotationStatus,
                TechnicalEvaluation = q.TechnicalEvaluation,
                CommercialEvaluation = q.CommercialEvaluation,
                ApprovalStatus = q.ApprovalStatus,
                Notes = q.Notes
            }).ToList(),
            Communications = communications.Select(c => new ProcurementCommunicationDto
            {
                ProcurementCommunicationId = c.ProcurementCommunicationId,
                Type = c.Type,
                Subject = c.Subject,
                Body = c.Body,
                VendorName = c.Vendor?.Name,
                RaisedByName = c.RaisedByName,
                RaisedDate = c.RaisedDate,
                ActionStatus = c.ActionStatus,
                ResponseText = c.ResponseText,
                RespondedByName = c.RespondedByName,
                ResponseDate = c.ResponseDate,
                AttachmentId = c.AttachmentId
            }).ToList(),
            Approvals = approvals.Select(a => new ProcurementApprovalDto
            {
                ProcurementApprovalId = a.ProcurementApprovalId,
                ApproverName = a.ApproverName,
                Decision = a.Decision,
                Comment = a.Comment,
                ApprovalDate = a.ApprovalDate
            }).ToList()
        };
    }

    public async Task<int> CreateAsync(string packageNumber, int projectId, int? disciplineId, string title, string? description)
    {
        // SCRUM-40: شماره خودکار از الگوی مدیر (شماره‌ی واردشده فقط اگر الگو ورود دستی را مجاز کرده باشد)؛ یکتایی همچنان در سرویس بررسی می‌شود.
        packageNumber = await _numbering.ResolveNumberAsync("ProcurementPackage", packageNumber, projectId, disciplineId);
        if (packageNumber.Length == 0)
            throw new InvalidOperationException("شماره‌ی بسته‌ی تدارکات الزامی است.");
        if (await _context.ProcurementPackages.AnyAsync(x => x.PackageNumber == packageNumber))
            throw new InvalidOperationException($"بسته‌ای با شماره‌ی «{packageNumber}» از قبل وجود دارد.");

        var package = new ProcurementPackage
        {
            PackageNumber = packageNumber,
            ProjectId = projectId,
            DisciplineId = disciplineId,
            Title = title,
            Description = description,
            Stage = "MR",
            Status = "Open",
            CreatedDate = DateTime.Now
        };

        await _context.ProcurementPackages.AddAsync(package);
        await _context.SaveChangesAsync();

        return package.ProcurementPackageId;
    }

    /// <summary>
    /// جلو/عقب بردن مرحله‌ی کاری Package (MR … Approval). مرحله‌ی «Closeout» عمداً از این مسیر
    /// قابل‌دسترس نیست: بستن Package باید فقط از <see cref="CloseoutAsync"/> عبور کند تا گیت بازرسی
    /// (و در لایه‌ی Controller مجوز Approve و SoD) دور زده نشود. Package بسته‌شده یا لغوشده نیز
    /// دیگر با این مسیر «احیا» نمی‌شود.
    /// </summary>
    public async Task AdvanceStageAsync(int packageId, string newStage)
    {
        if (!StageOrder.Contains(newStage))
            throw new InvalidOperationException($"مرحله‌ی نامعتبر: {newStage}");

        if (newStage == CloseoutStage)
            throw new InvalidOperationException(
                "مرحله‌ی Closeout از مسیر تغییر مرحله‌ی عمومی قابل‌انجام نیست؛ از عملیات «Closeout» (نیازمند مجوز Approve و گیت بازرسی) استفاده کنید.");

        var package = await _context.ProcurementPackages.FirstOrDefaultAsync(x => x.ProcurementPackageId == packageId);
        if (package == null) throw new Exception("Package یافت نشد");

        EnsureNotTerminal(package);

        package.Stage = newStage;
        package.Status = "InProgress";

        await _context.SaveChangesAsync();
    }

    // طبق ممیزی Package Procurement Contract Finalization (§D/§I قاعده‌ی ۱۱، Phase 5.4): پیش از
    // این، Closeout هیچ وابستگی‌ای به وضعیت بازرسی نداشت - یک بسته‌ی نیازمند بازرسی می‌توانست با
    // صفر رکورد بازرسی بسته شود. این گیت سمت-سرور است (نه فقط UI) - دقیقاً هم‌الگو با سایر
    // گیت‌های این پروژه (مثلاً IrnDispositionService).
    // تنها نقطه‌ی ورود به Stage=Closeout / Status=Closed است (AdvanceStageAsync آن را رد می‌کند).
    // سیاست: در حال حاضر قاعده‌ای که «ثبت تاییدیه‌ی Approval پیش از Closeout» را الزامی کند در
    // دامنه تعریف نشده و اختراع نشده است - این یک شکاف سیاستی مستند است، نه رفتار پیاده‌شده.
    public async Task CloseoutAsync(int packageId)
    {
        var package = await _context.ProcurementPackages.FirstOrDefaultAsync(x => x.ProcurementPackageId == packageId);
        if (package == null) throw new Exception("Package یافت نشد");

        EnsureNotTerminal(package);

        if (package.RequiresInspection)
        {
            var linkedItps = await _context.InspectionTestPlans
                .Where(x => x.ProcurementPackageId == packageId)
                .ToListAsync();

            if (linkedItps.Count == 0)
                throw new InvalidOperationException(
                    "این بسته نیازمند کنترل‌های بازرسی الزامی است ولی هیچ ITP ای برایش تعریف نشده - Closeout مسدود است.");

            var incomplete = linkedItps.Where(x => x.Status != "Closed").ToList();
            if (incomplete.Count > 0)
                throw new InvalidOperationException(
                    $"{incomplete.Count} ITP این بسته هنوز به وضعیت Closed نرسیده‌اند - Closeout مسدود است تا همه‌ی بازرسی‌های الزامی کامل شوند.");
        }

        package.Stage = CloseoutStage;
        package.Status = "Closed";
        package.ClosedDate = DateTime.Now;

        await _context.SaveChangesAsync();
    }

    private const string CloseoutStage = "Closeout";

    private static void EnsureNotTerminal(ProcurementPackage package)
    {
        if (package.Status == "Closed" || package.Status == "Cancelled")
            throw new InvalidOperationException(
                $"Package در وضعیت {package.Status} است و مرحله‌ی آن دیگر قابل‌تغییر نیست.");
    }

    public async Task LinkExistingMRAsync(int packageId, int mrId)
    {
        var mr = await _context.MaterialRequisitions.FirstOrDefaultAsync(x => x.MRId == mrId);
        if (mr == null) throw new Exception("MR یافت نشد");

        mr.ProcurementPackageId = packageId;

        await _context.SaveChangesAsync();
    }

    public async Task<int> AddCommunicationAsync(int packageId, string type, string subject, string body,
        int? vendorId, string? raisedByName, int? attachmentId, int? tenderId = null)
    {
        var communication = new ProcurementCommunication
        {
            ProcurementPackageId = packageId,
            TenderId = tenderId,
            Type = type,
            Subject = subject,
            Body = body,
            VendorId = vendorId,
            RaisedByName = raisedByName,
            AttachmentId = attachmentId,
            ActionStatus = "Open",
            RaisedDate = DateTime.Now
        };

        await _context.ProcurementCommunications.AddAsync(communication);

        // پیشرفت خودکار مرحله بر اساس نوع ارتباط ثبت‌شده (طبق زنجیره‌ی سند)
        var package = await _context.ProcurementPackages.FirstOrDefaultAsync(x => x.ProcurementPackageId == packageId);

        if (package != null)
        {
            var impliedStage = type switch
            {
                "VendorInquiry" => "VendorInquiry",
                "TechnicalClarification" or "EngineeringQuestion" => "TechnicalClarification",
                "Response" => "Response",
                _ => package.Stage
            };

            if (Array.IndexOf(StageOrder, impliedStage) > Array.IndexOf(StageOrder, package.Stage))
            {
                package.Stage = impliedStage;
                package.Status = "InProgress";
            }
        }

        await _context.SaveChangesAsync();

        return communication.ProcurementCommunicationId;
    }

    public async Task RespondToCommunicationAsync(int packageId, int communicationId, string responseText, string? respondedByName)
    {
        var communication = await _context.ProcurementCommunications
            .FirstOrDefaultAsync(x => x.ProcurementCommunicationId == communicationId && x.ProcurementPackageId == packageId);

        if (communication == null) return;

        // طبق گزارش سخت‌افزاری نهایی (Gap ۲): پیش از این، پاسخ قبلی این‌جا بدون اثر بازنویسی
        // می‌شد - اکنون هر پاسخ یک ردیف تازه‌ی append-only در Responses ثبت می‌کند، و فیلدهای
        // هم‌نام روی خودِ رکورد فقط Cache آخرین پاسخ‌اند (دقیقاً الگوی Document/DocumentRevision)
        _context.ProcurementCommunicationResponses.Add(new ProcurementCommunicationResponse
        {
            ProcurementCommunicationId = communicationId,
            ResponseText = responseText,
            RespondedByName = respondedByName,
            ResponseDate = DateTime.Now
        });

        communication.ResponseText = responseText;
        communication.RespondedByName = respondedByName;
        communication.ResponseDate = DateTime.Now;
        communication.ActionStatus = "Answered";

        await _context.SaveChangesAsync();
    }

    public async Task SetCommunicationStatusAsync(int packageId, int communicationId, string actionStatus)
    {
        var communication = await _context.ProcurementCommunications
            .FirstOrDefaultAsync(x => x.ProcurementCommunicationId == communicationId && x.ProcurementPackageId == packageId);

        if (communication == null) return;

        communication.ActionStatus = actionStatus;

        await _context.SaveChangesAsync();
    }

    public async Task AddApprovalAsync(int packageId, string approverName, string decision, string? comment)
    {
        await _context.ProcurementApprovals.AddAsync(new ProcurementApproval
        {
            ProcurementPackageId = packageId,
            ApproverName = approverName,
            Decision = decision,
            Comment = comment,
            ApprovalDate = DateTime.Now
        });

        var package = await _context.ProcurementPackages.FirstOrDefaultAsync(x => x.ProcurementPackageId == packageId);

        if (package != null && decision == "Approved" && Array.IndexOf(StageOrder, "Approval") > Array.IndexOf(StageOrder, package.Stage))
        {
            package.Stage = "Approval";
            package.Status = "InProgress";
        }

        await _context.SaveChangesAsync();
    }
}
