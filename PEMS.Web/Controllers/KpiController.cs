using Microsoft.AspNetCore.Mvc;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Web.Models;
using PEMS.Web.Security;

namespace PEMS.Web.Controllers;

/// <summary>
/// طبق CMMS Production Baseline v1 - CMMS-08 (Cost &amp; KPI): داشبورد یکپارچه‌ی Maintenance
/// KPI + Cost KPI (طبق بند ۲۴: نباید همه‌ی KPI ها روی یک داشبورد بار شوند - این‌جا فقط دو
/// دسته‌ی هم‌نام با عنوان فاز). معیارهای Reliability از IReliabilityService (CMMS-06) خونده
/// می‌شوند - دوباره‌سازی نمی‌شوند.
/// </summary>
public class KpiController : Controller
{
    // سقف بازه‌ی داشبورد مدیریتی (روز)؛ بازه‌ی بزرگ‌تر کوئری‌ها را بی‌دلیل سنگین می‌کند
    private const int MaxPeriodDays = 366;

    private readonly IKpiService _kpiService;
    private readonly IReliabilityService _reliabilityService;
    private readonly ICurrentUserService _currentUser;
    private readonly IPermissionService _permissionService;
    private readonly IExecutiveKpiService? _executive;
    private readonly IKpiTargetService? _targets;

    public KpiController(IKpiService kpiService, IReliabilityService reliabilityService,
        ICurrentUserService currentUser, IPermissionService permissionService,
        IExecutiveKpiService? executive = null, IKpiTargetService? targets = null)
    {
        _kpiService = kpiService;
        _reliabilityService = reliabilityService;
        _currentUser = currentUser;
        _permissionService = permissionService;
        _executive = executive;
        _targets = targets;
    }

    // همان ماژول مجوز "Reliability" موجود بازاستفاده شد (نه یک ماژول جدید) - این داشبورد هم
    // فقط-مشاهده است و از همان الگوی ReliabilityController پیروی می‌کند.
    public async Task<IActionResult> Index(DateTime? fromDate, DateTime? toDate)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Reliability", "View"))
            return Forbid();

        ViewData["Title"] = "KPI Dashboard";
        ViewData["Section"] = "CMMS Integration";

        var cost = await _kpiService.GetCostKpisAsync(fromDate, toDate);
        var maintenance = await _kpiService.GetMaintenanceKpisAsync(fromDate, toDate);
        var fleet = await _reliabilityService.GetFleetReliabilityAsync();

        var dashboard = new KpiDashboardDto
        {
            Cost = cost,
            Maintenance = maintenance,
            Reliability = new ReliabilitySummaryDto
            {
                EquipmentWithFailuresCount = fleet.Count,
                TotalFailureCount = fleet.Sum(x => x.FailureCount),
                AverageAvailabilityPercent = fleet.Any(x => x.AvailabilityPercent.HasValue)
                    ? fleet.Where(x => x.AvailabilityPercent.HasValue).Average(x => x.AvailabilityPercent!.Value)
                    : null
            },
            FromDate = fromDate,
            ToDate = toDate
        };

        return View(dashboard);
    }

    // داشبورد مدیریتی: فقط شاخص‌هایی که داده‌ی واقعی دارند (KpiCatalog)، با وضعیت نسبت به هدف. همان ماژول مجوز "Reliability".
    [HttpGet]
    public async Task<IActionResult> Executive(DateTime? fromDate, DateTime? toDate)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Reliability", "View"))
            return Forbid();
        if (_executive == null || _targets == null) return NotFound();

        var to = (toDate ?? DateTime.Today).Date;
        var from = (fromDate ?? to.AddDays(-29)).Date;
        if (from > to) (from, to) = (to, from);
        if ((to - from).TotalDays > MaxPeriodDays) from = to.AddDays(-MaxPeriodDays);

        ViewData["Title"] = "Executive KPI Dashboard";
        ViewData["Section"] = "CMMS Integration";

        return View(new ExecutiveKpiPageViewModel
        {
            Dashboard = await _executive.GetAsync(from, to),
            Targets = await _targets.GetEffectiveAsync(),
            CanEditTargets = await _permissionService.HasPermissionAsync(_currentUser.Roles, "Reliability", "Edit"),
            From = from,
            To = to
        });
    }

    // ذخیره‌ی هدف یک شاخص (خالی = بدون هدف). مجوز Reliability.Edit؛ اعتبارسنجی مقدار در IKpiTargetService.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveTarget(string key, string? target, string? warn, DateTime? fromDate, DateTime? toDate)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Reliability", "Edit"))
            return Forbid();
        if (_targets == null) return NotFound();

        var route = new { fromDate = fromDate?.ToString("yyyy-MM-dd"), toDate = toDate?.ToString("yyyy-MM-dd") };

        if (!TryParseOptional(target, out var targetValue) || !TryParseOptional(warn, out var warnValue))
        {
            TempData["KpiError"] = "Kpi_Err_NotANumber";
            return RedirectToAction(nameof(Executive), route);
        }

        var result = await _targets.SaveAsync(key, targetValue, warnValue);
        if (result.Success) TempData["KpiMessage"] = "Kpi_Msg_TargetSaved";
        else TempData["KpiError"] = result.Errors[0].Key;
        return RedirectToAction(nameof(Executive), route);
    }

    // اعداد با نقطه‌ی اعشار ثابت (فیلد type=number مرورگر همیشه همین قالب را می‌فرستد)؛ خالی = null
    private static bool TryParseOptional(string? text, out decimal? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!decimal.TryParse(text.Trim(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            return false;
        value = parsed;
        return true;
    }
}
