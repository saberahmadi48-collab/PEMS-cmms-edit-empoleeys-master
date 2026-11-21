using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.Definitions;
using PEMS.Application.Grids;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Seed;
using PEMS.Tests.TestSupport;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>The master-data pages of the waves (maintenance groups, skills, storage locations ...) run on the generic layer: lookups in the grid, selects from entities, composite unique rules, the catalog's permissions.</summary>
public sealed class WaveMasterDataTests
{
    private static readonly EntityCatalog Catalog = EntityCatalog.Default();

    private sealed class Access(Func<string?, bool> allow) : IDefinitionAccess
    {
        public Task<bool> CanOpenAsync(string? permission, IReadOnlyCollection<string> roles, CancellationToken ct = default) => Task.FromResult(allow(permission));
    }

    private static (IServiceScopeFactory Scopes, string Db) Setup(Func<string?, bool>? allow = null)
    {
        var db = "wave-" + Guid.NewGuid();
        var sp = new ServiceCollection().AddScoped(_ => PemsDbContextFactory.CreateInMemory(db)).AddSingleton<IDefinitionAccess>(new Access(allow ?? (_ => true))).BuildServiceProvider();
        return (sp.GetRequiredService<IServiceScopeFactory>(), db);
    }

    private static JsonObject Shipped(string kind, string key)
        => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, SystemDefinitionSeeder.FolderName, kind, "core", key + ".json")))!["payload"]!;

    private static GridDataRequest Grid(string dataSource, params (string, string)[] query)
        => new(GridRequest.FromQuery(query.Select(q => new KeyValuePair<string, string?>(q.Item1, q.Item2))), "en", ["Engineer"], "x",
            Shipped("data-source", dataSource)["parameters"]!.AsObject().ToDictionary(x => x.Key, x => (string?)(x.Value is JsonValue v && v.TryGetValue<string>(out var s) ? s : x.Value!.ToJsonString())));

    private static ActionInvocation Invoke(string action, params (string, string?)[] values)
        => new(values.ToDictionary(v => v.Item1, v => v.Item2), "en", ["Engineer"], "x", Shipped("action", action)["parameters"]!.AsObject());

    private static string En(PEMS.UI.Abstractions.TranslatableText t) => t.Resolve(new CultureInfo("en"));

    [Fact]
    public async Task A_maintenance_group_is_created_with_its_discipline_and_the_grid_shows_the_discipline_name()
    {
        var (scopes, name) = Setup();
        int discipline;
        using (var db = PemsDbContextFactory.CreateInMemory(name)) { var d = new Discipline { Code = "MECH", Name = "Mechanical", IsActive = true }; db.Disciplines.Add(d); db.SaveChanges(); discipline = d.DisciplineId; }

        var create = new EntityCreateAction(scopes, Catalog);
        Assert.True((await create.ExecuteAsync(Invoke("maintenance-groups-create", ("code", " PUMP "), ("name", "Pumps"), ("disciplineId", discipline.ToString()), ("description", "")), CancellationToken.None)).Ok);
        var dupName = await create.ExecuteAsync(Invoke("maintenance-groups-create", ("code", "X"), ("name", "pumps"), ("disciplineId", discipline.ToString())), CancellationToken.None);
        Assert.Equal("A maintenance group with this name already exists.", En(dupName.FieldErrors!["name"]));
        var dupCode = await create.ExecuteAsync(Invoke("maintenance-groups-create", ("code", "pump"), ("name", "Other"), ("disciplineId", discipline.ToString())), CancellationToken.None);
        Assert.Equal("A maintenance group with this code already exists.", En(dupCode.FieldErrors!["code"]));
        Assert.Contains("disciplineId", (await create.ExecuteAsync(Invoke("maintenance-groups-create", ("code", "Z"), ("name", "Z"), ("disciplineId", "")), CancellationToken.None)).FieldErrors!.Keys);   // the discipline is required

        var rows = (await new EntityGridProvider(scopes, Catalog).QueryAsync(Grid("maintenance-groups-grid"), CancellationToken.None)).Items.Cast<Dictionary<string, object?>>().ToList();
        Assert.Equal(("PUMP", "Pumps", "Mechanical"), ((string)rows[0]["code"]!, (string)rows[0]["name"]!, (string)rows[0]["discipline"]!));
    }

    [Fact]
    public async Task A_skill_may_have_no_discipline_and_a_location_code_is_unique_only_inside_its_warehouse()
    {
        var (scopes, name) = Setup();
        var create = new EntityCreateAction(scopes, Catalog);
        Assert.True((await create.ExecuteAsync(Invoke("skills-create", ("name", "Welding"), ("disciplineId", "")), CancellationToken.None)).Ok);
        using (var db = PemsDbContextFactory.CreateInMemory(name)) Assert.Null((await db.Skills.SingleAsync()).DisciplineId);

        int w1, w2;
        using (var db = PemsDbContextFactory.CreateInMemory(name)) { var a = new Warehouse { Code = "W1", Name = "One" }; var b = new Warehouse { Code = "W2", Name = "Two" }; db.Warehouses.AddRange(a, b); db.SaveChanges(); w1 = a.WarehouseId; w2 = b.WarehouseId; }
        Assert.True((await create.ExecuteAsync(Invoke("storage-locations-create", ("code", "A1"), ("name", "Shelf"), ("warehouseId", w1.ToString())), CancellationToken.None)).Ok);
        Assert.True((await create.ExecuteAsync(Invoke("storage-locations-create", ("code", "A1"), ("name", "Shelf"), ("warehouseId", w2.ToString())), CancellationToken.None)).Ok);   // same code, another warehouse
        var clash = await create.ExecuteAsync(Invoke("storage-locations-create", ("code", "a1"), ("name", "Other"), ("warehouseId", w1.ToString())), CancellationToken.None);
        Assert.False(clash.Ok);
        Assert.Equal("A location with this code already exists in this warehouse.", En(clash.FieldErrors?.Values.FirstOrDefault() ?? clash.FormError!));

        var rows = (await new EntityGridProvider(scopes, Catalog).QueryAsync(Grid("storage-locations-grid", ("sort", "warehouseId")), CancellationToken.None)).Items.Cast<Dictionary<string, object?>>().ToList();
        Assert.Equal(["One", "Two"], rows.Select(r => (string)r["warehouse"]!));
        Assert.All(rows, r => Assert.Null(r["parent"]));
    }

    [Fact]
    public async Task The_maintenance_master_data_permissions_of_the_catalog_decide_reads_and_writes()
    {
        var (scopes, db) = Setup(permission => permission is null || permission == "MaintenanceMasterData.View");
        Assert.NotNull((await new EntityGridProvider(scopes, Catalog).QueryAsync(Grid("skills-grid"), CancellationToken.None)).Items);          // View is allowed
        var refused = await new EntityCreateAction(scopes, Catalog).ExecuteAsync(Invoke("skills-create", ("name", "Sneaky")), CancellationToken.None);   // Create is not
        Assert.False(refused.Ok);
        using var ctx = PemsDbContextFactory.CreateInMemory(db);
        Assert.Empty(await ctx.Skills.ToListAsync());
    }

    [Fact]
    public void The_lookup_parameters_are_checked_at_load()
    {
        var grid = new EntityGridProvider(null!, Catalog);
        JsonObject P(string json) => JsonNode.Parse(json)!.AsObject();
        Assert.Empty(grid.ValidateParameters(P("""{"entity":"Skill","fields":["Name","DisciplineId"],"lookups":[{"name":"discipline","entity":"Discipline","foreignKey":"DisciplineId","label":"Name"}]}""")));
        Assert.Contains("unknown-entity", grid.ValidateParameters(P("""{"entity":"Skill","fields":["Name"],"lookups":[{"name":"x","entity":"Nope","foreignKey":"DisciplineId","label":"Name"}]}""")).Select(e => e.Code));
        Assert.Contains("not-readable", grid.ValidateParameters(P("""{"entity":"Skill","fields":["Name"],"lookups":[{"name":"x","entity":"Discipline","foreignKey":"Secret","label":"Name"}]}""")).Select(e => e.Code));
        Assert.Contains("not-readable", grid.ValidateParameters(P("""{"entity":"Skill","fields":["Name"],"lookups":[{"name":"x","entity":"Discipline","foreignKey":"DisciplineId","label":"Secret"}]}""")).Select(e => e.Code));
        Assert.Contains("not-exposed", grid.ValidateParameters(P("""{"entity":"Skill","fields":["Name"],"sort":["Description"]}""")).Select(e => e.Code));
    }

    [Fact]
    public void The_deny_rules_are_checked_at_load_and_decide_at_run_time()
    {
        var update = new EntityUpdateAction(null!, Catalog);
        JsonObject P(string json) => JsonNode.Parse(json)!.AsObject();
        Assert.Empty(update.ValidateParameters(P("""{"entity":"Skill","map":{"name":"Name"},"deny":[{"when":"row.IsActive == false","message":{"en":"Inactive","fa":"غیرفعال"}}]}""")));
        Assert.Contains("Def_Err_Expression", update.ValidateParameters(P("""{"entity":"Skill","map":{"name":"Name"},"deny":[{"when":"row.IsActive ==","message":{"en":"a","fa":"ب"}}]}""")).Select(e => e.MessageKey));
        Assert.Contains("Def_Err_Expression", update.ValidateParameters(P("""{"entity":"Skill","map":{"name":"Name"},"deny":[{"when":"page.title == 1","message":{"en":"a","fa":"ب"}}]}""")).Select(e => e.MessageKey));   // a root the rule may not read
        Assert.Contains("missing-translation", update.ValidateParameters(P("""{"entity":"Skill","map":{"name":"Name"},"deny":[{"when":"true","message":{"en":"a"}}]}""")).Select(e => e.Code));
    }
}

/// <summary>Generic capabilities added for the waves: time of day and enum values, fixed filters and presets (column against column) in a data source, and the spare parts pages that use them.</summary>
public sealed class GenericCapabilityTests
{
    private static readonly EntityCatalog Catalog = EntityCatalog.Default();

    private enum Level { Low, High }

    private sealed class Shift
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public TimeSpan Start { get; set; }
        public Level Level { get; set; }
    }

    private static (IServiceScopeFactory Scopes, string Db) Setup()
    {
        var db = "gen-" + Guid.NewGuid();
        var sp = new ServiceCollection().AddScoped(_ => PemsDbContextFactory.CreateInMemory(db)).AddSingleton<IDefinitionAccess>(new AllowAll()).BuildServiceProvider();
        return (sp.GetRequiredService<IServiceScopeFactory>(), db);
    }

    private sealed class AllowAll : IDefinitionAccess
    {
        public Task<bool> CanOpenAsync(string? permission, IReadOnlyCollection<string> roles, CancellationToken ct = default) => Task.FromResult(true);
    }

    private static JsonObject Shipped(string kind, string key)
        => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, SystemDefinitionSeeder.FolderName, kind, "core", key + ".json")))!["payload"]!;

    private static GridDataRequest Grid(string dataSource)
        => new(GridRequest.FromQuery([]), "en", ["Engineer"], "x", Shipped("data-source", dataSource)["parameters"]!.AsObject().ToDictionary(x => x.Key, x => (string?)(x.Value is JsonValue v && v.TryGetValue<string>(out var s) ? s : x.Value!.ToJsonString())));

    private static ActionInvocation Invoke(string action, params (string, string?)[] values)
        => new(values.ToDictionary(v => v.Item1, v => v.Item2), "en", ["Engineer"], "x", Shipped("action", action)["parameters"]!.AsObject());

    [Fact]
    public void Time_of_day_and_enum_values_are_converted_shown_and_refused_when_they_do_not_fit()
    {
        var convert = typeof(EntityCatalog).Assembly.GetType("PEMS.Web.Definitions.EntityJson")!.GetMethod("Convert", [typeof(Type), typeof(string)])!;
        object? C(Type t, string? v) => convert.Invoke(null, [t, v]);
        Assert.Equal(new TimeSpan(6, 30, 0), C(typeof(TimeSpan), "06:30"));
        Assert.Equal(new TimeSpan(6, 30, 0), C(typeof(TimeSpan), "۰۶:۳۰"));
        Assert.Equal("NotConvertible", C(typeof(TimeSpan), "25:99")!.GetType().Name);
        Assert.Equal(Level.High, C(typeof(Level), "high"));
        Assert.Equal("NotConvertible", C(typeof(Level), "Nope")!.GetType().Name);
        Assert.Equal("NotConvertible", C(typeof(Level), "7")!.GetType().Name);                   // a number that is not a defined value
        var supported = typeof(EntityCatalog).Assembly.GetType("PEMS.Web.Definitions.EntityJson")!.GetMethod("Supported")!;
        Assert.True((bool)supported.Invoke(null, [typeof(TimeSpan)])!);
        Assert.True((bool)supported.Invoke(null, [typeof(Level?)])!);
        Assert.False((bool)supported.Invoke(null, [typeof(byte[])])!);
    }

    [Fact]
    public async Task Spare_parts_are_created_edited_and_deactivated_and_the_low_stock_page_applies_its_preset()
    {
        var (scopes, name) = Setup();
        var create = new EntityCreateAction(scopes, Catalog);
        Assert.True((await create.ExecuteAsync(Invoke("spare-parts-create", ("partNumber", " P-1 "), ("partName", "Bearing"), ("quantityOnHand", "2"), ("reorderPoint", "5")), CancellationToken.None)).Ok);   // low
        Assert.True((await create.ExecuteAsync(Invoke("spare-parts-create", ("partNumber", "P-2"), ("partName", "Seal"), ("quantityOnHand", "10"), ("reorderPoint", "5")), CancellationToken.None)).Ok);       // fine
        Assert.True((await create.ExecuteAsync(Invoke("spare-parts-create", ("partNumber", "P-3"), ("partName", "Gasket"), ("quantityOnHand", "1")), CancellationToken.None)).Ok);                           // no reorder point
        Assert.Contains("partNumber", (await create.ExecuteAsync(Invoke("spare-parts-create", ("partNumber", " "), ("partName", "X")), CancellationToken.None)).FieldErrors!.Keys);

        var grid = new EntityGridProvider(scopes, Catalog);
        Assert.Equal(3, (await grid.QueryAsync(Grid("spare-parts-grid"), CancellationToken.None)).Total);
        var low = (await grid.QueryAsync(Grid("spare-parts-low-stock-grid"), CancellationToken.None)).Items.Cast<Dictionary<string, object?>>().ToList();
        Assert.Equal(["P-1"], low.Select(r => (string)r["partNumber"]!));                      // a missing reorder point never matches, as in the old query

        int id;
        using (var db = PemsDbContextFactory.CreateInMemory(name)) id = (await db.SpareParts.SingleAsync(p => p.PartNumber == "P-1")).SparePartId;
        Assert.True((await new EntityUpdateAction(scopes, Catalog).ExecuteAsync(Invoke("spare-parts-edit", ("id", id.ToString()), ("partNumber", "P-1"), ("partName", "Bearing 6204"), ("quantityOnHand", "9"), ("reorderPoint", "5")), CancellationToken.None)).Ok);
        Assert.Empty((await grid.QueryAsync(Grid("spare-parts-low-stock-grid"), CancellationToken.None)).Items);

        Assert.True((await new EntityDeleteAction(scopes, Catalog).ExecuteAsync(Invoke("spare-parts-delete", ("id", id.ToString())), CancellationToken.None)).Ok);
        using var after = PemsDbContextFactory.CreateInMemory(name);
        Assert.False((await after.SpareParts.SingleAsync(p => p.SparePartId == id)).IsActive);   // deactivated, never removed
        Assert.Equal(2, (await grid.QueryAsync(Grid("spare-parts-grid"), CancellationToken.None)).Total);   // and the list shows active rows only, like the old one
    }

    [Fact]
    public void The_fixed_filters_and_presets_are_checked_at_load()
    {
        var grid = new EntityGridProvider(null!, Catalog);
        JsonObject P(string json) => JsonNode.Parse(json)!.AsObject();
        Assert.Empty(grid.ValidateParameters(P("""{"entity":"SparePart","fields":["PartNumber"],"where":{"IsActive":true},"presets":{"low":{"left":"QuantityOnHand","op":"lte","right":"ReorderPoint"}},"apply":["low"]}""")));
        Assert.Contains("unknown-preset", grid.ValidateParameters(P("""{"entity":"SparePart","fields":["PartNumber"],"apply":["nope"]}""")).Select(e => e.Code));
        Assert.Contains("wrong-op", grid.ValidateParameters(P("""{"entity":"SparePart","fields":["PartNumber"],"presets":{"x":{"left":"QuantityOnHand","op":"~","right":"ReorderPoint"}}}""")).Select(e => e.Code));
        Assert.Contains("not-readable", grid.ValidateParameters(P("""{"entity":"SparePart","fields":["PartNumber"],"presets":{"x":{"left":"PartName","op":"lt","right":"ReorderPoint"}}}""")).Select(e => e.Code));   // text is not comparable
        Assert.Contains("not-readable", grid.ValidateParameters(P("""{"entity":"SparePart","fields":["PartNumber"],"where":{"Secret":1}}""")).Select(e => e.Code));
    }
}
