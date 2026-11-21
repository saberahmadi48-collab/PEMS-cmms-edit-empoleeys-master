using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Services;
using PEMS.Persistence.Services.TaskManager;
using PEMS.Tests.TestSupport;
using PEMS.Web.Controllers;

namespace PEMS.Tests.TaskManager;

/// <summary>
/// رندر Viewهای واقعی Task Manager (بدون HTTP): هر صفحه بدون خطا رندر می‌شود، کنترل‌ها فقط برای کسی که مجاز است نمایش داده می‌شوند،
/// متن کاربر HTML-encode می‌شود و پیوند به مورد غیرقابل‌دید فقط «مورد محدود» است.
/// </summary>
public class TaskManagerViewRenderTests
{
    private const int Alice = 10, Bob = 11, Carol = 12, Erin = 14;
    private static readonly TaskActor Admin = new(9, null, new[] { TaskActor.SystemAdminRole }, "Admin");

    private sealed record World(PEMSDbContext Ctx, int SpaceId, int ItemId, int PersonalId, int LegacyId);

    private static async Task<World> SeedAsync(string name)
    {
        var ctx = PemsDbContextFactory.CreateInMemory($"TMV_{name}");
        ctx.Departments.Add(new Department { DepartmentId = 1, Code = "MNT", Name = "Maintenance" });
        ctx.Employees.AddRange(
            new Employee { EmployeeId = Alice, FirstName = "Alice", LastName = "A", DepartmentId = 1, DisciplineId = 1 },
            new Employee { EmployeeId = Bob, FirstName = "Bob", LastName = "B", DepartmentId = 1, DisciplineId = 1 },
            new Employee { EmployeeId = Carol, FirstName = "Carol", LastName = "C", DepartmentId = 2, DisciplineId = 1 },
            new Employee { EmployeeId = Erin, FirstName = "Erin", LastName = "E", DepartmentId = 2, DisciplineId = 1 });
        ctx.Users.AddRange(
            new ApplicationUser { Id = 1, UserName = "alice", EmployeeId = Alice },
            new ApplicationUser { Id = 2, UserName = "bob", EmployeeId = Bob },
            new ApplicationUser { Id = 3, UserName = "carol", EmployeeId = Carol },
            new ApplicationUser { Id = 5, UserName = "erin", EmployeeId = Erin },
            new ApplicationUser { Id = 9, UserName = "admin" });
        await ctx.SaveChangesAsync();

        var perms = new FakePermissionService();
        var spaces = new WorkSpaceService(ctx, perms);
        var tasks = new TaskManagerService(ctx, perms, new FakeNumberingService());
        var spaceId = (await spaces.SaveSpaceAsync(new WorkSpaceInput { Key = "MNT", Name = "Maintenance", NameFa = "تعمیرات", OwnerDepartmentId = 1 }, Admin)).Id!.Value;
        await spaces.AddMemberAsync(spaceId, Carol, WorkSpaceMemberRoles.Lead, Admin);
        var alice = new TaskActor(1, Alice, new[] { "Engineer" }, "Alice A");
        var carol = new TaskActor(3, Carol, new[] { "Engineer" }, "Carol C");
        var sprint = (await spaces.CreateSprintAsync(spaceId, "Sprint 1", "Stabilise pumps", DateTime.Today, DateTime.Today.AddDays(14), carol)).Id!.Value;
        await spaces.StartSprintAsync(sprint, carol);

        var item = (await tasks.CreateAsync(new WorkItemCreateInput { WorkSpaceId = spaceId, Title = "Pump <b>P-101</b> overhaul", Description = "Line1\nLine2", AssigneeEmployeeId = Bob, DueDate = DateTime.Today.AddDays(-1), SprintId = sprint }, alice)).Id!.Value;
        await tasks.CreateAsync(new WorkItemCreateInput { WorkSpaceId = spaceId, Title = "Backlog item" }, alice);
        var personal = (await tasks.CreateAsync(new WorkItemCreateInput { Title = "Private prep", AssigneeEmployeeId = Alice }, carol)).Id!.Value;
        await tasks.AddLinkAsync(item, WorkItemLinkTypes.Blocks, "#" + personal, alice);
        await tasks.AddCommentAsync(item, "<img src=x onerror=alert(1)>", alice);
        var legacy = await new TaskService(ctx, null!).CreateAsync("Minutes follow-up", null, Bob, null, "High", "Meeting", 3, 1, "Alice A");
        return new World(ctx, spaceId, item, personal, legacy);
    }

    private static (TaskManagerController Tm, TaskController Task) Controllers(PEMSDbContext ctx, int userId, params string[] roles)
    {
        var perms = new FakePermissionService();
        var user = new FakeCurrentUserService { UserId = userId, UserName = "u" + userId, FullName = "User " + userId, Roles = roles.ToList() };
        var manager = new TaskManagerService(ctx, perms, new FakeNumberingService());
        return (new TaskManagerController(manager, new WorkSpaceService(ctx, perms), user, perms).WithTempData(),
                new TaskController(new TaskService(ctx, null!), manager, user, perms).WithTempData());
    }

    private static Task<string> Render(Controller c, IActionResult r, string path) =>
        RazorViewRenderer.RenderAsync(c, Assert.IsType<ViewResult>(r), path);

    [Fact]
    public async Task EveryTaskManagerPage_Renders_ForAMember()
    {
        var w = await SeedAsync(nameof(EveryTaskManagerPage_Renders_ForAMember));
        var (tm, _) = Controllers(w.Ctx, 1, "Engineer");

        var index = await Render(tm, await tm.Index(), "~/Views/TaskManager/Index.cshtml");
        Assert.Contains("MNT", index);

        var backlog = await Render(tm, await tm.Space("MNT", "backlog"), "~/Views/TaskManager/Backlog.cshtml");
        Assert.Contains("Sprint 1", backlog);
        Assert.Contains("Backlog item", backlog);
        Assert.Contains("Pump &lt;b&gt;P-101&lt;/b&gt; overhaul", backlog);        // متن کاربر encode می‌شود
        Assert.DoesNotContain("name=\"sprintId\" class", backlog);                 // عضو عادی Lead نیست: انتقال Sprint/رتبه ندارد
        Assert.Contains("id=\"tmCreateModal\"", backlog);

        var board = await Render(tm, await tm.Space("MNT", "board"), "~/Views/TaskManager/Board.cshtml");
        Assert.Contains("data-tm-board", board);
        Assert.Contains("MNT-1", board);
        Assert.Contains("name=\"statusId\"", board);                                // Alice گزارش‌دهنده است ⇒ کنترل انتقال

        var all = await Render(tm, await tm.AllWork(new WorkItemQuery { WorkSpaceId = w.SpaceId }), "~/Views/TaskManager/AllWork.cshtml");
        Assert.Contains("MNT-2", all);
        Assert.Contains("data-tm-bulk-value=\"status\"", all);
    }

    [Fact]
    public async Task Board_ShowsNoMoveControls_ForAMemberWhoIsNotInvolved()
    {
        var w = await SeedAsync(nameof(Board_ShowsNoMoveControls_ForAMemberWhoIsNotInvolved));
        // Bob مسئول MNT-1 است ولی MNT-2 را نه؛ Sprint فعال فقط MNT-1 را دارد
        var other = (await new TaskManagerService(w.Ctx, new FakePermissionService(), new FakeNumberingService())
            .CreateAsync(new WorkItemCreateInput { WorkSpaceId = w.SpaceId, Title = "Not Bobs", SprintId = w.Ctx.Sprints.Single().SprintId }, new TaskActor(1, Alice, new[] { "Engineer" }, "Alice A"))).Id!.Value;
        var (tm, _) = Controllers(w.Ctx, 2, "Engineer");
        var board = await Render(tm, await tm.Space("MNT", "board"), "~/Views/TaskManager/Board.cshtml");
        Assert.Contains($"data-tm-id=\"{w.ItemId}\"", board);
        Assert.Contains($"draggable=\"false\" data-tm-card data-tm-id=\"{other}\"", board);
    }

    [Fact]
    public async Task Details_EncodesUserText_AndShowsRestrictedLinks_AndHidesEditingForNonInvolvedMembers()
    {
        var w = await SeedAsync(nameof(Details_EncodesUserText_AndShowsRestrictedLinks_AndHidesEditingForNonInvolvedMembers));
        // عضو ضمنی دیگری (واحد ۱) که درگیر نیست
        w.Ctx.Employees.Add(new Employee { EmployeeId = 20, FirstName = "Zed", LastName = "Z", DepartmentId = 1, DisciplineId = 1 });
        w.Ctx.Users.Add(new ApplicationUser { Id = 20, UserName = "zed", EmployeeId = 20 });
        await w.Ctx.SaveChangesAsync();

        var (_, asZed) = Controllers(w.Ctx, 20, "Engineer");
        var html = await Render(asZed, await asZed.Details(w.ItemId), "~/Views/Task/Details.cshtml");
        Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", html);
        Assert.DoesNotContain("<img src=x", html);
        Assert.DoesNotContain("Private prep", html);                               // عنوان مقصد پیوند غیرقابل‌دید
        Assert.Contains("bi-lock", html);
        Assert.DoesNotContain("name=\"Title\"", html);                             // فرم ویرایش ندارد
        Assert.DoesNotContain("name=\"assigneeEmployeeId\"", html);
        Assert.Contains("name=\"body\"", html);                                    // ولی می‌تواند نظر بدهد

        var (_, asAlice) = Controllers(w.Ctx, 1, "Engineer");
        var own = await Render(asAlice, await asAlice.Details(w.ItemId), "~/Views/Task/Details.cshtml");
        Assert.Contains("Private prep", own);
        Assert.Contains("name=\"Title\"", own);
        Assert.Contains("name=\"assigneeEmployeeId\"", own);
        Assert.Contains("name=\"statusId\"", own);

        var (_, asErin) = Controllers(w.Ctx, 5, "Engineer");
        Assert.IsType<NotFoundResult>(await asErin.Details(w.ItemId));
    }

    [Fact]
    public async Task LegacyAndMeetingTasks_RenderInTheExistingTaskPages()
    {
        var w = await SeedAsync(nameof(LegacyAndMeetingTasks_RenderInTheExistingTaskPages));
        var (_, asBob) = Controllers(w.Ctx, 2, "Engineer");
        var details = await Render(asBob, await asBob.Details(w.LegacyId), "~/Views/Task/Details.cshtml");
        Assert.Contains("name=\"legacyStatus\"", details);
        Assert.Contains("Meeting", details);

        var index = await Render(asBob, await asBob.Index(), "~/Views/Task/Index.cshtml");
        Assert.Contains("Minutes follow-up", index);
        Assert.Contains("MNT-1", index);                                          // وظیفه‌ی فضا در «اقدام وظایف» با کلید
        Assert.Contains("value=\"InProgress\"", index);                           // فقط گذارهای مجاز
        Assert.DoesNotContain("value=\"Open\" selected", index);
    }

    [Fact]
    public async Task AdminPages_Render_ForSystemAdminOnly()
    {
        var w = await SeedAsync(nameof(AdminPages_Render_ForSystemAdminOnly));
        var (tm, _) = Controllers(w.Ctx, 9, TaskActor.SystemAdminRole);
        var admin = await Render(tm, await tm.Admin(), "~/Views/TaskManager/Admin.cshtml");
        Assert.Contains("MNT", admin);
        var space = await Render(tm, await tm.AdminSpace(w.SpaceId), "~/Views/TaskManager/AdminSpace.cshtml");
        Assert.Contains("name=\"fromStatusId\"", space);
        Assert.Contains("Carol C", space);

        var (asAlice, _) = Controllers(w.Ctx, 1, "Engineer");
        Assert.IsType<ForbidResult>(await asAlice.Admin());
        Assert.IsType<ForbidResult>(await asAlice.AdminSpace(w.SpaceId));
        Assert.IsType<ForbidResult>(await asAlice.SaveSpace(new WorkSpaceInput { Key = "X1", Name = "x" }));
        Assert.IsType<NotFoundResult>(await asAlice.Space("NOPE"));
    }
}
