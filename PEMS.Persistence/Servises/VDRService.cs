using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

public class VDRService : IVDRService
{
    private readonly PEMSDbContext _context;

    public VDRService(PEMSDbContext context)
    {
        _context = context;
    }

    public async Task<List<VDRItemDto>> GetAllAsync(int? projectId = null, int? vendorId = null, string? workflowStatus = null)
    {
        var query = _context.VDRItems
            .Include(x => x.Project)
            .Include(x => x.Vendor)
            .Include(x => x.Po)
            .Include(x => x.Equipment)
            .Include(x => x.Submissions)
                .ThenInclude(x => x.Attachment)
            .Include(x => x.Submissions)
                .ThenInclude(x => x.CommentSheetAttachment)
            .Include(x => x.Submissions)
                .ThenInclude(x => x.ReplySheetAttachment)
            .Where(x => x.IsActive)
            .AsQueryable();

        if (projectId.HasValue) query = query.Where(x => x.ProjectId == projectId.Value);
        if (vendorId.HasValue) query = query.Where(x => x.VendorId == vendorId.Value);
        if (!string.IsNullOrWhiteSpace(workflowStatus)) query = query.Where(x => x.WorkflowStatus == workflowStatus);

        var items = await query.OrderByDescending(x => x.CreatedDate).ToListAsync();

        return items.Select(MapToDto).ToList();
    }

    // طبق سند Global Data Grid Standard فاز ۲ (بند ۴/۹): برخلاف GetAllAsync بالا (که کل
    // Submissions/Attachments هر آیتم را Include می‌کند و همه را یک‌جا بار می‌کند)، این‌جا
    // فقط ستون‌های واقعاً لازم برای ردیف‌های Grid مستقیم Project می‌شوند - هیچ Include ای وجود
    // ندارد، LatestRevisionNo/LatestReviewCategory/IsOverdue همگی زیرکوئری‌های همبسته‌ی
    // Scalar هستند که EF Core می‌تواند کامل سمت سرور Translate کند (نه بارگذاری کامل
    // Submissions و پردازش در حافظه مثل MapToDto).
    public async Task<PagedResult<VDRListItemDto>> GetPagedAsync(
        int? projectId, int? vendorId, string? workflowStatus, int page, int pageSize, string? search = null)
    {
        var query = FilteredList(projectId, vendorId, workflowStatus, search);

        var totalCount = await query.CountAsync();

        var normalizedPage = page < 1 ? 1 : page;
        var normalizedPageSize = DataGridPageSize.Normalize(pageSize);

        var items = await ProjectListRows(Ordered(query)
                .Skip((normalizedPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize))
            .ToListAsync();

        return new PagedResult<VDRListItemDto>
        {
            Items = items,
            Page = normalizedPage,
            PageSize = normalizedPageSize,
            TotalCount = totalCount
        };
    }

    public async Task<(List<VDRListItemDto> Items, int Total)> GetAllMatchingAsync(
        int? projectId, int? vendorId, string? workflowStatus, string? search, int maxRows)
    {
        var query = FilteredList(projectId, vendorId, workflowStatus, search);
        var total = await query.CountAsync();
        var items = await ProjectListRows(Ordered(query).Take(Math.Max(0, maxRows))).ToListAsync();
        return (items, total);
    }

    // فیلترهای مشترک فهرست و خروجی Excel (SCRUM-409): پروژه، فروشنده، وضعیت و جستجوی متنی در شماره، شرح و نام فروشنده
    private IQueryable<VDRItem> FilteredList(int? projectId, int? vendorId, string? workflowStatus, string? search)
    {
        var query = _context.VDRItems
            .AsNoTracking()
            .Where(x => x.IsActive)
            .AsQueryable();

        if (projectId.HasValue) query = query.Where(x => x.ProjectId == projectId.Value);
        if (vendorId.HasValue) query = query.Where(x => x.VendorId == vendorId.Value);
        if (!string.IsNullOrWhiteSpace(workflowStatus)) query = query.Where(x => x.WorkflowStatus == workflowStatus);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.DocumentNumber.Contains(term)
                || (x.DocumentDescription != null && x.DocumentDescription.Contains(term))
                || (x.Vendor != null && x.Vendor.Name.Contains(term)));
        }
        return query;
    }

    // طبق بند ۵ سند فاز ۲: ترتیب پایدار و قطعی - CreatedDate به‌تنهایی برای ردیف‌های
    // Import شده‌ی دسته‌ای (که می‌توانند دقیقاً هم‌زمان ایجاد شوند) کافی نیست
    private static IQueryable<VDRItem> Ordered(IQueryable<VDRItem> query) =>
        query.OrderByDescending(x => x.CreatedDate).ThenByDescending(x => x.DocumentId);

    private static IQueryable<VDRListItemDto> ProjectListRows(IQueryable<VDRItem> query) =>
        query.Select(x => new VDRListItemDto
        {
            VDRItemId = x.DocumentId,
            DocumentNumber = x.DocumentNumber,
            DocumentDescription = x.DocumentDescription,
            DocumentTypeCode = x.DocumentTypeCode,
            VendorName = x.Vendor != null ? x.Vendor.Name : "",
            ProjectName = x.Project != null ? x.Project.ProjectName : "",
            DisciplineName = x.Discipline != null ? x.Discipline.Name : x.DisciplineCodeRaw,
            WorkflowStatus = x.WorkflowStatus,
            LatestRevisionNo = x.Submissions
                .OrderByDescending(s => s.RevisionNo)
                .Select(s => (int?)s.RevisionNo)
                .FirstOrDefault(),
            LatestReviewCategory = x.Submissions
                .OrderByDescending(s => s.RevisionNo)
                .Select(s => s.ReviewCategory)
                .FirstOrDefault(),
            IsOverdue = x.WorkflowStatus != "Closed"
                && x.PlannedFirstIssueDate != null
                && !x.Submissions.Any()
                && x.PlannedFirstIssueDate < DateTime.Now
        });


    public async Task<VDRItemDto?> GetByIdAsync(int id)
    {
        var item = await _context.VDRItems
            .Include(x => x.Project)
            .Include(x => x.Vendor)
            .Include(x => x.Po)
            .Include(x => x.Equipment)
            .Include(x => x.Submissions)
                .ThenInclude(x => x.Attachment)
            .Include(x => x.Submissions)
                .ThenInclude(x => x.CommentSheetAttachment)
            .Include(x => x.Submissions)
                .ThenInclude(x => x.ReplySheetAttachment)
            .FirstOrDefaultAsync(x => x.DocumentId == id);

        if (item == null) return null;
        var dto = MapToDto(item);
        if (item.IsOnHold)
        {
            // SCRUM-559: the open hold shown in the banner
            var hold = await _context.DocumentHolds.AsNoTracking().Where(h => h.DocumentId == id && h.ReleaseDate == null)
                .OrderByDescending(h => h.HoldDate).Select(h => new { h.Reason, h.HoldDate }).FirstOrDefaultAsync();
            dto.HoldReason = hold?.Reason;
            dto.HoldDate = hold?.HoldDate;
        }
        return dto;
    }

    // ================================================================== SCRUM-559: More actions menu

    private const string VdrBusinessObjectType = "VDRItem";   // same as VdrWorkflowService

    public async Task<VdrActionResult> UpdateDetailsAsync(int vdrItemId, VdrItemEditInput input, string? userName)
    {
        var item = await _context.VDRItems.FirstOrDefaultAsync(x => x.DocumentId == vdrItemId);
        if (item == null) return VdrActionResult.Fail("Vdr_Err_NotFound");
        if (!item.IsActive) return VdrActionResult.Fail("Vdr_Err_Inactive");
        if (input.DocumentClass is <= 0) return VdrActionResult.Fail("Vdr_Err_Class");
        int? equipmentId = null;
        var tag = Clean(input.EquipmentTag, 200);
        if (tag != null)
        {
            var upperTag = tag.ToUpperInvariant();
            equipmentId = await _context.Equipments.AsNoTracking().Where(e => e.TagNumber.ToUpper() == upperTag)
                .OrderBy(e => e.ProjectId == item.ProjectId ? 0 : 1).ThenBy(e => e.EquipmentId).Select(e => (int?)e.EquipmentId).FirstOrDefaultAsync();
            if (equipmentId == null) return VdrActionResult.Fail("Vdr_Err_Equipment");
        }

        var changes = new List<string>();
        void Set<T>(string name, T current, T value, Action<T> apply)
        {
            if (EqualityComparer<T>.Default.Equals(current, value)) return;
            changes.Add($"{name}: {Show(current)} -> {Show(value)}");
            apply(value);
        }
        Set("Description", item.DocumentDescription, Clean(input.DocumentDescription, 1000), v => item.DocumentDescription = v);
        Set("Class", item.DocumentClass, input.DocumentClass, v => { item.DocumentClass = v; item.DocumentClassRaw = v?.ToString(); });
        Set("PlannedFirstIssue", item.PlannedFirstIssueDate?.Date, input.PlannedFirstIssueDate?.Date, v => item.PlannedFirstIssueDate = v);
        Set("Equipment", item.EquipmentId, equipmentId, v => item.EquipmentId = v);
        Set("Discipline", item.DisciplineCodeRaw, Clean(input.DisciplineCodeRaw, 50)?.ToUpperInvariant(), v => item.DisciplineCodeRaw = v);
        Set("VendorFormatNo", item.VendorFormatDocumentNumber, Clean(input.VendorFormatDocumentNumber, 200), v => item.VendorFormatDocumentNumber = v);
        if (changes.Count == 0) return VdrActionResult.Ok();

        item.ModifiedDate = DateTime.Now;
        Audit(vdrItemId, "Update", string.Join("\n", changes), userName);
        await _context.SaveChangesAsync();
        return VdrActionResult.Ok();
    }

    public async Task<VdrActionResult> DeactivateAsync(int vdrItemId, string? reason, string? userName)
    {
        if (string.IsNullOrWhiteSpace(reason)) return VdrActionResult.Fail("Vdr_Err_ReasonRequired");
        var item = await _context.VDRItems.FirstOrDefaultAsync(x => x.DocumentId == vdrItemId);
        if (item == null) return VdrActionResult.Fail("Vdr_Err_NotFound");
        if (!item.IsActive) return VdrActionResult.Fail("Vdr_Err_Inactive");
        if (await _context.WorkflowInstances.AnyAsync(w => w.BusinessObjectType == VdrBusinessObjectType && w.BusinessObjectId == vdrItemId && w.Status == "Running"))
            return VdrActionResult.Fail("Vdr_Err_ReviewRunning");

        item.IsActive = false;
        item.ModifiedDate = DateTime.Now;
        Audit(vdrItemId, "Deactivate", "Reason: " + Clean(reason, 1000), userName);
        await _context.SaveChangesAsync();
        return VdrActionResult.Ok();
    }

    private void Audit(int vdrItemId, string action, string summary, string? userName) =>
        _context.AuditLogs.Add(new AuditLog
        {
            EntityName = nameof(VDRItem), EntityKeyValue = vdrItemId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Action = action, ChangedFieldsSummary = summary, UserName = userName, Timestamp = DateTime.Now
        });

    private static string? Clean(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        return s.Length <= max ? s : s[..max];
    }

    private static string Show<T>(T value) => value switch
    {
        null => "-",
        DateTime d => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "-"
    };

    public async Task<int> CreateManualAsync(VDRItemDto dto)
    {
        var item = new VDRItem
        {
            SequenceNo = dto.SequenceNo,
            ProjectId = dto.ProjectId,
            DisciplineCodeRaw = dto.DisciplineCodeRaw,
            IsMultiDiscipline = dto.IsMultiDiscipline,
            MRNumber = dto.MRNumber,
            VendorId = dto.VendorId,
            EquipmentId = dto.EquipmentId,
            PackageDescription = dto.PackageDescription,
            DocumentTypeCode = dto.DocumentTypeCode,
            DocumentNumber = dto.DocumentNumber,
            DocumentDescription = dto.DocumentDescription,
            MaterialCode = dto.MaterialCode,
            TypologyCode = dto.TypologyCode,
            ResponsiblePartyCode = dto.ResponsiblePartyCode,
            DocumentClass = dto.DocumentClass,
            PlannedFirstIssueDate = dto.PlannedFirstIssueDate,
            WorkflowStatus = "Planned",
            IsActive = true,
            CreatedDate = DateTime.Now,

            // هم‌گام‌سازی با فیلدهای مشترک Document Core برای جستجو/گزارش یکپارچه
            DocumentKind = "Vendor",
            DocumentNo = dto.DocumentNumber,
            Title = string.IsNullOrWhiteSpace(dto.DocumentDescription) ? dto.DocumentNumber : dto.DocumentDescription,
            DocumentType = dto.DocumentTypeCode,
            Status = "Planned",
            PlannedDate = dto.PlannedFirstIssueDate
        };

        await _context.VDRItems.AddAsync(item);
        await _context.SaveChangesAsync();

        return item.DocumentId;
    }

    // ==============================================================================
    // AddSubmissionAsync قبلاً این‌جا بود - طبق ممیزی حذف شد چون بدون RBAC و بدون بازبینی
    // موازی دیسیپلین، هرکسی می‌توانست مستقیم ReviewCategory (AP/RE/...) را ثبت کند. جایگزین:
    // PEMS.Persistence.Services.VdrWorkflowService (SubmitForReviewAsync/DisciplineRespondAsync/
    // LeadReviewAsync/ReturnToVendorAsync) که از موتور عمومی IWorkflowRuntime عبور می‌کند.
    // ==============================================================================

    public async Task<VDRDashboardDto> GetDashboardAsync(int? projectId = null)
    {
        var query = _context.VDRItems
            .Include(x => x.Vendor)
            .Include(x => x.Submissions)
                .ThenInclude(x => x.Attachment)
            .Include(x => x.Submissions)
                .ThenInclude(x => x.CommentSheetAttachment)
            .Include(x => x.Submissions)
                .ThenInclude(x => x.ReplySheetAttachment)
            .Where(x => x.IsActive)
            .AsQueryable();

        if (projectId.HasValue)
            query = query.Where(x => x.ProjectId == projectId.Value);

        var items = await query.ToListAsync();

        var dashboard = new VDRDashboardDto
        {
            TotalItems = items.Count,
            ClosedItems = items.Count(x => x.WorkflowStatus == "Closed"),
            PendingResubmissionItems = items.Count(x => x.WorkflowStatus == "PendingResubmission"),
            UnderReviewItems = items.Count(x => x.WorkflowStatus == "UnderReview"),
            OverdueItems = items.Count(IsOverdue),
            SlaOverdueItems = items.Count(x => x.Submissions.Any(s => s.IsContractorCommentOverdue || s.IsVendorReturnOverdue)),
        };

        dashboard.OnTimePercentage = dashboard.TotalItems == 0
            ? 0
            : Math.Round(100.0 * items.Count(x => !IsOverdue(x)) / dashboard.TotalItems, 1);

        dashboard.ByWorkflowStatus = items
            .GroupBy(x => x.WorkflowStatus)
            .ToDictionary(g => g.Key, g => g.Count());

        dashboard.ByReviewCategory = items
            .SelectMany(x => x.Submissions)
            .Where(s => !string.IsNullOrWhiteSpace(s.ReviewCategory))
            .GroupBy(s => s.ReviewCategory!)
            .ToDictionary(g => g.Key, g => g.Count());

        dashboard.TopDelayedVendors = items
            .GroupBy(x => x.Vendor?.Name ?? "Unknown")
            .Select(g =>
            {
                var delays = new List<double>();

                foreach (var x in g.Where(x => x.PlannedFirstIssueDate.HasValue))
                {
                    var firstSubmission = x.Submissions.OrderBy(s => s.RevisionNo).FirstOrDefault();

                    if (firstSubmission?.VendorActualIssuedDate != null)
                        delays.Add((firstSubmission.VendorActualIssuedDate.Value - x.PlannedFirstIssueDate!.Value).TotalDays);
                }

                return new VendorDelayDto
                {
                    VendorName = g.Key,
                    OverdueCount = g.Count(IsOverdue),
                    AverageDelayDays = delays.Any() ? Math.Round(delays.Average(), 1) : 0,
                    RiskLevel = g.First().Vendor?.RiskLevel ?? "Unknown"
                };
            })
            .OrderByDescending(x => x.OverdueCount)
            .Take(10)
            .ToList();

        return dashboard;
    }

    // =========================
    // Helpers
    // =========================

    public static bool IsOverdue(VDRItem item)
    {
        if (item.WorkflowStatus == "Closed") return false;
        if (item.PlannedFirstIssueDate == null) return false;

        var hasAnySubmission = item.Submissions.Any();

        return !hasAnySubmission && item.PlannedFirstIssueDate < DateTime.Now;
    }

    public static string ComputeWorkflowStatus(string? latestReviewCategory, DateTime? plannedDate)
    {
        if (string.IsNullOrWhiteSpace(latestReviewCategory))
            return "Submitted";

        // طبق رفع باگ ممیزی VDR: AC (Approved with Comments) یعنی می‌تواند ادامه دهد -
        // فقط CO (Comment/Revise & Resubmit) و RE (Rejected) واقعاً نیاز به ارسال مجدد دارند
        return latestReviewCategory switch
        {
            "AP" or "NC" or "AC" => "Closed",
            "CO" or "RE" => "PendingResubmission",
            _ => "UnderReview"
        };
    }

    private static VDRItemDto MapToDto(VDRItem item)
    {
        var orderedSubmissions = item.Submissions
            .OrderBy(s => s.RevisionNo)
            .ToList();

        var latest = orderedSubmissions.LastOrDefault();

        return new VDRItemDto
        {
            VDRItemId = item.DocumentId,
            SequenceNo = item.SequenceNo,
            ProjectId = item.ProjectId,
            ProjectName = item.Project?.ProjectName ?? "",
            DisciplineCodeRaw = item.DisciplineCodeRaw,
            IsMultiDiscipline = item.IsMultiDiscipline,
            MRNumber = item.MRNumber,
            VendorId = item.VendorId,
            VendorName = item.Vendor?.Name ?? "",
            EquipmentId = item.EquipmentId,
            EquipmentTagNumber = item.Equipment?.TagNumber,
            PoNumber = item.Po?.PoNumber,
            PackageDescription = item.PackageDescription,
            DocumentTypeCode = item.DocumentTypeCode,
            DocumentNumber = item.DocumentNumber,
            DocumentDescription = item.DocumentDescription,
            MaterialCode = item.MaterialCode,
            TypologyCode = item.TypologyCode,
            VendorFormatDocumentNumber = item.VendorFormatDocumentNumber,
            ResponsiblePartyCode = item.ResponsiblePartyCode,
            DocumentClass = item.DocumentClass,
            PlannedFirstIssueDate = item.PlannedFirstIssueDate,
            WorkflowStatus = item.WorkflowStatus,
            RevisionCount = orderedSubmissions.Count,
            LatestRevisionNo = latest?.RevisionNo,
            LatestReviewCategory = latest?.ReviewCategory,
            LatestVendorActualIssuedDate = latest?.VendorActualIssuedDate,
            IsOverdue = IsOverdue(item),
            IsActive = item.IsActive,
            IsOnHold = item.IsOnHold,

            Submissions = orderedSubmissions.Select(s => new VDRSubmissionDto
            {
                VDRSubmissionId = s.VDRSubmissionId,
                RevisionNo = s.RevisionNo,
                VendorTransmittalNo = s.VendorTransmittalNo,
                ClientTransmittalNo = s.ClientTransmittalNo,
                ClientTransmittalId = s.ClientTransmittalId,
                AttachmentId = s.AttachmentId,
                MainFilePath = s.Attachment?.FilePath,
                VendorActualIssuedDate = s.VendorActualIssuedDate,
                PurposeOfIssueCode = s.PurposeOfIssueCode,
                ClientCommentSheetNo = s.ClientCommentSheetNo,
                ClientCommentDate = s.ClientCommentDate,
                ReviewCategory = s.ReviewCategory,
                ReplySheetNo = s.ReplySheetNo,
                ReplySheetDate = s.ReplySheetDate,
                RequiresResubmission = s.RequiresResubmission,
                ContractorCommentDueDate = s.ContractorCommentDueDate,
                IsContractorCommentOverdue = s.IsContractorCommentOverdue,
                VendorReturnDueDate = s.VendorReturnDueDate,
                IsVendorReturnOverdue = s.IsVendorReturnOverdue,
                FtpUploadedDate = s.FtpUploadedDate,
                FtpRemotePath = s.FtpRemotePath,
                CommentSheetAttachmentId = s.CommentSheetAttachmentId,
                CommentSheetFileName = s.CommentSheetAttachment?.FileName,
                CommentSheetFilePath = s.CommentSheetAttachment?.FilePath,
                ReplySheetAttachmentId = s.ReplySheetAttachmentId,
                ReplySheetFileName = s.ReplySheetAttachment?.FileName,
                ReplySheetFilePath = s.ReplySheetAttachment?.FilePath
            }).ToList()
        };
    }
}
