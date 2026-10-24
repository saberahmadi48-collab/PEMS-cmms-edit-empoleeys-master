using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;

namespace PEMS.Web.Controllers;

/// <summary>
/// طبق درخواست: هر بخش گزارش‌گیری مخصوص خودش را دارد، با فیلترهای گوناگون (پروژه، دیسیپلین،
/// بازه‌ی زمانی، و فیلترهای مخصوص هر بخش مثل وضعیت/اولویت).
/// </summary>
public class ReportController : Controller
{
    private readonly ICorrespondenceReportService _service;
    private readonly IEngineeringReportService _engineeringReportService;
    private readonly IVDRReportService _vdrReportService;
    private readonly ICmmsReportService _cmmsReportService;
    private readonly IProcurementReportService _procurementReportService;
    private readonly IOperationsReportService _operationsReportService;
    private readonly PEMSDbContext _context;
    private readonly IPermissionService _permissionService;
    private readonly PEMS.Web.Security.ICurrentUserService _currentUser;

    public ReportController(ICorrespondenceReportService service, IEngineeringReportService engineeringReportService,
        IVDRReportService vdrReportService, ICmmsReportService cmmsReportService,
        IProcurementReportService procurementReportService, IOperationsReportService operationsReportService,
        PEMSDbContext context, IPermissionService permissionService, PEMS.Web.Security.ICurrentUserService currentUser)
    {
        _permissionService = permissionService;
        _currentUser = currentUser;
        _service = service;
        _engineeringReportService = engineeringReportService;
        _vdrReportService = vdrReportService;
        _cmmsReportService = cmmsReportService;
        _procurementReportService = procurementReportService;
        _operationsReportService = operationsReportService;
        _context = context;
    }

    private async Task LoadCommonFiltersAsync(int? projectId, int? disciplineId)
    {
        ViewBag.Projects = await _context.Projects.Where(x => x.IsActive)
            .Select(x => new { x.ProjectId, x.ProjectName }).ToListAsync();

        ViewBag.Disciplines = await _context.Disciplines.Where(x => x.IsActive)
            .Select(x => new { x.DisciplineId, x.Name }).ToListAsync();

        ViewBag.SelectedProjectId = projectId;
        ViewBag.SelectedDisciplineId = disciplineId;
    }

    public async Task<IActionResult> Index(int? projectId, int? disciplineId, DateTime? fromDate, DateTime? toDate)
    {
        ViewData["Title"] = "Correspondence Reports";
        ViewData["Section"] = "Correspondence Management";

        var report = await _service.GetReportAsync(projectId, disciplineId, fromDate, toDate);

        await LoadCommonFiltersAsync(projectId, disciplineId);
        ViewBag.FromDate = fromDate;
        ViewBag.ToDate = toDate;

        return View(report);
    }

    // طبق درخواست: گزارش‌گیری از بخش‌های مختلف پروژه (Engineering Documents/MDR)
    public async Task<IActionResult> Engineering(int? projectId, int? disciplineId, DateTime? fromDate, DateTime? toDate)
    {
        ViewData["Title"] = "Engineering Documents Report";
        ViewData["Section"] = "Engineering Management";

        var report = await _engineeringReportService.GetReportAsync(projectId, disciplineId, fromDate, toDate);

        await LoadCommonFiltersAsync(projectId, disciplineId);
        ViewBag.FromDate = fromDate;
        ViewBag.ToDate = toDate;

        return View(report);
    }

    // طبق درخواست: گزارش‌گیری از بخش VDR
    public async Task<IActionResult> Vdr(int? projectId, int? disciplineId, DateTime? fromDate, DateTime? toDate)
    {
        ViewData["Title"] = "VDR Report";
        ViewData["Section"] = "Engineering Management";

        var report = await _vdrReportService.GetReportAsync(projectId, disciplineId, fromDate, toDate);

        await LoadCommonFiltersAsync(projectId, disciplineId);
        ViewBag.FromDate = fromDate;
        ViewBag.ToDate = toDate;

        return View(report);
    }

    // طبق درخواست: گزارش‌گیری از بخش CMMS (Work Order)
    public async Task<IActionResult> Cmms(string? status, string? priority, DateTime? fromDate, DateTime? toDate)
    {
        ViewData["Title"] = "CMMS / Work Order Report";
        ViewData["Section"] = "CMMS Integration";

        var report = await _cmmsReportService.GetReportAsync(status, priority, fromDate, toDate);

        ViewBag.SelectedStatus = status;
        ViewBag.SelectedPriority = priority;
        ViewBag.FromDate = fromDate;
        ViewBag.ToDate = toDate;

        return View(report);
    }

    // طبق درخواست: گزارش‌گیری از بخش تدارکات (Procurement Package/MR/MRQ)
    public async Task<IActionResult> Procurement(int? projectId, int? disciplineId, DateTime? fromDate, DateTime? toDate, int page = 1)
    {
        // P0-09 (D-OPEN-02، تصمیم مالک): ماژول «Report» مجوزی ندارد؛ این گزارش Package/MR/MRQ و زنجیره‌ی
        // Tender→Contract→PO را نشان می‌دهد، پس نزدیک‌ترین مجوز موجود ProcurementPackage.View است.
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "ProcurementPackage", "View"))
            return Forbid();

        ViewData["Title"] = "Procurement Report";
        ViewData["Section"] = "Engineering Management";

        var report = await _procurementReportService.GetReportAsync(projectId, disciplineId, fromDate, toDate);

        // طبق گزارش سخت‌افزاری نهایی (Gap ۵): جدول زنجیره‌ی Tender→Contract→PO→NTP، جداگانه و
        // صفحه‌بندی‌شده - همان فیلترهای بالا، بدون تغییر رفتار GetReportAsync موجود
        ViewBag.Pipeline = await _procurementReportService.GetPipelineAsync(projectId, disciplineId, fromDate, toDate, page);

        await LoadCommonFiltersAsync(projectId, disciplineId);
        ViewBag.FromDate = fromDate;
        ViewBag.ToDate = toDate;

        return View(report);
    }

    // ==============================================================================
    // طبق «PEMS Operations Phase 2A — Foundational Hardening» بخش ۲۱: پایه‌ی حداقلی گزارش‌گیری
    // Operations - فقط Day/Month/CustomRange (نه Week - چون WeekStartDay هنوز توسط کسب‌وکار
    // تایید نشده و ReportingPeriod.Resolve عمداً بدون مقدار پیش‌فرض برای آن Exception می‌دهد؛
    // افزودن Week به این فرم قبل از آن تصمیم فقط یک مسیر همیشه-خطا می‌ساخت). Gross/Net عمداً
    // در این گزارش نیست - بند ۴ سند فاز.
    // ==============================================================================
    public async Task<IActionResult> Operations(string periodType, DateTime? anchorOrFrom, DateTime? toDate, int? productionWorkCenterId, int? materialItemId)
    {
        ViewData["Title"] = "Operations Report";
        ViewData["Section"] = "Operations & Production";

        var normalizedPeriodType = periodType switch
        {
            "Month" => PeriodType.Month,
            "CustomRange" => PeriodType.CustomRange,
            _ => PeriodType.Day
        };

        var summary = await _operationsReportService.GetSummaryAsync(
            normalizedPeriodType, anchorOrFrom, toDate, weekStartDay: null, productionWorkCenterId, materialItemId);

        ViewBag.PeriodType = normalizedPeriodType.ToString();
        ViewBag.AnchorOrFrom = anchorOrFrom;
        ViewBag.ToDate = toDate;

        ViewBag.WorkCenters = await _context.ProductionWorkCenters.Where(x => x.IsActive)
            .OrderBy(x => x.Code).Select(x => new { x.ProductionWorkCenterId, x.Code, x.Name }).ToListAsync();
        ViewBag.SelectedWorkCenterId = productionWorkCenterId;

        ViewBag.MaterialItems = await _context.MaterialItems.Where(x => x.IsActive)
            .OrderBy(x => x.MaterialCode).Select(x => new { x.MaterialItemId, x.MaterialCode }).ToListAsync();
        ViewBag.SelectedMaterialItemId = materialItemId;

        return View(summary);
    }
}
