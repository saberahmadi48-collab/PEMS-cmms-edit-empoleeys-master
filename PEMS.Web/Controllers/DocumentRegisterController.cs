using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Web.Security;

namespace PEMS.Web.Controllers;

/// <summary>
/// یک نمای فیلترپذیر و قابل Export از تمام مدارک مهندسی سیستم (Document Register رسمی پروژه).
/// طبق بازبینی معماری: فیلتر چند-انتخابی دیسیپلین (Multi-Discipline)، مدارک بدون دیسیپلین
/// تطبیق‌یافته دیگر بی‌صدا ناپدید نمی‌شوند (سطل "Unassigned")، و فیلترهای ذخیره‌شده/سریع.
///
/// طبق ممیزی مقیاس‌پذیری (P1 - Document Register Remediation): قبلاً هر سه Action این کنترلر
/// (Index/ExportExcel/ExportSelected) کل جدول EngineeringDocuments را با GetAllAsync (بدون
/// Pagination، با Include(Attachments) بلااستفاده) می‌خواندند و فیلتر/مرتب‌سازی/Distinct را در
/// حافظه‌ی C# انجام می‌دادند. اکنون فیلتر/صفحه‌بندی/مرتب‌سازی همه در SQL انجام می‌شوند
/// (GetRegisterPagedAsync/GetRegisterAllMatchingAsync/GetRegisterByIdsAsync/
/// GetRegisterFilterOptionsAsync در EngineeringDocumentRepository) - رفتار فیلترها/شمارش/دکمه‌ها
/// عیناً حفظ شده، فقط دیگر کل جدول بارگذاری نمی‌شود.
/// </summary>
public class DocumentRegisterController : Controller
{
    private const string PageKey = "DocumentRegister";

    // طبق بند ۶ درخواست ممیزی: صادرات هم باید یک سقف امن داشته باشد، نه واقعاً نامحدود -
    // امروز حداکثر واقعی حدود ۹۱۹۱ سند است؛ این سقف با فاصله‌ی زیاد امن است و فقط از رشد
    // بی‌کنترل آینده جلوگیری می‌کند، نه یک محدودیت عملی امروز.
    private const int MaxExportRows = 20000;

    private readonly IEngineeringDocumentService _service;
    private readonly IExcelExportService _excelExportService;
    private readonly ISavedFilterService _savedFilterService;
    private readonly ICurrentUserService _currentUser;
    private readonly PEMSDbContext _context;
    private readonly IAccessControlService _accessControlService;

    public DocumentRegisterController(
        IEngineeringDocumentService service,
        IExcelExportService excelExportService,
        ISavedFilterService savedFilterService,
        ICurrentUserService currentUser,
        PEMSDbContext context,
        IAccessControlService accessControlService)
    {
        _service = service;
        _excelExportService = excelExportService;
        _savedFilterService = savedFilterService;
        _currentUser = currentUser;
        _context = context;
        _accessControlService = accessControlService;
    }

    // طبق فاز ۳ RBAC-ABAC: دقیقاً همون منطق EngineeringDocumentsController.Index (E-03) -
    // دو سیگنال جدا (isRestricted/myDisciplineId) که مستقیم به Repository پاس داده می‌شوند،
    // نه فیلتر درون‌حافظه‌ای بعد از واکشی کامل.
    private async Task<(bool IsRestricted, int? MyDisciplineId)> ResolveScopeAsync()
    {
        var canViewAllDisciplines = (await _accessControlService.AuthorizeForRolesAsync(
            _currentUser.Roles, "EngineeringDocuments", "ViewAllDisciplines")).Allowed;

        var isRestricted = _currentUser.UserId.HasValue && !canViewAllDisciplines;
        if (!isRestricted) return (false, null);

        var employeeId = await _context.Users
            .Where(u => u.Id == _currentUser.UserId!.Value)
            .Select(u => u.EmployeeId)
            .FirstOrDefaultAsync();

        if (!employeeId.HasValue) return (false, null);

        var employee = await _context.Employees.FirstOrDefaultAsync(e => e.EmployeeId == employeeId.Value);
        if (employee == null) return (false, null);

        return (true, employee.DisciplineId);
    }

    private static DocumentRegisterFilter BuildFilter(
        string? project, string? disciplines, string? status, string? q, int page, int? pageSize)
    {
        var selectedDisciplines = (disciplines ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        return new DocumentRegisterFilter
        {
            ProjectName = project,
            Disciplines = selectedDisciplines,
            Status = status,
            Search = q,
            Page = page,
            // طبق سند Global Data Grid Standard بند ۴: فقط ۲۰/۵۰/۱۰۰ پذیرفته می‌شود - هر مقدار
            // دیگری (از جمله دستکاری مستقیم QueryString) بی‌صدا به ۵۰ برمی‌گردد
            PageSize = DataGridPageSize.Normalize(pageSize)
        };
    }

    /// <summary>SCRUM-538: opens a document's own details page by its type (engineering, VDR, contract, maintenance); the target page checks the permissions.</summary>
    [HttpGet]
    public async Task<IActionResult> Open(int id)
    {
        var target = PEMS.Web.Helpers.DocumentLinks.Target(await _context.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.DocumentId == id));
        return target is { } t ? RedirectToAction("Details", t.Controller, new { id = t.Id }) : NotFound();
    }

    public async Task<IActionResult> Index(string? project, string? disciplines, string? status, string? q, int page = 1, int? pageSize = null)
    {
        ViewData["Title"] = "Document Register";
        ViewData["Section"] = "Engineering Management";
        ViewData["HelpKey"] = "DocumentRegister.Index";

        var (isRestricted, myDisciplineId) = await ResolveScopeAsync();
        var filter = BuildFilter(project, disciplines, status, q, page, pageSize);

        var result = await _service.GetRegisterPagedAsync(filter, isRestricted, myDisciplineId);
        var options = await _service.GetRegisterFilterOptionsAsync(isRestricted, myDisciplineId);

        ViewBag.Projects = options.Projects;
        ViewBag.Disciplines = options.Disciplines;
        ViewBag.Statuses = options.Statuses;

        ViewBag.SelectedProject = project;
        ViewBag.SelectedDisciplines = filter.Disciplines;
        ViewBag.SelectedStatus = status;
        ViewBag.SearchTerm = q;

        if (_currentUser.UserId.HasValue)
            ViewBag.SavedFilters = await _savedFilterService.GetForPageAsync(_currentUser.UserId.Value, PageKey);
        else
            ViewBag.SavedFilters = new List<SavedFilterDto>();

        return View(result);
    }

    public async Task<IActionResult> ExportExcel(string? project, string? disciplines, string? status, string? q)
    {
        var (isRestricted, myDisciplineId) = await ResolveScopeAsync();
        var filter = BuildFilter(project, disciplines, status, q, page: 1, pageSize: null);

        var (items, totalMatching) = await _service.GetRegisterAllMatchingAsync(filter, isRestricted, myDisciplineId, MaxExportRows);

        if (totalMatching > MaxExportRows)
        {
            // طبق انضباط این نشست: هیچ‌وقت خاموش کوتاه نکن - کاربر باید بداند خروجی ناقص است،
            // نه این‌که فکر کند فایل کامل را گرفته
            return BadRequest(
                $"تعداد مدارک منطبق با فیلتر ({totalMatching}) از سقف امن Export ({MaxExportRows}) بیشتر است. " +
                "لطفاً فیلتر را محدودتر کنید (مثلاً بر اساس پروژه یا دیسیپلین).");
        }

        var bytes = _excelExportService.ExportDocumentRegister(items);

        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"DocumentRegister_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExportSelected(List<int> ids)
    {
        var (isRestricted, myDisciplineId) = await ResolveScopeAsync();

        var selected = await _service.GetRegisterByIdsAsync(ids, isRestricted, myDisciplineId);

        var bytes = _excelExportService.ExportDocumentRegister(selected);

        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"DocumentRegister_Selected_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveFilter(string name, string queryString, bool isDefault)
    {
        if (_currentUser.UserId.HasValue)
            await _savedFilterService.SaveAsync(_currentUser.UserId.Value, PageKey, name, queryString, isDefault);

        TempData["Message"] = $"Filter \"{name}\" saved";
        return Redirect("/DocumentRegister?" + queryString);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteFilter(int id)
    {
        if (_currentUser.UserId.HasValue)
            await _savedFilterService.DeleteAsync(id, _currentUser.UserId.Value);

        return RedirectToAction(nameof(Index));
    }
}
