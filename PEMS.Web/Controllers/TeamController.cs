using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Web.Security;

namespace PEMS.Web.Controllers;

/// <summary>
/// طبق سند معماری Employee/Team/Labor Cost: جایگزین MaintenanceCrewController - Employee
/// دیگر این‌جا تکرار نمی‌شود (فقط EmployeeId ارجاع)، و منطق سه‌حالته‌ی عضویت (Reject/
/// Reactivate/New) از ITeamService می‌آید، نه مستقیم در Controller.
/// </summary>
/// <remarks>
/// طبق «PEMS — P1 Organization &amp; Master Data Authorization Hardening»: این Controller قبلاً
/// هیچ بررسی مجوزی نداشت - CreateTeam/AddMember/RemoveMember/MoveMember (که عضویت تیم را تغییر
/// می‌دهند و روی تخصیص کار/مسئولیت اجرایی اثر می‌گذارند) برای هر کاربر احرازهویت‌شده‌ای باز
/// بودند. ماژول جدید "Team" چون هیچ ماژول موجودی این قابلیت را پوشش نمی‌دهد.
/// AddMember/RemoveMember/MoveMember با "Edit" گیت شدند (نه Approve) چون فقط ترکیب یک واحد
/// سازمانی موجود را به‌روز می‌کنند، نه یک تصمیم تایید نهایی.
///
/// طبق «PEMS — P1 Object-Level Authorization &amp; Data Scope Hardening»: داشتن Team.Edit به‌تنهایی
/// نباید یعنی «هر Team ای در کل دیتابیس قابل‌ویرایش است». تنها بُعد Scope واقعی که این Schema
/// برایش شواهد قطعی دارد Discipline است - Team.MaintenanceGroup.DisciplineId (اجباری، نه
/// Nullable) و Employee.DisciplineId (اجباری) هر دو همیشه پر هستند؛ EmployeeAreaScope
/// (Plant/Area/Unit) رد شد چون طبق کامنت خودش «جدول خالی Ship می‌شود... صرفاً Advisory»
/// (طراحی صریح قبلی، نه یک بُعد Scope قابل‌اعتماد امروز). SystemAdmin/EngineeringManager معاف‌اند
/// چون در تمام ماژول‌های دیگر PEMS (Contracts/EngineeringDocuments/VDR/...) این دو نقش هرگز به
/// یک Discipline محدود نشده‌اند - محدودکردنشان اینجا خودش یک قاعده‌ی بی‌سابقه و اختراعی بود.
/// اگر کاربر جاری هیچ Employee/Discipline ای نداشته باشد، Fail-Closed (رد می‌شود، نه اجازه).
/// Crew عمداً همین رفتار را ندارد - نه Discipline/Department مستقیم دارد، نه WorkOrderId
/// اجباری، و طبق کامنت خودِ Entity ممکن است عمداً چند-دیسیپلینه باشد؛ افزودن یک قاعده‌ی Scope
/// آن‌جا بدون شواهد Schema، دقیقاً همان «اختراع قاعده‌ی کسب‌وکار» است که این ممیزی ممنوعش کرده.
/// </remarks>
public class TeamController : Controller
{
    private readonly PEMSDbContext _context;
    private readonly ITeamService _teamService;
    private readonly IOrganizationStructureService _organizationService;
    private readonly ICurrentUserService _currentUser;
    private readonly IPermissionService _permissionService;

    public TeamController(PEMSDbContext context, ITeamService teamService,
        IOrganizationStructureService organizationService,
        ICurrentUserService currentUser, IPermissionService permissionService)
    {
        _context = context;
        _teamService = teamService;
        _organizationService = organizationService;
        _currentUser = currentUser;
        _permissionService = permissionService;
    }

    // طبق «PEMS Dynamic Maintenance Organization — Phase 6A»: صفحه‌ی نمودار سازمانی گرافیکی.
    // این اکشن فقط Shell صفحه را می‌دهد؛ خودِ درخت از طریق همان OrganizationTree (JSON، پایین)
    // با fetch سمت کلاینت خوانده می‌شود - View مستقیم به EF Core دسترسی ندارد.
    [HttpGet]
    public async Task<IActionResult> OrganizationChart(int? disciplineId)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Team", "View"))
            return Forbid();

        ViewData["Title"] = "Maintenance Organization Chart";
        ViewData["Section"] = "CMMS Integration";

        ViewBag.Disciplines = await _context.Disciplines
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new { x.DisciplineId, x.Name })
            .ToListAsync();

        ViewBag.SelectedDisciplineId = disciplineId;

        return View();
    }

    // SCRUM-757: the team list, the team page and every write are the definition pages /p/teams and /p/team-details over ITeamOperations; the chart shell and its JSON reads stay (the chart script).

    // طبق بخش ۲۰/۲۶ سند: مدل خواندنیِ درخت سازمانی - پایه‌ی داده‌ای برای نمودار سازمانی
    // آینده (فاز جداگانه‌ی UI)؛ این‌جا فقط API خواندنی JSON عرضه می‌شود.
    [HttpGet]
    public async Task<IActionResult> OrganizationTree(int? disciplineId)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Team", "View"))
            return Forbid();

        var tree = await _organizationService.GetOrganizationTreeAsync(disciplineId);
        return Json(tree);
    }

    [HttpGet]
    public async Task<IActionResult> TeamNodeDetails(int teamId)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Team", "View"))
            return Forbid();

        var details = await _organizationService.GetTeamDetailsAsync(teamId);
        if (details == null) return NotFound();
        return Json(details);
    }

    // ==============================================================================
    // برای فرم Work Order: با انتخاب تیم، فقط اعضای همون تیم (با نقششون) برگردونده بشه -
    // نه لیست کامل کارمندها
    // ==============================================================================
    // طبق ممیزی: این AJAX توسط فرم ساخت/جزئیات WorkOrder هم مصرف می‌شود (نه فقط صفحه‌ی Team) -
    // به همون سطح دسترسی View که ماژول WorkOrder به DisciplineEngineer/Viewer می‌دهد نیاز دارد؛
    // وگرنه بازبینی WorkOrder برای این دو نقش، اعضای تیم را نشان نمی‌دهد (Case C - لینک مرده).
    [HttpGet]
    public async Task<IActionResult> GetTeamMembers(int teamId)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Team", "View"))
            return Forbid();

        var members = await _context.TeamMembers
            .Include(x => x.Employee)
            .Include(x => x.Role)
            .Where(x => x.TeamId == teamId && x.IsActive)
            .Select(x => new
            {
                employeeId = x.EmployeeId,
                name = x.Employee!.FirstName + " " + x.Employee.LastName,
                role = x.Role!.Name
            })
            .ToListAsync();

        return Json(members);
    }
}
