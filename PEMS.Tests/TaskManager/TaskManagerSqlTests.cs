using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Domain.Entities;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Services;
using PEMS.Persistence.Services.TaskManager;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.TaskManager;

/// <summary>
/// Task Manager روی دیتابیس SQL یک‌بارمصرف PEMS_TEST_P03_* (همه‌ی Migrationهای واقعی، از جمله TaskManagerCore): کلید یکتا و بدون حفره
/// در ایجاد هم‌زمان، یک Sprint فعال در هر فضا (ایندکس فیلترشده)، RowVersion، پیش‌فرض IsActive برای ردیف‌های قدیمی. به PEMSDB چیزی نوشته نمی‌شود.
/// </summary>
public class TaskManagerSqlTests
{
    private static readonly TaskActor Admin = new(9, null, new[] { TaskActor.SystemAdminRole }, "Admin");

    private static async Task<(int SpaceId, TaskActor Lead, TaskActor Member)> SeedAsync(DisposableSqlDatabase db)
    {
        await using var ctx = db.NewContext();
        var dept = new Department { Code = "TMT", Name = "Task Manager Test" };
        ctx.Departments.Add(dept);
        await ctx.SaveChangesAsync();
        var lead = new Employee { FirstName = "Lena", LastName = "Lead", DepartmentId = dept.DepartmentId, DisciplineId = 1 };
        var member = new Employee { FirstName = "Mo", LastName = "Member", DepartmentId = dept.DepartmentId, DisciplineId = 1 };
        ctx.Employees.AddRange(lead, member);
        await ctx.SaveChangesAsync();
        var leadUser = new ApplicationUser { UserName = "tm.lead", NormalizedUserName = "TM.LEAD", EmployeeId = lead.EmployeeId, FullName = "Lena Lead" };
        var memberUser = new ApplicationUser { UserName = "tm.member", NormalizedUserName = "TM.MEMBER", EmployeeId = member.EmployeeId, FullName = "Mo Member" };
        ctx.Users.AddRange(leadUser, memberUser);
        await ctx.SaveChangesAsync();

        var spaces = new WorkSpaceService(ctx, new FakePermissionService());
        var space = await spaces.SaveSpaceAsync(new WorkSpaceInput { Key = "TMT", Name = "TM Test", OwnerDepartmentId = dept.DepartmentId }, Admin);
        Assert.True(space.Success, space.ErrorKey);
        Assert.True((await spaces.AddMemberAsync(space.Id!.Value, lead.EmployeeId, WorkSpaceMemberRoles.Lead, Admin)).Success);
        return (space.Id!.Value,
            new TaskActor(leadUser.Id, lead.EmployeeId, new[] { "Engineer" }, "Lena Lead"),
            new TaskActor(memberUser.Id, member.EmployeeId, new[] { "Engineer" }, "Mo Member"));
    }

    private static TaskManagerService Service(PEMS.Persistence.Context.PEMSDbContext ctx) =>
        new(ctx, new FakePermissionService(), new NumberingService(ctx));

    [Fact]
    public async Task ConcurrentCreates_GetUniqueGaplessKeys_AndFailedCreatesConsumeNoNumber()
    {
        await using var db = await DisposableSqlDatabase.CreateAsync();
        var (spaceId, lead, member) = await SeedAsync(db);

        // اعتبارسنجی پیش از تراکنش: ایجاد ناموفق شماره‌ای مصرف نمی‌کند
        await using (var ctx = db.NewContext())
            Assert.False((await Service(ctx).CreateAsync(new WorkItemCreateInput { WorkSpaceId = spaceId, Title = " " }, member)).Success);

        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(async i =>
        {
            await using var ctx = db.NewContext();
            return await Service(ctx).CreateAsync(new WorkItemCreateInput { WorkSpaceId = spaceId, Title = $"Parallel {i}" }, i % 2 == 0 ? lead : member);
        }));
        Assert.All(results, r => Assert.True(r.Success, r.ErrorKey));

        var keys = await db.QueryAsync("SELECT ItemKey FROM TaskItems WHERE WorkSpaceId = @s", r => r.GetString(0), ("@s", spaceId));
        Assert.Equal(12, keys.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, 12).Select(n => $"TMT-{n}").OrderBy(k => k), keys.OrderBy(k => k));
    }

    [Fact]
    public async Task OnlyOneActiveSprintPerSpace_IsEnforcedByTheDatabase()
    {
        await using var db = await DisposableSqlDatabase.CreateAsync();
        var (spaceId, _, _) = await SeedAsync(db);
        await using var ctx = db.NewContext();
        ctx.Sprints.Add(new Sprint { WorkSpaceId = spaceId, Name = "A", State = SprintStates.Active });
        await ctx.SaveChangesAsync();
        ctx.Sprints.Add(new Sprint { WorkSpaceId = spaceId, Name = "B", State = SprintStates.Active });
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task StaleRowVersion_IsRejected_WithAConcurrencyError()
    {
        await using var db = await DisposableSqlDatabase.CreateAsync();
        var (spaceId, lead, member) = await SeedAsync(db);
        int id;
        string staleVersion;
        await using (var ctx = db.NewContext())
        {
            id = (await Service(ctx).CreateAsync(new WorkItemCreateInput { WorkSpaceId = spaceId, Title = "Versioned" }, member)).Id!.Value;
            staleVersion = (await Service(ctx).GetDetailAsync(id, member))!.Item.RowVersion!;
        }
        await using (var ctx = db.NewContext())
            Assert.True((await Service(ctx).UpdateAsync(new WorkItemUpdateInput { Id = id, Title = "Edited by lead", RowVersion = staleVersion }, lead)).Success);

        await using (var ctx = db.NewContext())
        {
            var inProgress = await ctx.WorkItemStatuses.Where(s => s.WorkSpaceId == spaceId && s.Name == "In Progress").Select(s => s.WorkItemStatusId).SingleAsync();
            var r = await Service(ctx).TransitionAsync(id, inProgress, null, null, staleVersion, member);
            Assert.Equal("Tm_Err_Concurrency", r.ErrorKey);
        }
        Assert.Equal("Open", (await db.QueryAsync("SELECT Status FROM TaskItems WHERE TaskItemId = @i", r => r.GetString(0), ("@i", id))).Single());
    }

    [Fact]
    public async Task LegacyRowsInsertedWithoutNewColumns_DefaultToActive_AndKeepNullWorkspace()
    {
        await using var db = await DisposableSqlDatabase.CreateAsync();
        await db.QueryAsync("INSERT INTO TaskItems (Title, Priority, Status, CreatedDate) OUTPUT INSERTED.TaskItemId VALUES (N'Old', N'Normal', N'Open', SYSDATETIME())", r => r.GetInt32(0));
        Assert.Equal(1, await db.ScalarIntAsync("SELECT COUNT(*) FROM TaskItems WHERE IsActive = 1 AND WorkSpaceId IS NULL AND ItemKey IS NULL"));
    }
}
