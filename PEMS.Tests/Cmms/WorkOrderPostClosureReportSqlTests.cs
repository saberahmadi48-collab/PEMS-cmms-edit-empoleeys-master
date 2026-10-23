using Microsoft.Data.SqlClient;
using PEMS.Domain.Entities;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Cmms;

/// <summary>
/// روی دیتابیس SQL یک‌بارمصرف (PEMS_TEST_P03_*، با Migrationهای واقعی - پس Migration جدید WorkOrderChangeLogs هم اینجا اجرا و تأیید می‌شود):
/// گزارش dry-run بند H30 فقط رکوردهای فرزندِ ساخته‌شده «پس از» بسته‌شدن را برمی‌گرداند و هیچ چیزی را تغییر نمی‌دهد. به PEMSDB هیچ‌چیز نوشته نمی‌شود.
/// </summary>
public class WorkOrderPostClosureReportSqlTests
{
    private static string ReportScript()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "scripts", "cmms", "Report-PostClosureChildRecords.sql");
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        throw new FileNotFoundException("scripts/cmms/Report-PostClosureChildRecords.sql not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public async Task Migration_applies_and_the_dry_run_report_lists_only_children_created_after_closure_without_changing_data()
    {
        await using var db = await DisposableSqlDatabase.CreateAsync();

        // 1) Migration: جدول و ایندکس‌های ممیزی وجود دارند و قابل‌نوشتن‌اند
        Assert.Equal(1, await db.ScalarIntAsync("SELECT COUNT(*) FROM sys.tables WHERE name = 'WorkOrderChangeLogs'"));
        Assert.Equal(2, await db.ScalarIntAsync("SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID('WorkOrderChangeLogs') AND name LIKE 'IX_WorkOrderChangeLogs_%'"));

        int closedId, openId;
        await using (var ctx = db.NewContext())
        {
            var project = new Project { ProjectCode = "P", ProjectName = "P" };
            var discipline = new Discipline { Name = "Mechanical", Code = "MECH", IsActive = true };
            ctx.AddRange(project, discipline);
            await ctx.SaveChangesAsync();
            var equipment = new Equipment { TagNumber = "P-101", Name = "Pump", DisciplineId = discipline.DisciplineId, ProjectId = project.ProjectId, IsActive = true };
            ctx.Equipments.Add(equipment);
            await ctx.SaveChangesAsync();

            var closed = new WorkOrder { WorkOrderNumber = "WO-CLOSED", EquipmentId = equipment.EquipmentId, WorkOrderType = "Corrective", Status = "Closed", IsActive = true };
            var open = new WorkOrder { WorkOrderNumber = "WO-OPEN", EquipmentId = equipment.EquipmentId, WorkOrderType = "Corrective", Status = "InProgress", IsActive = true };
            ctx.WorkOrders.AddRange(closed, open);
            await ctx.SaveChangesAsync();
            closedId = closed.WorkOrderId; openId = open.WorkOrderId;

            ctx.WorkOrderStatusHistories.Add(new WorkOrderStatusHistory { WorkOrderId = closedId, FromStatus = "Completed", ToStatus = "Closed", ChangedDate = new DateTime(2026, 3, 1, 10, 0, 0) });
            ctx.Crews.AddRange(
                new Crew { Code = "PRE", Name = "before close", WorkOrderId = closedId, IsActive = true },
                new Crew { Code = "POST", Name = "after close", WorkOrderId = closedId, IsActive = true },
                new Crew { Code = "POSTINACTIVE", Name = "already flagged", WorkOrderId = closedId, IsActive = false },
                new Crew { Code = "OPENWO", Name = "open work order", WorkOrderId = openId, IsActive = true },
                new Crew { Code = "STANDALONE", Name = "no work order", WorkOrderId = null, IsActive = true });
            await ctx.SaveChangesAsync();

            ctx.WorkOrderChangeLogs.Add(new WorkOrderChangeLog { WorkOrderId = closedId, Section = "Crew", Action = "Add", Outcome = "Denied", Code = "WorkOrderImmutable", ChangedDate = DateTime.Now, IsSecurityEvent = true });
            await ctx.SaveChangesAsync();
        }

        // تاریخ‌ها صریحاً تنظیم می‌شوند (CreatedDate را EF/ممیزی ممکن است عوض کند) - فقط در همین دیتابیس یک‌بارمصرف
        await using (var conn = new SqlConnection(db.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"UPDATE Crews SET CreatedDate = '2026-02-01' WHERE Code = 'PRE';
                                UPDATE Crews SET CreatedDate = '2026-04-01' WHERE Code IN ('POST','POSTINACTIVE','OPENWO','STANDALONE');";
            await cmd.ExecuteNonQueryAsync();
        }

        var before = await db.ScalarIntAsync("SELECT CHECKSUM_AGG(CHECKSUM(CrewId, Code, IsActive, CreatedDate)) FROM Crews");

        // 2) گزارش dry-run
        var details = new List<string>(); var summaryRows = 0;
        await using (var conn = new SqlConnection(db.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = ReportScript();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) details.Add($"{r["WorkOrderNumber"]}|{r["ChildType"]}|{r["Detail"]}");
            Assert.True(await r.NextResultAsync());
            while (await r.ReadAsync()) { summaryRows++; Assert.Equal("WO-CLOSED", r["WorkOrderNumber"]); Assert.Equal(1, (int)r["CrewsAfterClose"]); }
        }

        var only = Assert.Single(details);
        Assert.Equal("WO-CLOSED|Crew|POST / after close", only);       // نه PRE (قبل از بسته‌شدن)، نه غیرفعال، نه WO باز، نه Crew مستقل
        Assert.Equal(1, summaryRows);

        // 3) فقط‌خواندنی: هیچ داده‌ای تغییر نکرده
        Assert.Equal(before, await db.ScalarIntAsync("SELECT CHECKSUM_AGG(CHECKSUM(CrewId, Code, IsActive, CreatedDate)) FROM Crews"));
        Assert.Equal(1, await db.ScalarIntAsync("SELECT COUNT(*) FROM WorkOrderChangeLogs WHERE IsSecurityEvent = 1"));
    }
}
