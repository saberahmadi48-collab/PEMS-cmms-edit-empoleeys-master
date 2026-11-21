using ClosedXML.Excel;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Organization;

/// <summary>
/// SCRUM-497: the organization Excel import must commit on SQL Server with the production retrying execution strategy
/// (Program.cs: EnableRetryOnFailure), which refuses a user transaction opened outside CreateExecutionStrategy().
/// InMemory has no execution strategy, so only a real SQL Server shows the bug. Runs on a temporary PEMS_ORGIMP_* database
/// that is dropped at the end (PEMSDB is never touched).
/// </summary>
public class OrganizationImportSqlServerTests : IAsyncLifetime
{
    private readonly string _dbName = "PEMS_ORGIMP_" + Guid.NewGuid().ToString("N")[..12];
    private string _connection = "";

    private DbContextOptions<PEMSDbContext> RetryingOptions() =>
        new DbContextOptionsBuilder<PEMSDbContext>().UseSqlServer(_connection, o => o.EnableRetryOnFailure()).Options;

    public async Task InitializeAsync()
    {
        try
        {
            _connection = new SqlConnectionStringBuilder(PemsDbContextFactory.ConnectionString) { InitialCatalog = _dbName }.ConnectionString;
            await using var ctx = new PEMSDbContext(new DbContextOptionsBuilder<PEMSDbContext>().UseSqlServer(_connection).Options);
            await ctx.Database.MigrateAsync();
            var dept = new Department { Name = "Ops", Code = "OPS", IsActive = true };
            var disc = new Discipline { Name = "Mechanical", Code = "MECH", IsActive = true };
            ctx.AddRange(dept, disc);
            await ctx.SaveChangesAsync();
            ctx.Employees.AddRange(
                new Employee { FirstName = "Org", LastName = "One", PersonnelCode = "ORG-P1", DepartmentId = dept.DepartmentId, DisciplineId = disc.DisciplineId, IsActive = true },
                new Employee { FirstName = "Org", LastName = "Two", PersonnelCode = "ORG-P2", DepartmentId = dept.DepartmentId, DisciplineId = disc.DisciplineId, IsActive = true });
            await ctx.SaveChangesAsync();
        }
        catch { await DisposeAsync(); throw; }
    }

    public async Task DisposeAsync()
    {
        if (_connection.Length == 0) return;
        SqlConnection.ClearAllPools();
        var master = new SqlConnectionStringBuilder(_connection) { InitialCatalog = "master" }.ConnectionString;
        await using var c = new SqlConnection(master);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"IF DB_ID('{_dbName}') IS NOT NULL BEGIN ALTER DATABASE [{_dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_dbName}]; END";
        await cmd.ExecuteNonQueryAsync();
    }

    private static MemoryStream Xlsx(params string[][] rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Org");
        for (var c = 0; c < OrganizationImportService.Columns.Length; c++) ws.Cell(1, c + 1).Value = OrganizationImportService.Columns[c];
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < rows[r].Length; c++) ws.Cell(r + 2, c + 1).Value = rows[r][c];
        var ms = new MemoryStream(); wb.SaveAs(ms); ms.Position = 0; return ms;
    }

    // Column order: Department, Position, PositionCode, PositionTitle, ParentPositionCode, EmployeePersonnelCode, IsPrimary, AssignmentType, EffectiveFrom, EffectiveTo
    private static string[] Ceo() => new[] { "Ops", "", "CEO", "Chief", "", "ORG-P2", "true", "Primary", "", "" };
    private static string[] Eng() => new[] { "Ops", "", "ENG", "Engineer", "CEO", "ORG-P1", "", "", "", "" };

    [Fact]
    public async Task Import_commits_under_the_production_retrying_execution_strategy()
    {
        await using (var ctx = new PEMSDbContext(RetryingOptions()))
        {
            var result = await new OrganizationImportService(ctx).ImportAsync(Xlsx(Ceo(), Eng()), commit: true);
            Assert.True(result.Committed, string.Join(" | ", result.Errors));
            Assert.Empty(result.Errors);
            Assert.Equal(2, result.PositionsCreated);
            Assert.Equal(2, result.AssignmentsCreated);
        }

        // read back with a fresh context: the rows are in the database, once, with the hierarchy from the file
        await using var check = new PEMSDbContext(RetryingOptions());
        var positions = await check.Positions.AsNoTracking().Where(p => p.PositionCode == "CEO" || p.PositionCode == "ENG").ToListAsync();
        Assert.Equal(2, positions.Count);
        Assert.Equal(positions.Single(p => p.PositionCode == "CEO").PositionId, positions.Single(p => p.PositionCode == "ENG").ParentPositionId);
        Assert.Equal(2, await check.EmployeeAssignments.CountAsync(a => a.IsActive));
    }

    [Fact]
    public async Task Import_again_on_sql_server_changes_nothing()
    {
        await using (var ctx = new PEMSDbContext(RetryingOptions()))
            Assert.True((await new OrganizationImportService(ctx).ImportAsync(Xlsx(Ceo(), Eng()), commit: true)).Committed);

        await using var again = new PEMSDbContext(RetryingOptions());
        var second = await new OrganizationImportService(again).ImportAsync(Xlsx(Ceo(), Eng()), commit: true);
        Assert.Empty(second.Errors);
        Assert.Equal(0, second.PositionsCreated + second.PositionsUpdated + second.AssignmentsCreated + second.AssignmentsUpdated);
        Assert.Equal(2, await again.Positions.CountAsync(p => p.PositionCode == "CEO" || p.PositionCode == "ENG"));
    }
}
