using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Web.Resources;
using PEMS.Web.Security;

namespace PEMS.Web.Controllers;

/// <summary>
/// داشبورد مدیریتی: صفحه‌ی میزبان Razor + نقطه‌ی JSON داده‌ی واقعی. رابط (React + Bklit UI) در frontend/executive-dashboard ساخته و به wwwroot/dist/executive منتقل می‌شود؛
/// همه‌ی محاسبه‌ها در IExecutiveDashboardService است و هر بخش فقط با مجوز View همان ماژول پر می‌شود.
/// </summary>
public class ExecutiveDashboardController : Controller
{
    private readonly IExecutiveDashboardService _dashboard;
    private readonly ICurrentUserService _currentUser;
    private readonly IPermissionService _permissions;
    private readonly IAccessControlService _accessControl;
    private readonly PEMSDbContext _context;
    private readonly IStringLocalizer<SharedResource> _localizer;

    public ExecutiveDashboardController(IExecutiveDashboardService dashboard, ICurrentUserService currentUser, IPermissionService permissions,
        IAccessControlService accessControl, PEMSDbContext context, IStringLocalizer<SharedResource> localizer)
    {
        _dashboard = dashboard;
        _currentUser = currentUser;
        _permissions = permissions;
        _accessControl = accessControl;
        _context = context;
        _localizer = localizer;
    }

    // SCRUM-697: the host page is the definition page /p/executive-dashboard (the script-island component); this controller only answers the JSON the bundle reads.

    [HttpGet]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Data(int? projectId)
    {
        var roles = _currentUser.Roles;
        var request = new ExecutiveDashboardRequest
        {
            ProjectId = projectId,
            CanViewEngineering = await _permissions.HasPermissionAsync(roles, "EngineeringDocuments", "View"),
            CanViewVdr = await _permissions.HasPermissionAsync(roles, "VDR", "View"),
            CanViewProcurement = await _permissions.HasPermissionAsync(roles, "MaterialRequisition", "View")
                                 || await _permissions.HasPermissionAsync(roles, "ProcurementPackage", "View"),
            CanViewCmms = await _permissions.HasPermissionAsync(roles, "WorkOrder", "View"),
            CanViewCorrespondence = await _permissions.HasPermissionAsync(roles, "Correspondence", "View")
        };

        // همان قاعده‌ی لیست مدارک مهندسی: بدون ViewAllDisciplines فقط دیسیپلین خود کارمند (و مدارک بدون دیسیپلین)
        var canViewAllDisciplines = (await _accessControl.AuthorizeForRolesAsync(roles, "EngineeringDocuments", "ViewAllDisciplines")).Allowed;
        if (!canViewAllDisciplines)
        {
            request.RestrictToOwnDiscipline = true;
            if (_currentUser.UserId.HasValue)
            {
                var userId = _currentUser.UserId.Value;
                request.OwnDisciplineId = await _context.Users.Where(u => u.Id == userId && u.EmployeeId != null)
                    .Join(_context.Employees, u => u.EmployeeId, e => (int?)e.EmployeeId, (u, e) => (int?)e.DisciplineId)
                    .FirstOrDefaultAsync();
            }
        }

        return Json(await _dashboard.GetAsync(request));
    }
}
