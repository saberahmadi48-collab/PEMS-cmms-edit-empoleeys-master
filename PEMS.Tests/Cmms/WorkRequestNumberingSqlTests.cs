using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Domain.Entities;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Cmms;

/// <summary>
/// A7 روی SQL Server یک‌بارمصرف: شماره‌ی Work Request در همان تراکنش ثبت سند گرفته می‌شود، پس شکست ثبت، شماره را برنمی‌سوزاند (بدون حفره) و ثبت‌های هم‌زمان
/// شماره‌ی تکراری نمی‌گیرند. توضیح «شروع از WR-2026-0003»: پیش‌تر شمارنده در تراکنش مستقل و پیش از ذخیره‌ی سند Commit می‌شد؛ هر تلاش ناموفق/حذف‌شده یا تست دستی یک شماره می‌سوزاند.
/// </summary>
public class WorkRequestNumberingSqlTests
{
    private static async Task<(int EmployeeId, int EquipmentId)> SeedAsync(DisposableSqlDatabase db)
    {
        await using var ctx = db.NewContext();
        var project = new Project { ProjectCode = "P", ProjectName = "P" };
        var discipline = new Discipline { Name = "Mechanical", Code = "MECH-T", IsActive = true };
        var dept = new Department { Name = "Maintenance", Code = "MNT-T" };
        ctx.AddRange(project, discipline, dept);
        await ctx.SaveChangesAsync();
        var equipment = new Equipment { TagNumber = "N-101", Name = "Pump", DisciplineId = discipline.DisciplineId, ProjectId = project.ProjectId, IsActive = true };
        var employee = new Employee { FirstName = "Num", LastName = "Tester", DepartmentId = dept.DepartmentId, DisciplineId = discipline.DisciplineId, IsActive = true };
        ctx.AddRange(equipment, employee);
        await ctx.SaveChangesAsync();
        return (employee.EmployeeId, equipment.EquipmentId);
    }

    private static WorkRequestService Service(PEMS.Persistence.Context.PEMSDbContext ctx) => new(ctx, new NumberingService(ctx), null!);

    [Fact]
    public async Task A7_a_failed_save_gives_the_number_back_and_concurrent_creates_never_share_a_number()
    {
        await using var db = await DisposableSqlDatabase.CreateAsync();
        var (emp, eq) = await SeedAsync(db);
        var year = DateTime.Now.Year;

        string[] numbers;
        await using (var ctx = db.NewContext())
        {
            var first = await Service(ctx).CreateAsync(new WorkRequestCreateRequest { Title = "one", RequestedById = emp, EquipmentId = eq });
            Assert.True(first.Success, first.Error);
        }

        // ذخیره‌ی سند شکست می‌خورد → تراکنش Rollback → شمارنده برنمی‌گردد جلو
        var fault = new NthSaveFaultInterceptor();
        await using (var ctx = db.NewContext(new[] { fault }))
        {
            fault.Arm(1);
            var failed = await Service(ctx).CreateAsync(new WorkRequestCreateRequest { Title = "boom", RequestedById = emp, EquipmentId = eq });
            Assert.False(failed.Success);
        }
        Assert.Equal(1, await db.ScalarIntAsync("SELECT COUNT(*) FROM WorkRequests"));
        Assert.Equal(1, await db.ScalarIntAsync("SELECT CurrentValue FROM NumberingSequences WHERE DocumentType = 'WorkRequest'"));

        // اعتبارسنجی ردشده هم شماره نمی‌سوزاند
        await using (var ctx = db.NewContext())
        {
            Assert.False((await Service(ctx).CreateAsync(new WorkRequestCreateRequest { Title = "no place", RequestedById = emp })).Success);
        }
        Assert.Equal(1, await db.ScalarIntAsync("SELECT CurrentValue FROM NumberingSequences WHERE DocumentType = 'WorkRequest'"));

        // ۸ ثبت هم‌زمان (Context جدا) → ۸ شماره‌ی یکتا و پیوسته ۰۰۰۲..۰۰۰۹
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async i =>
        {
            await using var ctx = db.NewContext();
            return await Service(ctx).CreateAsync(new WorkRequestCreateRequest { Title = "parallel " + i, RequestedById = emp, EquipmentId = eq });
        }));
        Assert.All(results, r => Assert.True(r.Success, r.Error));

        numbers = (await db.QueryAsync("SELECT RequestNumber FROM WorkRequests ORDER BY RequestNumber", r => r.GetString(0))).ToArray();
        Assert.Equal(9, numbers.Length);
        Assert.Equal(numbers.Distinct().Count(), numbers.Length);
        Assert.Equal(Enumerable.Range(1, 9).Select(i => $"WR-{year}-{i:D4}"), numbers);          // بدون حفره
    }

    [Fact]
    public async Task A7_numbering_joins_an_open_transaction_and_a_rollback_releases_the_number()
    {
        await using var db = await DisposableSqlDatabase.CreateAsync();
        await using var ctx = db.NewContext();
        var numbering = new NumberingService(ctx);
        var year = DateTime.Now.Year;

        await ctx.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await ctx.Database.BeginTransactionAsync();
            Assert.Equal($"WR-{year}-0001", await numbering.GetNextNumberAsync("WorkRequest"));
            await tx.RollbackAsync();
        });
        Assert.Equal($"WR-{year}-0001", await numbering.GetNextNumberAsync("WorkRequest"));            // همان شماره دوباره داده می‌شود
        Assert.Equal($"WR-{year}-0002", await numbering.GetNextNumberAsync("WorkRequest"));
    }
}
