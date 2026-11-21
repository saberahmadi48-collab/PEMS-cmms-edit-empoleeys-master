using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Web.Helpers;
using PEMS.Web.Security;

namespace PEMS.Web.Controllers;

/// <summary>
/// طبق «Instrument Equipment Passport»: رجیستر مرکزی ابزار دقیق (Master Instrument List)،
/// پاسپورت چاپی هر تگ، و تاریخچه‌ی کالیبراسیون (Append-only). Equipment والد باقی می‌ماند؛
/// Instrument فقط جزئیات اختصاصی ابزار دقیق را نگه می‌دارد.
/// </summary>
public class InstrumentController : Controller
{
    private readonly PEMSDbContext _context;
    private readonly INumberingService _numberingService;
    private readonly IWorkRequestService _workRequestService;
    private readonly ICurrentUserService _currentUser;
    private readonly IInstrumentService _instrumentService;

    private readonly IWorkOrderLifecycleService? _workOrderLifecycle;

    public InstrumentController(PEMSDbContext context, INumberingService numberingService,
        IWorkRequestService workRequestService, ICurrentUserService currentUser, IInstrumentService instrumentService,
        IWorkOrderLifecycleService? workOrderLifecycle = null)
    {
        _workOrderLifecycle = workOrderLifecycle;
        _context = context;
        _numberingService = numberingService;
        _workRequestService = workRequestService;
        _currentUser = currentUser;
        _instrumentService = instrumentService;
    }

    // طبق قرارداد پروژه: Overdue اگر سررسید گذشته، DueSoon اگر ظرف ۳۰ روز آینده، وگرنه OK.
    // Unknown اگر اصلاً سررسیدی ثبت نشده (نه اینکه حدس زده بشه Overdue/OK هست).
    // Passport/Export هنوز از این استفاده می‌کنند - فقط Master List (Index) به SQL منتقل شد.
    private static string CalibrationStatus(DateTime? nextDue)
    {
        if (!nextDue.HasValue) return "Unknown";
        if (nextDue.Value.Date < DateTime.Today) return "Overdue";
        if (nextDue.Value.Date <= DateTime.Today.AddDays(30)) return "DueSoon";
        return "OK";
    }

    // طبق سند «PEMS — Global Data Grid Standard»: قبلاً کل جدول Instruments با ToListAsync
    // (بدون Skip/Take) بارگذاری می‌شد و فیلتر Status/شمارنده‌های داشبورد در حافظه‌ی C# حساب
    // می‌شدند. اکنون فیلتر/صفحه‌بندی/شمارش همه در InstrumentService به SQL منتقل شده‌اند.
    public async Task<IActionResult> Index(string? status, string? q, int page = 1, int? pageSize = null)
    {
        ViewData["Title"] = "Master Instrument List";
        ViewData["Section"] = "CMMS Integration";

        // برای مودال "New Instrument" روی همین صفحه - قبلاً فقط در GET Create/Create.cshtml
        // جداگانه بارگذاری می‌شد؛ طبق درخواست دیگر صفحه‌ی جدا برای ایجاد وجود ندارد
        var equipmentIdsWithInstrument = await _context.Instruments.Select(x => x.EquipmentId).ToListAsync();
        ViewBag.Equipments = await _context.Equipments
            .Where(x => x.IsActive && !equipmentIdsWithInstrument.Contains(x.EquipmentId))
            .Select(x => new { x.EquipmentId, Label = x.TagNumber + " - " + x.Name })
            .OrderBy(x => x.Label)
            .ToListAsync();

        var normalizedPageSize = DataGridPageSize.Normalize(pageSize);
        var result = await _instrumentService.GetListAsync(page, normalizedPageSize, status, q);
        var dashboard = await _instrumentService.GetDashboardAsync(q);

        ViewBag.SelectedStatus = status;
        ViewBag.SearchTerm = q;
        ViewBag.OverdueCount = dashboard.OverdueCount;
        ViewBag.DueSoonCount = dashboard.DueSoonCount;
        ViewBag.TotalCount = dashboard.TotalCount;

        return View(result);
    }

    public async Task<IActionResult> Passport(int id)
    {
        ViewData["Title"] = "Instrument Passport";
        ViewData["Section"] = "CMMS Integration";
        ViewData["HelpKey"] = "Instrument.Passport";

        var instrument = await _context.Instruments
            .Include(x => x.Equipment)
            .FirstOrDefaultAsync(x => x.InstrumentId == id);

        if (instrument == null) return NotFound();

        ViewBag.CalibrationStatus = CalibrationStatus(instrument.NextCalibrationDue);

        var calibrationHistory = await _context.CalibrationHistories
            .Include(x => x.Technician)
            .Where(x => x.InstrumentId == id)
            .OrderByDescending(x => x.CalibrationDate)
            .ToListAsync();

        ViewBag.CalibrationHistoryList = calibrationHistory;
        ViewBag.LatestCalibration = calibrationHistory.FirstOrDefault();

        ViewBag.LatestMaintenance = await _context.MaintenanceHistories
            .Where(x => x.EquipmentId == instrument.EquipmentId)
            .OrderByDescending(x => x.ExecutionDate)
            .FirstOrDefaultAsync();

        // ==============================================================================
        // طبق CMMS Production Baseline v1 - CMMS-09 (I&C Integration): زنجیره‌ی
        // Instrument → ControlLoop → IOPoint → Alarm → CauseEffect - قبلاً این Entity ها
        // هیچ Controller/View ای نداشتند (فقط IOPoint از طریق AI Import پر می‌شد).
        // ==============================================================================
        var alarms = await _context.Alarms
            .Where(x => x.InstrumentId == id && x.IsActive)
            .OrderBy(x => x.AlarmType)
            .ToListAsync();

        var alarmIds = alarms.Select(x => x.AlarmId).ToList();
        var causeEffects = await _context.CauseEffects
            .Where(x => alarmIds.Contains(x.AlarmId) && x.IsActive)
            .ToListAsync();

        ViewBag.Alarms = alarms;
        ViewBag.CauseEffectsByAlarm = causeEffects.GroupBy(x => x.AlarmId).ToDictionary(g => g.Key, g => g.ToList());

        ViewBag.ControlLoop = await _context.ControlLoops
            .FirstOrDefaultAsync(x => x.InstrumentId == id && x.IsActive);

        ViewBag.IOPoints = await _context.IOPoints
            .Where(x => x.InstrumentId == id && x.IsActive)
            .ToListAsync();

        return View(instrument);
    }

    // طبق درخواست: بدون صفحه‌ی جدا - فرم ایجاد Instrument حالا یک مودال روی Index.cshtml است؛
    // این اکشن فقط POST را می‌پذیرد و همیشه به Index برمی‌گردد (نه یک صفحه‌ی GET جداگانه)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        int equipmentId, string instrumentType, string? loopNumber, string? system,
        string? signalClass, decimal? rangeMin, decimal? rangeMax,
        string? unit, string? signalType, string? processVariable, string? accuracy, int? calibrationInterval)
    {
        if (equipmentId <= 0)
        {
            TempData["Error"] = "Equipment is required";
            return RedirectToAction(nameof(Index));
        }

        // یک‌به‌یک با Equipment: هر تجهیز حداکثر یک پروفایل Instrument دارد؛ TagNumber/
        // Manufacturer/Model از همان Equipment می‌آیند، این‌جا دوباره پرسیده نمی‌شوند.
        var duplicate = await _context.Instruments.AnyAsync(x => x.EquipmentId == equipmentId);
        if (duplicate)
        {
            TempData["Error"] = "This equipment already has an instrument profile.";
            return RedirectToAction(nameof(Index));
        }

        var instrument = new Instrument
        {
            EquipmentId = equipmentId,
            InstrumentType = instrumentType ?? "",
            LoopNumber = loopNumber,
            System = system,
            SignalClass = signalClass,
            RangeMin = rangeMin,
            RangeMax = rangeMax,
            Unit = unit,
            SignalType = signalType,
            ProcessVariable = processVariable,
            Accuracy = accuracy,
            CalibrationInterval = calibrationInterval,
            IsActive = true,
            CreatedDate = DateTime.Now
        };

        await _context.Instruments.AddAsync(instrument);
        await _context.SaveChangesAsync();

        TempData["Message"] = "Instrument created";
        return RedirectToAction(nameof(Passport), new { id = instrument.InstrumentId });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        ViewData["Title"] = "Edit Instrument";
        ViewData["Section"] = "CMMS Integration";

        var instrument = await _context.Instruments
            .Include(x => x.Equipment)
            .FirstOrDefaultAsync(x => x.InstrumentId == id);
        if (instrument == null) return NotFound();

        return View(instrument);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int instrumentId, Instrument form)
    {
        var instrument = await _context.Instruments.FirstOrDefaultAsync(x => x.InstrumentId == instrumentId);
        if (instrument == null) return NotFound();

        instrument.InstrumentType = form.InstrumentType;
        instrument.LoopNumber = form.LoopNumber;
        instrument.System = form.System;
        instrument.SignalClass = form.SignalClass;
        instrument.RangeMin = form.RangeMin;
        instrument.RangeMax = form.RangeMax;
        instrument.Unit = form.Unit;
        instrument.SignalType = form.SignalType;
        instrument.ProcessVariable = form.ProcessVariable;
        instrument.Accuracy = form.Accuracy;
        instrument.OutputSignal = form.OutputSignal;
        instrument.PowerSupply = form.PowerSupply;
        instrument.Communication = form.Communication;
        instrument.IPRating = form.IPRating;
        instrument.ProcessConnection = form.ProcessConnection;
        instrument.ElectricalConnection = form.ElectricalConnection;
        instrument.HazardousArea = form.HazardousArea;
        instrument.ExProtection = form.ExProtection;
        instrument.Zone = form.Zone;
        instrument.GasGroup = form.GasGroup;
        instrument.TempClass = form.TempClass;
        instrument.FailAction = form.FailAction;
        instrument.AlarmSetpoint = form.AlarmSetpoint;
        instrument.TripSetpoint = form.TripSetpoint;
        instrument.SIL = form.SIL;
        instrument.JunctionBox = form.JunctionBox;
        instrument.MarshallingCabinet = form.MarshallingCabinet;
        instrument.CableNumber = form.CableNumber;
        instrument.BarrierRelay = form.BarrierRelay;
        instrument.IOCardChannel = form.IOCardChannel;
        instrument.SystemCabinet = form.SystemCabinet;
        instrument.CalibrationInterval = form.CalibrationInterval;
        instrument.CurrentStatus = string.IsNullOrWhiteSpace(form.CurrentStatus) ? instrument.CurrentStatus : form.CurrentStatus;

        await _context.SaveChangesAsync();

        TempData["Message"] = "Instrument updated";
        return RedirectToAction(nameof(Passport), new { id = instrumentId });
    }

    // ==============================================================================
    // طبق الگوی CalibrationHistory: Append-only - هیچ Update/Delete روی رکورد قبلی نداریم.
    // با ثبت هر کالیبراسیون، Instrument.LastCalibrationDate/NextCalibrationDue کش می‌شود.
    // ==============================================================================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddCalibration(
        int instrumentId, DateTime calibrationDate, string calibrationType, string? standardReference,
        string? asFound, string? asLeft, string result, int? technicianEmployeeId, string? certificateNo,
        string? remarks)
    {
        var instrument = await _context.Instruments.FirstOrDefaultAsync(x => x.InstrumentId == instrumentId);
        if (instrument == null) return NotFound();

        DateTime? nextDue = instrument.CalibrationInterval.HasValue
            ? calibrationDate.AddDays(instrument.CalibrationInterval.Value)
            : null;

        await _context.CalibrationHistories.AddAsync(new CalibrationHistory
        {
            InstrumentId = instrumentId,
            CalibrationDate = calibrationDate,
            CalibrationType = string.IsNullOrWhiteSpace(calibrationType) ? "Routine" : calibrationType,
            StandardReference = standardReference,
            AsFound = asFound,
            AsLeft = asLeft,
            Result = string.IsNullOrWhiteSpace(result) ? "Pass" : result,
            TechnicianEmployeeId = technicianEmployeeId,
            CertificateNo = certificateNo,
            NextDue = nextDue,
            Remarks = remarks,
            CreatedDate = DateTime.Now
        });

        // کش سررسید روی خودِ Instrument برای نمایش سریع در Master List بدون Join اضافی
        instrument.LastCalibrationDate = calibrationDate;
        instrument.NextCalibrationDue = nextDue;

        await _context.SaveChangesAsync();

        TempData["Message"] = "Calibration recorded";
        return RedirectToAction(nameof(Passport), new { id = instrumentId });
    }

    // ==============================================================================
    // طبق CMMS-09: رجیستر آلارم‌های واقعی این Instrument (High/Low/HighHigh/LowLow) -
    // Instrument.AlarmSetpoint/TripSetpoint فقط دو مقدار خلاصه‌ان؛ این‌جا لیست کامل با
    // Priority/Action مجزا برای هر آلارم ثبت می‌شود.
    // ==============================================================================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddAlarm(
        int instrumentId, string alarmTag, string alarmType, string priority,
        decimal? setPoint, string? unit, string? description, string? action)
    {
        var instrument = await _context.Instruments.FirstOrDefaultAsync(x => x.InstrumentId == instrumentId);
        if (instrument == null) return NotFound();

        await _context.Alarms.AddAsync(new Alarm
        {
            InstrumentId = instrumentId,
            AlarmTag = alarmTag,
            AlarmType = alarmType,
            Priority = priority,
            SetPoint = setPoint,
            Unit = unit,
            Description = description,
            Action = action,
            IsActive = true,
            CreatedDate = DateTime.Now
        });

        await _context.SaveChangesAsync();

        TempData["Message"] = "آلارم ثبت شد.";
        return RedirectToAction(nameof(Passport), new { id = instrumentId });
    }

    // طبق §27 Architecture Gate: زنجیره‌ی Alarm → CauseEffect (Cause/Effect/ActionType شامل
    // Trip/Shutdown) - قبلاً این Entity هیچ راه ورودی نداشت.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddCauseEffect(
        int alarmId, int instrumentId, string cause, string effect, string? actionType,
        string? tripSignal, string? description)
    {
        var alarmExists = await _context.Alarms.AnyAsync(x => x.AlarmId == alarmId);
        if (!alarmExists) return NotFound();

        await _context.CauseEffects.AddAsync(new CauseEffect
        {
            AlarmId = alarmId,
            Cause = cause,
            Effect = effect,
            ActionType = actionType,
            TripSignal = tripSignal,
            Description = description,
            IsActive = true,
            CreatedDate = DateTime.Now
        });

        await _context.SaveChangesAsync();

        TempData["Message"] = "Cause & Effect ثبت شد.";
        return RedirectToAction(nameof(Passport), new { id = instrumentId });
    }

    // طبق §27: Instrument → ControlLoop - هر Instrument حداکثر یک Loop اصلی دارد (همون
    // الگوی یک‌به‌یک Instrument↔Equipment که در این پروژه جا افتاده).
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignControlLoop(
        int instrumentId, string loopNumber, string? description, string? processArea,
        string? controllerType, string? controllerSystem)
    {
        var instrument = await _context.Instruments.FirstOrDefaultAsync(x => x.InstrumentId == instrumentId);
        if (instrument == null) return NotFound();

        var existing = await _context.ControlLoops.FirstOrDefaultAsync(x => x.InstrumentId == instrumentId && x.IsActive);
        if (existing != null)
        {
            TempData["Error"] = "این Instrument از قبل به یک Control Loop وصل است.";
            return RedirectToAction(nameof(Passport), new { id = instrumentId });
        }

        await _context.ControlLoops.AddAsync(new ControlLoop
        {
            LoopNumber = loopNumber,
            Description = description,
            ProcessArea = processArea,
            ControllerType = controllerType,
            ControllerSystem = controllerSystem,
            InstrumentId = instrumentId,
            IsActive = true,
            CreatedDate = DateTime.Now
        });

        await _context.SaveChangesAsync();

        TempData["Message"] = "Control Loop ثبت شد.";
        return RedirectToAction(nameof(Passport), new { id = instrumentId });
    }

    // طبق §27 و همون الگوی Alert → WorkRequest که در CMMS-07 (Inspection) ساخته شد: اقدام
    // یک‌کلیکی انسانی - چون هیچ منبع رویداد زنده‌ای (SCADA/DCS Feed) در سیستم وجود نداره که
    // بشه ازش خودکار WorkRequest ساخت.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RaiseWorkRequestFromCauseEffect(int causeEffectId)
    {
        var causeEffect = await _context.CauseEffects
            .Include(x => x.Alarm).ThenInclude(a => a!.Instrument).ThenInclude(i => i!.Equipment)
            .FirstOrDefaultAsync(x => x.CauseEffectId == causeEffectId);

        if (causeEffect?.Alarm?.Instrument == null) return NotFound();

        var employeeId = await GetCurrentEmployeeIdAsync();
        if (!employeeId.HasValue)
        {
            TempData["Error"] = "کارمند مرتبط با این کاربر پیدا نشد.";
            return RedirectToAction(nameof(Passport), new { id = causeEffect.Alarm.InstrumentId });
        }

        var instrument = causeEffect.Alarm.Instrument;
        var title = $"I&C Alarm/Trip - {causeEffect.Alarm.AlarmTag} - Equipment #{instrument.EquipmentId}";
        var reportedCondition = $"آلارم {causeEffect.Alarm.AlarmTag} ({causeEffect.Alarm.AlarmType}): علت «{causeEffect.Cause}» → نتیجه «{causeEffect.Effect}»" +
            (string.IsNullOrWhiteSpace(causeEffect.ActionType) ? "" : $" - اقدام: {causeEffect.ActionType}") +
            (string.IsNullOrWhiteSpace(causeEffect.TripSignal) ? "" : $" (سیگنال: {causeEffect.TripSignal})") + ".";

        var priority = causeEffect.Alarm.Priority is "Critical" or "High" ? causeEffect.Alarm.Priority : "High";

        var (success, error, workRequestId) = await _workRequestService.CreateAsync(
            title, reportedCondition, employeeId.Value, instrument.EquipmentId, null, null,
            reportedCondition, priority, null);

        TempData[success ? "Message" : "Error"] = success ? "Work Request ساخته شد." : error;

        if (success && workRequestId.HasValue)
            return RedirectToAction("Details", "WorkRequest", new { id = workRequestId });

        return RedirectToAction(nameof(Passport), new { id = causeEffect.Alarm.InstrumentId });
    }

    private async Task<int?> GetCurrentEmployeeIdAsync()
    {
        if (!_currentUser.UserId.HasValue) return null;
        return await _context.Users.Where(u => u.Id == _currentUser.UserId.Value).Select(u => u.EmployeeId).FirstOrDefaultAsync();
    }

    // طبق درخواست سند: از یک کالیبراسیون/PM سررسیدشده باید بشه یک Work Order واقعی ساخت -
    // دقیقاً همون الگوی MaintenancePlanController.GenerateWorkOrder
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateCalibrationWorkOrder(int instrumentId)
    {
        var instrument = await _context.Instruments
            .Include(x => x.Equipment)
            .FirstOrDefaultAsync(x => x.InstrumentId == instrumentId);

        if (instrument == null) return NotFound();

        var workOrderNumber = await _numberingService.GetNextNumberAsync("WorkOrder");

        var workOrder = new WorkOrder
        {
            EquipmentId = instrument.EquipmentId,
            WorkOrderNumber = workOrderNumber,
            WorkOrderType = "Preventive",
            Priority = "Medium",
            Status = "Open",
            Description = $"Calibration due for instrument {instrument.Equipment?.TagNumber}",
            IsActive = true,
            CreatedDate = DateTime.Now
        };

        await _context.WorkOrders.AddAsync(workOrder);
        await _context.SaveChangesAsync();

        // Phase 3B: Work Order تولیدشده حاکمیتی می‌شود (Snapshot مبدأ از کاربر + شروع چرخه‌عمر)؛ بدون Binding بی‌اثر
        if (_workOrderLifecycle != null)
        {
            var employeeId = await GetCurrentEmployeeIdAsync();
            var (ok, govError, _) = await _workOrderLifecycle.GovernPendingNewAsync(new PEMS.Application.DTOs.WorkOrderActor(employeeId, _currentUser.Roles));
            if (!ok) { TempData["Error"] = govError; return RedirectToAction("Details", "WorkOrder", new { id = workOrder.WorkOrderId }); }
        }

        TempData["Message"] = $"Work order {workOrderNumber} generated for calibration";
        return RedirectToAction("Details", "WorkOrder", new { id = workOrder.WorkOrderId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var instrument = await _context.Instruments.FirstOrDefaultAsync(x => x.InstrumentId == id);
        if (instrument == null) return NotFound();

        instrument.IsActive = !instrument.IsActive;
        await _context.SaveChangesAsync();

        TempData["Message"] = instrument.IsActive ? "Instrument reactivated" : "Instrument deactivated";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Export()
    {
        var instruments = await _context.Instruments
            .Include(x => x.Equipment)
            .Where(x => x.IsActive)
            .OrderBy(x => x.Equipment!.TagNumber)
            .ToListAsync();

        using var workbook = new ClosedXML.Excel.XLWorkbook();
        var sheet = workbook.Worksheets.Add("Instruments");
        sheet.AddStyledHeaderRow("Equipment", "InstrumentType", "LoopNumber", "System",
            "Manufacturer", "Model", "CalibrationInterval", "LastCalibrationDate", "NextCalibrationDue", "Status");

        int row = 2;
        foreach (var i in instruments)
        {
            sheet.Cell(row, 1).Value = i.Equipment?.TagNumber;
            sheet.Cell(row, 2).Value = i.InstrumentType;
            sheet.Cell(row, 3).Value = i.LoopNumber;
            sheet.Cell(row, 4).Value = i.System;
            sheet.Cell(row, 5).Value = i.Equipment?.Manufacturer;
            sheet.Cell(row, 6).Value = i.Equipment?.Model;
            sheet.Cell(row, 7).Value = i.CalibrationInterval;
            if (i.LastCalibrationDate.HasValue) sheet.Cell(row, 8).Value = i.LastCalibrationDate.Value;
            if (i.NextCalibrationDue.HasValue) sheet.Cell(row, 9).Value = i.NextCalibrationDue.Value;
            sheet.Cell(row, 10).Value = CalibrationStatus(i.NextCalibrationDue);
            row++;
        }

        sheet.FinalizeExportSheet();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        return File(stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Instruments_{DateTime.Now:yyyyMMdd}.xlsx");
    }

}
