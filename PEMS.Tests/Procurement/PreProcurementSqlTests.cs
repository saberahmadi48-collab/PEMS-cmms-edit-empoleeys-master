using Microsoft.EntityFrameworkCore;
using PEMS.Domain.Entities;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Procurement;

/// <summary>
/// SCRUM-471 on real SQL Server (an isolated database migrated with the real history): the filtered unique index is the final
/// guard that one source has at most one live pre-procurement item, also when two buyers or two clicks race.
/// </summary>
public class PreProcurementSqlTests
{
    private const string DbName = "PEMSDB_PreProcurementSqlTest";

    private static ProcurementDemand Demand(int sourceId, string status = ProcurementDemandStatuses.Open) => new()
    {
        SourceType = ProcurementDemandSources.WorkOrderDirectPurchase,
        SourceId = sourceId,
        Description = "SQL test",
        Quantity = 1,
        Status = status
    };

    [Fact]
    public async Task A_second_live_item_for_the_same_source_is_refused_by_the_database_but_a_dismissed_one_is_not()
    {
        var sourceId = Random.Shared.Next(1_000_000, int.MaxValue);
        await using (var ctx = PemsDbContextFactory.CreateIsolatedSqlServerOrThrow(DbName))
        {
            ctx.ProcurementDemands.Add(Demand(sourceId, ProcurementDemandStatuses.Dismissed));
            ctx.ProcurementDemands.Add(Demand(sourceId));
            await ctx.SaveChangesAsync();                                   // one dismissed + one live: allowed
        }

        await using (var ctx = PemsDbContextFactory.CreateIsolatedSqlServerOrThrow(DbName))
        {
            ctx.ProcurementDemands.Add(Demand(sourceId, ProcurementDemandStatuses.Converted));
            await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());   // a second live one: refused
        }

        await using (var ctx = PemsDbContextFactory.CreateIsolatedSqlServerOrThrow(DbName))
        {
            Assert.Equal(1, await ctx.ProcurementDemands.CountAsync(d => d.SourceId == sourceId && ProcurementDemandStatuses.Live.Contains(d.Status)));
        }
    }
}
