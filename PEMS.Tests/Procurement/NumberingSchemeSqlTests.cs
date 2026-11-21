using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Application.Numbering;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Procurement;

/// <summary>
/// SCRUM-40 on a real SQL Server (isolated database built from the migrations; fails, not skips, without SQL Server):
/// 50 parallel allocations give 50 different numbers, a number that already exists is skipped, the counter is separate per
/// project when the pattern uses {PROJECT}, and a typed number is ignored unless the scheme allows manual entry.
/// </summary>
public class NumberingSchemeSqlTests
{
    private const string Db = "PEMSDB_NumberingSchemeTest";

    private static async Task UpsertSchemeAsync(PEMSDbContext ctx, string type, string pattern, string reset, bool allowManual = false)
    {
        var s = await ctx.NumberingSchemes.FirstOrDefaultAsync(x => x.DocumentType == type);
        if (s == null) ctx.NumberingSchemes.Add(s = new NumberingScheme { DocumentType = type });
        s.Pattern = pattern; s.PadWidth = 4; s.Calendar = NumberingCalendars.Gregorian; s.ResetPeriod = reset; s.IsActive = true; s.AllowManualEntry = allowManual;
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task Fifty_parallel_allocations_never_share_a_number()
    {
        var type = "SqlTest" + Guid.NewGuid().ToString("N")[..8];
        await using (var seed = PemsDbContextFactory.CreateIsolatedSqlServerOrThrow(Db))
            await UpsertSchemeAsync(seed, type, "T-{YYYY}-{SEQ}", NumberingResetPeriods.Yearly);

        var numbers = await Task.WhenAll(Enumerable.Range(0, 50).Select(async _ =>
        {
            await using var ctx = PemsDbContextFactory.CreateIsolatedSqlServerOrThrow(Db);
            return await new NumberingService(ctx).GetNextNumberAsync(type, NumberingContext.Empty);
        }));

        Assert.Equal(50, numbers.Distinct().Count());
        var year = DateTime.Now.Year;
        Assert.Equal(Enumerable.Range(1, 50).Select(i => $"T-{year}-{i:0000}").OrderBy(x => x), numbers.OrderBy(x => x));
    }

    [Fact]
    public async Task Purchase_orders_skip_existing_numbers_count_per_project_and_ignore_a_typed_number_unless_allowed()
    {
        var lit = "Z" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        int p1, p2, vendorId;
        string code1;
        await using (var seed = PemsDbContextFactory.CreateIsolatedSqlServerOrThrow(Db))
        {
            var a = new Project { ProjectName = "Numbering A", ProjectCode = "NA" + Guid.NewGuid().ToString("N")[..6] };
            var b = new Project { ProjectName = "Numbering B", ProjectCode = "NB" + Guid.NewGuid().ToString("N")[..6] };
            var v = new Vendor { Name = "Numbering vendor " + lit };
            seed.AddRange(a, b, v);
            await seed.SaveChangesAsync();
            (p1, p2, vendorId, code1) = (a.ProjectId, b.ProjectId, v.VendorId, a.ProjectCode.ToUpperInvariant());

            await UpsertSchemeAsync(seed, "PurchaseOrder", lit + "-{PROJECT}-{SEQ}", NumberingResetPeriods.Never);
            // an imported PO already holds what would be the first number of project A
            seed.PurchaseOrders.Add(new PurchaseOrder { PoNumber = $"{lit}-{code1}-0001", ProjectId = p1, VendorId = vendorId, Status = PurchaseOrderStatuses.Issued });
            await seed.SaveChangesAsync();
        }

        async Task<string> CreateAsync(string? typed, int projectId)
        {
            await using var ctx = PemsDbContextFactory.CreateIsolatedSqlServerOrThrow(Db);
            var svc = new PurchaseOrderService(ctx, new NumberingService(ctx));
            var id = await svc.CreateAsync(typed!, projectId, vendorId, null, "numbering test", 1m);
            return (await ctx.PurchaseOrders.AsNoTracking().FirstAsync(x => x.PoId == id)).PoNumber;
        }

        Assert.Equal($"{lit}-{code1}-0002", await CreateAsync(null, p1));                    // 0001 exists: skipped
        Assert.Equal($"{lit}-{code1}-0003", await CreateAsync("TYPED-1", p1));               // manual entry not allowed: ignored
        Assert.EndsWith("-0001", await CreateAsync("", p2));                                  // other project: own counter

        await using (var ctx = PemsDbContextFactory.CreateIsolatedSqlServerOrThrow(Db))
            await UpsertSchemeAsync(ctx, "PurchaseOrder", lit + "-{PROJECT}-{SEQ}", NumberingResetPeriods.Never, allowManual: true);
        Assert.Equal("TYPED-" + lit, await CreateAsync("TYPED-" + lit, p1));                // allowed: kept
        Assert.Equal($"{lit}-{code1}-0004", await CreateAsync(" ", p1));                     // empty: automatic

        // leave the shared isolated database with the default PO scheme for other runs
        await using (var ctx = PemsDbContextFactory.CreateIsolatedSqlServerOrThrow(Db))
            await UpsertSchemeAsync(ctx, "PurchaseOrder", "PO-{YYYY}-{SEQ}", NumberingResetPeriods.Yearly);
    }
}
