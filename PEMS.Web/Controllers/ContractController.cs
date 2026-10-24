using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Persistence.Services;
using PEMS.Web.Security;
using PEMS.WorkflowPlatform.Interfaces;

namespace PEMS.Web.Controllers;

public class ContractController : Controller
{
    private readonly IContractService _service;
    private readonly IContractWorkflowService _workflowService;
    private readonly IWorkflowRuntime _runtime;
    private readonly IAccessControlService _accessControlService;
    private readonly IPermissionService _permissionService;
    private readonly ICurrentUserService _currentUser;
    private readonly PEMSDbContext _context;

    public ContractController(
        IContractService service,
        IContractWorkflowService workflowService,
        IWorkflowRuntime runtime,
        IAccessControlService accessControlService,
        IPermissionService permissionService,
        ICurrentUserService currentUser,
        PEMSDbContext context)
    {
        _service = service;
        _workflowService = workflowService;
        _runtime = runtime;
        _accessControlService = accessControlService;
        _permissionService = permissionService;
        _currentUser = currentUser;
        _context = context;
    }

    // P0-09 (D-OPEN-02، تصمیم مالک): صفحات فقط-خواندنی هم مجوز View می‌خواهند.
    private Task<bool> HasAsync(string action) => _permissionService.HasPermissionAsync(_currentUser.Roles, "Contracts", action);

    // طبق «PEMS — Global Grid Hardening» P1: این Grid فیلتری نداشت؛ فقط صفحه‌بندی واقعی اضافه شد.
    public async Task<IActionResult> Index(int page = 1, int? pageSize = null)
    {
        if (!await HasAsync("View")) return Forbid();

        ViewData["Title"] = "Contracts";
        ViewData["Section"] = "Contract Management";
        ViewData["HelpKey"] = "Contract.Index";

        var normalizedPageSize = DataGridPageSize.Normalize(pageSize);
        var result = await _service.GetPagedAsync(page, normalizedPageSize);

        ViewBag.Projects = await _context.Projects.Where(x => x.IsActive)
            .Select(x => new { x.ProjectId, x.ProjectName }).ToListAsync();

        ViewBag.Vendors = await _context.Vendors.Where(x => x.IsActive)
            .Select(x => new { x.VendorId, x.Name }).ToListAsync();

        return View(result);
    }

    public async Task<IActionResult> Details(int id)
    {
        if (!await HasAsync("View")) return Forbid();

        ViewData["Title"] = "Contract Details";
        ViewData["Section"] = "Contract Management";

        var contract = await _service.GetByIdAsync(id);
        if (contract == null) return NotFound();

        ViewBag.DisciplineReviews = await _context.ContractDisciplineReviews
            .Include(x => x.Reviewer)
            .Where(x => x.ContractId == id)
            .ToListAsync();

        ViewBag.Revisions = await _context.ContractRevisions
            .Include(x => x.RequestedBy)
            .Include(x => x.ApprovedBy)
            .Where(x => x.ContractId == id)
            .OrderByDescending(x => x.RevisionNo)
            .ToListAsync();

        ViewBag.ContractDocuments = await _context.ContractDocuments
            .Where(x => x.ContractId == id)
            .OrderByDescending(x => x.CreatedDate)
            .ToListAsync();

        var instance = await _runtime.GetInstanceAsync(Persistence.Seed.ContractWorkflowSeeder.BusinessObjectType, id);
        ViewBag.WorkflowInstance = instance;
        ViewBag.WorkflowHistory = instance != null ? await _runtime.GetHistoryAsync(instance.WorkflowInstanceId) : null;

        ViewBag.RequiredDisciplines = ContractWorkflowService.RequiredDisciplines;

        // طبق ممیزی نهایی Ownership/Traceability (§2/§6): Contract هیچ FK مستقیمی به سمت
        // ProcurementPackage/Tender ندارد - Tender.ContractId تنها منبع حقیقت این رابطه است
        // (طبق Phase 3). این‌جا فقط همان رابطه‌ی موجود را در جهت معکوس می‌خوانیم تا در UI دیده
        // شود - نه یک FK تازه‌ی افزونه که با Tender.ContractId ناسازگار می‌شد.
        ViewBag.OriginatingTender = await _context.Tenders
            .Include(x => x.ProcurementPackage)
            .Include(x => x.AwardedBid)
            .FirstOrDefaultAsync(x => x.ContractId == id);

        ViewBag.ContractNTPs = await _context.ContractNTPs
            .Where(x => x.ContractId == id)
            .OrderByDescending(x => x.IssuedDate)
            .ToListAsync();

        ViewBag.KickoffMeetings = await _context.Meetings
            .Where(x => x.ContractId == id && x.MeetingType == "KickOff")
            .OrderByDescending(x => x.MeetingDate)
            .ToListAsync();

        ViewBag.LinkedItps = await _context.InspectionTestPlans
            .Where(x => x.ContractId == id)
            .ToListAsync();

        return View(contract);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        if (!await HasAsync("Create")) return Forbid();

        ViewData["Title"] = "New Contract";
        ViewData["Section"] = "Contract Management";

        ViewBag.Projects = await _context.Projects.Where(x => x.IsActive)
            .Select(x => new { x.ProjectId, x.ProjectName }).ToListAsync();

        ViewBag.Vendors = await _context.Vendors.Where(x => x.IsActive)
            .Select(x => new { x.VendorId, x.Name }).ToListAsync();

        return View();
    }

    // طبق §J (Test Coverage Gap): این کنترلر پیش از این هیچ بررسی مجوزی روی Create نداشت
    // (اصلاً [RequirePermission] هم نبود). بررسی این‌جا به‌صورت درون‌خطی است - دقیقاً همون الگوی
    // ProcurementPackageController - چون [RequirePermission] یک IAsyncAuthorizationFilter است و
    // فقط داخل پایپ‌لاین واقعی MVC اجرا می‌شود، نه وقتی اکشن مستقیماً در تست صدا زده می‌شود.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string contractNo, int projectId, int vendorId, string? scope,
        decimal? value, DateTime? startDate, DateTime? endDate, bool requiresInspection = false)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Contracts", "Create"))
            return Forbid();

        var ip = GetClientIp();

        // P0.4 / H-1: بدون هویت احرازشده نه ایجادکننده قابل‌انتساب است نه SoD قابل‌ارزیابی؛ قرارداد ساخته نمی‌شود (قبلاً رویداد بی‌صدا نوشته نمی‌شد).
        if (!_currentUser.UserId.HasValue)
        {
            try { await _accessControlService.RecordDenialAsync(null, "Contracts", "Create", null, ip, AccessDecisionReason.DeniedByPolicy, "شناسه‌ی کاربر احرازهویت‌شده در دسترس نیست."); }
            catch (Exception) { /* شکست ثبت ممیزی هرگز به Allow تبدیل نمی‌شود */ }
            return Forbid();
        }

        // P0.4 / H-1: مجوز آگاه-از-کاربر (Fail-Closed، پیش از هر درج)، تراکنش واحد قرارداد + AuditLog + رویداد «Contracts/Create/Allow» برای همین کاربر، و قفل
        // شماره‌ی قرارداد - همه داخل سرویس (همان قفل و تراکنش مسیر مناقصه). قبلاً رویداد بعد از Commit با AuthorizeAsync نوشته می‌شد، نتیجه‌اش نادیده گرفته
        // می‌شد و استثنای آن قرارداد را بی‌رویداد (SoD کور) می‌گذاشت.
        var result = await _service.CreateWithCreatorEventAsync(contractNo, projectId, vendorId, scope, value, startDate, endDate,
            requiresInspection, _currentUser.UserId.Value, ip);

        if (result.AuthorizationDenied)
            return Forbid();

        if (result.AuthorizationIndeterminate)
        {
            TempData["Error"] = result.Error;
            return RedirectToAction(nameof(Create));
        }

        if (result.OutcomeUnknown)
        {
            // Commit نامطمئن بود و بررسی بعدی هم وضعیت را روشن نکرد: هرگز «ساخته نشد» نمی‌گوییم
            TempData["Error"] = result.Error;
            return RedirectToAction(nameof(Index));
        }

        if (!result.Success)
        {
            // P0: شماره‌ی خالی/تکراری، مبلغ منفی، بازه‌ی تاریخ نامعتبر یا شکست ذخیره (Rollback کامل) - ردِ قابل‌فهم به‌جای ثبت رکورد نامعتبر
            TempData["Error"] = result.Error;
            return RedirectToAction(nameof(Create));
        }

        if (result.CreatorEvent == ContractCreatorEventStatus.NotRecorded)
            TempData["Error"] = "قرارداد ساخته شد، اما رویداد «ایجادکننده‌ی قرارداد» ثبت نشد (فقط در Provider بدون تراکنش ممکن است)؛ برای ترمیم با مدیر سیستم تماس بگیرید.";
        else if (result.RecoveredAfterUncertainCommit)
            TempData["Message"] = "Contract created successfully (the commit result was uncertain and was confirmed by re-reading the database; no duplicate was created)";
        else
            TempData["Message"] = "Contract created successfully";

        return RedirectToAction(nameof(Details), new { id = result.ContractId });
    }

    // طبق §D (Phase 2.2): جایگزین رشته‌ی آزاد قدیمی - فقط همین چند گذار مشخص و اعتبارسنجی‌شده
    // مجازند؛ هر ورودی دیگر رد می‌شود. ChangeStatusAsync قدیمی (رشته‌ی آزاد) دیگر از این‌جا صدا
    // زده نمی‌شود.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeStatus(int contractId, string status)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Contracts", "Edit"))
            return Forbid();

        (bool Success, string? Error) result = status switch
        {
            "UnderReview" => await SubmitForReviewInternalAsync(contractId),
            _ => (false, $"گذار مستقیم به وضعیت «{status}» مجاز نیست. از اکشن‌های Approve/Reject/Sign/MarkEffective استفاده کنید.")
        };

        if (!result.Success)
            TempData["Error"] = result.Error;
        else
            TempData["Message"] = "وضعیت قرارداد به‌روزرسانی شد.";

        return RedirectToAction(nameof(Details), new { id = contractId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitForReview(int contractId)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Contracts", "Edit"))
            return Forbid();

        var result = await SubmitForReviewInternalAsync(contractId);
        if (!result.Success) TempData["Error"] = result.Error;
        else TempData["Message"] = "قرارداد برای بازبینی چند-رشته‌ای ارسال شد.";
        return RedirectToAction(nameof(Details), new { id = contractId });
    }

    private const string WorkflowDenialMessage = "نقش شما در این مرحله‌ی گردش‌کار قرارداد مجاز نیست.";

    private async Task<(bool Success, string? Error)> SubmitForReviewInternalAsync(int contractId)
    {
        var employeeId = await GetCurrentEmployeeIdAsync();
        // P0-10: ارسال‌کننده «ویرایشگر» قرارداد است و نباید همان را تأیید کند (رویداد Contracts.Edit در همان ذخیره)
        return await _workflowService.SubmitForReviewAsync(contractId, null, employeeId, _currentUser.Roles,
            _currentUser.UserId, HttpContext?.Connection.RemoteIpAddress?.ToString());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RecordDisciplineReview(int contractId, string disciplineRole, string decision, string? comment)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Contracts", "Edit"))
            return Forbid();

        var employeeId = await GetCurrentEmployeeIdAsync();
        (bool Success, string? Error) result;
        try
        {
            result = await _workflowService.RecordDisciplineReviewAsync(
                contractId, disciplineRole, decision, comment, employeeId, _currentUser.Roles);
        }
        catch (UnauthorizedAccessException)
        {
            // P0: ردِ نقش/مرحله در موتور گردش‌کار باید Fail-Closed و با پیام قابل‌فهم باشد، نه HTTP 500
            result = (false, WorkflowDenialMessage);
        }

        if (!result.Success) TempData["Error"] = result.Error;
        else TempData["Message"] = "تصمیم بازبینی ثبت شد.";

        return RedirectToAction(nameof(Details), new { id = contractId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int contractId, string? comment)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Contracts", "Approve"))
            return Forbid();
        if (!_currentUser.UserId.HasValue) return Forbid();

        var employeeId = await GetCurrentEmployeeIdAsync();
        (bool Success, string? Error) result;
        try
        {
            result = await _workflowService.ApproveAsync(
                contractId, comment, _currentUser.UserId.Value, employeeId, _currentUser.Roles, GetClientIp());
        }
        catch (UnauthorizedAccessException)
        {
            // P0: نقش کاربر مجاز به مرحله‌ی «تایید نهایی» گردش‌کار نیست (مثلاً SystemAdmin بدون نقش مرحله) - رد با پیام، بدون 500
            result = (false, WorkflowDenialMessage);
        }

        if (!result.Success) TempData["Error"] = result.Error;
        else TempData["Message"] = "قرارداد تایید شد.";

        return RedirectToAction(nameof(Details), new { id = contractId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int contractId, string? comment)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Contracts", "Approve"))
            return Forbid();

        var employeeId = await GetCurrentEmployeeIdAsync();
        (bool Success, string? Error) result;
        try
        {
            result = await _workflowService.RejectAsync(contractId, comment, employeeId, _currentUser.Roles);
        }
        catch (UnauthorizedAccessException)
        {
            result = (false, WorkflowDenialMessage);
        }

        if (!result.Success) TempData["Error"] = result.Error;
        else TempData["Message"] = "قرارداد رد شد و به Draft بازگشت.";

        return RedirectToAction(nameof(Details), new { id = contractId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Sign(int contractId)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Contracts", "Approve"))
            return Forbid();
        if (!_currentUser.UserId.HasValue) return Forbid();

        var employeeId = await GetCurrentEmployeeIdAsync();
        var result = await _workflowService.SignAsync(
            contractId, _currentUser.UserId.Value, employeeId, _currentUser.Roles, GetClientIp());

        if (!result.Success) TempData["Error"] = result.Error;
        else TempData["Message"] = "قرارداد امضا شد.";

        return RedirectToAction(nameof(Details), new { id = contractId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkEffective(int contractId)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Contracts", "Approve"))
            return Forbid();

        var result = await _workflowService.MarkEffectiveAsync(contractId, _currentUser.Roles);

        if (!result.Success) TempData["Error"] = result.Error;
        else TempData["Message"] = "قرارداد به وضعیت Effective رسید.";

        return RedirectToAction(nameof(Details), new { id = contractId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(int contractId)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Contracts", "Approve"))
            return Forbid();

        var result = await _workflowService.CompleteAsync(contractId, _currentUser.Roles);

        if (!result.Success) TempData["Error"] = result.Error;
        else TempData["Message"] = "قرارداد تکمیل شد.";

        return RedirectToAction(nameof(Details), new { id = contractId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Terminate(int contractId, string? reason)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Contracts", "Approve"))
            return Forbid();

        var result = await _workflowService.TerminateAsync(contractId, reason, _currentUser.Roles);

        if (!result.Success) TempData["Error"] = result.Error;
        else TempData["Message"] = "قرارداد فسخ شد.";

        return RedirectToAction(nameof(Details), new { id = contractId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestRevision(int contractId, string changeType, decimal? newValue, string? reason, string? changeDescription)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Contracts", "Edit"))
            return Forbid();

        var employeeId = await GetCurrentEmployeeIdAsync();
        var result = await _workflowService.RequestRevisionAsync(contractId, changeType, newValue, reason, changeDescription, employeeId,
            _currentUser.UserId, HttpContext?.Connection.RemoteIpAddress?.ToString());

        if (!result.Success) TempData["Error"] = result.Error;
        else TempData["Message"] = "درخواست تغییر ارزش قرارداد ثبت شد (در انتظار تایید).";

        return RedirectToAction(nameof(Details), new { id = contractId });
    }

    // طبق §D/§G (Phase 4.6): ContractDocument از قبل به‌عنوان یک نوع مدرک اسکلتی وجود داشت
    // (ToTable/EF config کامل) ولی هیچ Controller/Service ای واقعاً یک ردیف در آن نمی‌ساخت -
    // این اکشن همان اتصال گمشده است، نه یک معماری موازی جدید.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddDocument(int contractId, string documentNo, string title, string documentType, string? clauseReference)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Contracts", "Edit"))
            return Forbid();

        var contract = await _context.Contracts.FirstOrDefaultAsync(x => x.ContractId == contractId);
        if (contract == null) return NotFound();

        _context.ContractDocuments.Add(new PEMS.Domain.Entities.ContractDocument
        {
            DocumentKind = "Contract",
            DocumentNo = documentNo,
            Title = title,
            DocumentType = documentType,
            ProjectId = contract.ProjectId,
            ContractId = contractId,
            ClauseReference = clauseReference,
            Status = "Draft"
        });
        // P0-10: افزودن سند ویرایش محتوای قرارداد است (رویداد Contracts.Edit با همان ذخیره)
        ContractEditorEvents.Add(_context, contractId, _currentUser.UserId, HttpContext?.Connection.RemoteIpAddress?.ToString(), "Added a contract document");
        await _context.SaveChangesAsync();

        TempData["Message"] = "سند قرارداد ثبت شد.";
        return RedirectToAction(nameof(Details), new { id = contractId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveRevision(int contractId, int contractRevisionId)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Contracts", "Approve"))
            return Forbid();

        // P0-10: بدون شناسه‌ی کاربر، SoD قابل‌ارزیابی نیست (Fail-Closed، همان قاعده‌ی ProtectedActionGuard)
        if (!_currentUser.UserId.HasValue)
            return Forbid();

        var employeeId = await GetCurrentEmployeeIdAsync();
        var result = await _workflowService.ApproveRevisionAsync(contractRevisionId, employeeId, _currentUser.Roles,
            _currentUser.UserId, HttpContext?.Connection.RemoteIpAddress?.ToString());

        if (!result.Success) TempData["Error"] = result.Error;
        else TempData["Message"] = "تغییر ارزش قرارداد تایید شد.";

        return RedirectToAction(nameof(Details), new { id = contractId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> IssueNtp(int contractId, string? reference, DateTime? effectiveStartDate)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Contracts", "Approve"))
            return Forbid();

        var employeeId = await GetCurrentEmployeeIdAsync();
        var result = await _workflowService.IssueNtpAsync(contractId, reference, effectiveStartDate, employeeId);

        if (!result.Success) TempData["Error"] = result.Error;
        else TempData["Message"] = "NTP صادر شد.";

        return RedirectToAction(nameof(Details), new { id = contractId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ScheduleKickoff(int contractId, DateTime meetingDate, string? location, string? agenda)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Contracts", "Edit"))
            return Forbid();

        var result = await _workflowService.ScheduleKickoffMeetingAsync(contractId, meetingDate, location, _currentUser.FullName, agenda);

        if (!result.Success) TempData["Error"] = result.Error;
        else TempData["Message"] = "جلسه‌ی Kick-off برنامه‌ریزی شد.";

        return RedirectToAction(nameof(Details), new { id = contractId });
    }

    private async Task<int?> GetCurrentEmployeeIdAsync()
    {
        if (!_currentUser.UserId.HasValue) return null;

        return await _context.Users
            .Where(u => u.Id == _currentUser.UserId.Value)
            .Select(u => u.EmployeeId)
            .FirstOrDefaultAsync();
    }

    private string? GetClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
}
