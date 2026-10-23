using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Web.Security;

namespace PEMS.Web.Controllers;

/// <summary>
/// طبق PEMS_CMMS_MAINTENANCE_ORGANIZATION_BLUEPRINT.md بخش ۱۴: ترکیب اجرایی نفرات برای
/// یک فعالیت مشخص - برخلاف Team (واحد سازمانی پایدار)، می‌تواند به یک WorkOrder مشخص وصل
/// باشد یا مستقل/پایدار بماند. همون الگوی TeamController (منطق سه‌حالته از ICrewService).
/// </summary>
/// <remarks>
/// طبق «PEMS — P1 Organization &amp; Master Data Authorization Hardening»: این Controller قبلاً
/// هیچ بررسی مجوزی نداشت - CreateCrew/AddMember/RemoveMember برای هر کاربر احرازهویت‌شده‌ای
/// باز بودند. ماژول جدید "Crew" - برخلاف Team، هیچ اکشن دیگری این ماژول را مصرف نمی‌کند
/// (بدون یک GetCrewMembers مشابه)، پس همون نقش‌های مدیریت CMMS کافی است.
///
/// طبق «PEMS — P1 Object-Level Authorization &amp; Data Scope Hardening»: برخلاف TeamController،
/// عمداً هیچ بررسی Object-Scope (مثلاً Discipline) این‌جا اضافه نشد - Crew Entity هیچ
/// DisciplineId/DepartmentId مستقیمی ندارد؛ تنها ارتباط بالقوه (WorkOrderId) هم Nullable است
/// (طبق کامنت خودِ Entity: "WorkOrderId=null یعنی Crew مستقل/پایدار") و حتی وقتی پر باشد فقط با
/// دو پرش غیرمستقیم (Crew → WorkOrder → Equipment → DisciplineId) به یک Discipline می‌رسد.
///
/// طبق تایید صریح صاحب محصول (بعد از این ممیزی): این یک طراحی عمدی است، نه فقط یک استنتاج از
/// نبود Schema - Crew واقعاً باید بتواند چند-دیسیپلینه باشد (مثلاً یک فعالیت مشترک که هم تکنسین
/// مکانیک هم برق نیاز دارد). بنابراین هیچ‌وقت نباید یک قاعده‌ی Object-Scope (Discipline/
/// Department/...) روی Crew اعمال شود - نه امروز، نه در آینده، مگر با یک تصمیم محصولی صریح
/// جدید. این یافته بسته شده تلقی می‌شود (نه یک نقص باز)؛ فقط سطح Module Permission (Crew.View/
/// Create/Edit، طبق پاس قبلی) لایه‌ی مجوز کافی و نهایی برای Crew است.
/// </remarks>
public class CrewController : Controller
{
    private readonly PEMSDbContext _context;
    private readonly ICrewService _crewService;
    private readonly ICurrentUserService _currentUser;
    private readonly IPermissionService _permissionService;
    private readonly IWorkOrderSectionGuard? _sectionGuard;

    // sectionGuard اختیاری است تا سازنده‌ی ۴-پارامتری قبلی (تست‌ها) سازگار بماند؛ DI همیشه آن را تأمین می‌کند.
    public CrewController(PEMSDbContext context, ICrewService crewService,
        ICurrentUserService currentUser, IPermissionService permissionService, IWorkOrderSectionGuard? sectionGuard = null)
    {
        _sectionGuard = sectionGuard;
        _context = context;
        _crewService = crewService;
        _currentUser = currentUser;
        _permissionService = permissionService;
    }

    public async Task<IActionResult> Index()
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Crew", "View"))
            return Forbid();

        ViewData["Title"] = "Crews";
        ViewData["Section"] = "CMMS Integration";

        var crews = await _context.Crews
            .Include(x => x.WorkOrder)
            .Include(x => x.Members.Where(m => m.IsActive)).ThenInclude(m => m.Employee)
            .Include(x => x.Members.Where(m => m.IsActive)).ThenInclude(m => m.Role)
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync();

        await LoadDropdownsAsync();

        return View(crews);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCrew(string code, string name, int? workOrderId)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Crew", "Create"))
            return Forbid();

        var denied = await GuardWorkOrderCrewAsync(workOrderId, WoSectionAction.Add, code);
        if (denied != null) return denied;

        var (success, error, crewId) = await _crewService.CreateAsync(code, name, workOrderId);
        if (success) await RecordCrewChangeAsync(workOrderId, WoSectionAction.Add, crewId?.ToString(), null, $"Code={code}; Name={name}");

        TempData[success ? "Message" : "Error"] = success ? "Crew ساخته شد." : error;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddMember(int crewId, int employeeId, int roleId)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Crew", "Edit"))
            return Forbid();

        var crewWorkOrderId = await _context.Crews.Where(x => x.CrewId == crewId).Select(x => x.WorkOrderId).FirstOrDefaultAsync();
        var denied = await GuardWorkOrderCrewAsync(crewWorkOrderId, WoSectionAction.Add, crewId.ToString());
        if (denied != null) return denied;

        try
        {
            await _crewService.AddOrReactivateMemberAsync(crewId, employeeId, roleId);
            await RecordCrewChangeAsync(crewWorkOrderId, WoSectionAction.Add, crewId.ToString(), null, $"EmployeeId={employeeId}; RoleId={roleId}");
            TempData["Message"] = "عضو اضافه شد.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveMember(int crewMemberId)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Crew", "Edit"))
            return Forbid();

        var crewWorkOrderId = await _context.CrewMembers.Where(x => x.CrewMemberId == crewMemberId).Select(x => x.Crew!.WorkOrderId).FirstOrDefaultAsync();
        var denied = await GuardWorkOrderCrewAsync(crewWorkOrderId, WoSectionAction.Remove, crewMemberId.ToString());
        if (denied != null) return denied;

        try
        {
            await _crewService.RemoveMemberAsync(crewMemberId);
            await RecordCrewChangeAsync(crewWorkOrderId, WoSectionAction.Remove, crewMemberId.ToString(), $"CrewMemberId={crewMemberId}; Active=true", "Active=false");
            TempData["Message"] = "عضو از Crew خارج شد.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// H27: Crew وصل به یک Work Order همان ماتریس «بخش × فاز × مجوز» Work Order را رعایت می‌کند (پس از Operations Issue و پس از Closed فقط‌خواندنی).
    /// Crew مستقل (بدون Work Order) دست‌نخورده می‌ماند. null = مجاز؛ در غیر این صورت پیام مشخص + Toast (و 400/403 برای JSON).
    /// </summary>
    private async Task<IActionResult?> GuardWorkOrderCrewAsync(int? workOrderId, WoSectionAction action, string? recordRef)
    {
        if (workOrderId == null || _sectionGuard == null) return null;

        int? employeeId = null;
        if (_currentUser.UserId is int uid)
            employeeId = await _context.Users.Where(u => u.Id == uid).Select(u => u.EmployeeId).FirstOrDefaultAsync();

        var caller = new WoCaller(_currentUser.UserId, _currentUser.UserName, employeeId, _currentUser.Roles, HttpContext.Connection.RemoteIpAddress?.ToString());
        var result = await _sectionGuard.AuthorizeAsync(workOrderId.Value, WoSection.Crew, action, caller, null, recordRef);
        if (result.Allowed) return null;

        var fa = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fa";
        var message = result.Message(fa);
        Response.Headers["X-PEMS-Policy"] = result.Code;
        var wantsJson = Request.Headers.XRequestedWith == "XMLHttpRequest" || Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);
        if (wantsJson) return StatusCode(result.HttpStatus, new { code = result.Code, error = message, phase = result.Phase.ToString() });

        TempData["Error"] = message;
        TempData["PolicyCode"] = result.Code;
        return RedirectToAction(nameof(Index));
    }

    private async Task RecordCrewChangeAsync(int? workOrderId, WoSectionAction action, string? recordRef, string? before, string? after)
    {
        if (workOrderId == null || _sectionGuard == null) return;
        try
        {
            int? employeeId = null;
            if (_currentUser.UserId is int uid)
                employeeId = await _context.Users.Where(u => u.Id == uid).Select(u => u.EmployeeId).FirstOrDefaultAsync();
            var caller = new WoCaller(_currentUser.UserId, _currentUser.UserName, employeeId, _currentUser.Roles, HttpContext.Connection.RemoteIpAddress?.ToString());
            await _sectionGuard.RecordChangeAsync(workOrderId.Value, WoSection.Crew, action, caller, recordRef, null, before, after);
        }
        catch (Exception) { _context.ChangeTracker.Clear(); /* ممیزی بهترین‌تلاش است؛ جهش قبلاً ذخیره شده */ }
    }

    private async Task LoadDropdownsAsync()
    {
        ViewBag.Employees = await _context.Employees.Where(x => x.IsActive)
            .Select(x => new { x.EmployeeId, FullName = x.FirstName + " " + x.LastName }).ToListAsync();

        ViewBag.MaintenanceRoles = await _context.MaintenanceRoles.Where(x => x.IsActive)
            .Select(x => new { x.MaintenanceRoleId, x.Name }).ToListAsync();

        ViewBag.WorkOrders = await _context.WorkOrders
            .Where(x => x.IsActive && x.Status != "Closed")
            .OrderByDescending(x => x.CreatedDate)
            .Select(x => new { x.WorkOrderId, x.WorkOrderNumber })
            .ToListAsync();
    }
}
