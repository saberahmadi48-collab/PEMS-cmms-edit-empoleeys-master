using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.Definitions;
using PEMS.Application.Migration;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Tests.TestSupport;
using PEMS.Web.Definitions;
using PEMS.Web.Services;

namespace PEMS.Tests.Deployment;

/// <summary>
/// SCRUM-807: a feed package is applied through the entity catalog (writable fields, permissions, validation), all or nothing, and a second run of the same package changes nothing. SCRUM-815: CSV rows, key fields and the
/// preview of the package. In-memory database; the transaction is a pass-through (the EF transaction is a SQL Server QA line).
/// </summary>
public sealed class PackageFeedApplierTests
{
    private readonly string _database = "feed-" + Guid.NewGuid();

    private sealed class PassThrough : IActionTransaction
    {
        public async Task<ActionOutcome> RunAsync(Func<Task<ActionOutcome>> body, CancellationToken ct) => await body();
    }

    private sealed class Allow(bool create = true, bool edit = true) : IDefinitionAccess
    {
        public Task<bool> CanOpenAsync(string? permission, IReadOnlyCollection<string> roles, CancellationToken ct = default) => Task.FromResult(permission switch { "Dept.Create" => create, "Dept.Edit" => edit, _ => true });
    }

    private PackageFeedApplier Applier(bool create = true, bool edit = true)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => PemsDbContextFactory.CreateInMemory(_database));
        services.AddSingleton<IDefinitionAccess>(new Allow(create, edit));
        var catalog = new EntityCatalog().Add<Department>("DepartmentId", e => e.Create("Dept.Create").Edit("Dept.Edit").Readable("Code", "Name", "Description").Writable("Code", "Name", "Description"));
        return new PackageFeedApplier(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), catalog, new PassThrough());
    }

    private static (PackageManifest Manifest, IReadOnlyDictionary<string, byte[]> Files) Package(string mode, string rowsJson, string keyFields = "[\"Code\"]")
    {
        var data = $"{{ \"keyFields\": {keyFields}, \"rows\": {rowsJson} }}";
        var manifest = new PackageManifest(1, PackageKind.Feed, null, "1.1.0", null, [], [new PackageDataSet("Department", mode, "data/Department.json", 2)], new Dictionary<string, string>(), "vendor-1", "sig");
        return (manifest, new Dictionary<string, byte[]> { ["data/Department.json"] = Encoding.UTF8.GetBytes(data) });
    }

    private async Task<List<(string Code, string Name)>> Rows()
    {
        await using var db = PemsDbContextFactory.CreateInMemory(_database);
        return (await db.Set<Department>().OrderBy(d => d.Code).ToListAsync()).Select(d => (d.Code, d.Name)).ToList();
    }

    private static Task<FeedApplyResult> Apply(PackageFeedApplier a, (PackageManifest Manifest, IReadOnlyDictionary<string, byte[]> Files) p, bool dryRun = false)
        => a.ApplyAsync(p.Manifest, p.Files, "hash", ["SystemAdmin"], "hamid", dryRun);

    [Fact]
    public async Task A_feed_adds_its_rows_and_a_second_run_of_the_same_package_changes_nothing()
    {
        var package = Package("append", """[ { "Code": "D1", "Name": "One" }, { "Code": "D2", "Name": "Two" } ]""");
        var first = await Apply(Applier(), package);
        Assert.True(first.Ok, first.Summary);
        Assert.True(first.Written);
        Assert.Equal([("D1", "One"), ("D2", "Two")], await Rows());
        var again = await Apply(Applier(), package);
        Assert.True(again.Ok);
        Assert.Equal(2, again.DataSets[0].Skipped);                 // idempotent: both rows exist, nothing is added
        Assert.Equal(2, (await Rows()).Count);
    }

    [Fact]
    public async Task A_dry_run_writes_nothing()
    {
        var result = await Apply(Applier(), Package("append", """[ { "Code": "D1", "Name": "One" } ]"""), dryRun: true);
        Assert.True(result.Ok);
        Assert.False(result.Written);
        Assert.Equal(1, result.DataSets[0].Added);
        Assert.Empty(await Rows());
    }

    [Fact]
    public async Task With_all_or_nothing_one_refused_row_leaves_the_data_unchanged_and_lists_the_row()
    {
        var package = Package("append", """[ { "Code": "D1", "Name": "One" }, { "Code": "D2", "Name": "Two", "Secret": "x" }, { "Code": "D1", "Name": "Again" } ]""");
        var result = await Apply(Applier(), package);
        Assert.False(result.Ok);
        Assert.False(result.Written);
        Assert.Empty(await Rows());                                                              // nothing was written
        var refused = result.DataSets[0].Refused;
        Assert.Contains(refused, r => r.Row == 2 && r.Error!.Contains("'Secret'"));              // not a field a feed may write
        Assert.Contains(refused, r => r.Row == 3 && r.Error!.Contains("twice"));                 // the key twice in the file
    }

    [Fact]
    public async Task Overwrite_changes_only_what_differs_and_merge_fills_only_empty_fields()
    {
        await Apply(Applier(), Package("append", """[ { "Code": "D1", "Name": "One" } ]"""));
        var over = await Apply(Applier(), Package("overwrite", """[ { "Code": "D1", "Name": "Uno" } ]"""));
        Assert.Equal(1, over.DataSets[0].Changed);
        Assert.Equal([("D1", "Uno")], await Rows());
        var merge = await Apply(Applier(), Package("merge", """[ { "Code": "D1", "Name": "Other", "Description": "filled" } ]"""));
        Assert.Equal(1, merge.DataSets[0].Changed);
        await using var db = PemsDbContextFactory.CreateInMemory(_database);
        var row = await db.Set<Department>().SingleAsync();
        Assert.Equal(("Uno", "filled"), (row.Name, row.Description));                           // the name was not empty, so merge kept it
    }

    [Fact]
    public async Task The_create_and_edit_permissions_of_the_entity_decide_who_may_feed()
    {
        var add = Package("append", """[ { "Code": "D1", "Name": "One" } ]""");
        var denied = await Apply(Applier(create: false), add);
        Assert.False(denied.Ok);
        Assert.Contains("may not add", denied.Error);
        Assert.Empty(await Rows());
        await Apply(Applier(), add);
        var change = Package("overwrite", """[ { "Code": "D1", "Name": "Uno" } ]""");
        Assert.Contains("may not change", (await Apply(Applier(edit: false), change)).Error);
    }

    [Fact]
    public async Task An_entity_the_catalog_does_not_register_is_refused()
    {
        var manifest = new PackageManifest(1, PackageKind.Feed, null, "1.1.0", null, [], [new PackageDataSet("Users", "append", "data/Users.json", 1)], new Dictionary<string, string>(), "vendor-1", "sig");
        var result = await Applier().ApplyAsync(manifest, new Dictionary<string, byte[]> { ["data/Users.json"] = Encoding.UTF8.GetBytes("""{ "keyFields": ["Id"], "rows": [ { "Id": "1" } ] }""") }, "h", ["SystemAdmin"], "hamid");
        Assert.False(result.Ok);
        Assert.Contains("not one a feed may write", result.Error);
    }

    [Theory]
    [InlineData("""{ "rows": [] }""")]
    [InlineData("""{ "keyFields": ["Code"] }""")]
    [InlineData("""[ { "Code": "D1" } ]""")]
    [InlineData("not json")]
    public void A_data_file_without_key_fields_and_rows_is_refused(string json)
        => Assert.Throws<PackageRefusedException>(() => PackageFeedApplier.ReadDataFile(Encoding.UTF8.GetBytes(json)));

    // ---- SCRUM-815: CSV, key fields, preview

    [Fact]
    public void Csv_rows_become_objects_with_quotes_separators_and_line_breaks_handled()
    {
        var rows = FeedRows.Parse("Code;Name;Description\r\nD1;\"One; the first\";\"line 1\nline 2\"\r\nD2;Two;\r\n");
        Assert.Equal(2, rows.Count);
        Assert.Equal("One; the first", (string)rows[0]!["Name"]!);
        Assert.Equal("line 1\nline 2", (string)rows[0]!["Description"]!);
        Assert.Equal("", (string)rows[1]!["Description"]!);
        Assert.Equal("Code", FeedRows.DefaultKey(rows));
    }

    [Theory]
    [InlineData("Code,Name")]
    [InlineData("Code,Name\nD1")]
    [InlineData("Code,Code\nD1,D2")]
    [InlineData("Code,Name\nD1,\"unclosed")]
    public void A_bad_csv_is_refused_with_a_reason(string csv) => Assert.Throws<FormatException>(() => FeedRows.Parse(csv));

    [Fact]
    public async Task The_preview_names_the_key_and_the_refused_rows_and_a_build_with_a_refused_row_signs_nothing()
    {
        var builder = new PackageBuilder(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<PackageBuilder>.Instance,
            new EntityCatalog().Add<Department>("DepartmentId", e => e.Readable("Code", "Name").Writable("Code", "Name")));
        var tooMany = await builder.PreviewFeedAsync("1.1.0", "Department", "append", "Code,Name\nD1,One\nD3,Three,extra");
        Assert.False(tooMany.Succeeded);                                                          // the CSV line with too many cells
        var secret = await builder.PreviewFeedAsync("1.1.0", "Department", "append", "Code,Name,Secret\nD1,One,x\nD2,Two,y");
        Assert.False(secret.Succeeded);
        Assert.Contains("'Secret' is not a field a feed may write", secret.Message);
        var ok = await builder.PreviewFeedAsync("1.1.0", "Department", "append", "Code,Name\nD1,One\nD2,Two");
        Assert.True(ok.Succeeded, ok.Message);
        Assert.Contains("2 row(s)", ok.Message);
        Assert.Contains("key Code", ok.Message);
        var built = await builder.BuildFeedAsync("1.1.0", "Department", "append", "Code,Name,Secret\nD1,One,x");
        Assert.False(built.Success);                                                              // refused before anything is signed (and there is no signing key here either)
        Assert.Null(built.Bytes);
    }
}
