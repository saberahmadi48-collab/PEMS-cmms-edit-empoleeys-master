using PEMS.Application.Grids;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>SCRUM-592: group by with count, sum, avg, min and max over the rows of a registered entity; the grouping and the shaping are pure and tested here, the query is a QA line (SQL Server).</summary>
public sealed class EntityAggregateTests
{
    // rows as the provider reads them: the group column first, then the value columns
    private static readonly object?[][] Rows =
    [
        ["Open", 10m, 2], ["Open", 5m, null], ["Closed", 1.5m, 4], ["Open", null, 6], [null, 7m, 1], ["Closed", 2.5m, 8],
    ];

    private static readonly AggregateMeasure[] Measures =
    [
        new("orders", "count", null), new("hours", "sum", 1), new("average", "avg", 1), new("smallest", "min", 2), new("largest", "max", 2),
    ];

    private static Dictionary<string, object?> Group(string key) => EntityAggregateProvider.Aggregate(Rows, "status", Measures).Single(r => (string?)r["status"] == key);

    [Fact]
    public void Rows_are_grouped_by_the_first_column_and_every_function_is_computed_per_group()
    {
        var open = Group("Open");
        Assert.Equal(3, open["orders"]);
        Assert.Equal(15m, open["hours"]);            // the null value is skipped, not counted as 0
        Assert.Equal(7.5m, open["average"]);         // 15 over the two values that exist
        Assert.Equal((2m, 6m), ((decimal?)open["smallest"], (decimal?)open["largest"]));
        var closed = Group("Closed");
        Assert.Equal((2, 4m, 2m), ((int)closed["orders"]!, (decimal)closed["hours"]!, (decimal)closed["average"]!));
        Assert.Equal((4m, 8m), ((decimal?)closed["smallest"], (decimal?)closed["largest"]));
    }

    [Fact]
    public void A_row_without_a_group_key_is_one_group_with_an_empty_key_and_an_all_null_column_gives_null()
    {
        var none = Group("");
        Assert.Equal(1, none["orders"]);
        Assert.Equal(7m, none["hours"]);
        var onlyNulls = EntityAggregateProvider.Aggregate([["A", null, null]], "g", [new("total", "sum", 1), new("n", "count", null)]).Single();
        Assert.Null(onlyNulls["total"]);
        Assert.Equal(1, onlyNulls["n"]);
        Assert.Empty(EntityAggregateProvider.Aggregate([], "g", Measures));
    }

    [Fact]
    public void The_result_is_sorted_by_a_measure_descending_and_paged_with_the_number_of_groups_as_total()
    {
        var grouped = EntityAggregateProvider.Aggregate(Rows, "status", Measures);
        var (items, total) = EntityAggregateProvider.Shape(grouped, new GridRequest(1, 2, [new GridSort("hours", true)], [], null));
        Assert.Equal(3, total);
        Assert.Equal(["Open", ""], items.Cast<Dictionary<string, object?>>().Select(r => (string?)r["status"]));       // 15, then 7, (Closed has 4)
        var (second, _) = EntityAggregateProvider.Shape(grouped, new GridRequest(2, 2, [new GridSort("hours", true)], [], null));
        Assert.Equal(["Closed"], second.Cast<Dictionary<string, object?>>().Select(r => (string?)r["status"]));
    }

    [Fact]
    public void Without_a_sort_the_groups_come_in_the_order_of_the_group_column_and_a_second_sort_breaks_ties()
    {
        var grouped = EntityAggregateProvider.Aggregate(Rows, "status", Measures);
        var (items, _) = EntityAggregateProvider.Shape(grouped, new GridRequest(1, 10, [], [], null));
        Assert.Equal(["", "Closed", "Open"], items.Cast<Dictionary<string, object?>>().Select(r => (string?)r["status"]));
        var tied = EntityAggregateProvider.Aggregate([["a", 1m], ["b", 1m], ["c", 2m]], "g", [new("v", "sum", 1)]);
        var (byValueThenName, _) = EntityAggregateProvider.Shape(tied, new GridRequest(1, 10, [new GridSort("v", false), new GridSort("g", true)], [], null));
        Assert.Equal(["b", "a", "c"], byValueThenName.Cast<Dictionary<string, object?>>().Select(r => (string?)r["g"]));
    }

    [Fact]
    public void Numbers_sort_as_numbers_so_ten_comes_after_nine()
    {
        var grouped = EntityAggregateProvider.Aggregate([["a", 9m], ["b", 10m], ["c", 100m]], "g", [new("v", "sum", 1)]);
        var (items, _) = EntityAggregateProvider.Shape(grouped, new GridRequest(1, 10, [new GridSort("v", false)], [], null));
        Assert.Equal(["a", "b", "c"], items.Cast<Dictionary<string, object?>>().Select(r => (string?)r["g"]));
    }
}
