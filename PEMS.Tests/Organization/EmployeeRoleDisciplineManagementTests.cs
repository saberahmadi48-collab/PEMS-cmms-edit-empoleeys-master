using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Application.Organization;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.Areas.Admin.Controllers;
using PEMS.Web.Areas.Admin.Models;
using PEMS.Web.Controllers;
using EmployeeController = PEMS.Web.Controllers.EmployeeController;
using PEMS.Web.Security;

namespace PEMS.Tests.Organization;

/// <summary>Employee / User / Role / Discipline management: Identity-role ↔ RoleAssignment consistency, strict master-data validation, discipline master data, navigation.</summary>
public class EmployeeRoleDisciplineManagementTests
{
    private sealed class Env
    {
        public required PEMSDbContext Ctx { get; init; }
        public required ServiceProvider Sp { get; init; }
        public UserManager<ApplicationUser> Users => Sp.GetRequiredService<UserManager<ApplicationUser>>();
        public RoleManager<ApplicationRole> Roles => Sp.GetRequiredService<RoleManager<ApplicationRole>>();
    }

    private static Env NewEnv()
    {
        var ctx = PemsDbContextFactory.CreateInMemory("erd_" + Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        services.AddLogging();
        services.AddIdentityCore<ApplicationUser>().AddRoles<ApplicationRole>().AddEntityFrameworkStores<PEMSDbContext>();
        return new Env { Ctx = ctx, Sp = services.BuildServiceProvider() };
    }

    private static async Task<(int DeptId, int DiscId)> Org(PEMSDbContext ctx)
    {
        var dept = new Department { Code = "D1", Name = "Dept 1" }; var disc = new Discipline { Code = "PR", Name = "Process", ImportAliases = "PROC, Proc Eng", IsActive = true };
        ctx.Departments.Add(dept); ctx.Disciplines.Add(disc); await ctx.SaveChangesAsync();
        return (dept.DepartmentId, disc.DisciplineId);
    }

    private static async Task<ApplicationRole> Role(Env e, string name, string? resource = null, string? action = null)
    {
        var role = new ApplicationRole(name) { NormalizedName = name.ToUpperInvariant() };
        e.Ctx.Roles.Add(role); await e.Ctx.SaveChangesAsync();
        if (resource != null)
        {
            var p = await e.Ctx.Permissions.FirstOrDefaultAsync(x => x.ResourceName == resource && x.ActionName == action);
            if (p == null) { p = new Permission { Name = $"{resource}.{action}", ResourceName = resource, ActionName = action!, IsActive = true, CreatedDate = DateTime.Now }; e.Ctx.Permissions.Add(p); await e.Ctx.SaveChangesAsync(); }
            e.Ctx.RolePermissionGrants.Add(new RolePermissionGrant { RoleId = role.Id, PermissionId = p.PermissionId, IsActive = true });
            await e.Ctx.SaveChangesAsync();
        }
        return role;
    }

    private static async Task<ApplicationUser> User(Env e, string name, int? employeeId = null)
    {
        var u = new ApplicationUser { UserName = name, NormalizedUserName = name.ToUpperInvariant(), FullName = name, EmployeeId = employeeId, IsActive = true, SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString() };
        e.Ctx.Users.Add(u); await e.Ctx.SaveChangesAsync(); return u;
    }

    private static async Task<bool> Allowed(Env e, int userId, string resource, string action, IDictionary<string, string>? ctx = null) =>
        (await new AccessControlService(e.Ctx).AuthorizeAsync(new AccessControlRequest { UserId = userId, ResourceName = resource, ActionName = action, Context = ctx })).Allowed;

    // ------------------------------------------------------------------ Identity role ↔ RoleAssignment

    [Fact]
    public async Task RemovedIdentityRole_WithoutSync_KeepsGranting_RootCauseDocumented_ThenSyncCloses()
    {
        var e = NewEnv();
        var roleA = await Role(e, "RoleA", "Thing", "View");
        var u = await User(e, "u1");
        e.Ctx.UserRoles.Add(new IdentityUserRole<int> { UserId = u.Id, RoleId = roleA.Id });
        await e.Ctx.SaveChangesAsync();
        var sync = new UserRoleAssignmentSynchronizer(e.Ctx);
        await sync.SyncAsync(u.Id);                                     // mirror exists (as the startup seeder would create)
        Assert.True(await Allowed(e, u.Id, "Thing", "View"));

        // remove only the Identity role (what UsersController did before the fix)
        e.Ctx.UserRoles.RemoveRange(e.Ctx.UserRoles);
        await e.Ctx.SaveChangesAsync();
        Assert.True(await Allowed(e, u.Id, "Thing", "View"));          // STALE grant via the active mirror RoleAssignment (root cause)
        Assert.Single(await sync.FindStaleMirrorsAsync());

        Assert.Equal(1, await sync.SyncAsync(u.Id));
        Assert.False(await Allowed(e, u.Id, "Thing", "View"));         // fixed
        Assert.Empty(await sync.FindStaleMirrorsAsync());
        var row = await e.Ctx.RoleAssignments.AsNoTracking().SingleAsync();
        Assert.False(row.IsActive); Assert.NotNull(row.EffectiveTo);    // deactivated, never deleted
    }

    [Fact]
    public async Task Sync_AddRemoveMultipleRolesAndNoRoles()
    {
        var e = NewEnv();
        var a = await Role(e, "A", "ResA", "View"); var b = await Role(e, "B", "ResB", "View");
        var u = await User(e, "u2");
        var sync = new UserRoleAssignmentSynchronizer(e.Ctx);

        e.Ctx.UserRoles.Add(new IdentityUserRole<int> { UserId = u.Id, RoleId = a.Id }); await e.Ctx.SaveChangesAsync();
        await sync.SyncAsync(u.Id);
        Assert.True(await Allowed(e, u.Id, "ResA", "View")); Assert.False(await Allowed(e, u.Id, "ResB", "View"));

        e.Ctx.UserRoles.Add(new IdentityUserRole<int> { UserId = u.Id, RoleId = b.Id }); await e.Ctx.SaveChangesAsync();
        await sync.SyncAsync(u.Id);
        Assert.Equal(2, await e.Ctx.RoleAssignments.CountAsync(x => x.IsActive));

        e.Ctx.UserRoles.Remove(e.Ctx.UserRoles.Single(x => x.RoleId == a.Id)); await e.Ctx.SaveChangesAsync();
        await sync.SyncAsync(u.Id);
        Assert.False(await Allowed(e, u.Id, "ResA", "View")); Assert.True(await Allowed(e, u.Id, "ResB", "View"));

        e.Ctx.UserRoles.RemoveRange(e.Ctx.UserRoles); await e.Ctx.SaveChangesAsync();
        await sync.SyncAsync(u.Id);
        Assert.False(await Allowed(e, u.Id, "ResB", "View"));
        Assert.Equal(0, await e.Ctx.RoleAssignments.CountAsync(x => x.IsActive));

        // role A added again after removal: the dormant mirror is reactivated (no duplicate rows)
        e.Ctx.UserRoles.Add(new IdentityUserRole<int> { UserId = u.Id, RoleId = a.Id }); await e.Ctx.SaveChangesAsync();
        await sync.SyncAsync(u.Id);
        Assert.True(await Allowed(e, u.Id, "ResA", "View"));
        Assert.Equal(1, await e.Ctx.RoleAssignments.CountAsync(x => x.RoleId == a.Id));
    }

    [Fact]
    public async Task Sync_PreservesScopedDelegatedEmergencyTemporaryAndDatedDirectAssignments()
    {
        var e = NewEnv();
        var scoped = await Role(e, "Scoped", "ResS", "View"); var deleg = await Role(e, "Deleg", "ResD", "View");
        var emerg = await Role(e, "Emerg", "ResE", "View"); var temp = await Role(e, "Temp", "ResT", "View");
        var u = await User(e, "u3"); var boss = await User(e, "boss");
        e.Ctx.RoleAssignments.AddRange(
            new RoleAssignment { UserId = u.Id, RoleId = scoped.Id, ScopeType = "Discipline", ScopeValue = "5", AssignmentType = "Direct", IsActive = true },
            new RoleAssignment { UserId = u.Id, RoleId = deleg.Id, ScopeType = "Global", AssignmentType = "Delegated", DelegatedByUserId = boss.Id, IsActive = true },
            new RoleAssignment { UserId = u.Id, RoleId = emerg.Id, ScopeType = "Global", AssignmentType = "Emergency", IsActive = true },
            new RoleAssignment { UserId = u.Id, RoleId = temp.Id, ScopeType = "Global", AssignmentType = "Direct", EffectiveFrom = DateTime.Now.AddDays(-1), EffectiveTo = DateTime.Now.AddDays(5), IsActive = true });
        await e.Ctx.SaveChangesAsync();

        Assert.Equal(0, await new UserRoleAssignmentSynchronizer(e.Ctx).SyncAsync(u.Id));      // user has no Identity roles, yet nothing else is touched
        Assert.Equal(4, await e.Ctx.RoleAssignments.CountAsync(x => x.IsActive));
        Assert.True(await Allowed(e, u.Id, "ResS", "View", new Dictionary<string, string> { ["Discipline"] = "5" }));     // scoped preserved
        Assert.False(await Allowed(e, u.Id, "ResS", "View", new Dictionary<string, string> { ["Discipline"] = "9" }));
        Assert.True(await Allowed(e, u.Id, "ResD", "View")); Assert.True(await Allowed(e, u.Id, "ResE", "View")); Assert.True(await Allowed(e, u.Id, "ResT", "View"));
    }

    [Fact]
    public async Task DirectGlobalAssignment_WithoutIdentityRole_IsNotMirror_ButIdentityRolesStillWork()
    {
        var e = NewEnv();
        var r = await Role(e, "R", "ResR", "View");
        var u = await User(e, "u4");
        // an explicitly dated direct-global assignment is NOT a mirror: sync leaves it alone
        e.Ctx.RoleAssignments.Add(new RoleAssignment { UserId = u.Id, RoleId = r.Id, ScopeType = "Global", AssignmentType = "Direct", EffectiveFrom = DateTime.Now.AddDays(-1), IsActive = true });
        await e.Ctx.SaveChangesAsync();
        await new UserRoleAssignmentSynchronizer(e.Ctx).SyncAsync(u.Id);
        Assert.True(await Allowed(e, u.Id, "ResR", "View"));
    }

    // ------------------------------------------------------------------ Users controller (create/edit)

    private static UsersController NewUsersController(Env e) =>
        new UsersController(e.Users, e.Roles, e.Ctx, null!, null!, null!, null!,
            new UserRoleAssignmentSynchronizer(e.Ctx), new OrganizationHierarchyService(e.Ctx, new OrganizationValidationService(e.Ctx))).WithTempData();

    [Fact]
    public async Task UserCreate_NewEmployee_RequiresValidDepartmentAndDiscipline_NoMagicZero()
    {
        var e = NewEnv(); var (dept, disc) = await Org(e.Ctx);
        var c = NewUsersController(e);
        var bad = await c.Create(new UserFormViewModel { UserName = "x1", Email = "x1@t.io", FullName = "X One", Password = "Abc@123456", DepartmentId = dept, DisciplineId = null });
        Assert.IsType<ViewResult>(bad);
        Assert.False(c.ModelState.IsValid);
        Assert.Empty(e.Ctx.Employees); Assert.Empty(e.Ctx.Users);                                  // nothing persisted, no DisciplineId = 0

        c = NewUsersController(e);
        var zero = await c.Create(new UserFormViewModel { UserName = "x2", Email = "x2@t.io", FullName = "X Two", Password = "Abc@123456", DepartmentId = dept, DisciplineId = 0 });
        Assert.IsType<ViewResult>(zero); Assert.Empty(e.Ctx.Employees);
    }

    [Fact]
    public async Task UserCreate_WithPositionAndRoles_CreatesEmployeeAssignmentUserAndMirror()
    {
        var e = NewEnv(); var (dept, disc) = await Org(e.Ctx);
        var role = await Role(e, "RoleX", "ResX", "View");
        var pos = new Position { Title = "Process Engineer", PositionCode = "ENG-PROC", IsActive = true, DepartmentId = dept }; e.Ctx.Positions.Add(pos); await e.Ctx.SaveChangesAsync();
        var c = NewUsersController(e);

        var result = await c.Create(new UserFormViewModel { UserName = "newuser", Email = "n@t.io", FullName = "New User", Password = "Abc@123456",
            DepartmentId = dept, DisciplineId = disc, PositionId = pos.PositionId, SelectedRoles = new() { "RoleX" } });
        Assert.IsType<RedirectToActionResult>(result);

        var emp = await e.Ctx.Employees.SingleAsync();
        Assert.Equal(disc, emp.DisciplineId); Assert.Equal("Process Engineer", emp.Position);       // legacy display text only
        var asg = await e.Ctx.EmployeeAssignments.SingleAsync();
        Assert.Equal(pos.PositionId, asg.PositionId); Assert.True(asg.IsPrimary); Assert.True(asg.IsActive);   // authoritative organizational position
        var user = await e.Ctx.Users.SingleAsync();
        Assert.Equal(emp.EmployeeId, user.EmployeeId);
        Assert.Equal(1, await e.Ctx.RoleAssignments.CountAsync(a => a.UserId == user.Id && a.RoleId == role.Id && a.IsActive));
        Assert.True(await Allowed(e, user.Id, "ResX", "View"));

        // a second user cannot take the (now occupied) position
        var c2 = NewUsersController(e);
        var again = await c2.Create(new UserFormViewModel { UserName = "second", Email = "s@t.io", FullName = "Second User", Password = "Abc@123456", DepartmentId = dept, DisciplineId = disc, PositionId = pos.PositionId });
        Assert.IsType<ViewResult>(again); Assert.Equal(1, await e.Ctx.Employees.CountAsync());
    }

    [Fact]
    public async Task C16_a_rejected_password_creates_no_orphan_employee_keeps_the_non_secret_values_and_roles_and_never_echoes_the_password()
    {
        var e = NewEnv(); var (dept, disc) = await Org(e.Ctx);
        await Role(e, "RoleX");
        var c = NewUsersController(e);

        var result = await c.Create(new UserFormViewModel { UserName = "weak", Email = "w@t.io", FullName = "Weak User", Password = "abc", DepartmentId = dept, DisciplineId = disc, SelectedRoles = new() { "RoleX" } });

        var back = Assert.IsType<UserFormViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Empty(e.Ctx.Employees); Assert.Empty(e.Ctx.Users);                         // پیش‌تر: کارمند ساخته می‌شد و با هر ارسال دوباره، کارمند تکراری
        Assert.Equal("weak", back.UserName); Assert.Equal("Weak User", back.FullName); Assert.Equal("w@t.io", back.Email);
        Assert.Equal(dept, back.DepartmentId); Assert.Equal(disc, back.DisciplineId); Assert.Contains("RoleX", back.SelectedRoles);
        Assert.Null(back.Password);                                                        // رمز هرگز برنمی‌گردد؛ ارسال بعدی دوباره رمز می‌خواهد
        Assert.True(c.ModelState.TryGetValue("Password", out var entry) && entry!.Errors.Count > 0);

        // ارسال دوباره با رمز معتبر: دقیقاً یک کارمند و یک کاربر
        var again = await NewUsersController(e).Create(new UserFormViewModel { UserName = "weak", Email = "w@t.io", FullName = "Weak User", Password = "Abc@123456", DepartmentId = dept, DisciplineId = disc, SelectedRoles = new() { "RoleX" } });
        Assert.IsType<RedirectToActionResult>(again);
        Assert.Equal(1, await e.Ctx.Employees.CountAsync()); Assert.Equal(1, await e.Ctx.Users.CountAsync());
        Assert.Equal(1, await e.Ctx.UserRoles.CountAsync());
    }

    [Fact]
    public async Task C16_duplicate_user_name_or_unknown_role_is_rejected_before_any_employee_is_created_and_a_blank_password_is_always_required()
    {
        var e = NewEnv(); var (dept, disc) = await Org(e.Ctx);
        await User(e, "taken");
        var before = await e.Ctx.Employees.CountAsync();

        Assert.IsType<ViewResult>(await NewUsersController(e).Create(new UserFormViewModel { UserName = "taken", Email = "t@t.io", FullName = "T T", Password = "Abc@123456", DepartmentId = dept, DisciplineId = disc }));
        Assert.IsType<ViewResult>(await NewUsersController(e).Create(new UserFormViewModel { UserName = "fresh", Email = "f@t.io", FullName = "F F", Password = "Abc@123456", DepartmentId = dept, DisciplineId = disc, SelectedRoles = new() { "NoSuchRole" } }));
        var noPassword = NewUsersController(e);
        Assert.IsType<ViewResult>(await noPassword.Create(new UserFormViewModel { UserName = "nopass", Email = "n@t.io", FullName = "N N", Password = "", DepartmentId = dept, DisciplineId = disc }));
        Assert.True(noPassword.ModelState.ContainsKey("Password"));

        Assert.Equal(before, await e.Ctx.Employees.CountAsync());
        Assert.Equal(1, await e.Ctx.Users.CountAsync());
    }

    [Fact]
    public async Task UserCreate_ExistingEmployee_MustExist_BeActive_AndNotLinkedElsewhere()
    {
        var e = NewEnv(); var (dept, disc) = await Org(e.Ctx);
        var emp = new Employee { FirstName = "A", LastName = "B", DepartmentId = dept, DisciplineId = disc, IsActive = true }; e.Ctx.Employees.Add(emp); await e.Ctx.SaveChangesAsync();
        await User(e, "owner", emp.EmployeeId);

        var c = NewUsersController(e);
        Assert.IsType<ViewResult>(await c.Create(new UserFormViewModel { UserName = "dup", Email = "d@t.io", FullName = "Dup", Password = "Abc@123456", ExistingEmployeeId = emp.EmployeeId }));
        Assert.Equal(1, await e.Ctx.Users.CountAsync());                                           // not linked twice

        c = NewUsersController(e);
        Assert.IsType<ViewResult>(await c.Create(new UserFormViewModel { UserName = "ghost", Email = "g@t.io", FullName = "Ghost", Password = "Abc@123456", ExistingEmployeeId = 9999 }));
        Assert.Equal(1, await e.Ctx.Users.CountAsync());
    }

    [Fact]
    public async Task UserEdit_RemovingRole_ClosesMirror_AddingAnotherOpensIt()
    {
        var e = NewEnv(); var (dept, disc) = await Org(e.Ctx);
        var a = await Role(e, "RA", "ResA2", "View"); var b = await Role(e, "RB", "ResB2", "View");
        var emp = new Employee { FirstName = "E", LastName = "E", DepartmentId = dept, DisciplineId = disc, IsActive = true }; e.Ctx.Employees.Add(emp); await e.Ctx.SaveChangesAsync();
        var u = await User(e, "editme", emp.EmployeeId); u.Email = "e@t.io"; await e.Ctx.SaveChangesAsync();
        await e.Users.AddToRoleAsync(u, "RA");
        await new UserRoleAssignmentSynchronizer(e.Ctx).SyncAsync(u.Id);
        Assert.True(await Allowed(e, u.Id, "ResA2", "View"));

        var c = NewUsersController(e);
        var res = await c.Edit(new UserFormViewModel { Id = u.Id, UserName = "editme", Email = "e@t.io", FullName = "editme", IsActive = true, ExistingEmployeeId = emp.EmployeeId, SelectedRoles = new() { "RB" } });
        Assert.IsType<RedirectToActionResult>(res);
        Assert.False(await Allowed(e, u.Id, "ResA2", "View"));                                      // removed role no longer grants
        Assert.True(await Allowed(e, u.Id, "ResB2", "View"));

        res = await c.Edit(new UserFormViewModel { Id = u.Id, UserName = "editme", Email = "e@t.io", FullName = "editme", IsActive = true, ExistingEmployeeId = emp.EmployeeId, SelectedRoles = new() });
        Assert.False(await Allowed(e, u.Id, "ResB2", "View"));                                      // no roles
    }

    // ------------------------------------------------------------------ Employee controller

    private static EmployeeController NewEmployeeController(Env e) =>
        new EmployeeController(e.Ctx, e.Users, e.Roles, null!, null!, new UserRoleAssignmentSynchronizer(e.Ctx)).WithTempData();

    [Fact]
    public async Task EmployeeCreateAndEdit_RejectInvalidOrInactiveDepartmentDiscipline()
    {
        var e = NewEnv(); var (dept, disc) = await Org(e.Ctx);
        var c = NewEmployeeController(e);
        await c.Create("", "A", "B", null, null, null, dept, 0, false, null, null, null, null, null, null, null, null);          // discipline 0
        await c.Create("", "A", "B", null, null, null, 9999, disc, false, null, null, null, null, null, null, null, null);       // unknown department
        Assert.Empty(e.Ctx.Employees);

        await c.Create("", "Good", "Employee", null, null, null, dept, disc, false, null, null, null, null, null, null, null, null);
        var emp = await e.Ctx.Employees.SingleAsync();
        await c.Edit(emp.EmployeeId, "", "Good", "Employee", null, null, null, dept, 0, null, null, null, null, null, null);       // edit to discipline 0
        Assert.Equal(disc, (await e.Ctx.Employees.AsNoTracking().SingleAsync()).DisciplineId);

        (await e.Ctx.Disciplines.FindAsync(disc))!.IsActive = false; await e.Ctx.SaveChangesAsync();
        await c.Create("", "Third", "Person", null, null, null, dept, disc, false, null, null, null, null, null, null, null, null);   // inactive discipline for a NEW employee
        Assert.Equal(1, await e.Ctx.Employees.CountAsync());
    }

    private static IFormFile Xlsx(params string[][] rows)
    {
        var wb = new XLWorkbook(); var ws = wb.AddWorksheet("Employees");
        var header = new[] { "Code", "First", "Last", "Position", "Email", "Phone", "Department", "Discipline" };
        for (var c = 0; c < header.Length; c++) ws.Cell(1, c + 1).Value = header[c];
        for (var r = 0; r < rows.Length; r++) for (var c = 0; c < rows[r].Length; c++) ws.Cell(r + 2, c + 1).Value = rows[r][c];
        var ms = new MemoryStream(); wb.SaveAs(ms); ms.Position = 0;
        return new FormFile(ms, 0, ms.Length, "file", "emp.xlsx");
    }

    [Fact]
    public async Task EmployeeImport_ValidAndInvalidDepartmentDiscipline_NoSilentFallbackToZero()
    {
        var e = NewEnv(); var (dept, disc) = await Org(e.Ctx);
        e.Ctx.Disciplines.Add(new Discipline { Code = "EL", Name = "Electrical", IsActive = true });
        e.Ctx.Disciplines.Add(new Discipline { Code = "XX", Name = "Retired", IsActive = false });
        e.Ctx.Disciplines.Add(new Discipline { Code = "A1", Name = "Dup Name", IsActive = true }); e.Ctx.Disciplines.Add(new Discipline { Code = "A2", Name = "Dup Name", IsActive = true });
        await e.Ctx.SaveChangesAsync();
        var c = NewEmployeeController(e);

        var result = await c.Import(Xlsx(
            new[] { "P1", "Ok", "ByName", "", "", "", "Dept 1", "Process" },
            new[] { "P2", "Ok", "ByCode", "", "", "", "D1", "EL" },
            new[] { "P3", "Ok", "ByAlias", "", "", "", "Dept 1", "Proc Eng" },
            new[] { "P4", "Bad", "UnknownDiscipline", "", "", "", "Dept 1", "Nope" },
            new[] { "P5", "Bad", "BlankDiscipline", "", "", "", "Dept 1", "" },
            new[] { "P6", "Bad", "InactiveDiscipline", "", "", "", "Dept 1", "Retired" },
            new[] { "P7", "Bad", "AmbiguousDiscipline", "", "", "", "Dept 1", "Dup Name" },
            new[] { "P8", "Bad", "UnknownDepartment", "", "", "", "Nowhere", "Process" }));
        Assert.IsType<RedirectToActionResult>(result);

        var saved = await e.Ctx.Employees.AsNoTracking().OrderBy(x => x.PersonnelCode).ToListAsync();
        Assert.Equal(new[] { "P1", "P2", "P3" }, saved.Select(x => x.PersonnelCode).ToArray());
        Assert.All(saved, x => Assert.True(x.DisciplineId > 0 && x.DepartmentId == dept));
        Assert.Equal(disc, saved[0].DisciplineId); Assert.Equal(disc, saved[2].DisciplineId);
        Assert.Contains("5 نامعتبر", (string)c.TempData["Message"]!);
    }

    [Fact]
    public async Task EmployeeDetails_ExposesLinkedAccountRolesAndOrganizationalPosition_NoSecondRoleManager()
    {
        var e = NewEnv(); var (dept, disc) = await Org(e.Ctx);
        var emp = new Employee { FirstName = "L", LastName = "U", DepartmentId = dept, DisciplineId = disc, IsActive = true }; e.Ctx.Employees.Add(emp); await e.Ctx.SaveChangesAsync();
        var role = await Role(e, "RoleD");
        var u = await User(e, "linked", emp.EmployeeId); e.Ctx.UserRoles.Add(new IdentityUserRole<int> { UserId = u.Id, RoleId = role.Id });
        var pos = new Position { Title = "Pos", IsActive = true, DepartmentId = dept }; e.Ctx.Positions.Add(pos); await e.Ctx.SaveChangesAsync();
        e.Ctx.EmployeeAssignments.Add(new EmployeeAssignment { PositionId = pos.PositionId, EmployeeId = emp.EmployeeId, IsActive = true, IsPrimary = true, StartDate = DateTime.Now.AddDays(-1) });
        await e.Ctx.SaveChangesAsync();

        var laborStub = new StubLabor();
        var c = new EmployeeController(e.Ctx, e.Users, e.Roles, laborStub, null!, null).WithTempData();
        var view = Assert.IsType<ViewResult>(await c.Details(emp.EmployeeId));
        object linked = c.ViewBag.LinkedUser;
        Assert.NotNull(linked);
        Assert.Equal("linked", (string)linked.GetType().GetProperty("UserName")!.GetValue(linked)!);
        Assert.Contains("RoleD", (IEnumerable<string>)linked.GetType().GetProperty("Roles")!.GetValue(linked)!);
        var ctx = Assert.IsType<OrganizationalContext>(c.ViewBag.OrgContext);
        Assert.Equal(pos.PositionId, ctx.PrimaryPositionId);
        // no role-mutation action exists on EmployeeController (roles are managed only in Administration → Users)
        Assert.DoesNotContain(typeof(EmployeeController).GetMethods().Select(m => m.Name), n => n.Contains("Role", StringComparison.OrdinalIgnoreCase) && n != "ToString");
    }

    private sealed class StubLabor : IEmployeeLaborRateService
    {
        public Task SetRateAsync(int employeeId, string rateType, decimal rate, string currency, DateTime effectiveFrom) => Task.CompletedTask;
        public Task<List<PEMS.Domain.Entities.EmployeeLaborRate>> GetHistoryAsync(int employeeId) => Task.FromResult(new List<PEMS.Domain.Entities.EmployeeLaborRate>());
        public Task<PEMS.Domain.Entities.EmployeeLaborRate?> GetEffectiveRateAsync(int employeeId, string rateType, DateTime asOf) => Task.FromResult<PEMS.Domain.Entities.EmployeeLaborRate?>(null);
    }

    // ------------------------------------------------------------------ Discipline master data

    [Fact]
    public async Task DisciplineMaster_CreateEditDeactivateReactivate_AndUniqueness()
    {
        var e = NewEnv();
        var svc = new DisciplineMasterService(e.Ctx);

        Assert.True((await svc.CreateAsync("mec", "Mechanical Eng", "desc", "ME, Mech", "m")).Success);
        var d = await e.Ctx.Disciplines.SingleAsync();
        Assert.Equal("MEC", d.Code); Assert.Equal("M", d.TransmittalCode); Assert.Equal("ME, Mech", d.ImportAliases);

        Assert.Equal("Disc_Err_DuplicateCode", (await svc.CreateAsync("MEC", "Other name", null, null, null)).ErrorKey);
        Assert.Equal("Disc_Err_DuplicateName", (await svc.CreateAsync("NEW", "mechanical eng", null, null, null)).ErrorKey);
        Assert.Equal("Disc_Err_AliasConflict", (await svc.CreateAsync("ELE", "Electrical", null, "me", null)).ErrorKey);       // alias clashes with another alias
        Assert.Equal("Disc_Err_AliasConflict", (await svc.CreateAsync("ELE", "Electrical", null, "MEC", null)).ErrorKey);      // alias clashes with another code
        Assert.Equal("Disc_Err_Required", (await svc.CreateAsync("", "x", null, null, null)).ErrorKey);
        Assert.Equal("Disc_Err_TransmittalCode", (await svc.CreateAsync("TTT", "Tx", null, null, "12")).ErrorKey);

        Assert.True((await svc.CreateAsync("ELE", "Electrical", null, "EL", "e")).Success);
        var ele = await e.Ctx.Disciplines.SingleAsync(x => x.Code == "ELE");
        Assert.Equal("Disc_Err_DuplicateCode", (await svc.UpdateAsync(ele.DisciplineId, "MEC", "Electrical", null, null, null)).ErrorKey);   // edit cannot steal a code
        Assert.True((await svc.UpdateAsync(ele.DisciplineId, "ELE", "Electrical Systems", "d2", "EL, Elec", "E")).Success);                 // editing itself is fine

        Assert.True((await svc.SetActiveAsync(ele.DisciplineId, false)).Success);
        Assert.False((await e.Ctx.Disciplines.AsNoTracking().SingleAsync(x => x.DisciplineId == ele.DisciplineId)).IsActive);
        Assert.True((await svc.SetActiveAsync(ele.DisciplineId, true)).Success);
        Assert.True((await e.Ctx.Disciplines.AsNoTracking().SingleAsync(x => x.DisciplineId == ele.DisciplineId)).IsActive);
        Assert.Equal(2, (await svc.GetAllAsync()).Count);                                                                                // never deleted
        Assert.Equal("Disc_Err_NotFound", (await svc.SetActiveAsync(9999, false)).ErrorKey);
    }

    private static async Task<(DisciplineController C, PEMSDbContext Ctx)> DisciplineCtl(params string[] roles)
    {
        var ctx = PemsDbContextFactory.CreateInMemory("dm_" + Guid.NewGuid().ToString("N"));
        foreach (var r in SystemRoles.All) ctx.Roles.Add(new ApplicationRole(r) { NormalizedName = r.ToUpperInvariant() });
        await ctx.SaveChangesAsync();
        var access = new AccessControlService(ctx);
        var perm = new PermissionService(ctx, access);
        await perm.SeedDefaultsAsync();
        var services = new ServiceCollection(); services.AddSingleton(ctx);
        await AccessControlSeeder.SeedFromLegacyAsync(services.BuildServiceProvider());
        var user = new FakeCurrentUserService { Roles = roles.ToList() };
        return (new DisciplineController(null!, ctx, user, perm).WithTempData(), ctx);
    }

    [Fact]
    public async Task DisciplineMasterData_IsPermissionBased_EngineeringManagerAndAdminMayManage_TaskRolesMayNot()
    {
        // EngineeringManager: permission DisciplineMaster (View/Create/Edit) -> can open and mutate
        var (mgr, mctx) = await DisciplineCtl(SystemRoles.EngineeringManager);
        Assert.IsType<ViewResult>(await mgr.Manage(null, null));
        Assert.IsType<RedirectToActionResult>(await mgr.CreateDiscipline("ABC", "Alpha", null, null, null));
        Assert.Equal(1, await mctx.Disciplines.CountAsync(d => d.Code == "ABC"));
        var id = (await mctx.Disciplines.SingleAsync(d => d.Code == "ABC")).DisciplineId;
        Assert.IsType<RedirectToActionResult>(await mgr.SetDisciplineActive(id, false));
        Assert.False((await mctx.Disciplines.AsNoTracking().SingleAsync(d => d.DisciplineId == id)).IsActive);

        // SystemAdmin bypass
        var (admin, _) = await DisciplineCtl(SystemRoles.SystemAdmin);
        Assert.IsType<ViewResult>(await admin.Manage(null, null));

        // roles that only hold the task-level DisciplineWorkspace permission (or none) are denied on every master-data action, server-side
        foreach (var role in new[] { SystemRoles.DisciplineEngineer, SystemRoles.EngineeringSupervisor, SystemRoles.DocumentController, SystemRoles.Viewer, SystemRoles.ContractManager })
        {
            var (c, ctx) = await DisciplineCtl(role);
            Assert.IsType<ForbidResult>(await c.Manage(null, null));
            Assert.IsType<ForbidResult>(await c.CreateDiscipline("ZZZ", "Zed", null, null, null));
            Assert.IsType<ForbidResult>(await c.EditDiscipline(1, "ZZZ", "Zed", null, null, null));
            Assert.IsType<ForbidResult>(await c.SetDisciplineActive(1, false));
            Assert.Empty(ctx.Disciplines.Where(d => d.Code == "ZZZ"));
        }
        var (none, _) = await DisciplineCtl();
        Assert.IsType<ForbidResult>(await none.Manage(null, null));

        // antiforgery on mutations; no hard-coded role attributes on the master-data actions
        var t = typeof(DisciplineController);
        foreach (var name in new[] { "CreateDiscipline", "EditDiscipline", "SetDisciplineActive" })
        {
            Assert.NotNull(t.GetMethod(name)!.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), false).SingleOrDefault());
            Assert.Null(t.GetMethod(name)!.GetCustomAttributes(typeof(AuthorizeAttribute), false).SingleOrDefault());
        }
    }

    [Fact]
    public async Task DisciplineMasterNavigation_FollowsThePermission_NotRoleNames()
    {
        var ctx = PemsDbContextFactory.CreateInMemory("dn_" + Guid.NewGuid().ToString("N"));
        foreach (var r in SystemRoles.All) ctx.Roles.Add(new ApplicationRole(r) { NormalizedName = r.ToUpperInvariant() });
        await ctx.SaveChangesAsync();
        var perm = new PermissionService(ctx, new AccessControlService(ctx));
        await perm.SeedDefaultsAsync();
        var services = new ServiceCollection(); services.AddSingleton(ctx);
        await AccessControlSeeder.SeedFromLegacyAsync(services.BuildServiceProvider());     // legacy matrix -> grants, as at startup
        // exactly what the sidebar evaluates
        Assert.True(await perm.HasPermissionAsync(new[] { SystemRoles.EngineeringManager }, "DisciplineMaster", "View"));
        Assert.True(await perm.HasPermissionAsync(new[] { SystemRoles.SystemAdmin }, "DisciplineMaster", "View"));
        foreach (var r in new[] { SystemRoles.DisciplineEngineer, SystemRoles.EngineeringSupervisor, SystemRoles.Viewer, SystemRoles.QAManager, SystemRoles.MaintenanceEngineer })
            Assert.False(await perm.HasPermissionAsync(new[] { r }, "DisciplineMaster", "View"), r);
        // granting the permission to any other role (matrix) makes the link appear for it - no code change
        await perm.SetPermissionsAsync(SystemRoles.QAManager, new List<PEMS.Application.DTOs.ModulePermissionInput> { new() { ModuleName = "DisciplineMaster", CanView = true } });
        Assert.True(await perm.HasPermissionAsync(new[] { SystemRoles.QAManager }, "DisciplineMaster", "View"));
    }

    // ------------------------------------------------------------------ Navigation (permission-derived, no role-name hard-coding)

    private static async Task<(PEMSDbContext Ctx, AccessControlService Access, NavigationVisibilityResolver Resolver)> NavEnv()
    {
        var ctx = PemsDbContextFactory.CreateInMemory("nav_" + Guid.NewGuid().ToString("N"));
        foreach (var roleName in SystemRoles.All) ctx.Roles.Add(new ApplicationRole(roleName) { NormalizedName = roleName.ToUpperInvariant() });
        await ctx.SaveChangesAsync();
        var access = new AccessControlService(ctx);
        await new PermissionService(ctx, access).SeedDefaultsAsync();
        await RoleCatalogSeeder.SeedAsync(ctx);
        return (ctx, access, new NavigationVisibilityResolver(new NavigationVisibilityOverrideService(ctx), access));
    }

    [Fact]
    public async Task Navigation_MaintenanceCatalogRoles_SeeCmms_ThroughCatalogModuleDefault_NotHardCodedNames()
    {
        var (ctx, access, resolver) = await NavEnv();

        // Head of Maintenance / planning / discipline head / experts: the Role Catalog declares the module they need (VisibleModules) -> override seeded;
        // items remain permission-filtered
        foreach (var role in new[] { "MNT.Manager", "MNT.PlanningHead", "MNT.PlanningExpert", "MNT.DisciplineHead", "MNT.SeniorExpert", "MNT.Expert", "OPS.IssueAuthority" })
        {
            var snap = await resolver.ResolveAsync(new[] { role });
            Assert.True(snap.IsModuleVisible("CmmsIntegration"), role);
            Assert.False(snap.IsModuleVisible("Administration"), role);
            Assert.False(snap.IsCmmsItemVisible("Equipment"), role);           // no permission signal for permission-only roles => no dead link
        }
        // item visibility follows real grants (the production grants come from WorkOrderGovernanceSeeder; granted explicitly here)
        async Task Grant(string roleName, string resource)
        {
            var roleId = await ctx.Roles.Where(r => r.Name == roleName).Select(r => r.Id).SingleAsync();
            var p = await ctx.Permissions.FirstOrDefaultAsync(x => x.ResourceName == resource && x.ActionName == "View")
                    ?? ctx.Permissions.Add(new Permission { Name = resource + ".View", ResourceName = resource, ActionName = "View", IsActive = true, CreatedDate = DateTime.Now }).Entity;
            await ctx.SaveChangesAsync();
            ctx.RolePermissionGrants.Add(new RolePermissionGrant { RoleId = roleId, PermissionId = p.PermissionId, IsActive = true });
            await ctx.SaveChangesAsync();
        }
        await Grant("MNT.Manager", "WorkOrder"); await Grant("MNT.PlanningHead", "WorkRequest");
        Assert.True((await resolver.ResolveAsync(new[] { "MNT.Manager" })).IsCmmsItemVisible("WorkOrder"));
        Assert.True((await resolver.ResolveAsync(new[] { "MNT.PlanningHead" })).IsCmmsItemVisible("WorkRequest"));
        Assert.False((await resolver.ResolveAsync(new[] { "MNT.Manager" })).IsCmmsItemVisible("WorkRequest"));

        // established roles unchanged
        var eng = await resolver.ResolveAsync(new[] { SystemRoles.MaintenanceEngineer });
        Assert.True(eng.IsModuleVisible("CmmsIntegration")); Assert.True(eng.IsCmmsItemVisible("Equipment"));
        Assert.True((await resolver.ResolveAsync(new[] { SystemRoles.EngineeringManager })).IsModuleVisible("CmmsIntegration"));
        Assert.False((await resolver.ResolveAsync(new[] { SystemRoles.QAManager })).IsModuleVisible("CmmsIntegration"));

        foreach (var role in new[] { RoleCatalog.WarehouseManager, RoleCatalog.WarehouseStorekeeper })
        {
            var snap = await resolver.ResolveAsync(new[] { role });
            Assert.False(snap.IsModuleVisible("CmmsIntegration")); Assert.True(snap.IsModuleVisible("Inventory"));
        }
        // visibility is not authorization
        Assert.False((await access.AuthorizeForRolesAsync(new[] { "MNT.Manager" }, "Equipment", "Create")).Allowed);
    }
}
