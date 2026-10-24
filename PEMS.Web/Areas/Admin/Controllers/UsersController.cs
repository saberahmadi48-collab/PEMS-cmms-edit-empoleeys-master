using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Identity;
using PEMS.Web.Areas.Admin.Models;
using PEMS.Web.Helpers;
using PEMS.Web.Security;

namespace PEMS.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = SystemRoles.SystemAdmin)]
public class UsersController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly PEMS.Persistence.Context.PEMSDbContext _context;
    private readonly IUserAuthorizationSummaryService _summaryService;
    private readonly IPermissionService _permissionService;
    private readonly IAccessControlService _accessControlService;
    private readonly NavigationVisibilityResolver _navigationResolver;
    private readonly IUserRoleAssignmentSynchronizer _roleSync;
    private readonly PEMS.Application.Organization.IOrganizationHierarchyService? _hierarchy;
    private readonly IRolePermissionAdminService _permissionAdmin;

    // سقف امن برای یک درخواست دسته‌ای تب Effective (کل کاتالوگ مجوزها کمتر از این است)
    private const int MaxPermissionChangesPerRequest = 1000;

    // طبق سند «PEMS — Global Data Grid Standard» بند ۶: Export هم باید یک سقف امن داشته باشد
    // (همون الگوی DocumentRegisterController.MaxExportRows) - نه واقعاً نامحدود. تعداد کاربران
    // امروز خیلی کمتر از این سقف است؛ این فقط از رشد بی‌کنترل آینده جلوگیری می‌کند.
    private const int MaxExportRows = 20000;

    // طبق بند «Audit» صفحه‌ی Access & Authorization: فقط آخرین چند ردیف - برای فهرست کامل با
    // فیلتر، لینک به Admin/AuthorizationAudit?userId= (صفحه‌ی موجود، تکرار نشده)
    private const int RecentAuditLogCount = 20;

    public UsersController(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        PEMS.Persistence.Context.PEMSDbContext context,
        IUserAuthorizationSummaryService summaryService,
        IPermissionService permissionService,
        IAccessControlService accessControlService,
        NavigationVisibilityResolver navigationResolver,
        IUserRoleAssignmentSynchronizer? roleSync = null,
        PEMS.Application.Organization.IOrganizationHierarchyService? hierarchy = null,
        IRolePermissionAdminService? permissionAdmin = null)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _context = context;
        _summaryService = summaryService;
        _permissionService = permissionService;
        _accessControlService = accessControlService;
        _navigationResolver = navigationResolver;
        _roleSync = roleSync ?? new PEMS.Persistence.Services.UserRoleAssignmentSynchronizer(context);
        _hierarchy = hierarchy;
        _permissionAdmin = permissionAdmin ?? new PEMS.Persistence.Services.RolePermissionAdminService(context);
    }

    private async Task<int> CreateEmployeeFromModelAsync(UserFormViewModel model)
    {
        var nameParts = model.FullName.Trim().Split(' ', 2);

        // Position متن آزاد دیگر منبع حقیقت نیست: اگر سمت سازمانی انتخاب شده باشد، عنوانش فقط به‌عنوان متن نمایشی (میراثی) ثبت می‌شود
        // و سمت واقعی از طریق EmployeeAssignment اصلی اعمال می‌شود. DisciplineId/DepartmentId پیش‌تر اعتبارسنجی شده‌اند (هیچ مقدار جادویی/0 وجود ندارد).
        string? displayTitle = null;
        if (model.PositionId.HasValue)
            displayTitle = await _context.Positions.Where(p => p.PositionId == model.PositionId.Value).Select(p => p.Title).FirstOrDefaultAsync();

        var newEmployee = new PEMS.Domain.Entities.Employee
        {
            FirstName = nameParts.Length > 0 ? nameParts[0] : model.FullName,
            LastName = nameParts.Length > 1 ? nameParts[1] : "",
            Position = displayTitle,
            Email = model.Email,
            DepartmentId = model.DepartmentId!.Value,
            DisciplineId = model.DisciplineId!.Value,
            IsActive = true,
            CreatedDate = DateTime.Now
        };

        await _context.Employees.AddAsync(newEmployee);
        await _context.SaveChangesAsync();

        if (model.PositionId.HasValue && _hierarchy != null)
        {
            // از همان سرویس سازمانی موجود: سمت فعال/خالی، یک سمت اصلیِ فعال برای هر کارمند، بدون مکانیزم تخصیص دوم
            var assigned = await _hierarchy.AssignAsync(new PEMS.Application.Organization.OrganizationAssignmentRequest(
                model.PositionId.Value, newEmployee.EmployeeId, true, DateTime.Now, null, "Created from Administration/Users"));
            if (!assigned.Success) TempData["Warning"] = "Employee created, but the position could not be assigned (" + assigned.ErrorKey + ").";
        }

        return newEmployee.EmployeeId;
    }

    /// <summary>اعتبارسنجی پیوند/ساخت کارمند (مشترک Create/Edit). خطاها به ModelState اضافه می‌شوند؛ هیچ FK نامعتبر یا مقدار جادویی ذخیره نمی‌شود.</summary>
    private async Task ValidateEmployeeLinkAsync(UserFormViewModel model, int? currentUserId)
    {
        if (model.ExistingEmployeeId.HasValue)
        {
            var emp = await _context.Employees.AsNoTracking().Where(e => e.EmployeeId == model.ExistingEmployeeId.Value)
                .Select(e => new { e.EmployeeId, e.IsActive }).FirstOrDefaultAsync();
            if (emp == null) { ModelState.AddModelError(nameof(model.ExistingEmployeeId), "The selected employee does not exist."); return; }
            if (!emp.IsActive) ModelState.AddModelError(nameof(model.ExistingEmployeeId), "The selected employee is inactive.");
            var linkedElsewhere = await _context.Users.AnyAsync(u => u.EmployeeId == emp.EmployeeId && (currentUserId == null || u.Id != currentUserId));
            if (linkedElsewhere) ModelState.AddModelError(nameof(model.ExistingEmployeeId), "This employee is already linked to another user account.");
            return;
        }

        if (!model.DepartmentId.HasValue) return;   // (Create) the "select employee or department" rule is enforced by the caller

        if (!await _context.Departments.AnyAsync(d => d.DepartmentId == model.DepartmentId.Value && d.IsActive))
            ModelState.AddModelError(nameof(model.DepartmentId), "Select a valid, active department.");
        if (!model.DisciplineId.HasValue || !await _context.Disciplines.AnyAsync(d => d.DisciplineId == model.DisciplineId.Value && d.IsActive))
            ModelState.AddModelError(nameof(model.DisciplineId), "A valid, active discipline is required to create an employee.");
        if (model.PositionId.HasValue)
        {
            var pos = await _context.Positions.AsNoTracking().Where(p => p.PositionId == model.PositionId.Value).Select(p => new { p.IsActive }).FirstOrDefaultAsync();
            if (pos == null || !pos.IsActive) ModelState.AddModelError(nameof(model.PositionId), "Select a valid, active position.");
            else if (await _context.EmployeeAssignments.AnyAsync(a => a.PositionId == model.PositionId.Value && a.IsActive))
                ModelState.AddModelError(nameof(model.PositionId), "This position already has a holder.");
        }
    }

    /// <summary>C16: همان اعتبارسنج‌های Identity (کاربر + رمز) را پیش از ساخت کارمند/کاربر اجرا می‌کند و نقش‌های نامعتبر را رد می‌کند.</summary>
    private async Task ValidateIdentityUpFrontAsync(UserFormViewModel model)
    {
        var probe = new ApplicationUser { UserName = model.UserName, Email = model.Email, FullName = model.FullName };
        foreach (var validator in _userManager.UserValidators)
        {
            var r = await validator.ValidateAsync(_userManager, probe);
            foreach (var e in r.Errors) ModelState.AddModelError(string.Empty, e.Description);
        }
        if (!string.IsNullOrWhiteSpace(model.Password))
            foreach (var validator in _userManager.PasswordValidators)
            {
                var r = await validator.ValidateAsync(_userManager, probe, model.Password);
                foreach (var e in r.Errors) ModelState.AddModelError(nameof(model.Password), e.Description);
            }
        var invalidRoles = model.SelectedRoles.Where(r => !model.AllRoles.Contains(r)).ToList();
        if (invalidRoles.Count > 0) ModelState.AddModelError(string.Empty, "Unknown role(s): " + string.Join(", ", invalidRoles));
    }

    private async Task RollbackCreatedEmployeeAsync(int? employeeId)
    {
        if (!employeeId.HasValue) return;
        try
        {
            _context.ChangeTracker.Clear();
            var assignments = await _context.EmployeeAssignments.Where(a => a.EmployeeId == employeeId.Value).ToListAsync();
            _context.EmployeeAssignments.RemoveRange(assignments);
            var employee = await _context.Employees.FirstOrDefaultAsync(e => e.EmployeeId == employeeId.Value);
            if (employee != null) _context.Employees.Remove(employee);
            await _context.SaveChangesAsync();
        }
        catch (Exception)
        {
            _context.ChangeTracker.Clear();
            TempData["Warning"] = $"The employee #{employeeId} created for this attempt could not be removed automatically; please deactivate it manually.";
        }
    }

    private async Task LoadEmployeeDropdownsAsync(UserFormViewModel model)
    {
        var employees = await _context.Employees.Where(x => x.IsActive)
            .Select(x => new { x.EmployeeId, Name = x.FirstName + " " + x.LastName })
            .ToListAsync();
        model.AllEmployees = employees.Select(x => (x.EmployeeId, x.Name)).ToList();

        var departments = await _context.Departments.Where(x => x.IsActive)
            .Select(x => new { x.DepartmentId, x.Name })
            .ToListAsync();
        model.AllDepartments = departments.Select(x => (x.DepartmentId, x.Name)).ToList();

        var disciplines = await _context.Disciplines.Where(x => x.IsActive)
            .Select(x => new { x.DisciplineId, x.Name })
            .ToListAsync();
        model.AllDisciplines = disciplines.Select(x => (x.DisciplineId, x.Name)).ToList();

        // فقط سمت‌های فعال و «خالی» (بدون دارنده‌ی فعال) قابل انتخاب‌اند
        var occupied = _context.EmployeeAssignments.Where(a => a.IsActive).Select(a => a.PositionId);
        var positions = await _context.Positions.Where(p => p.IsActive && !occupied.Contains(p.PositionId))
            .OrderBy(p => p.Title).Select(p => new { p.PositionId, p.Title, p.PositionCode }).ToListAsync();
        model.AllPositions = positions.Select(p => (p.PositionId, string.IsNullOrWhiteSpace(p.PositionCode) ? p.Title : p.Title + " [" + p.PositionCode + "]")).ToList();
    }



    // طبق سند «PEMS — Global Data Grid Standard» (فاز ۶B - P1): قبلاً pageSize=25 با
    // const محلی Hardcode شده بود، نه از QueryString.
    public async Task<IActionResult> Index(string? search, int page = 1, int? pageSize = null)
    {
        ViewData["Title"] = "Users";
        ViewData["Section"] = "Administration";
        ViewData["HelpKey"] = "Admin.Users.Index";

        var normalizedPageSize = DataGridPageSize.Normalize(pageSize);

        var query = _userManager.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();

            // نقش‌ها روی جدول جدایی هستن؛ برای این‌که جستجو مثل قبل شامل نام نقش هم بشه،
            // آی‌دی کاربرهایی که نقش مطابق دارن رو جدا پیدا می‌کنیم (بدون N+1)
            var matchingUserIdsByRole = await _context.UserRoles
                .Join(_context.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, RoleName = r.Name })
                .Where(x => x.RoleName != null && x.RoleName.ToLower().Contains(term))
                .Select(x => x.UserId)
                .ToListAsync();

            query = query.Where(u =>
                (u.UserName != null && u.UserName.ToLower().Contains(term)) ||
                u.FullName.ToLower().Contains(term) ||
                (u.Email != null && u.Email.ToLower().Contains(term)) ||
                matchingUserIdsByRole.Contains(u.Id));
        }

        var total = await query.CountAsync();

        var users = await query
            .OrderBy(u => u.UserName)
            .ThenBy(u => u.Id)
            .Skip((page - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync();

        // یک کوئری برای نقش‌های کاربرهای همین صفحه، به‌جای یک کوئری جداگانه به ازای هر کاربر
        var userIds = users.Select(u => u.Id).ToList();
        var rolesByUserId = (await _context.UserRoles
            .Where(ur => userIds.Contains(ur.UserId))
            .Join(_context.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .ToListAsync())
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Name!).ToList());

        var items = users.Select(user => new UserListItemViewModel
        {
            Id = user.Id,
            UserName = user.UserName ?? "",
            Email = user.Email,
            FullName = user.FullName,
            IsActive = user.IsActive,
            LockedOut = user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow,
            Roles = rolesByUserId.TryGetValue(user.Id, out var roles) ? roles : new List<string>(),
            LastLoginDate = user.LastLoginDate
        }).ToList();

        ViewBag.SearchTerm = search;

        return View(new PagedResult<UserListItemViewModel>
        {
            Items = items,
            Page = page,
            PageSize = normalizedPageSize,
            TotalCount = total
        });
    }



    public async Task<IActionResult> Create()
    {
        ViewData["Title"] = "New User";
        ViewData["Section"] = "Administration";

        var model = new UserFormViewModel
        {
            AllRoles = await _roleManager.Roles.Select(x => x.Name!).ToListAsync()
        };

        await LoadEmployeeDropdownsAsync(model);

        return View(model);
    }



    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UserFormViewModel model)
    {
        model.AllRoles = _roleManager.Roles.Select(x => x.Name!).ToList();

        if (string.IsNullOrWhiteSpace(model.Password))
            ModelState.AddModelError(nameof(model.Password), "Password is required for a new user.");

        // طبق بررسی: کاربر جدید باید یا به یک کارمند موجود وصل شود، یا دپارتمانش برای ساخت
        // کارمند جدید مشخص باشد - وگرنه محدودیت دیسیپلین، ارجاع نامه، تخصیص وظیفه و همه‌ی
        // چیزهایی که به Employee وابسته‌اند برای این کاربر کار نمی‌کنند
        if (!model.ExistingEmployeeId.HasValue && !model.DepartmentId.HasValue)
            ModelState.AddModelError(nameof(model.DepartmentId), "Select an existing employee or specify a department to create one.");

        await ValidateEmployeeLinkAsync(model, null);
        // C16: سیاست رمز/یکتایی نام‌کاربری و ایمیل/نقش‌ها «قبل از» هر اثر جانبی بررسی می‌شود تا کارمند یتیم (و تکرار آن در ارسال مجدد فرم) ساخته نشود
        await ValidateIdentityUpFrontAsync(model);

        if (!ModelState.IsValid)
        {
            ViewData["Title"] = "New User";
            ViewData["Section"] = "Administration";

            model.Password = null;                    // رمز هرگز در پاسخ برنگردد؛ هر ارسال دوباره رمز می‌خواهد (مقادیر غیرمحرمانه و نقش‌ها حفظ می‌شوند)
            await LoadEmployeeDropdownsAsync(model);
            return View(model);
        }


        int? employeeId = model.ExistingEmployeeId;
        int? createdEmployeeId = null;

        if (!employeeId.HasValue && model.DepartmentId.HasValue)
            employeeId = createdEmployeeId = await CreateEmployeeFromModelAsync(model);

        var user = new ApplicationUser
        {
            UserName = model.UserName,
            Email = model.Email,
            EmailConfirmed = true,
            FullName = model.FullName,
            IsActive = model.IsActive,
            EmployeeId = employeeId,
            CreatedDate = DateTime.Now
        };

        IdentityResult result;
        try { result = await _userManager.CreateAsync(user, model.Password!); }
        catch (Exception ex) { result = IdentityResult.Failed(new IdentityError { Description = ex.GetBaseException().Message }); }

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);

            // جبران: کارمندی که همین درخواست ساخته بود حذف می‌شود تا یتیم نماند و در لیست‌ها آلودگی نسازد
            await RollbackCreatedEmployeeAsync(createdEmployeeId);

            ViewData["Title"] = "New User";
            ViewData["Section"] = "Administration";

            model.Password = null;
            await LoadEmployeeDropdownsAsync(model);
            return View(model);
        }

        // نقش‌ها را اضافه می‌کنیم و اگر تخصیص نقش به هر دلیلی شکست خورد، کاربر تازه ایجادشده را حذف می‌کنیم
        if (model.SelectedRoles.Any())
        {
            var roleResult = await _userManager.AddToRolesAsync(user, model.SelectedRoles);
            if (!roleResult.Succeeded)
            {
                // کاربر بدون نقش‌های انتخاب‌شده رها نمی‌شود: ساخت کامل برگردانده می‌شود
                // سعی می‌کنیم کاربر نیمه‌ساخته را حذف کنیم تا وضعیت دیتابیس سازگار بماند
                foreach (var error in roleResult.Errors) ModelState.AddModelError(string.Empty, error.Description);
                await _userManager.DeleteAsync(user);
                await RollbackCreatedEmployeeAsync(createdEmployeeId);
                ViewData["Title"] = "New User";
                ViewData["Section"] = "Administration";
                model.Password = null;
                await LoadEmployeeDropdownsAsync(model);
                return View(model);
            }
        }

        // فقط در صورتی همگام‌سازی می‌کنیم که تخصیص نقش موفق بوده (یا نقشی انتخاب نشده باشد)
        await _roleSync.SyncAsync(user.Id);

        TempData["Message"] = "User created successfully";

        return RedirectToAction(nameof(Index));
    }



    public async Task<IActionResult> Edit(int id)
    {
        ViewData["Title"] = "Edit User";
        ViewData["Section"] = "Administration";

        var user = await _userManager.FindByIdAsync(id.ToString());

        if (user == null)
            return NotFound();

        var roles = await _userManager.GetRolesAsync(user);

        var model = new UserFormViewModel
        {
            Id = user.Id,
            UserName = user.UserName ?? "",
            Email = user.Email ?? "",
            FullName = user.FullName,
            IsActive = user.IsActive,
            SelectedRoles = roles.ToList(),
            AllRoles = _roleManager.Roles.Select(x => x.Name!).ToList(),
            ExistingEmployeeId = user.EmployeeId
        };

        await LoadEmployeeDropdownsAsync(model);

        return View(model);
    }



    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(UserFormViewModel model)
    {
        model.AllRoles = _roleManager.Roles.Select(x => x.Name!).ToList();

        await ValidateEmployeeLinkAsync(model, model.Id);

        if (!ModelState.IsValid)
        {
            ViewData["Title"] = "Edit User";
            ViewData["Section"] = "Administration";

            await LoadEmployeeDropdownsAsync(model);
            return View(model);
        }

        var user = await _userManager.FindByIdAsync(model.Id.ToString());

        if (user == null)
            return NotFound();

        user.Email = model.Email;
        user.FullName = model.FullName;
        user.IsActive = model.IsActive;

        // طبق بررسی: امکان اصلاح/تنظیم ارتباط با کارمند بعد از ساخت هم فراهم شد - برای کاربران
        // قدیمی که قبل از این اصلاح ساخته شده بودند و اصلاً کارمند وصل نداشتند
        if (model.ExistingEmployeeId.HasValue)
        {
            user.EmployeeId = model.ExistingEmployeeId.Value;
        }
        else if (model.DepartmentId.HasValue)
        {
            user.EmployeeId = await CreateEmployeeFromModelAsync(model);
        }

        await _userManager.UpdateAsync(user);


        if (!string.IsNullOrWhiteSpace(model.Password))
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);

            await _userManager.ResetPasswordAsync(user, token, model.Password);
        }


        var currentRoles = await _userManager.GetRolesAsync(user);

        var rolesToRemove = currentRoles.Except(model.SelectedRoles).ToList();

        var rolesToAdd = model.SelectedRoles.Except(currentRoles).ToList();

        if (rolesToRemove.Any())
            await _userManager.RemoveFromRolesAsync(user, rolesToRemove);

        IdentityResult? roleResult = null;
        if (rolesToAdd.Any())
            roleResult = await _userManager.AddToRolesAsync(user, rolesToAdd);

        // نقش حذف‌شده نباید از طریق RoleAssignment آینه‌ایِ باقی‌مانده همچنان دسترسی بدهد؛ Scoped/Delegated/Emergency/Temporary دست‌نخورده می‌مانند
        await _roleSync.SyncAsync(user.Id);

        // همگام‌سازی حتی در صورت شکست تخصیص نقش انجام می‌شود تا نقش‌های حذف‌شده دسترسی باقی‌مانده ندهند
        if (roleResult != null && !roleResult.Succeeded)
        {
            TempData["Error"] = "User updated, but some role assignments failed: "
                + string.Join(", ", roleResult.Errors.Select(e => e.Description));
            return RedirectToAction(nameof(Index));
        }

        TempData["Message"] = "User updated successfully";

        return RedirectToAction(nameof(Index));
    }



    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());

        if (user == null)
            return NotFound();

        user.IsActive = !user.IsActive;

        await _userManager.UpdateAsync(user);

        return RedirectToAction(nameof(Index));
    }


    // طبق سند «PEMS — Global Data Grid Standard» بند A-9: Pagination گرید نباید به‌طور
    // تصادفی روی Export اعمال شود (معنای Export = همه، نه فقط صفحه‌ی فعلی) - اما Export هم
    // نباید واقعاً نامحدود باشد. اینجا فقط یک سقف امن اضافه شده (MaxExportRows)؛ Authorization
    // (SystemAdmin، سطح کلاس) دست‌نخورده ماند.
    public async Task<IActionResult> Export()
    {
        var totalUsers = await _userManager.Users.CountAsync();
        if (totalUsers > MaxExportRows)
        {
            return BadRequest(
                $"تعداد کاربران ({totalUsers}) از سقف امن Export ({MaxExportRows}) بیشتر است. " +
                "لطفاً با مدیر سیستم برای Export دسته‌ای تماس بگیرید.");
        }

        var users = await _userManager.Users.ToListAsync();

        var rolesByUserId = (await _context.UserRoles
            .Join(_context.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .ToListAsync())
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Name!).ToList());

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Users");
        sheet.AddStyledHeaderRow("UserName", "FullName", "Email", "Roles", "IsActive", "LastLoginDate");

        int row = 2;
        foreach (var u in users)
        {
            var roles = rolesByUserId.TryGetValue(u.Id, out var r2) ? r2 : new List<string>();

            sheet.Cell(row, 1).Value = u.UserName;
            sheet.Cell(row, 2).Value = u.FullName;
            sheet.Cell(row, 3).Value = u.Email;
            sheet.Cell(row, 4).Value = string.Join(", ", roles);
            sheet.Cell(row, 5).Value = u.IsActive;
            if (u.LastLoginDate.HasValue) sheet.Cell(row, 6).Value = u.LastLoginDate.Value;
            row++;
        }

        sheet.FinalizeExportSheet();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        return File(stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Users_{DateTime.Now:yyyyMMdd}.xlsx");
    }


    // طبق «PEMS — Enterprise User-Centric Authorization Workspace Consolidation»: صفحه‌ی تازه -
    // Users → [User] → Access & Authorization. نمایشی است - Edit موجود همچنان تنها مسیر تخصیص نقش
    // به کاربر است. تنها استثنا تب Effective است که با SetPermissionAccess مجوزها را روی Grant
    // نقش‌های کاربر تغییر می‌دهد (نه مجوز به‌ازای کاربر). هر پنج دیدگاه (Effective/Matrix/Simulator/
    // SoD/Navigation) از همان سرویس‌های واقعیِ موجود می‌آیند - هیچ موتور مجوزدهی/SoD/ناوبری
    // تازه‌ای این‌جا نوشته نشده.
    public async Task<IActionResult> Details(int id)
    {
        var vm = await BuildWorkspaceVmAsync(id);
        if (vm == null) return NotFound();

        ViewData["Title"] = "User";
        ViewData["Section"] = "Administration";

        return View(vm);
    }

    // طبق بند «Simulator must remain a simulation tool... must never mutate real authorization
    // state»: عیناً همان AccessControlService.AuthorizeAsync که Admin/Authorization/Simulate
    // موجود صدا می‌زند - نه یک ارزیاب دوم. فقط صفحه‌ی تازه دوباره با نتیجه رندر می‌شود، هیچ‌چیز
    // نوشته نمی‌شود.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Simulate(int id, string resourceName, string actionName, string? resourceId)
    {
        var vm = await BuildWorkspaceVmAsync(id);
        if (vm == null) return NotFound();

        vm.Simulator.ResourceName = resourceName;
        vm.Simulator.ActionName = actionName;
        vm.Simulator.ResourceId = resourceId;

        if (!string.IsNullOrWhiteSpace(resourceName) && !string.IsNullOrWhiteSpace(actionName))
        {
            var decision = await _accessControlService.AuthorizeAsync(new AccessControlRequest
            {
                UserId = id,
                ResourceName = resourceName.Trim(),
                ActionName = actionName.Trim(),
                ResourceId = string.IsNullOrWhiteSpace(resourceId) ? null : resourceId.Trim(),
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
            });

            vm.Simulator.Result = new SimulateResultVm
            {
                Allowed = decision.Allowed,
                Reason = decision.Reason.ToString(),
                Detail = decision.Detail,
                MatchedRoleName = decision.MatchedRoleName,
                MatchedScopeType = decision.MatchedScopeType,
                MatchedScopeValue = decision.MatchedScopeValue
            };
        }

        ViewData["Title"] = "User";
        ViewData["Section"] = "Administration";
        ViewData["ActiveAccessTab"] = "simulator";

        return View("Details", vm);
    }

    // ذخیره‌ی خودکار تب Effective (تک‌ردیف یا دسته‌ای). مجوز مؤثر کاربر از Grant نقش‌هایش می‌آید،
    // پس تغییر روی Grant همان نقش(ها) اعمال می‌شود و همه‌ی کاربران آن نقش را شامل می‌شود؛ منطق در
    // IRolePermissionAdminService است، این‌جا فقط اعتبارسنجی ورودی و بازگرداندن وضعیت تازه.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetPermissionAccess(int id, [FromBody] SetPermissionAccessRequest request)
    {
        if (request?.Changes == null || request.Changes.Count == 0)
            return BadRequest(new { error = "NoChanges" });
        if (request.Changes.Count > MaxPermissionChangesPerRequest)
            return BadRequest(new { error = "TooManyChanges" });

        var user = await _userManager.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return NotFound();

        var roleNames = (await _userManager.GetRolesAsync(user)).OrderBy(r => r).ToList();
        if (!string.IsNullOrWhiteSpace(request.TargetRole) && !roleNames.Contains(request.TargetRole))
            return BadRequest(new { error = "InvalidTargetRole" });

        var result = await _permissionAdmin.ApplyAsync(
            roleNames,
            string.IsNullOrWhiteSpace(request.TargetRole) ? null : request.TargetRole,
            request.Changes,
            User.Identity?.Name ?? "unknown");

        // وضعیت تازه‌ی همان مجوزها از همان منبع صفحه (نه حدس سمت کلاینت)
        var rows = await _summaryService.GetEffectivePermissionsForRolesAsync(roleNames);
        var requested = result.Items.Select(i => i.PermissionName).ToHashSet(StringComparer.Ordinal);

        return Json(new
        {
            applied = result.AppliedCount,
            affectedRoles = result.AffectedRoles,
            items = result.Items.Select(i => new { permission = i.PermissionName, applied = i.Applied, reason = i.Reason }),
            states = rows.Where(r => requested.Contains(r.PermissionName))
                .Select(r => new { permission = r.PermissionName, allowed = r.Allowed }),
            allowedCount = rows.Count(r => r.Allowed),
            totalCount = rows.Count
        });
    }

    private async Task<UserWorkspaceVm?> BuildWorkspaceVmAsync(int id)
    {
        var user = await _userManager.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return null;

        var roleNames = (await _userManager.GetRolesAsync(user)).OrderBy(r => r).ToList();

        var employee = user.EmployeeId.HasValue
            ? await _context.Employees
                .Where(e => e.EmployeeId == user.EmployeeId.Value)
                .Select(e => new { e.Position, e.DepartmentId, e.DisciplineId })
                .FirstOrDefaultAsync()
            : null;

        string? departmentName = null;
        string? disciplineName = null;
        if (employee != null)
        {
            if (employee.DepartmentId != 0)
                departmentName = await _context.Departments.Where(d => d.DepartmentId == employee.DepartmentId).Select(d => d.Name).FirstOrDefaultAsync();
            if (employee.DisciplineId != 0)
                disciplineName = await _context.Disciplines.Where(d => d.DisciplineId == employee.DisciplineId).Select(d => d.Name).FirstOrDefaultAsync();
        }

        var effectivePermissions = await _summaryService.GetEffectivePermissionsForRolesAsync(roleNames);
        var nonEditable = await _permissionAdmin.GetNonEditableAsync(roleNames);
        var roleUserCounts = new Dictionary<string, int>();
        foreach (var roleName in roleNames)
            roleUserCounts[roleName] = (await _userManager.GetUsersInRoleAsync(roleName)).Count;
        var sodExposure = await _summaryService.GetSoDExposureForRolesAsync(roleNames);

        var fullMatrix = await _permissionService.GetMatrixAsync();
        var matrixRows = fullMatrix.Where(r => roleNames.Contains(r.RoleName)).ToList();

        var navSnapshot = await _navigationResolver.ResolveAsync(roleNames);
        var navigationTree = NavigationVisibilityTreeBuilder.BuildUserTree(navSnapshot);

        var recentAudit = await _context.AuthorizationAuditLogs
            .Where(a => a.UserId == id)
            .OrderByDescending(a => a.Timestamp)
            .Take(RecentAuditLogCount)
            .Select(a => new UserAuditLogRowVm
            {
                Timestamp = a.Timestamp,
                ResourceName = a.ResourceName,
                ActionName = a.ActionName,
                ResourceId = a.ResourceId,
                Decision = a.Decision,
                Reason = a.Reason
            })
            .ToListAsync();

        var allUsers = await _userManager.Users
            .Select(u => new { u.Id, u.UserName, u.FullName })
            .OrderBy(u => u.UserName)
            .ToListAsync();

        return new UserWorkspaceVm
        {
            UserId = user.Id,
            UserName = user.UserName ?? "",
            FullName = user.FullName,
            Email = user.Email,
            IsActive = user.IsActive,
            LockedOut = user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow,
            LastLoginDate = user.LastLoginDate,
            DepartmentName = departmentName,
            DisciplineName = disciplineName,
            Position = employee?.Position,
            HasLinkedEmployee = user.EmployeeId.HasValue,
            RoleNames = roleNames,
            EffectivePermissions = effectivePermissions,
            NonEditablePermissions = nonEditable,
            RoleUserCounts = roleUserCounts,
            MatrixRows = matrixRows,
            SoDExposure = sodExposure,
            NavigationTree = navigationTree,
            RecentAuditLogs = recentAudit,
            Simulator = new SimulateFormVm
            {
                UserId = id,
                AllUsers = allUsers.Select(u => (u.Id, string.IsNullOrWhiteSpace(u.FullName) ? u.UserName! : $"{u.FullName} ({u.UserName})")).ToList(),
                AllPermissionNames = effectivePermissions.Select(p => p.PermissionName).ToList()
            }
        };
    }
}
