using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Seed;
using PEMS.WorkflowPlatform.Interfaces;

namespace PEMS.Persistence.Services;

/// <summary>
/// طبق ممیزی Package Procurement Contract Finalization (§D/§F، Phase 2): جایگزین
/// ContractService.ChangeStatusAsync برای گذارهای واقعی گردش‌کار. تنها Draft→UnderReview→
/// Approved→Signed→Effective از این‌جا عبور می‌کند - ContractService.ChangeStatusAsync قدیمی
/// دست‌نخورده ماند (برای سازگاری با فراخوان‌های موجود) ولی ContractController دیگر آن را برای
/// این گذارها صدا نمی‌زند.
/// </summary>
public class ContractWorkflowService : IContractWorkflowService
{
    // طبق §F: لیست رشته‌های الزامی به‌صورت پیکربندی کد است - دقیقاً همون الگوی
    // ProcurementPackageService.StageOrder - نه هاردکد در دیتابیس و نه یک ساختار سازمانی اختراعی.
    public static readonly string[] RequiredDisciplines =
        { "Engineering", "TechnicalInspection", "Legal", "Finance", "Procurement", "HSE" };

    public static string RoleForDiscipline(string discipline) => discipline switch
    {
        "Engineering" => SystemRoles.EngineeringManager,
        "TechnicalInspection" => SystemRoles.QAManager,
        "Legal" => SystemRoles.Legal,
        "Finance" => SystemRoles.Finance,
        "Procurement" => SystemRoles.ContractManager,
        "HSE" => SystemRoles.HSE,
        _ => throw new ArgumentException($"رشته‌ی ناشناخته: {discipline}")
    };

    private readonly PEMSDbContext _context;
    private readonly IWorkflowRuntime _runtime;
    private readonly IAccessControlService _accessControlService;
    private readonly INumberingService _numbering;

    public ContractWorkflowService(
        PEMSDbContext context, IWorkflowRuntime runtime, IAccessControlService accessControlService, INumberingService numbering)
    {
        _context = context;
        _runtime = runtime;
        _accessControlService = accessControlService;
        _numbering = numbering;
    }

    public async Task<(bool Success, string? Error)> SubmitForReviewAsync(
        int contractId, IReadOnlyList<string>? requiredDisciplines, int? actorEmployeeId, IReadOnlyList<string> actorRoles,
        int? editorUserId = null, string? ipAddress = null)
    {
        var contract = await _context.Contracts.FirstOrDefaultAsync(x => x.ContractId == contractId);
        if (contract == null) return (false, "قرارداد یافت نشد.");
        // P0 / بازارسال بعد از رد: Reject نهایی قرارداد را به Draft برمی‌گرداند و ردِ رشته‌ای آن را در «Rejected» نگه می‌دارد؛
        // هر دو باید قابل اصلاح و ارسال مجدد باشند (قبلاً Rejected فقط با Terminate قابل خروج بود).
        if (contract.Status != "Draft" && contract.Status != "Rejected")
            return (false, $"ارسال برای بازبینی فقط از وضعیت Draft یا Rejected مجاز است (وضعیت فعلی: {contract.Status}).");

        var disciplines = (requiredDisciplines != null && requiredDisciplines.Count > 0)
            ? requiredDisciplines.Distinct().ToList()
            : RequiredDisciplines.ToList();

        var participantRoles = disciplines.Select(RoleForDiscipline).Distinct().ToList();

        // P0 / بازارسال بعد از رد: ردیف‌های بازبینی هر رشته یکتاست (Index یکتا روی ContractId+DisciplineRole). در ارسال مجدد ردیف‌های
        // موجود به Pending بازنشانی و دوباره استفاده می‌شوند (به‌جای درج ردیف تکراری که استثنای پایگاه‌داده می‌داد و بعد از شروع
        // Instance گردش‌کار، Instance یتیم باقی می‌گذاشت)؛ رشته‌ای که دیگر لازم نیست IsRequired=false می‌شود.
        var existingReviews = await _context.ContractDisciplineReviews.Where(x => x.ContractId == contractId).ToListAsync();
        foreach (var review in existingReviews)
        {
            if (disciplines.Contains(review.DisciplineRole))
            {
                review.IsRequired = true;
                review.Decision = "Pending";
                review.Comment = null;
                review.ReviewDate = null;
                review.ReviewerId = null;
            }
            else
            {
                review.IsRequired = false;
            }
        }

        foreach (var d in disciplines.Where(d => existingReviews.All(r => r.DisciplineRole != d)))
        {
            _context.ContractDisciplineReviews.Add(new ContractDisciplineReview
            {
                ContractId = contractId,
                DisciplineRole = d,
                IsRequired = true,
                Decision = "Pending"
            });
        }

        await _runtime.StartInstanceAsync(
            ContractWorkflowSeeder.Module, ContractWorkflowSeeder.BusinessObjectType, contractId,
            ContractWorkflowSeeder.Category, contract.ProjectId, actorEmployeeId, participantRoles);

        contract.Status = "UnderReview";
        ContractEditorEvents.Add(_context, contractId, editorUserId, ipAddress, "Submitted the contract for review");
        await _context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> RecordDisciplineReviewAsync(
        int contractId, string disciplineRole, string decision, string? comment, int? actorEmployeeId, IReadOnlyList<string> actorRoles)
    {
        if (decision != "Approve" && decision != "Reject")
            return (false, "تصمیم باید Approve یا Reject باشد.");

        var contract = await _context.Contracts.FirstOrDefaultAsync(x => x.ContractId == contractId);
        if (contract == null) return (false, "قرارداد یافت نشد.");
        if (contract.Status != "UnderReview")
            return (false, $"قرارداد در حال حاضر در مرحله‌ی بازبینی نیست (وضعیت فعلی: {contract.Status}).");

        var expectedRole = RoleForDiscipline(disciplineRole);
        // SystemAdmin عمداً جانشین نقش رشته نیست: بازبینی رشته‌ای تاییدیه‌ی مستقل است و Runtime هم با
        // نقش رشته اجرا می‌شود؛ اگر SystemAdmin این نقش را ندارد باید صریحاً به او تخصیص داده شود.
        if (!actorRoles.Contains(expectedRole))
            return (false, $"شما مجاز به ثبت بازبینی رشته‌ی {disciplineRole} نیستید.");

        var review = await _context.ContractDisciplineReviews
            .FirstOrDefaultAsync(x => x.ContractId == contractId && x.DisciplineRole == disciplineRole);
        if (review == null) return (false, "ردیف بازبینی این رشته یافت نشد.");
        if (review.Decision != "Pending") return (false, "این رشته قبلاً تصمیم خود را ثبت کرده است.");

        var instance = await _runtime.GetInstanceAsync(ContractWorkflowSeeder.BusinessObjectType, contractId);
        if (instance == null) return (false, "Instance گردش‌کار یافت نشد.");

        await _runtime.ExecuteActionAsync(
            instance.WorkflowInstanceId, decision, actorEmployeeId, new[] { expectedRole }, comment, null, null);

        review.Decision = decision == "Approve" ? "Approved" : "Rejected";
        review.Comment = comment;
        review.ReviewDate = DateTime.Now;
        review.ReviewerId = actorEmployeeId;

        var updated = await _runtime.GetInstanceByIdAsync(instance.WorkflowInstanceId);
        if (updated!.Status == "Rejected")
            contract.Status = "Rejected";

        await _context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ApproveAsync(
        int contractId, string? comment, int currentUserId, int? actorEmployeeId, IReadOnlyList<string> actorRoles, string? ipAddress)
    {
        var contract = await _context.Contracts.FirstOrDefaultAsync(x => x.ContractId == contractId);
        if (contract == null) return (false, "قرارداد یافت نشد.");
        if (contract.Status != "UnderReview")
            return (false, $"تایید نهایی فقط از وضعیت UnderReview مجاز است (وضعیت فعلی: {contract.Status}).");

        var anyPending = await _context.ContractDisciplineReviews
            .AnyAsync(x => x.ContractId == contractId && x.IsRequired && x.Decision == "Pending");
        if (anyPending)
            return (false, "همه‌ی رشته‌های الزامی هنوز بازبینی خود را ثبت نکرده‌اند.");

        // منبع مرجع کامل‌بودن بازبینی، همین ContractDisciplineReviews است (بالا چک شد)؛ اینجا
        // فقط باید Instance فعالی وجود داشته باشد تا بشود روی آن ExecuteActionAsync("Approve") زد -
        // به نام دقیق مرحله‌ی جاری در موتور وابسته نیستیم (که برای Fake های تست شکننده می‌شد).
        var instance = await _runtime.GetInstanceAsync(ContractWorkflowSeeder.BusinessObjectType, contractId);
        if (instance == null)
            return (false, "Instance گردش‌کار یافت نشد.");

        // طبق §F: بررسی تفکیک وظایف - کاربری که خودش این قرارداد را ایجاد/ویرایش کرده نمی‌تواند
        // خودش آن را تایید کند. این همان مسیر AuthorizeAsync آگاه-از-کاربر است که پیش از این
        // ممیزی، ContractController اصلاً صدا نمی‌زد.
        var sodDecision = await _accessControlService.AuthorizeAsync(new AccessControlRequest
        {
            UserId = currentUserId,
            ResourceName = "Contracts",
            ActionName = "Approve",
            ResourceId = contractId.ToString(),
            IpAddress = ipAddress
        });
        if (!sodDecision.Allowed && sodDecision.Reason == AccessDecisionReason.DeniedBySoD)
            return (false, "طبق سیاست تفکیک وظایف، کاربری که این قرارداد را ایجاد/ویرایش کرده نمی‌تواند خودش آن را تایید کند.");

        // P0 / Fail-Closed: قبلاً فقط ردِ SoD متوقف می‌کرد و ردِ Scope/نقش/سیاست نادیده گرفته می‌شد. حالا فقط «Allowed صریح با
        // دلیل Allow» عبور می‌کند؛ هر نتیجه‌ی دیگر (و نتیجه‌ی ناسازگار) تایید را بدون استثنای پیش‌بینی‌نشده متوقف می‌کند.
        if (!(sodDecision.Allowed && sodDecision.Reason == AccessDecisionReason.Allow))
            return (false, "شما برای تایید نهایی این قرارداد مجاز نیستید (مجوز، محدوده‌ی دسترسی یا سیاست تفکیک وظایف).");

        // طبق §I قاعده‌ی ۸ (Phase 4.4): ماتریس تایید بر مبنای ارزش/بحرانی‌بودن - فقط وقتی حداقل
        // یک قاعده پیکربندی شده باشد فعال می‌شود (بدون آستانه‌ی از پیش Seed شده). اگر پیکربندی
        // شده ولی هیچ قاعده‌ای با این قرارداد مطابقت نداشت، Fail-Closed: تایید مسدود می‌شود.
        var activeRules = await _context.ContractApprovalAuthorityRules
            .Where(x => x.IsActive)
            .OrderBy(x => x.Priority)
            .ToListAsync();
        if (activeRules.Count > 0)
        {
            var value = contract.CurrentValue ?? contract.ContractValue ?? 0m;
            // طبق Gap ۳: وقتی چند قاعده مطابقت دارند، Priority کوچک‌تر برنده است (اولین مطابقت
            // پس از مرتب‌سازی صعودی) - رفع ابهامی که پیش از این جلسه وجود داشت
            var matchingRule = activeRules.FirstOrDefault(r =>
                (r.MinValue == null || value >= r.MinValue) &&
                (r.MaxValue == null || value <= r.MaxValue) &&
                (r.CriticalityClass == null || r.CriticalityClass == contract.CriticalityClass));

            if (matchingRule == null)
                return (false, "هیچ قاعده‌ی ماتریس تایید پیکربندی‌شده‌ای با ارزش/بحرانی‌بودن این قرارداد مطابقت ندارد - تایید مسدود است تا این مورد پیکربندی شود.");

            // SystemAdmin عمداً مستثنی نیست: بدون نقش تعیین‌شده توسط ماتریس اختیار، تایید مسدود می‌ماند
            if (!actorRoles.Contains(matchingRule.RequiredApproverRole))
                return (false, $"طبق ماتریس تایید پیکربندی‌شده، این قرارداد نیازمند تایید نقش «{matchingRule.RequiredApproverRole}» است.");
        }

        await _runtime.ExecuteActionAsync(instance.WorkflowInstanceId, "Approve", actorEmployeeId, actorRoles, comment, ipAddress, null);

        contract.Status = "Approved";
        contract.ApprovedDate = DateTime.Now;
        contract.ApprovedById = actorEmployeeId;

        await _context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> RejectAsync(
        int contractId, string? comment, int? actorEmployeeId, IReadOnlyList<string> actorRoles)
    {
        var contract = await _context.Contracts.FirstOrDefaultAsync(x => x.ContractId == contractId);
        if (contract == null) return (false, "قرارداد یافت نشد.");
        if (contract.Status != "UnderReview")
            return (false, $"رد فقط از وضعیت UnderReview مجاز است (وضعیت فعلی: {contract.Status}).");

        var instance = await _runtime.GetInstanceAsync(ContractWorkflowSeeder.BusinessObjectType, contractId);
        if (instance == null) return (false, "Instance گردش‌کار یافت نشد.");

        await _runtime.ExecuteActionAsync(instance.WorkflowInstanceId, "Reject", actorEmployeeId, actorRoles, comment, null, null);

        contract.Status = "Draft";

        var reviews = await _context.ContractDisciplineReviews.Where(x => x.ContractId == contractId).ToListAsync();
        foreach (var r in reviews)
        {
            r.Decision = "Pending";
            r.Comment = null;
            r.ReviewDate = null;
            r.ReviewerId = null;
        }

        await _context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> SignAsync(
        int contractId, int currentUserId, int? actorEmployeeId, IReadOnlyList<string> actorRoles, string? ipAddress)
    {
        var contract = await _context.Contracts.FirstOrDefaultAsync(x => x.ContractId == contractId);
        if (contract == null) return (false, "قرارداد یافت نشد.");
        if (contract.Status != "Approved")
            return (false, $"امضا فقط پس از تایید (Approved) مجاز است (وضعیت فعلی: {contract.Status}).");

        contract.Status = "Signed";
        contract.SignedDate = DateTime.Now;
        contract.SignedById = actorEmployeeId;
        contract.CurrentValue ??= contract.ContractValue;

        await _context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> MarkEffectiveAsync(int contractId, IReadOnlyList<string> actorRoles)
    {
        var contract = await _context.Contracts.FirstOrDefaultAsync(x => x.ContractId == contractId);
        if (contract == null) return (false, "قرارداد یافت نشد.");
        if (contract.Status != "Signed")
            return (false, $"اثربخشی فقط پس از امضا (Signed) مجاز است (وضعیت فعلی: {contract.Status}).");

        contract.Status = "Effective";
        contract.EffectiveDate = DateTime.Now;

        await _context.SaveChangesAsync();
        return (true, null);
    }

    // طبق گزارش سخت‌افزاری نهایی (Gap ۱): Effective → Completed. Effective پایانِ گردش‌کار
    // تایید/امضاست، نه پایانِ کاملِ عمر قرارداد - Completed/Terminated از قبل جزو واژگان مجاز
    // Status (کامنت اصلی، پیش از این جلسه) بودند. دقیقاً هم‌الگو با گیت بازرسیِ
    // ProcurementPackageService.CloseoutAsync - نه یک سیستم بازرسی دوم.
    public async Task<(bool Success, string? Error)> CompleteAsync(int contractId, IReadOnlyList<string> actorRoles)
    {
        var contract = await _context.Contracts.FirstOrDefaultAsync(x => x.ContractId == contractId);
        if (contract == null) return (false, "قرارداد یافت نشد.");
        if (contract.Status != "Effective")
            return (false, $"تکمیل قرارداد فقط از وضعیت Effective مجاز است (وضعیت فعلی: {contract.Status}).");

        if (contract.RequiresInspection)
        {
            var linkedItps = await _context.InspectionTestPlans
                .Where(x => x.ContractId == contractId)
                .ToListAsync();

            if (linkedItps.Count == 0)
                return (false, "این قرارداد نیازمند کنترل‌های بازرسی الزامی است ولی هیچ ITP ای برایش تعریف نشده - تکمیل مسدود است.");

            var incomplete = linkedItps.Where(x => x.Status != "Closed").ToList();
            if (incomplete.Count > 0)
                return (false, $"{incomplete.Count} ITP این قرارداد هنوز به وضعیت Closed نرسیده‌اند - تکمیل مسدود است تا همه‌ی بازرسی‌های الزامی کامل شوند.");
        }

        contract.Status = "Completed";
        contract.CompletedDate = DateTime.Now;

        await _context.SaveChangesAsync();
        return (true, null);
    }

    // خروج زودهنگام (فسخ) - از هر وضعیت غیر-نهایی مجاز؛ بدون گیت بازرسی چون این تکمیل عادی نیست
    public async Task<(bool Success, string? Error)> TerminateAsync(int contractId, string? reason, IReadOnlyList<string> actorRoles)
    {
        var contract = await _context.Contracts.FirstOrDefaultAsync(x => x.ContractId == contractId);
        if (contract == null) return (false, "قرارداد یافت نشد.");
        if (contract.Status == "Completed" || contract.Status == "Terminated")
            return (false, $"فسخ از وضعیت نهایی «{contract.Status}» مجاز نیست.");

        contract.Status = "Terminated";
        contract.TerminatedDate = DateTime.Now;
        contract.TerminationReason = reason;

        await _context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error, int? RevisionId)> RequestRevisionAsync(
        int contractId, string changeType, decimal? newValue, string? reason, string? changeDescription, int? actorEmployeeId,
        int? editorUserId = null, string? ipAddress = null)
    {
        var contract = await _context.Contracts.Include(x => x.Revisions).FirstOrDefaultAsync(x => x.ContractId == contractId);
        if (contract == null) return (false, "قرارداد یافت نشد.", null);
        if (contract.Status != "Signed" && contract.Status != "Effective")
            return (false, "تغییر ارزش قرارداد فقط برای قرارداد امضاشده/فعال مجاز است.", null);

        var nextRevisionNo = (contract.Revisions.Count == 0 ? 0 : contract.Revisions.Max(x => x.RevisionNo)) + 1;
        var currentValue = contract.CurrentValue ?? contract.ContractValue;

        var revision = new ContractRevision
        {
            ContractId = contractId,
            RevisionNo = nextRevisionNo,
            ChangeType = changeType,
            PreviousValue = currentValue,
            NewValue = newValue,
            Reason = reason,
            ChangeDescription = changeDescription,
            RequestedById = actorEmployeeId,
            RequestedDate = DateTime.Now,
            Status = "Pending"
        };
        _context.ContractRevisions.Add(revision);
        ContractEditorEvents.Add(_context, contractId, editorUserId, ipAddress, "Requested a contract revision");
        await _context.SaveChangesAsync();

        return (true, null, revision.ContractRevisionId);
    }

    public async Task<(bool Success, string? Error)> ApproveRevisionAsync(
        int contractRevisionId, int? actorEmployeeId, IReadOnlyList<string> actorRoles,
        int? currentUserId = null, string? ipAddress = null)
    {
        var revision = await _context.ContractRevisions
            .Include(x => x.Contract)
            .FirstOrDefaultAsync(x => x.ContractRevisionId == contractRevisionId);
        if (revision == null) return (false, "این تغییر یافت نشد.");
        if (revision.Status != "Pending") return (false, "این تغییر قبلاً تصمیم‌گیری شده است.");

        // P0-10 / D-OPEN-05: تأیید تغییر ارزش هم «تأیید قرارداد» است؛ همان مسیر کاربر-آگاه ApproveAsync (Scope + SoD) و Fail-Closed.
        if (currentUserId.HasValue)
        {
            var decision = await _accessControlService.AuthorizeAsync(new AccessControlRequest
            {
                UserId = currentUserId.Value,
                ResourceName = "Contracts",
                ActionName = "Approve",
                ResourceId = revision.ContractId.ToString(),
                IpAddress = ipAddress
            });
            if (!decision.Allowed && decision.Reason == AccessDecisionReason.DeniedBySoD)
                return (false, "طبق سیاست تفکیک وظایف، کاربری که این قرارداد را ایجاد/ویرایش کرده نمی‌تواند تغییر آن را تایید کند.");
            if (!(decision.Allowed && decision.Reason == AccessDecisionReason.Allow))
                return (false, "شما برای تایید تغییر این قرارداد مجاز نیستید (مجوز، محدوده‌ی دسترسی یا سیاست تفکیک وظایف).");
        }

        revision.Status = "Approved";
        revision.ApprovedById = actorEmployeeId;
        revision.ApprovedDate = DateTime.Now;

        // طبق §D قاعده‌ی ۱۵: ContractValue اصلی هرگز بازنویسی نمی‌شود - فقط CurrentValue به‌روز می‌شود
        revision.Contract!.CurrentValue = revision.NewValue;

        await _context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error, int? NtpId)> IssueNtpAsync(
        int contractId, string? reference, DateTime? effectiveStartDate, int? actorEmployeeId)
    {
        var contract = await _context.Contracts.FirstOrDefaultAsync(x => x.ContractId == contractId);
        if (contract == null) return (false, "قرارداد یافت نشد.", null);

        // طبق §C/§I قاعده‌ی ۵: NTP فقط وقتی قرارداد به وضعیت اجراپذیر رسیده باشد صادر می‌شود
        if (contract.Status != "Signed" && contract.Status != "Effective")
            return (false, $"صدور NTP فقط پس از امضا (Signed) یا اثربخشی (Effective) قرارداد مجاز است (وضعیت فعلی: {contract.Status}).", null);

        var existing = await _context.ContractNTPs.AnyAsync(x => x.ContractId == contractId && x.Status != "Revoked");
        if (existing) return (false, "برای این قرارداد قبلاً یک NTP فعال صادر شده است.", null);

        var ntpNumber = await _numbering.GetNextNumberAsync("NTP");

        var ntp = new ContractNTP
        {
            ContractId = contractId,
            NTPNumber = ntpNumber,
            IssuedById = actorEmployeeId,
            IssuedDate = DateTime.Now,
            EffectiveStartDate = effectiveStartDate,
            Reference = reference,
            Status = "Issued"
        };
        _context.ContractNTPs.Add(ntp);
        await _context.SaveChangesAsync();

        return (true, null, ntp.ContractNTPId);
    }

    public async Task<(bool Success, string? Error, int? MeetingId)> ScheduleKickoffMeetingAsync(
        int contractId, DateTime meetingDate, string? location, string? organizerName, string? agenda)
    {
        var contract = await _context.Contracts.FirstOrDefaultAsync(x => x.ContractId == contractId);
        if (contract == null) return (false, "قرارداد یافت نشد.", null);

        // طبق §I قاعده‌ی ۱۰: جلسه‌ی Kick-off رسمی نباید پیش از رسیدن قرارداد به وضعیت اجراپذیر برگزار شود
        if (contract.Status != "Signed" && contract.Status != "Effective")
            return (false, $"جلسه‌ی Kick-off فقط پس از امضا (Signed) یا اثربخشی (Effective) قرارداد مجاز است (وضعیت فعلی: {contract.Status}).", null);

        var meeting = new Meeting
        {
            Title = $"Kick-off — {contract.ContractNo}",
            MeetingDate = meetingDate,
            Location = location,
            OrganizerName = organizerName,
            ProjectId = contract.ProjectId,
            MeetingType = "KickOff",
            ContractId = contractId,
            Agenda = agenda,
            Status = "Scheduled"
        };
        _context.Meetings.Add(meeting);
        await _context.SaveChangesAsync();

        return (true, null, meeting.MeetingId);
    }
}
