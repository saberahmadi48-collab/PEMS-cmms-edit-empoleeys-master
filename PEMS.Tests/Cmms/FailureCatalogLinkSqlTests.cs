using Microsoft.Data.SqlClient;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Cmms;

/// <summary>B9: اسکریپت مهاجرت امن متن آزاد خرابی → کاتالوگ ISO 14224 روی دیتابیس SQL یک‌بارمصرف: dry-run پیش‌فرض، فقط تطبیق یکتا، متن اصلی دست‌نخورده.</summary>
public class FailureCatalogLinkSqlTests
{
    private static string Script()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var f = Path.Combine(dir.FullName, "scripts", "cmms", "Link-FailureTextToCatalog.sql");
            if (File.Exists(f)) return File.ReadAllText(f);
        }
        throw new FileNotFoundException();
    }

    private static async Task ExecAsync(DisposableSqlDatabase db, string sql)
    {
        await using var conn = new SqlConnection(db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Free_text_is_linked_only_on_a_unique_catalog_match_and_the_original_text_is_never_rewritten()
    {
        await using var db = await DisposableSqlDatabase.CreateAsync();
        await using (var ctx = db.NewContext())
        {
            var project = new PEMS.Domain.Entities.Project { ProjectCode = "P", ProjectName = "P" };
            var discipline = new PEMS.Domain.Entities.Discipline { Name = "Test discipline", Code = "TD", IsActive = true };
            ctx.AddRange(project, discipline);
            await ctx.SaveChangesAsync();
            var equipment = new PEMS.Domain.Entities.Equipment { TagNumber = "E1", Name = "Pump", DisciplineId = discipline.DisciplineId, ProjectId = project.ProjectId, IsActive = true };
            ctx.Equipments.Add(equipment);
            ctx.FailureModes.AddRange(new PEMS.Domain.Entities.FailureMode { Code = "LEAK", Name = "Leakage", IsActive = true },
                new PEMS.Domain.Entities.FailureMode { Code = "DUP1", Name = "Twin", IsActive = true }, new PEMS.Domain.Entities.FailureMode { Code = "DUP2", Name = "twin", IsActive = true });
            ctx.FailureCauses.Add(new PEMS.Domain.Entities.FailureCause { Code = "WEAR", Name = "Wear", IsActive = true });
            await ctx.SaveChangesAsync();
            foreach (var (n, m, c) in new[] { ("WO-1", "  leak ", "wear"), ("WO-2", "Leakage", null), ("WO-3", "Vibration?", "Gremlins"), ("WO-4", "TWIN", null) })
                ctx.WorkOrders.Add(new PEMS.Domain.Entities.WorkOrder { EquipmentId = equipment.EquipmentId, WorkOrderNumber = n, WorkOrderType = "Corrective", Priority = "Medium", Status = "Closed", IsActive = true, FailureModeCode = m, FailureCauseCode = c });
            await ctx.SaveChangesAsync();
        }

        var checksum = "SELECT CHECKSUM_AGG(CHECKSUM(WorkOrderId, FailureModeCode, FailureCauseCode, FailureModeId, FailureCauseId)) FROM WorkOrders";
        var before = await db.ScalarIntAsync(checksum);
        await ExecAsync(db, Script());                                                     // dry-run
        Assert.Equal(before, await db.ScalarIntAsync(checksum));
        Assert.Equal(0, await db.ScalarIntAsync("SELECT COUNT(*) FROM WorkOrders WHERE FailureModeId IS NOT NULL"));

        await ExecAsync(db, Script().Replace("DECLARE @Apply bit = 0;", "DECLARE @Apply bit = 1;"));
        Assert.Equal(2, await db.ScalarIntAsync("SELECT COUNT(*) FROM WorkOrders WHERE FailureModeId IS NOT NULL"));      // WO-1 (Code, trimmed, case-insens.) و WO-2 (Name)
        Assert.Equal(1, await db.ScalarIntAsync("SELECT COUNT(*) FROM WorkOrders WHERE FailureCauseId IS NOT NULL"));     // فقط WO-1: «wear»؛ «Gremlins» نامطابق
        Assert.Equal(0, await db.ScalarIntAsync("SELECT COUNT(*) FROM WorkOrders WHERE WorkOrderNumber IN (N'WO-3', N'WO-4') AND FailureModeId IS NOT NULL"));   // نامطابق و مبهم (دو ردیف کاتالوگ) لمس نمی‌شود
        Assert.Equal(1, await db.ScalarIntAsync("SELECT COUNT(*) FROM WorkOrders WHERE WorkOrderNumber = N'WO-1' AND FailureModeCode = N'  leak '"));               // متن اصلی بازنویسی نشد

        await ExecAsync(db, Script().Replace("DECLARE @Apply bit = 0;", "DECLARE @Apply bit = 1;"));                      // Idempotent
        Assert.Equal(2, await db.ScalarIntAsync("SELECT COUNT(*) FROM WorkOrders WHERE FailureModeId IS NOT NULL"));
    }
}
