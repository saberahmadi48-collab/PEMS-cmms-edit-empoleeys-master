using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Web.Controllers;


public class EngineeringDocumentsController : Controller
{
    private readonly IEngineeringDocumentService _service;
    private readonly PEMSDbContext _context;
    private readonly IDocumentWorkflowService _workflowService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PEMS.Application.Interfaces.ILetterService _letterService;
    private readonly PEMS.Application.Interfaces.IDocumentNumberingService _numberingService;
    private readonly PEMS.Application.Interfaces.IPermissionService _permissionService;
    private readonly PEMS.Web.Security.ICurrentUserService _currentUser;
    private readonly PEMS.Application.Interfaces.IDocumentCommentService _commentService;
    private readonly PEMS.Application.Interfaces.IEngineeringCopilotService _copilotService;
    private readonly PEMS.Application.Interfaces.IDocumentHoldService _holdService;
    private readonly PEMS.Application.Interfaces.IAccessControlService _accessControlService;
    private readonly PEMS.Application.Interfaces.IFileStorageService _fileStorage;
    private readonly PEMS.Application.Interfaces.ITransmittalService _transmittalService;
    private readonly ILogger<EngineeringDocumentsController> _logger;


    public EngineeringDocumentsController(
        IEngineeringDocumentService service,
        PEMSDbContext context,
        IDocumentWorkflowService workflowService,
        IServiceScopeFactory scopeFactory,
        PEMS.Application.Interfaces.ILetterService letterService,
        PEMS.Application.Interfaces.IDocumentNumberingService numberingService,
        PEMS.Application.Interfaces.IPermissionService permissionService,
        PEMS.Web.Security.ICurrentUserService currentUser,
        PEMS.Application.Interfaces.IDocumentCommentService commentService,
        PEMS.Application.Interfaces.IEngineeringCopilotService copilotService,
        PEMS.Application.Interfaces.IDocumentHoldService holdService,
        PEMS.Application.Interfaces.IAccessControlService accessControlService,
        PEMS.Application.Interfaces.IFileStorageService fileStorage,
        PEMS.Application.Interfaces.ITransmittalService transmittalService,
        ILogger<EngineeringDocumentsController> logger)
    {
        _service = service;
        _context = context;
        _workflowService = workflowService;
        _scopeFactory = scopeFactory;
        _letterService = letterService;
        _numberingService = numberingService;
        _permissionService = permissionService;
        _currentUser = currentUser;
        _commentService = commentService;
        _copilotService = copilotService;
        _holdService = holdService;
        _accessControlService = accessControlService;
        _fileStorage = fileStorage;
        _transmittalService = transmittalService;
        _logger = logger;
    }



    // =========================
    // LIST
    // =========================

    public async Task<IActionResult> Index(PEMS.Application.DTOs.EngineeringDocumentListFilter filter)
    {
        ViewData["Title"] = "Engineering Documents";
        ViewData["Section"] = "Engineering Management";
        ViewData["HelpKey"] = "EngineeringDocuments.Index";

        var (isRestricted, myDisciplineId) = await ResolveListScopeAsync();

        // طبق سند Global Data Grid Standard بند ۴: فقط ۲۰/۵۰/۱۰۰ پذیرفته می‌شود
        filter.PageSize = PEMS.Application.Common.DataGridPageSize.Normalize(filter.PageSize);

        var result = await _service.GetPagedAsync(filter, isRestricted, myDisciplineId);
        var summary = await _service.GetSummaryCountsAsync(isRestricted, myDisciplineId);

        ViewBag.Filter = filter;
        ViewBag.Summary = summary;
        ViewBag.Disciplines = await _context.Disciplines.Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();

        return View(result);
    }

    // محدودیت دیسیپلین فهرست - یک‌جا برای Index و Export تا Export هرگز بیش از آنچه کاربر در فهرست می‌بیند خارج نکند
    private async Task<(bool IsRestricted, int? MyDisciplineId)> ResolveListScopeAsync()
    {
        // طبق Phase E-03 (رفع P1 ممیزی Performance): دقیقاً همان دو سیگنال جدایی که
        // ScopeToOwnDisciplineIfRestrictedAsync امروز درون‌حافظه‌ای اعمال می‌کند - این‌جا
        // زودتر محاسبه و به لایه‌ی SQL پاس داده می‌شود. کارمند بدون دیسیپلین همچنان محدود به
        // مدارک بدون‌دیسیپلین می‌ماند (نه "نامحدود") - طبق رفتار دقیق فعلی.
        var canViewAllDisciplines = (await _accessControlService.AuthorizeForRolesAsync(
            _currentUser.Roles, "EngineeringDocuments", "ViewAllDisciplines")).Allowed;
        var isRestricted = _currentUser.UserId.HasValue && !canViewAllDisciplines;
        int? myDisciplineId = null;
        if (isRestricted)
        {
            var employeeId = await GetCurrentEmployeeIdAsync();
            if (!employeeId.HasValue)
            {
                isRestricted = false;
            }
            else
            {
                var employee = await _context.Employees.FirstOrDefaultAsync(e => e.EmployeeId == employeeId.Value);
                if (employee == null) isRestricted = false;
                else myDisciplineId = employee.DisciplineId;
            }
        }

        return (isRestricted, myDisciplineId);
    }

    // سقف ردیف Export - همان مقدار Export رجیستر (DocumentRegisterController.MaxExportRows)
    private const int MaxListExportRows = 20000;

    /// <summary>
    /// Export فهرست فعلی Index به Excel: همان EngineeringDocumentListFilter (Search/Discipline/Status/Area/
    /// Project/MinRevisionCount) و همان محدودیت دیسیپلین Index، بدون صفحه‌بندی. کاربر دقیقاً همان ردیف‌هایی را
    /// می‌گیرد که با این فیلتر در فهرست می‌بیند (نه کل پایگاه‌داده)؛ ستون‌ها = ستون‌های فهرست.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Export(PEMS.Application.DTOs.EngineeringDocumentListFilter filter)
    {
        var (isRestricted, myDisciplineId) = await ResolveListScopeAsync();

        var (items, total) = await _service.GetAllMatchingAsync(filter, isRestricted, myDisciplineId, MaxListExportRows);
        if (total > MaxListExportRows)
        {
            TempData["Error"] = $"Export limited to {MaxListExportRows:N0} rows; the current filter matches {total:N0}. Narrow the filter and try again.";
            return RedirectToAction(nameof(Index), new
            {
                filter.Search, filter.ProjectId, filter.DisciplineId, filter.Status, filter.Area, filter.MinRevisionCount
            });
        }

        var bytes = PEMS.Web.Helpers.EngineeringDocumentListExport.Build(items);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"EngineeringDocuments_{DateTime.Now:yyyyMMdd}.xlsx");
    }

    // طبق دانش سیستم‌های EDMS معتبر (AVEVA Engineering / Aconex / Bentley ProjectWise):
    // محدودیت "فقط دیسیپلین خودم" یک قانون کورکورانه برای همه نیست - فقط روی نقش‌های اجرایی
    // (Discipline Engineer) اعمال می‌شود. نقش‌های نظارتی/تاییدکننده (Manager, Supervisor,
    // Document Controller, System Admin) باید همچنان کل MDR را ببینند، چون کارشان دقیقاً
    // بررسی کامل بودن مدارک بین‌رشته‌ای و تایید گردش‌کار است - محدودکردن آن‌ها هم، کارشان را
    // غیرممکن می‌کند. مدارک بدون دیسیپلین مشخص (Unassigned) هم از دید هیچ‌کس مخفی نمی‌شوند.
    private async Task<int?> GetCurrentEmployeeIdAsync()
    {
        if (!_currentUser.UserId.HasValue) return null;

        return await _context.Users
            .Where(u => u.Id == _currentUser.UserId.Value)
            .Select(u => u.EmployeeId)
            .FirstOrDefaultAsync();
    }

    // طبق فاز ۱ ممیزی گردش‌کار: برای Audit Trail واقعی (IP/Browser) طبق سند
    private string? GetClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();

    private string? GetUserAgent() => Request.Headers["User-Agent"].ToString();

    // طبق Phase E-01: تشخیص نقض ایندکس Unique شماره‌ی مدرک (DocumentNo) - همون منطق
    // NumberingService.IsUniqueConstraintViolation
    private static bool IsDuplicateDocumentNo(Microsoft.EntityFrameworkCore.DbUpdateException ex)
    {
        return ex.InnerException is Microsoft.Data.SqlClient.SqlException sqlEx
            && (sqlEx.Number == 2601 || sqlEx.Number == 2627);
    }

    private async Task<int?> GetCurrentEmployeeDisciplineIdAsync()
    {
        var employeeId = await GetCurrentEmployeeIdAsync();
        if (!employeeId.HasValue) return null;

        return await _context.Employees
            .Where(e => e.EmployeeId == employeeId.Value)
            .Select(e => (int?)e.DisciplineId)
            .FirstOrDefaultAsync();
    }

    // طبق بررسی چرخه‌ی مدرک: دیدن مدارک و اجازه‌ی تایید دو چیز جدا هستند. مدیر مهندسی و ادمین
    // سیستم می‌توانند هر دیسیپلینی را تایید کنند؛ سرپرست فقط دیسیپلین خودش را (نه یک تاییدکننده‌ی
    // عمومی برای همه‌چیز) - این دقیقاً همان چیزی است که در پروژه‌های بزرگ EPC واقعی هم رعایت می‌شود.
    // طبق «PEMS Authorization Architecture Completion» فاز ۳: جایگزین داده‌محور سه نقش ثابت قبلی
    // (SystemAdmin/EngineeringManager/EngineeringSupervisor) - رفتار عیناً یکسان، اثبات‌شده با
    // این‌که RolePermission همین دو نقش غیر-Admin را از قبل با CanApprove=true داشت (SystemAdmin
    // از طریق Bypass عمومی AccessControlService عبور می‌کند، دقیقاً مثل قبل). «هر دیسیپلینی»
    // اکنون یک مجوز جدا (EngineeringDocuments.ApproveAllDisciplines، فقط برای Manager) است - نه
    // بازاستفاده از ViewAllDisciplines (که به سرپرست هم داده شده و دامنه‌ی تایید را غلط می‌گستراند).
    private async Task<bool> CanApproveDocumentDisciplineAsync(int? documentDisciplineId)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "EngineeringDocuments", "Approve"))
            return false;

        var unrestricted = await _accessControlService.AuthorizeForRolesAsync(_currentUser.Roles, "EngineeringDocuments", "ApproveAllDisciplines");
        if (unrestricted.Allowed) return true;

        if (!documentDisciplineId.HasValue) return true; // مدرک بدون دیسیپلین مشخص، محدودیتی ندارد

        var myEmployeeId = await GetCurrentEmployeeIdAsync();
        if (!myEmployeeId.HasValue) return false;

        var myEmployee = await _context.Employees.FirstOrDefaultAsync(e => e.EmployeeId == myEmployeeId.Value);
        return myEmployee != null && myEmployee.DisciplineId == documentDisciplineId.Value;
    }

    // طبق سند RBAC-ABAC Authorization Architecture (فاز ۳ - جایگزینی مکانیزم D): آرایه‌ی ثابت
    // UnrestrictedRoles قبلی جایگزین شد با مجوز داده‌محور «EngineeringDocuments.ViewAllDisciplines»
    // (AccessControlSeeder.SeedPhase3PermissionsAsync) - رفتار عیناً یکسان (همان ۴ نقش،
    // اثبات‌شده با AccessControlPhase3ParityTests)، اما دیگر در سه فایل کپی نشده.
    private async Task<List<PEMS.Application.DTOs.EngineeringDocumentDto>> ScopeToOwnDisciplineIfRestrictedAsync(
        List<PEMS.Application.DTOs.EngineeringDocumentDto> documents)
    {
        var canViewAllDisciplines = (await _accessControlService.AuthorizeForRolesAsync(
            _currentUser.Roles, "EngineeringDocuments", "ViewAllDisciplines")).Allowed;
        if (canViewAllDisciplines)
            return documents;

        if (!_currentUser.UserId.HasValue)
            return documents;

        var myEmployeeId = await _context.Users
            .Where(u => u.Id == _currentUser.UserId.Value)
            .Select(u => u.EmployeeId)
            .FirstOrDefaultAsync();

        if (!myEmployeeId.HasValue)
            return documents;

        var myEmployee = await _context.Employees.FirstOrDefaultAsync(e => e.EmployeeId == myEmployeeId.Value);

        if (myEmployee == null)
            return documents;

        return documents
            .Where(x => x.DisciplineId == myEmployee.DisciplineId || x.DisciplineId == 0)
            .ToList();
    }





    // =========================
    // CREATE GET
    // =========================

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        LoadDropdowns();

        ViewBag.CanManualNumber = await _permissionService.HasPermissionAsync(_currentUser.Roles, "EngineeringDocuments", "ManualNumber");

        return View();
    }





    // =========================
    // CREATE POST
    // =========================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        EngineeringDocument document, bool useManualNumber = false)
    {
        // طبق «PEMS EDMS Phase 4 Audit» یافته P4-01: پیش از این هیچ بررسی مجوز روی ایجاد مدرک
        // وجود نداشت - هر کاربر احرازهویت‌شده (با هر نقشی) می‌توانست مدرک بسازد. دقیقاً همان
        // الگوی Freeze/Unfreeze/Hold همین کنترلر.
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "EngineeringDocuments", "Create"))
            return Forbid();

        if (!ModelState.IsValid)
        {
            LoadDropdowns();
            return View(document);
        }



        document.Status = "Draft";

        document.RevisionNo = "00";

        // طبق بررسی چرخه‌ی مدرک: کسی که مدرک را ایجاد می‌کند، همان "Prepared By" واقعی است -
        // نه یک فیلد متنی که بعداً دستی پر شود
        var creatorEmployeeId = await GetCurrentEmployeeIdAsync();
        document.PreparedById = creatorEmployeeId;

        if (creatorEmployeeId.HasValue)
        {
            var creator = await _context.Employees.FirstOrDefaultAsync(e => e.EmployeeId == creatorEmployeeId.Value);
            if (creator != null) document.PreparedBy = creator.FirstName + " " + creator.LastName;
        }

        // شماره‌گذاری خودکار پیش‌فرض؛ فقط اگر کاربر مجوز CanManualNumber داشته باشد و صریحاً
        // شماره‌ی دستی وارد کرده باشد، همان حفظ می‌شود - طبق بند ۱۰ بازبینی معماری
        var canManualNumber = await _permissionService.HasPermissionAsync(_currentUser.Roles, "EngineeringDocuments", "ManualNumber");

        if (!useManualNumber || !canManualNumber || string.IsNullOrWhiteSpace(document.DocumentNo))
        {
            document.DocumentNo = await _numberingService.GenerateNextNumberAsync(
                document.ProjectId, document.DisciplineId, document.DocumentType);
        }

        // ایجاد مدرک اصلی
        // طبق Phase E-01 (رفع P0 ممیزی): شماره‌ی خودکار حالا اتمیک است، ولی شماره‌ی دستی هنوز
        // می‌تواند با یک مدرک دیگر تصادفی یکی باشد - ایندکس Unique سطح دیتابیس این را رد می‌کند؛
        // این‌جا فقط پیام دوستانه به‌جای خطای خام SQL نشان داده می‌شود.
        try
        {
            await _service.AddAsync(document);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex) when (IsDuplicateDocumentNo(ex))
        {
            LoadDropdowns();
            ModelState.AddModelError(nameof(document.DocumentNo), "این شماره‌ی مدرک قبلاً استفاده شده است.");
            return View(document);
        }





        // =========================
        // Initial Revision
        // =========================


        var revision = new DocumentRevision
        {
            DocumentId = document.DocumentId,

            RevisionNo = "00",

            Status = "Draft",

            Comment = "Initial Revision",

            RevisionDate = DateTime.Now,

            IsActive = true
        };


        await _context.DocumentRevisions
            .AddAsync(revision);






        // =========================
        // Initial Workflow
        // =========================


        var workflow = new DocumentWorkflow
        {
            DocumentId = document.DocumentId,

            FromStatus = "",

            ToStatus = "Created",

            ActionType = "Create",

            Comment = "Document Created",

            ActionDate = DateTime.Now,

            IsActive = true
        };


        await _context.DocumentWorkflows
            .AddAsync(workflow);




        await _context.SaveChangesAsync();

        // طبق یافته‌ی P4-01: ثبت این اقدام در تاریخچه‌ی مجوزدهی - همان رکوردی که بعداً هنگام
        // Approve توسط AuthorizeAsync برای تشخیص خودتاییدی (SoD) خوانده می‌شود
        if (_currentUser.UserId.HasValue)
        {
            await _accessControlService.AuthorizeAsync(new PEMS.Application.DTOs.AccessControlRequest
            {
                UserId = _currentUser.UserId.Value,
                ResourceName = "EngineeringDocuments",
                ActionName = "Create",
                ResourceId = document.DocumentId.ToString(),
                IpAddress = GetClientIp()
            });
        }

        return RedirectToAction(nameof(Index));

    }






    // =========================
    // DETAILS
    // =========================


    public async Task<IActionResult> Details(int id)
    {
        ViewData["HelpKey"] = "EngineeringDocuments.Details";

        var document =
            await _service.GetByIdAsync(id);


        if (document == null)
            return NotFound();

        // جلوگیری از دور زدن محدودیت دیسیپلین با ورود مستقیم به آدرس - نه فقط پنهان‌کردن از لیست
        var visibleCheck = await ScopeToOwnDisciplineIfRestrictedAsync(new List<PEMS.Application.DTOs.EngineeringDocumentDto> { document });
        if (!visibleCheck.Any())
            return Forbid();

        ViewBag.RelatedLetters = await _letterService.GetRelatedToDocumentAsync(id);

        // طبق «PEMS Phase 2A — EDMS Foundation» بخش ۲: پیوند متقابل با Transmittal (قبلاً هیچ‌جا
        // روی این صفحه نشان داده نمی‌شد - سرویس/موجودیت موجود، فقط یک Query هدفمند جدید)
        ViewBag.RelatedTransmittals = await _transmittalService.GetForDocumentAsync(id);

        // طبق Phase E-01
        ViewBag.CanFreeze = await _permissionService.HasPermissionAsync(_currentUser.Roles, "EngineeringDocuments", "Freeze");
        ViewBag.CanUnfreeze = await _permissionService.HasPermissionAsync(_currentUser.Roles, "EngineeringDocuments", "Unfreeze");

        // طبق بخش M ممیزی سخت‌سازی مهندسی
        ViewBag.CanHold = await _permissionService.HasPermissionAsync(_currentUser.Roles, "EngineeringDocuments", "Hold");
        ViewBag.CanReleaseHold = await _permissionService.HasPermissionAsync(_currentUser.Roles, "EngineeringDocuments", "ReleaseHold");
        ViewBag.HoldHistory = await _holdService.GetHistoryAsync(id);

        // طبق درخواست: چرخه‌ی کامنت/ریپلای دقیقاً مثل VDR
        ViewBag.Comments = await _commentService.GetForDocumentAsync(id);

        // برای پاپ‌آپ Submit (انتخاب دیسیپلین‌های IDC) و نمایش وضعیت لحظه‌ای IDC
        ViewBag.AllDisciplines = await _context.Disciplines.Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();
        ViewBag.IdcStatus = await _workflowService.GetIdcStatusAsync(id);
        ViewBag.MyDisciplineId = await GetCurrentEmployeeDisciplineIdAsync();

        // طبق «PEMS Engineering Workflow History Read-Path Correction»: Model.Workflows
        // (EngineeringDocument.Workflows) جدول قدیمی DocumentWorkflow است که فقط موقع Create
        // یک ردیف می‌گیرد - منبع واقعیِ تاریخچه‌ی کامل گردش‌کار WorkflowHistory است
        ViewBag.WorkflowHistory = await _workflowService.GetWorkflowHistoryAsync(id);

        // برای پاپ‌آپ Issue (انتخاب واحد تحویل داخلی) و پاپ‌آپ پاسخ کارفرما (انتخاب نامه‌ی وارده)
        ViewBag.AllDepartments = await _context.Departments.Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();
        ViewBag.AvailableIncomingLetters = await _context.Letters
            .Where(l => l.IsActive && l.LetterType == "Incoming" && (l.RelatedDocumentId == null || l.RelatedDocumentId == id))
            .OrderByDescending(l => l.LetterDate)
            .Take(30)
            .ToListAsync();

        // ==============================================================================
        // طبق درخواست: کلیک روی شماره P&ID از تجهیز به این مدرک منتقل می‌شود، و برعکس. طبق
        // رفع باگ گزارش‌شده (پسوند رویژن اضافه در انتهای شماره‌ی ثبت‌شده روی تجهیز)، از همون
        // DocumentNumberMatcher سه‌لایه استفاده می‌شود که برای جهت رفت هم استفاده شد - این‌جا
        // چون ممکنه چند تجهیز مختلف به یک مدرک ارجاع بدن، همه‌ی نامزدهای منطبق نگه داشته می‌شن.
        // ==============================================================================
        var equipmentCandidates = await _context.Equipments
            .Where(x => x.IsActive && x.PIDNumber != null && x.ProjectId == document.ProjectId)
            .Select(x => new { x.EquipmentId, x.TagNumber, x.Name, x.PIDNumber })
            .ToListAsync();

        ViewBag.EquipmentUsingThisAsPID = equipmentCandidates
            .Where(x => PEMS.Shared.Search.DocumentNumberMatcher.IsMatch(x.PIDNumber, document.DocumentNo))
            .Select(x => new { x.EquipmentId, x.TagNumber, x.Name })
            .ToList();

        return View(document);

    }


    // طبق فاز ۴ نقشه‌راه معماری: کشف رابطه‌ی معنایی این مدرک با بقیه، به کمک همون زیرساخت
    // Semantic Search موجود - نه پیاده‌سازی جدید
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AnalyzeRelationships(int id)
    {
        var added = await _copilotService.AnalyzeDocumentRelationshipsAsync(id);

        TempData["Message"] = added > 0
            ? $"{added} رابطه‌ی معنایی جدید (پیشنهادی) پیدا شد - توی Relationship Explorer قابل‌مشاهده‌ست."
            : "رابطه‌ی معنایی جدیدی پیدا نشد (یا Ollama در دسترس نبود).";

        return RedirectToAction(nameof(Details), new { id });
    }





    // =========================
    // EDIT GET
    // =========================


    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        // طبق یافته‌ی P4-01: همان بررسی سمت‌سرور - نمایش فرم ویرایش هم باید مجوز داشته باشد
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "EngineeringDocuments", "Edit"))
            return Forbid();

        var document =
            await _service.GetEntityByIdAsync(id);


        if (document == null)
            return NotFound();


        LoadDropdowns();


        return View(document);

    }





    // =========================
    // EDIT POST
    // =========================


    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        EngineeringDocument document)
    {
        // طبق یافته‌ی P4-01
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "EngineeringDocuments", "Edit"))
            return Forbid();

        if (!ModelState.IsValid)
        {
            LoadDropdowns();
            return View(document);
        }


        document.ModifiedDate = DateTime.Now;


        try
        {
            await _service.UpdateAsync(document);
        }
        catch (PEMS.Application.Exceptions.DocumentFrozenException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Details), new { id = document.DocumentId });
        }

        // طبق یافته‌ی P4-01: ثبت در تاریخچه‌ی مجوزدهی - Edit هم طبق SoDPolicy موجود با Approve
        // در تعارض است (کاربری که ویرایش کرده نمی‌تواند همان مدرک را تایید کند)
        if (_currentUser.UserId.HasValue)
        {
            await _accessControlService.AuthorizeAsync(new PEMS.Application.DTOs.AccessControlRequest
            {
                UserId = _currentUser.UserId.Value,
                ResourceName = "EngineeringDocuments",
                ActionName = "Edit",
                ResourceId = document.DocumentId.ToString(),
                IpAddress = GetClientIp()
            });
        }

        TempData["Message"] = "Document updated successfully";
        return RedirectToAction(nameof(Details), new { id = document.DocumentId });

    }





    // =========================
    // DELETE DOCUMENT
    // =========================


    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {

        var document =
            await _service.GetEntityByIdAsync(id);


        if (document == null)
            return NotFound();


        await _service.DeleteAsync(document);


        return RedirectToAction(nameof(Index));

    }
        // =========================
    // DROPDOWNS
    // =========================

    private void LoadDropdowns()
    {
        ViewBag.Projects =
            new SelectList(
                _context.Projects,
                "ProjectId",
                "ProjectName"
            );


        ViewBag.Disciplines =
            new SelectList(
                _context.Disciplines,
                "DisciplineId",
                "Name"
            );


        ViewBag.Equipments =
            new SelectList(
                _context.Equipments,
                "EquipmentId",
                "TagNumber"
            );


        ViewBag.Contracts =
            new SelectList(
                _context.Contracts,
                "ContractId",
                "ContractNo"
            );

        ViewBag.MaterialRequisitions =
            new SelectList(
                _context.MaterialRequisitions,
                "MRId",
                "MRNumber"
            );
    }





    // =========================
    // UPLOAD ATTACHMENT GET
    // =========================

    [HttpGet]
    public async Task<IActionResult> UploadAttachment(int id)
    {
        ViewBag.DocumentId = id;

        // طبق یافته F-06 ممیزی EDMS: کاربر باید بتواند صریحاً انتخاب کند این فایل برای کدام
        // رویژن است - اختیاری می‌ماند (گزینه‌ی "سطح مدرک، بدون رویژن مشخص" همیشه موجود است) تا
        // با جریان قدیمی/پیوست‌های عمومی سازگار بماند
        ViewBag.Revisions = await _context.DocumentRevisions
            .Where(x => x.DocumentId == id && x.IsActive)
            .OrderByDescending(x => x.RevisionId)
            .Select(x => new { x.RevisionId, x.RevisionNo, x.Status })
            .ToListAsync();

        return View();
    }





    // =========================
    // UPLOAD ATTACHMENT POST
    // =========================

    [HttpPost]
    [ValidateAntiForgeryToken]
    [PEMS.Web.Security.Uploads.UploadPolicy(PEMS.Application.Uploads.UploadProfile.Attachment)]
    public async Task<IActionResult> UploadAttachment(
        int documentId,
        IFormFile file,
        int? revisionId,
        string? fileRole)
    {

        if(file == null || file.Length == 0)
        {
            ModelState.AddModelError(
                "",
                "Please select a file"
            );

            ViewBag.DocumentId = documentId;
            ViewBag.Revisions = await _context.DocumentRevisions
                .Where(x => x.DocumentId == documentId && x.IsActive)
                .OrderByDescending(x => x.RevisionId)
                .Select(x => new { x.RevisionId, x.RevisionNo, x.Status })
                .ToListAsync();

            return View();
        }

        // طبق یافته F-06 ممیزی EDMS: اگر revisionId داده شده، باید واقعاً متعلق به همین مدرک باشد -
        // هرگز به یک رویژن مدرک دیگر وصل نمی‌شود
        if (revisionId.HasValue)
        {
            var revisionBelongsToDocument = await _context.DocumentRevisions
                .AnyAsync(x => x.RevisionId == revisionId.Value && x.DocumentId == documentId);
            if (!revisionBelongsToDocument)
                return BadRequest("رویژن انتخاب‌شده متعلق به این مدرک نیست.");
        }

        // طبق ممیزی امنیتی (P0 - Engineering File Security Containment): دیگر مستقیم زیر
        // wwwroot نوشته نمی‌شود - IFileStorageService ریشه‌ی امن بیرون از wwwroot را مدیریت می‌کند.
        // طبق یافته F-07 ممیزی EDMS: SHA-256 حین همان یک بار Streaming محاسبه می‌شود (نه با خواندن
        // دوباره‌ی فایل) - HashingStream همان کلاس ساخته‌شده برای Enterprise File Storage است،
        // این‌جا دوباره استفاده می‌شود نه بازتولید.
        string storageKey;
        string fileHash;
        await using (var uploadStream = file.OpenReadStream())
        {
            var hashingStream = new PEMS.Persistence.Services.HashingStream(uploadStream);
            storageKey = await _fileStorage.SaveAsync("documents", file.FileName, hashingStream);
            fileHash = hashingStream.GetHexHash();
        }

        // طبق یافته F-06: اگر این رویژن هنوز هیچ فایل رسمی ندارد، اولین فایل آپلودشده برایش
        // خودکار رسمی می‌شود؛ در غیر این صورت کاربر باید بعداً صریحاً "Set Official" کند - هرگز دو
        // فایل هم‌زمان به‌طور خاموش رسمی یک رویژن نمی‌شوند
        var isFirstOfficialForRevision = revisionId.HasValue &&
            !await _context.DocumentAttachments.AnyAsync(x =>
                x.DocumentRevisionId == revisionId.Value && x.IsActive && x.IsOfficial);

        var attachment =
            new DocumentAttachment
            {
                DocumentId = documentId,

                DocumentRevisionId = revisionId,

                FileRole = string.IsNullOrWhiteSpace(fileRole) ? null : fileRole,

                IsOfficial = isFirstOfficialForRevision,

                FileHash = fileHash,

                FileName = file.FileName,

                FileExtension =
                    Path.GetExtension(file.FileName),


                FilePath = storageKey,


                FileSize = file.Length,


                MimeType = file.ContentType,


                AIStatus = "Pending",


                UploadDate = DateTime.Now,


                IsActive = true
            };



        await _context.DocumentAttachments
            .AddAsync(attachment);



        await _context.SaveChangesAsync();


        var attachmentId = attachment.AttachmentId;

        // Hangfire: کار در دیتابیس ذخیره می‌شود (اگر برنامه Restart شود گم نمی‌شود) و در صورت خطا
        // خودکار Retry می‌شود - جایگزین Task.Run قبلی که بدون Persistence/Retry بود
        Hangfire.BackgroundJob.Enqueue<PEMS.Application.Interfaces.IDocumentAIIngestionService>(
            x => x.ProcessAttachmentAsync(attachmentId));


        return RedirectToAction(
            nameof(Details),
            new { id = documentId }
        );
    }





    // =========================
    // DOWNLOAD ATTACHMENT
    // =========================

    // طبق ممیزی امنیتی (P0 - Engineering File Security Containment): این Action قبلاً فقط
    // IsActive چک می‌کرد - هیچ محدودیت Scope/دیسیپلینی نداشت و مستقیم زیر wwwroot می‌خواند.
    // منطق واقعی (احراز هویت → مجوز/Scope → بررسی دسترسی والد → یافتن پیوست → Resolve
    // ذخیره‌سازی → Streaming امن) اکنون یک‌جا در FilesController است - این Route قدیمی
    // (که در Viewها/لینک‌های قبلی استفاده شده) حفظ می‌شود اما فقط به همان مسیر امن هدایت می‌کند،
    // نه یک پیاده‌سازی موازی و ضعیف‌تر.
    public IActionResult DownloadAttachment(int id)
    {
        return RedirectToAction("Download", "Files", new { id });
    }





    // =========================
    // DELETE ATTACHMENT
    // =========================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAttachment(int id)
    {

        var attachment =
            await _context.DocumentAttachments
            .FirstOrDefaultAsync(x =>
                x.AttachmentId == id);



        if(attachment == null)
            return NotFound();



        // Soft Delete

        attachment.IsActive = false;



        await _context.SaveChangesAsync();



        return RedirectToAction(
            nameof(Details),
            new
            {
                id = attachment.DocumentId
            });

    }



    // =========================
    // CREATE REVISION
    // =========================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateRevision(int id, string? reason, string? changeDescription, string? vendorComment, int? triggeringCommentId)
    {
        var document =
            await _context.EngineeringDocuments
            .Include(x => x.Revisions)
            .FirstOrDefaultAsync(
                x => x.DocumentId == id);



        if (document == null)
            return NotFound();

        // طبق Phase E-01 (رفع P0 ممیزی Freeze): مدرک منجمد نباید رویژن جدید هم بگیرد
        if (document.IsFrozen)
        {
            TempData["Error"] = $"مدرک {document.DocumentNo} منجمد (Frozen) است و رویژن جدید نمی‌تواند بگیرد.";
            return RedirectToAction(nameof(Details), new { id });
        }



        var lastRevision =
            document.Revisions
            .OrderByDescending(x => x.RevisionId)
            .FirstOrDefault();



        // طبق درخواست: شماره‌ی رویژن هرگز نباید تکراری ثبت شود. به‌جای اتکا به «آخرین رکورد
        // درج‌شده» (که در شرایط مسابقه می‌تواند اشتباه باشد)، بزرگ‌ترین شماره‌ی واقعی موجود
        // پیدا می‌شود و یکی به آن اضافه می‌شود.
        int nextRevision = 0;

        foreach (var rev in document.Revisions)
        {
            if (int.TryParse(rev.RevisionNo, out var parsed) && parsed >= nextRevision)
                nextRevision = parsed + 1;
        }



        var currentEmployeeId = await GetCurrentEmployeeIdAsync();

        var revision =
            new DocumentRevision
            {
                DocumentId = id,

                RevisionNo =
                    nextRevision.ToString("00"),

                Status = "Draft",

                Comment =
                    string.IsNullOrWhiteSpace(vendorComment) ? "New Revision Created" : vendorComment,

                RevisionDate =
                    DateTime.Now,

                Reason = reason,

                ChangeDescription = changeDescription,

                VendorComment = vendorComment,

                SupersededRevisionId = lastRevision?.RevisionId,

                CreatedById = currentEmployeeId,

                CreatedDate = DateTime.Now,

                IsActive = true
            };



        await _context.DocumentRevisions
            .AddAsync(revision);

        // طبق چرخه‌ی واقعی مدرک: رویژن قبلی باید Superseded شود، وگرنه برای همیشه Approved
        // نشان داده می‌شود حتی وقتی رویژن جدیدتری جایگزینش شده
        if (lastRevision != null)
        {
            lastRevision.Status = "Superseded";
        }



        document.RevisionNo =
            nextRevision.ToString("00");


        document.Status =
            "Draft";

        // طبق «PEMS EDMS Phase 2 Lifecycle Design» یافته L2-01: رویژن تازه هنوز پاسخ کارفرما
        // ندارد - این Cache نباید پاسخ کارفرمای رویژن قبلی را به‌اشتباه نشان بدهد. حقیقت مرجع
        // (rev-به-rev) روی خودِ رویژن‌های قبلی دست‌نخورده باقی می‌ماند.
        document.ClientApprovalStatus = null;
        document.ClientComment = null;
        document.ClientResponseDate = null;

        document.ModifiedDate =
            DateTime.Now;



        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // طبق درخواست: اگر دو کاربر هم‌زمان "Create New Revision" بزنند و شماره‌ی یکسانی
            // تولید شود، Index یکتا در سطح دیتابیس جلویش را می‌گیرد - این‌جا فقط پیام دوستانه
            // نشان داده می‌شود، نه خطای خام SQL
            TempData["Error"] = "This revision number was just created by someone else. Please try again.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // طبق بخش M (بند ۱۷ - Clouded Traceability): اگر این رویژن برای رفع یک کامنت
        // Marked-for-Revision مشخص ساخته شده، همان‌جا وصل می‌شود - «آیا رویژن جدید ساخته شد؟»
        // بدون گزارش جداگانه، مستقیم قابل پاسخ می‌ماند
        if (triggeringCommentId.HasValue)
        {
            var comment = await _context.DocumentComments.FirstOrDefaultAsync(c => c.DocumentCommentId == triggeringCommentId.Value && c.DocumentId == id);
            if (comment != null)
            {
                comment.TriggeredRevisionId = revision.RevisionId;
                await _context.SaveChangesAsync();
            }
        }

        return RedirectToAction(
            nameof(Details),
            new { id });
    }

    // =========================
    // FREEZE / UNFREEZE (Phase E-01)
    // =========================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Freeze(int id, string reason)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "EngineeringDocuments", "Freeze"))
            return Forbid();

        if (string.IsNullOrWhiteSpace(reason))
        {
            TempData["Error"] = "دلیل انجماد (Freeze Reason) الزامی است.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var actorEmployeeId = await GetCurrentEmployeeIdAsync();

        try
        {
            await _service.FreezeAsync(id, actorEmployeeId, reason);
            TempData["Message"] = "مدرک منجمد (Frozen) شد.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unfreeze(int id, string reason)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "EngineeringDocuments", "Unfreeze"))
            return Forbid();

        if (string.IsNullOrWhiteSpace(reason))
        {
            TempData["Error"] = "دلیل رفع انجماد (Unfreeze Reason) الزامی است.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var actorEmployeeId = await GetCurrentEmployeeIdAsync();

        try
        {
            await _service.UnfreezeAsync(id, actorEmployeeId, reason);
            TempData["Message"] = "انجماد مدرک برداشته شد.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    // =========================
    // HOLD / RELEASE HOLD (Engineering Hardening Audit, بخش M)
    // =========================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Hold(int id, string reason, int? responsibleDisciplineId)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "EngineeringDocuments", "Hold"))
            return Forbid();

        var actorEmployeeId = await GetCurrentEmployeeIdAsync();

        try
        {
            await _holdService.PlaceHoldAsync(id, reason, responsibleDisciplineId, actorEmployeeId);
            TempData["Message"] = "مدرک در وضعیت Hold قرار گرفت.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReleaseHold(int id, string reason)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "EngineeringDocuments", "ReleaseHold"))
            return Forbid();

        var actorEmployeeId = await GetCurrentEmployeeIdAsync();

        try
        {
            await _holdService.ReleaseHoldAsync(id, reason, actorEmployeeId);
            TempData["Message"] = "Hold مدرک رفع شد.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    // =========================
    // WORKFLOW ACTIONS
    // =========================


    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(int id, string? comment, List<int>? idcDisciplineIds)
    {

        var document =
            await _context.EngineeringDocuments
            .FirstOrDefaultAsync(
                x => x.DocumentId == id);



        if (document == null)
            return NotFound();



        // فقط Draft یا Rejected اجازه Submit دارد

        if (document.Status != "Draft" &&
           document.Status != "Rejected")
        {
            TempData["Error"] =
                "Document cannot be submitted in current status";

            return RedirectToAction(
                nameof(Details),
                new { id });
        }



        try
        {
            // طبق فاز ۱ ممیزی: قبلاً به‌جای کامنت واقعی کاربر، همیشه رشته‌ی ثابت "Submitted
            // for review" ثبت می‌شد - یعنی تاریخچه‌ی گردش‌کار هیچ‌وقت نظر واقعی کاربر رو نداشت
            await _workflowService.SubmitAsync(
                id,
                comment,
                await GetCurrentEmployeeIdAsync(),
                _currentUser.Roles,
                idcDisciplineIds,
                GetClientIp(),
                GetUserAgent());
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }



        return RedirectToAction(
            nameof(Details),
            new { id });

    }







    // طبق بررسی چرخه‌ی مدرک: مرحله‌ی Check مستقل - قبلاً اصلاً وجود نداشت
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Check(int id, string? comment)
    {
        var document = await _context.EngineeringDocuments.FirstOrDefaultAsync(x => x.DocumentId == id);
        if (document == null) return NotFound();

        if (document.Status != "Submitted")
        {
            TempData["Error"] = "Only submitted documents can be checked";
            return RedirectToAction(nameof(Details), new { id });
        }

        try
        {
            await _workflowService.CheckAsync(
                id, comment, await GetCurrentEmployeeIdAsync(), _currentUser.Roles, GetClientIp(), GetUserAgent());
            TempData["Message"] = "Document checked - ready for approval";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }


    // ==============================================================================
    // پاسخ یک دیسیپلین به بازبینی IDC. decision یکی از Approve/Comment/Reject است.
    // ==============================================================================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> IdcRespond(int id, string decision, string? comment)
    {
        var myDisciplineId = await GetCurrentEmployeeDisciplineIdAsync();
        var myEmployeeId = await GetCurrentEmployeeIdAsync();

        if (!myDisciplineId.HasValue || !myEmployeeId.HasValue)
        {
            TempData["Error"] = "کارمند مرتبط با این کاربر یا دیسیپلینش مشخص نیست.";
            return RedirectToAction(nameof(Details), new { id });
        }

        try
        {
            await _workflowService.IdcRespondAsync(
                id, decision, comment, myEmployeeId.Value, myDisciplineId.Value, GetClientIp(), GetUserAgent());

            TempData["Message"] = "پاسخ بازبینی شما ثبت شد.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id, string? comment)
    {

        var document =
            await _context.EngineeringDocuments
            .FirstOrDefaultAsync(
                x => x.DocumentId == id);



        if (document == null)
            return NotFound();



        // طبق بررسی چرخه‌ی مدرک: تایید فقط بعد از Check مجاز است، نه مستقیم بعد از Submit

        if (document.Status != "Checked")
        {
            TempData["Error"] =
                "Only checked documents can be approved";

            return RedirectToAction(
                nameof(Details),
                new { id });
        }

        if (!await CanApproveDocumentDisciplineAsync(document.DisciplineId))
        {
            TempData["Error"] = "You can only approve documents in your own discipline";
            return RedirectToAction(nameof(Details), new { id });
        }

        // طبق «PEMS EDMS Phase 4 Audit» یافته P4-02: AccessControlService.AuthorizeAsync از فاز ۱
        // RBAC-ABAC ساخته و تست شده بود (AccessControlScopeAndSoDTests) و SoDPolicy موجود
        // («EngineeringDocuments.Create/Edit در تعارض با Approve») همین الان هم در دیتابیس Seed
        // شده - اما هیچ فراخوان واقعی‌ای در کل پروژه نداشت. این‌جا اولین فراخوان واقعی است: اگر
        // همین کاربر قبلاً این مدرک را ایجاد یا ویرایش کرده باشد (طبق تاریخچه‌ی واقعی
        // AuthorizationAuditLog که همان اقدامات بالا در Create/Edit ثبت می‌کنند)، تایید رد می‌شود.
        if (_currentUser.UserId.HasValue)
        {
            var sodDecision = await _accessControlService.AuthorizeAsync(new PEMS.Application.DTOs.AccessControlRequest
            {
                UserId = _currentUser.UserId.Value,
                ResourceName = "EngineeringDocuments",
                ActionName = "Approve",
                ResourceId = document.DocumentId.ToString(),
                IpAddress = GetClientIp()
            });

            if (!sodDecision.Allowed && sodDecision.Reason == PEMS.Application.DTOs.AccessDecisionReason.DeniedBySoD)
            {
                TempData["Error"] = string.IsNullOrWhiteSpace(sodDecision.Detail)
                    ? "طبق سیاست تفکیک وظایف، شما نمی‌توانید مدرکی را که خودتان ایجاد/ویرایش کرده‌اید تایید کنید."
                    : sodDecision.Detail;
                return RedirectToAction(nameof(Details), new { id });
            }
        }



        try
        {
            await _workflowService.ApproveAsync(
                id,
                comment,
                await GetCurrentEmployeeIdAsync(),
                _currentUser.Roles,
                GetClientIp(),
                GetUserAgent());
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }



        return RedirectToAction(
            nameof(Details),
            new { id });

    }








    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id, string? comment)
    {

        var document =
            await _context.EngineeringDocuments
            .FirstOrDefaultAsync(
                x => x.DocumentId == id);



        if (document == null)
            return NotFound();




        // طبق بررسی چرخه‌ی مدرک: رد کردن هم بعد از Submit و هم بعد از Check ممکن است

        if (document.Status != "Submitted" &&
           document.Status != "Checked")
        {
            TempData["Error"] =
                "Only submitted or checked documents can be rejected";

            return RedirectToAction(
                nameof(Details),
                new { id });
        }




        try
        {
            await _workflowService.RejectAsync(
                id,
                comment,
                await GetCurrentEmployeeIdAsync(),
                _currentUser.Roles,
                GetClientIp(),
                GetUserAgent());
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }



        return RedirectToAction(
            nameof(Details),
            new { id });

    }


    // طبق بررسی کامل چرخه‌ی مدرک: بعد از Approve هیچ اقدام رسمی "Issue" وجود نداشت؛
    // مدرک تاییدشده هیچ‌وقت رسماً "صادر" نمی‌شد (فقط در ذهن کاربر Approved = تمام‌شده بود)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Issue(
        int id, string? comment, string deliveryUnitType, int? deliveryDepartmentId,
        string? externalReceiverName, string? letterNumber, string? letterSubject, string? letterBody,
        string? issuePurpose)
    {
        var document = await _context.EngineeringDocuments.FirstOrDefaultAsync(x => x.DocumentId == id);
        if (document == null) return NotFound();

        if (document.Status != "Approved")
        {
            TempData["Error"] = "Only approved documents can be issued";
            return RedirectToAction(nameof(Details), new { id });
        }

        try
        {
            await _workflowService.IssueAsync(
                id, comment, await GetCurrentEmployeeIdAsync(), _currentUser.Roles,
                deliveryUnitType, deliveryDepartmentId, externalReceiverName,
                letterNumber, letterSubject, letterBody, issuePurpose,
                GetClientIp(), GetUserAgent());
            TempData["Message"] = "Document issued";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }


    // طبق Phase E-04: برخلاف Reject (رد قطعی)، این یعنی "برگردان برای اصلاح - انتظار داریم
    // دوباره Submit شود" - همان مجوز Reject کافی است (از نظر اختیار، دقیقاً همان اقدام است)،
    // دقیقاً طبق الگوی PermitController.ReturnForRework که از همون مجوز Reject استفاده می‌کند.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReviseAndResubmit(int id, string? comment)
    {
        var document = await _context.EngineeringDocuments.FirstOrDefaultAsync(x => x.DocumentId == id);
        if (document == null) return NotFound();

        if (document.Status != "Submitted" && document.Status != "Checked")
        {
            TempData["Error"] = "Only submitted or checked documents can be returned for revision";
            return RedirectToAction(nameof(Details), new { id });
        }

        try
        {
            await _workflowService.ReviseAndResubmitAsync(
                id,
                comment,
                await GetCurrentEmployeeIdAsync(),
                _currentUser.Roles,
                GetClientIp(),
                GetUserAgent());
            TempData["Message"] = "Document returned for revision";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RecordClientResponse(int id, string clientStatus, int incomingLetterId, string? clientComment)
    {
        try
        {
            await _workflowService.RecordClientResponseAsync(
                id, clientStatus, incomingLetterId, clientComment, await GetCurrentEmployeeIdAsync(), _currentUser.Roles, GetClientIp(), GetUserAgent());
            TempData["Message"] = "Client response recorded";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }


    // طبق درخواست: چرخه‌ی کامنت/ریپلای دقیقاً مثل VDR - همان سرویس مشترک، همان قابلیت پیوست
    [HttpPost]
    [ValidateAntiForgeryToken]
    [PEMS.Web.Security.Uploads.UploadPolicy(PEMS.Application.Uploads.UploadProfile.Attachment)]
    public async Task<IActionResult> AddComment(int documentId, string commentText, string? raisedByName,
        string? sheet, string? zone, string? reference, string? category, string? severity, string? requiredAction,
        DateTime? dueDate, IFormFile? attachment, int? pageNumber, double? pinX, double? pinY, bool marksRevisionRequired)
    {
        var attachmentId = attachment != null ? await SaveCommentFileAsync(attachment, "comments") : null;

        await _commentService.AddCommentAsync(documentId, commentText, raisedByName, null,
            sheet, zone, reference, category, severity, requiredAction, dueDate, attachmentId,
            pageNumber, pinX, pinY, marksRevisionRequired);

        TempData["Message"] = "Comment recorded";
        return RedirectToAction(nameof(Details), new { id = documentId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetCommentAction(int documentId, int commentId, string actionStatus)
    {
        await _commentService.SetActionStatusAsync(commentId, actionStatus);
        return RedirectToAction(nameof(Details), new { id = documentId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [PEMS.Web.Security.Uploads.UploadPolicy(PEMS.Application.Uploads.UploadProfile.Attachment)]
    public async Task<IActionResult> RecordCommentResponse(int documentId, int commentId, string? responseText, IFormFile? replyAttachment)
    {
        var replyAttachmentId = replyAttachment != null ? await SaveCommentFileAsync(replyAttachment, "replies") : null;

        await _commentService.RecordResponseAsync(commentId, responseText, replyAttachmentId);
        TempData["Message"] = "Response recorded";
        return RedirectToAction(nameof(Details), new { id = documentId });
    }

    private async Task<int?> SaveCommentFileAsync(IFormFile file, string subFolder)
    {
        if (file == null || file.Length == 0) return null;

        string storageKey;
        await using (var uploadStream = file.OpenReadStream())
        {
            storageKey = await _fileStorage.SaveAsync($"engineering-comments/{subFolder}", file.FileName, uploadStream);
        }

        var attachment = new DocumentAttachment
        {
            FileName = file.FileName,
            FileExtension = Path.GetExtension(file.FileName),
            FilePath = storageKey,
            FileSize = file.Length,
            MimeType = file.ContentType,
            UploadDate = DateTime.Now,
            IsActive = true,
            AIStatus = "NotApplicable"
        };

        await _context.DocumentAttachments.AddAsync(attachment);
        await _context.SaveChangesAsync();

        return attachment.AttachmentId;
    }


}