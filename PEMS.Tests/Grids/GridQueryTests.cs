using Microsoft.EntityFrameworkCore;
using PEMS.Application.Grids;
using PEMS.Domain.Entities;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Grids;

/// <summary>SCRUM-54: the server side of the data grid: the request is read safely, only declared columns can be filtered or sorted, values are parsed by type, the order is always total.</summary>
public sealed class GridQueryTests
{
    private sealed record Row(int Id, string Name, int Age, DateTime Created, bool Active, string? Note);

    private static readonly Row[] Data =
    [
        new(1, "Pump A", 30, new DateTime(2026, 1, 5), true, "north"), new(2, "pump B", 18, new DateTime(2026, 2, 5), false, null),
        new(3, "Valve", 18, new DateTime(2026, 3, 5), true, "south"), new(4, "Motor", 45, new DateTime(2026, 4, 5), true, "North wing"),
        new(5, "Fan", 30, new DateTime(2026, 5, 5), false, "east")
    ];

    private static GridFields<Row> Fields() => new GridFields<Row>()
        .Add("id", r => r.Id).Add("name", r => r.Name, searchable: true).Add("age", r => r.Age).Add("created", r => r.Created)
        .Add("active", r => r.Active).Add("note", r => r.Note, searchable: true).TieBreak("id");

    private static GridRequest Req(string query, int pageSize = 50)
        => GridRequest.FromQuery(query.Split('&', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=', 2)).Select(a => new KeyValuePair<string, string?>(a[0], Uri.UnescapeDataString(a.Length > 1 ? a[1] : ""))), pageSize);

    private static List<int> Ids(string query)
    {
        var req = Req(query);
        return Data.AsQueryable().ApplyGridFilters(req, Fields()).ApplyGridSort(req, Fields()).Paged(req).Select(r => r.Id).ToList();
    }

    [Fact]
    public void The_request_is_read_with_clamped_sizes_sorts_and_filters()
    {
        var r = Req("page=0&pageSize=99999&sort=name,-created,%2Bage&q=%20pump%20&f.name=abc&f.age=gte:18&f.status=in:Open,Closed&f.bad=unknown:1");
        Assert.Equal((1, GridRequest.MaxPageSize), (r.Page, r.PageSize));
        Assert.Equal(new[] { "name", "created", "age" }, r.Sort.Select(s => s.Field));
        Assert.Equal(new[] { false, true, false }, r.Sort.Select(s => s.Descending));
        Assert.Equal("pump", r.Search);
        Assert.Contains(new GridFilter("name", "", "abc"), r.Filters);
        Assert.Contains(new GridFilter("age", "gte", "18"), r.Filters);
        Assert.Contains(new GridFilter("status", "in", "Open,Closed"), r.Filters);
        Assert.Contains(new GridFilter("bad", "", "unknown:1"), r.Filters);   // not an operator: a plain value
    }

    [Fact]
    public void Filters_work_by_the_type_of_the_column()
    {
        Assert.Equal(new[] { 1, 2 }, Ids("f.name=pump"));                  // text: contains, any case
        Assert.Equal(new[] { 3 }, Ids("f.name=startsWith:val"));
        Assert.Equal(new[] { 1, 4, 5 }, Ids("f.age=gte:30"));
        Assert.Equal(new[] { 2, 3 }, Ids("f.age=18"));                     // number: equals
        Assert.Equal(new[] { 4, 5 }, Ids("f.created=gt:2026-03-05"));
        Assert.Equal(new[] { 2, 3 }, Ids("f.created=range:2026-02-01..2026-04-01"));   // from inclusive, to exclusive
        Assert.Equal(new[] { 4, 5 }, Ids("f.created=range:2026-04-01.."));
        Assert.Equal(new[] { 2, 5 }, Ids("f.active=false"));
        Assert.Equal(new[] { 1, 5 }, Ids("f.id=in:1,5"));
        Assert.Equal(new[] { 1, 3, 4, 5 }, Ids("f.id=ne:2"));
        Assert.Equal(new[] { 1, 4 }, Ids("f.note=north"));                 // null-safe
    }

    [Fact]
    public void The_quick_search_looks_in_the_searchable_text_columns_only()
    {
        Assert.Equal(new[] { 1, 2 }, Ids("q=PUMP"));
        Assert.Equal(new[] { 1, 4 }, Ids("q=north"));
        Assert.Empty(Ids("q=18"));                                          // age is not searchable
    }

    [Fact]
    public void Sorting_follows_the_request_and_ties_are_broken_by_the_key_so_paging_is_stable()
    {
        Assert.Equal(new[] { 4, 1, 5, 2, 3 }, Ids("sort=-age"));            // 45, then 30 and 30 by key, then 18 and 18 by key
        Assert.Equal(new[] { 2, 3, 1, 5, 4 }, Ids("sort=age"));
        Assert.Equal(new[] { 4, 5, 1, 3, 2 }, Ids("sort=-age,-id"));
        // pages never repeat or lose a row
        var all = new List<int>();
        for (var page = 1; page <= 3; page++)
        {
            var req = Req($"sort=age&page={page}&pageSize=2");
            all.AddRange(Data.AsQueryable().ApplyGridFilters(req, Fields()).ApplyGridSort(req, Fields()).Paged(req).Select(r => r.Id));
        }
        Assert.Equal(new[] { 2, 3, 1, 5, 4 }, all);
    }

    [Fact]
    public void An_unknown_column_a_bad_value_or_a_wrong_operator_is_refused()
    {
        Assert.Throws<GridQueryException>(() => Ids("f.secret=1"));
        Assert.Throws<GridQueryException>(() => Ids("sort=secret"));
        Assert.Throws<GridQueryException>(() => Ids("f.age=abc"));
        Assert.Throws<GridQueryException>(() => Ids("f.name=gt:a"));
        Assert.Throws<GridQueryException>(() => Ids("f.created=range:2026-01-01"));
        Assert.Throws<GridQueryException>(() => Ids("f.created=gte:not-a-date"));
    }

    [Fact]
    public async Task The_same_request_is_translated_by_entity_framework()
    {
        var ctx = PemsDbContextFactory.CreateInMemory("GridQuery_ef");
        for (var i = 1; i <= 30; i++)
            ctx.FunctionalLocations.Add(new FunctionalLocation { FunctionalLocationId = i, Code = "C-" + i.ToString("D2"), Name = i % 2 == 0 ? "Even " + i : "Odd " + i, IsActive = i % 3 != 0 });
        await ctx.SaveChangesAsync();
        var fields = new GridFields<FunctionalLocation>().Add("code", f => f.Code, true).Add("name", f => f.Name, true).Add("active", f => f.IsActive).Add("id", f => f.FunctionalLocationId).TieBreak("id");
        var req = GridRequest.FromQuery([new("f.name", "even"), new("f.active", "true"), new("sort", "-code"), new("pageSize", "5")]);

        var filtered = ctx.FunctionalLocations.AsNoTracking().ApplyGridFilters(req, fields);
        var total = await filtered.CountAsync();
        var page = await filtered.ApplyGridSort(req, fields).Paged(req).Select(f => f.Code).ToListAsync();
        Assert.Equal(10, total);                                            // even numbers that are not multiples of 3: 2,4,8,10,14,16,20,22,26,28
        Assert.Equal(new[] { "C-28", "C-26", "C-22", "C-20", "C-16" }, page);
    }

    [Fact]
    public void The_csv_export_quotes_cells_keeps_persian_text_and_neutralises_formulas()
    {
        var bytes = GridCsv.Write(["Name", "Note"], [["پمپ", "say \"hi\", ok"], ["=SUM(A1)", null], [new DateTime(2026, 3, 5, 14, 30, 0), 5.5d]]);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3));
        var text = System.Text.Encoding.UTF8.GetString(bytes[3..]);
        Assert.Contains("پمپ,\"say \"\"hi\"\", ok\"", text);
        Assert.Contains("'=SUM(A1),", text);
        Assert.Contains("2026-03-05 14:30,5.5", text);
    }
}
