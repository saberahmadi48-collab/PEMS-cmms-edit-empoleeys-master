using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Domain.Entities;
using PEMS.Persistence.Seed;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Cmms;

/// <summary>
/// C18 روی دیتابیس SQL یک‌بارمصرف (Migration واقعی + داده‌ی HasData): Seeder جدید دیسیپلین تکراری نمی‌سازد و اسکریپت پاک‌سازی روی دیتابیس‌های «قبلاً تکراری‌شده»
/// حالت dry-run دارد، فقط تکراری‌های بدون ارجاع را غیرفعال می‌کند و چیزی را حذف نمی‌کند. به PEMSDB هیچ‌چیز نوشته نمی‌شود.
/// </summary>
public class DisciplineDedupeSqlTests
{
    private static string Script(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "scripts", "cmms", name);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        throw new FileNotFoundException(name);
    }

    private static async Task<List<string>> RunAsync(string connectionString, string sql)
    {
        var rows = new List<string>();
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await using var r = await cmd.ExecuteReaderAsync();
        do { while (await r.ReadAsync()) rows.Add(string.Join("|", Enumerable.Range(0, r.FieldCount).Select(i => Convert.ToString(r.GetValue(i))))); }
        while (await r.NextResultAsync());
        return rows;
    }

    [Fact]
    public async Task Seeder_adds_no_duplicate_of_a_legacy_discipline_and_the_cleanup_script_is_a_safe_dry_run_first_tool()
    {
        await using var db = await DisposableSqlDatabase.CreateAsync();
        await using (var ctx = db.NewContext())
        {
            var legacyElectrical = await ctx.Disciplines.CountAsync(d => d.Name == "Electrical");
            Assert.Equal(1, legacyElectrical);                                                     // HasData: EL / Electrical
            var sp = new ServiceCollection().AddSingleton(ctx).BuildServiceProvider();
            await DisciplineSeeder.SeedAsync(sp);
            await DisciplineSeeder.SeedAsync(sp);                                                  // Idempotent

            foreach (var name in new[] { "Electrical", "Process", "Piping", "Civil" })
                Assert.Equal(1, await ctx.Disciplines.CountAsync(d => d.Name == name));
            Assert.Equal(0, await ctx.Disciplines.CountAsync(d => d.Code == "ELEC"));              // نیاز به ELEC با Alias برآورده می‌شود
            Assert.NotNull(DisciplineSeeder.Resolve(await ctx.Disciplines.ToListAsync(), "ELEC"));
            Assert.True(await ctx.Disciplines.AnyAsync(d => d.Code == "UTIL"));                    // موارد واقعاً جدید همچنان ساخته می‌شوند

            await MaintenanceGroupSeeder.SeedAsync(sp);
            Assert.True(await ctx.MaintenanceGroups.AnyAsync(g => g.Code == "ELEC-MOT"));          // گروه‌های وابسته به ELEC روی دیسیپلین قدیمی (EL) ساخته شدند
        }

        // دیتابیس «قبلاً تکراری‌شده»: Seeder قدیمی یک ELEC دوم ساخته بود
        await using (var conn = new SqlConnection(db.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT Disciplines (Code, Name, Description, ImportAliases, IsActive, CreatedDate) VALUES (N'ELEC', N'Electrical', N'old seeder', N'EL', 1, SYSDATETIME()),
                                                                                         (N'PROC', N'Process',    N'old seeder', N'PR', 1, SYSDATETIME());";
            await cmd.ExecuteNonQueryAsync();
        }
        var procId = await db.ScalarIntAsync("SELECT DisciplineId FROM Disciplines WHERE Code = N'PROC'");
        await using (var conn = new SqlConnection(db.ConnectionString))      // PROC دوم «استفاده‌شده» است (یک Employee به آن وصل شده)
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT Departments (Name, Code, IsActive, CreatedDate) VALUES (N'D', N'DDD', 1, SYSDATETIME());
                                DECLARE @dept int = SCOPE_IDENTITY();
                                INSERT Employees (FirstName, LastName, DepartmentId, DisciplineId, IsActive, CreatedDate)
                                VALUES (N'A', N'B', @dept, @d, 1, SYSDATETIME());";
            cmd.Parameters.AddWithValue("@d", procId);
            await cmd.ExecuteNonQueryAsync();
        }

        var script = Script("Cleanup-DuplicateDisciplines.sql");
        var checksum = "SELECT CHECKSUM_AGG(CHECKSUM(DisciplineId, Code, Name, IsActive)) FROM Disciplines";
        var before = await db.ScalarIntAsync(checksum);

        var report = await RunAsync(db.ConnectionString, script);                                 // dry-run (@Apply = 0)
        Assert.Contains(report, r => r.Contains("|ELEC|Electrical|") && r.Contains("unused"));
        Assert.Contains(report, r => r.Contains("|PROC|Process|") && r.Contains("referenced: manual merge required"));
        Assert.Equal(before, await db.ScalarIntAsync(checksum));                                  // هیچ تغییری

        await RunAsync(db.ConnectionString, script.Replace("DECLARE @Apply bit = 0;", "DECLARE @Apply bit = 1;"));
        Assert.Equal(0, await db.ScalarIntAsync("SELECT COUNT(*) FROM Disciplines WHERE Code = N'ELEC' AND IsActive = 1"));   // بدون ارجاع → غیرفعال
        Assert.Equal(1, await db.ScalarIntAsync("SELECT COUNT(*) FROM Disciplines WHERE Code = N'ELEC'"));                    // حذف نشده
        Assert.Equal(1, await db.ScalarIntAsync("SELECT COUNT(*) FROM Disciplines WHERE Code = N'PROC' AND IsActive = 1"));   // ارجاع‌دار دست‌نخورده

        // گزارش مشکوک‌های تست فقط‌خواندنی است و اجرا می‌شود
        var suspects = await RunAsync(db.ConnectionString, Script("Report-TestDataSuspects.sql"));
        Assert.NotNull(suspects);
    }
}
