using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Seed;
using PEMS.WorkflowPlatform.Dtos;
using PEMS.WorkflowPlatform.Interfaces;

namespace PEMS.Persistence.Services;


// ==============================================================================
// آداپتور نازک روی موتور عمومی IWorkflowRuntime برای ماژول VDR - دقیقاً همان معماری
// DocumentWorkflowService (مدرک مهندسی):
//   - RBAC واقعی از موتور (نه هاردکد، نه بدون‌چک مثل نسخه‌ی قبلی VDR)
//   - بازبینی موازی دیسیپلین‌ها با اعضای پویا (بازاستفاده از DocumentIdcReferral چون
//     DocumentId کلید مشترک VDRItem و EngineeringDocument است - هر دو از Document ارث می‌برند)
//   - بازگشت رسمی به وندور همیشه یک نامه‌ی خروجی رسمی می‌سازد (وندور همیشه «مقصد بیرونی»
//     است طبق تصمیم DCC، پس این‌جا برخلاف Issue مدرک مهندسی، نامه اختیاری نیست)
// ==============================================================================
public class VdrWorkflowService : IVdrWorkflowService
{
    private const string Module = VdrWorkflowSeeder.Module;
    private const string BusinessObjectType = "VDRItem";
    private const string Category = VdrWorkflowSeeder.Category;

    private static readonly string[] ValidReviewCategories = { "AP", "NC", "AC", "CO", "RE" };

    private readonly PEMSDbContext _context;
    private readonly IWorkflowRuntime _runtime;
    private readonly ILetterService _letterService;

    public VdrWorkflowService(PEMSDbContext context, IWorkflowRuntime runtime, ILetterService letterService)
    {
        _context = context;
        _runtime = runtime;
        _letterService = letterService;
    }


    public async Task SubmitForReviewAsync(
        int vdrItemId, int revisionNo, string vendorTransmittalNo, DateTime? vendorActualIssuedDate,
        string? purposeOfIssueCode, IReadOnlyList<int>? disciplineIds,
        int? actionByEmployeeId, IReadOnlyList<string> actorRoles,
        string? ipAddress = null, string? userAgent = null)
    {
        if (string.IsNullOrWhiteSpace(vendorTransmittalNo))
            throw new Exception("شماره‌ی ترانسمیتال وندور الزامی است.");

        var item = await GetItemOrThrowAsync(vdrItemId);

        var existingInstance = await _runtime.GetInstanceAsync(BusinessObjectType, vdrItemId);
        if (existingInstance != null && existingInstance.Status == "Running")
            throw new Exception("این مدرک از قبل در حال بازبینی است - نمی‌توان چرخه‌ی جدید شروع کرد.");

        var duplicateRevision = await _context.VDRSubmissions
            .AnyAsync(s => s.VDRItemId == vdrItemId && s.RevisionNo == revisionNo);
        if (duplicateRevision)
            throw new Exception($"رویژن {revisionNo} قبلاً برای این مدرک ثبت شده است.");

        await _context.VDRSubmissions.AddAsync(new VDRSubmission
        {
            VDRItemId = vdrItemId,
            RevisionNo = revisionNo,
            VendorTransmittalNo = vendorTransmittalNo,
            VendorActualIssuedDate = vendorActualIssuedDate,
            PurposeOfIssueCode = purposeOfIssueCode,
            CreatedDate = DateTime.Now
        });

        // ==============================================================================
        // ارجاع دیسیپلین‌ها برای این چرخه‌ی بازبینی - بازاستفاده از همان جدول IDC مدرک
        // مهندسی، چون مفهوم یکسان است («کدام دیسیپلین‌ها باید این مدرک را بازبینی کنند»)
        // و DocumentId کلید مشترک هر دو نوع مدرک است.
        // ==============================================================================
        var previousReferrals = _context.DocumentIdcReferrals.Where(r => r.DocumentId == vdrItemId);
        _context.DocumentIdcReferrals.RemoveRange(previousReferrals);

        if (disciplineIds != null)
        {
            foreach (var disciplineId in disciplineIds.Distinct())
            {
                _context.DocumentIdcReferrals.Add(new DocumentIdcReferral
                {
                    DocumentId = vdrItemId,
                    DisciplineId = disciplineId,
                    CreatedById = actionByEmployeeId,
                    CreatedDate = DateTime.Now
                });
            }
        }

        await _context.SaveChangesAsync();

        var participantRoles = disciplineIds?.Select(EngineeringDocumentWorkflowSeeder.DisciplineRole).ToList();

        await _runtime.StartInstanceAsync(
            Module, BusinessObjectType, vdrItemId, Category, item.ProjectId, actionByEmployeeId, participantRoles);

        item.WorkflowStatus = "UnderReview";
        item.Status = "UnderReview";
        item.RevisionNo = revisionNo.ToString();
        item.ModifiedDate = DateTime.Now;
        if (vendorActualIssuedDate.HasValue) item.ActualDate = vendorActualIssuedDate;

        await _context.SaveChangesAsync();
    }


    public async Task DisciplineRespondAsync(
        int vdrItemId, string decision, string? comment,
        int actorEmployeeId, int actorDisciplineId,
        string? ipAddress = null, string? userAgent = null)
    {
        var normalizedDecision = decision.Trim();
        if (normalizedDecision != "Approve" && normalizedDecision != "Comment" && normalizedDecision != "Reject")
            throw new Exception("تصمیم نامعتبر است (باید Approve/Comment/Reject باشد).");

        await GetItemOrThrowAsync(vdrItemId);   // SCRUM-559: refuses a held or inactive item
        var instanceId = await GetActiveInstanceIdOrThrowAsync(vdrItemId);
        var actorRoles = new[] { EngineeringDocumentWorkflowSeeder.DisciplineRole(actorDisciplineId) };

        await _runtime.ExecuteActionAsync(
            instanceId, normalizedDecision, actorEmployeeId, actorRoles, comment, ipAddress, userAgent);
    }


    public async Task LeadReviewAsync(
        int vdrItemId, string reviewCategory, string? commentSheetNo, DateTime? commentDate,
        int? actionByEmployeeId, IReadOnlyList<string> actorRoles,
        string? ipAddress = null, string? userAgent = null)
    {
        var normalized = reviewCategory.Trim().ToUpperInvariant();
        if (!ValidReviewCategories.Contains(normalized))
            throw new Exception($"کد بازبینی نامعتبر است: {reviewCategory} (باید یکی از AP/NC/AC/CO/RE باشد)");

        var item = await GetItemOrThrowAsync(vdrItemId);
        var instanceId = await GetActiveInstanceIdOrThrowAsync(vdrItemId);

        await _runtime.ExecuteActionAsync(
            instanceId, normalized, actionByEmployeeId, actorRoles, null, ipAddress, userAgent);

        var latestSubmission = await GetLatestSubmissionAsync(vdrItemId);
        if (latestSubmission != null)
        {
            latestSubmission.ReviewCategory = normalized;
            latestSubmission.ClientCommentSheetNo = commentSheetNo;
            latestSubmission.ClientCommentDate = commentDate;
        }

        item.CommentStatus = normalized;
        item.ModifiedDate = DateTime.Now;

        await _context.SaveChangesAsync();
    }


    // ==============================================================================
    // بازگشت رسمی به وندور - همیشه یک نامه‌ی خروجی رسمی می‌سازد (وندور همیشه یک مقصد
    // بیرونی است، برخلاف Issue مدرک مهندسی که مقصد می‌تواند داخلی هم باشد).
    // ==============================================================================
    public async Task ReturnToVendorAsync(
        int vdrItemId, string? replySheetNo, string? clientTransmittalNo,
        int? actionByEmployeeId, IReadOnlyList<string> actorRoles,
        string? ipAddress = null, string? userAgent = null)
    {
        var item = await GetItemOrThrowAsync(vdrItemId);
        var instanceId = await GetActiveInstanceIdOrThrowAsync(vdrItemId);

        await _runtime.ExecuteActionAsync(
            instanceId, "Issue", actionByEmployeeId, actorRoles, null, ipAddress, userAgent);

        var latestSubmission = await GetLatestSubmissionAsync(vdrItemId);
        var reviewCategory = latestSubmission?.ReviewCategory;

        if (latestSubmission != null)
        {
            latestSubmission.ReplySheetNo = replySheetNo;
            latestSubmission.ReplySheetDate = DateTime.Now;
            latestSubmission.ClientTransmittalNo = clientTransmittalNo;
        }

        // طبق رفع باگ نگاشت قدیمی: AC (Approved with Comments) نباید نیاز به ارسال مجدد
        // اجباری داشته باشد - فقط CO و RE واقعاً نیاز به Resubmission دارند
        item.WorkflowStatus = reviewCategory switch
        {
            "AP" or "NC" or "AC" => "Closed",
            "CO" or "RE" => "PendingResubmission",
            _ => "UnderReview"
        };
        item.Status = item.WorkflowStatus;
        item.ModifiedDate = DateTime.Now;

        await _context.SaveChangesAsync();

        var vendor = await _context.Vendors.FirstOrDefaultAsync(v => v.VendorId == item.VendorId);
        var reviewLabel = reviewCategory switch
        {
            "AP" => "Approved",
            "NC" => "No Comment",
            "AC" => "Approved with Comments",
            "CO" => "Comment / Revise & Resubmit",
            "RE" => "Rejected",
            _ => reviewCategory ?? "N/A"
        };

        await _letterService.CreateAsync(
            letterType: "Outgoing",
            letterNumber: clientTransmittalNo ?? GenerateLetterNumber(item.DocumentNo),
            letterDate: DateTime.Now,
            sender: null,
            receiver: vendor?.Name,
            subject: $"Review Result: {item.DocumentNo} Rev.{item.RevisionNo} - {reviewLabel}",
            body: $"نتیجه‌ی بازبینی مدرک {item.DocumentNo} (رویژن {item.RevisionNo}): {reviewLabel}."
                  + (string.IsNullOrWhiteSpace(replySheetNo) ? "" : $" شماره برگه پاسخ: {replySheetNo}"),
            priority: "Normal",
            confidentialityLevel: "Internal",
            projectId: item.ProjectId,
            disciplineId: item.DisciplineId,
            relatedDocumentId: vdrItemId,
            relatedMRId: item.MRId,
            registeredById: actionByEmployeeId);
    }


    public async Task<IReadOnlyList<VdrReviewStatusItemDto>> GetDisciplineReviewStatusAsync(int vdrItemId)
    {
        var instance = await _context.WorkflowInstances
            .Where(x => x.Module == Module && x.BusinessObjectType == BusinessObjectType && x.BusinessObjectId == vdrItemId)
            .OrderByDescending(x => x.StartDate)
            .FirstOrDefaultAsync();

        if (instance == null) return Array.Empty<VdrReviewStatusItemDto>();

        var stage = await _context.WorkflowStages
            .FirstOrDefaultAsync(s => s.WorkflowVersionId == instance.WorkflowVersionId && s.Name == "DisciplineReview");

        if (stage == null) return Array.Empty<VdrReviewStatusItemDto>();

        var instanceStages = await _context.WorkflowInstanceStages
            .Where(x => x.WorkflowInstanceId == instance.WorkflowInstanceId && x.WorkflowStageId == stage.WorkflowStageId)
            .ToListAsync();

        if (instanceStages.Count == 0) return Array.Empty<VdrReviewStatusItemDto>();

        var disciplineIds = instanceStages
            .Select(x => ParseDisciplineId(x.AssignedToRole))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var disciplineNames = await _context.Disciplines
            .Where(d => disciplineIds.Contains(d.DisciplineId))
            .ToDictionaryAsync(d => d.DisciplineId, d => d.Name);

        return instanceStages
            .Select(x =>
            {
                var did = ParseDisciplineId(x.AssignedToRole);
                return new VdrReviewStatusItemDto
                {
                    DisciplineId = did ?? 0,
                    DisciplineName = did.HasValue ? disciplineNames.GetValueOrDefault(did.Value, "?") : "?",
                    Status = x.Status
                };
            })
            .ToList();
    }


    // =========================
    // Helpers
    // =========================

    private static int? ParseDisciplineId(string? role)
    {
        if (role == null || !role.StartsWith(EngineeringDocumentWorkflowSeeder.DisciplineRolePrefix)) return null;
        var idPart = role[EngineeringDocumentWorkflowSeeder.DisciplineRolePrefix.Length..];
        return int.TryParse(idPart, out var id) ? id : null;
    }

    private static string GenerateLetterNumber(string documentNo)
        => $"OUT-{documentNo}-{DateTime.Now:yyyyMMdd-HHmmss}";

    private async Task<VDRSubmission?> GetLatestSubmissionAsync(int vdrItemId)
        => await _context.VDRSubmissions
            .Where(s => s.VDRItemId == vdrItemId)
            .OrderByDescending(s => s.RevisionNo)
            .FirstOrDefaultAsync();

    private async Task<VDRItem> GetItemOrThrowAsync(int vdrItemId)
    {
        var item = await _context.VDRItems.FirstOrDefaultAsync(x => x.DocumentId == vdrItemId);
        if (item == null) throw new Exception("VDR Item یافت نشد.");
        // SCRUM-559: no review step on a deactivated item or on one that is on hold
        if (!item.IsActive) throw new Exception("این مدرک VDR غیرفعال شده است - گردش‌کار روی آن ممکن نیست.");
        if (item.IsOnHold) throw new Exception("این مدرک VDR در وضعیت توقف (Hold) است - اول توقف را رفع کنید.");
        return item;
    }

    private async Task<int> GetActiveInstanceIdOrThrowAsync(int vdrItemId)
    {
        var instance = await _runtime.GetInstanceAsync(BusinessObjectType, vdrItemId);

        if (instance == null || instance.Status != "Running")
            throw new Exception("این مدرک Instance فعالی در گردش‌کار ندارد. اول باید برای بازبینی Submit شود.");

        return instance.WorkflowInstanceId;
    }
}
