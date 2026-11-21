using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.Definitions;
using PEMS.Application.Grids;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Tests.TestSupport;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>The entity layer of the definitions: a registered entity is listed, offered as options and created from JSON only; the catalog's permissions are checked by the code and a definition cannot reach what is not registered or not allowed.</summary>
public sealed class EntityDefinitionsTests
{
    private sealed class Access(Func<string?, bool> allow) : IDefinitionAccess
    {
        public Task<bool> CanOpenAsync(string? permission, IReadOnlyCollection<string> roles, CancellationToken ct = default) => Task.FromResult(allow(permission));
    }

    private static (IServiceScopeFactory Scopes, string Db) Setup(Func<string?, bool>? allow = null)
    {
        var db = "ent-" + Guid.NewGuid();
        var sp = new ServiceCollection().AddScoped(_ => PemsDbContextFactory.CreateInMemory(db)).AddSingleton<IDefinitionAccess>(new Access(allow ?? (_ => true))).BuildServiceProvider();
        return (sp.GetRequiredService<IServiceScopeFactory>(), db);
    }

    private static readonly EntityCatalog Catalog = EntityCatalog.Default();

    private static GridDataRequest Grid(string entity, string fields, string search, params (string, string)[] query)
        => new(GridRequest.FromQuery(query.Select(q => new KeyValuePair<string, string?>(q.Item1, q.Item2))), "en", ["Admin"], "x",
            new Dictionary<string, string?> { ["entity"] = entity, ["fields"] = fields, ["search"] = search });

    private static void SeedUnits(string db)
    {
        using var ctx = PemsDbContextFactory.CreateInMemory(db);
        ctx.MaterialUOMs.AddRange(new MaterialUOM { Code = "KG", Name = "Kilogram", Description = "Mass", IsActive = true, CreatedDate = new DateTime(2026, 1, 1) },
            new MaterialUOM { Code = "M", Name = "Metre", ConversionFactor = 1, IsActive = true, CreatedDate = new DateTime(2026, 2, 1) },
            new MaterialUOM { Code = "OLD", Name = "Old unit", IsActive = false, CreatedDate = new DateTime(2026, 3, 1) });
        ctx.SaveChanges();
    }

    // ---- grid

    [Fact]
    public async Task The_grid_lists_the_exposed_fields_with_the_standard_query()
    {
        var (scopes, db) = Setup(); SeedUnits(db);
        var grid = new EntityGridProvider(scopes, Catalog);
        const string fields = """["Code","Name","IsActive","MaterialUOMId"]""", search = """["Code","Name"]""";

        var all = await grid.QueryAsync(Grid("MaterialUOM", fields, search, ("sort", "-name"), ("pageSize", "2")), CancellationToken.None);
        Assert.Equal(3, all.Total);
        var first = (Dictionary<string, object?>)all.Items[0];
        Assert.Equal("Old unit", first["name"]);
        Assert.Equal(["code", "name", "isActive", "materialUOMId"], first.Keys);       // only the exposed fields, camel case
        Assert.False(first.ContainsKey("description"));                                  // a column not in fields never leaves the server
        Assert.Equal(2, all.Items.Count);
        Assert.Equal(["M"], (await grid.QueryAsync(Grid("MaterialUOM", fields, search, ("q", "metre")), CancellationToken.None)).Items.Cast<Dictionary<string, object?>>().Select(r => (string)r["code"]!));
        Assert.Equal(2, (await grid.QueryAsync(Grid("MaterialUOM", fields, search, ("f.isActive", "eq:true")), CancellationToken.None)).Total);
        await Assert.ThrowsAsync<GridQueryException>(() => grid.QueryAsync(Grid("MaterialUOM", fields, search, ("sort", "description")), CancellationToken.None));   // not exposed: not sortable either
    }

    [Fact]
    public async Task Without_the_registered_read_permission_nothing_is_read()
    {
        var (scopes, db) = Setup(permission => permission != "Secret.View");
        SeedUnits(db);
        var catalog = new EntityCatalog().Add<MaterialUOM>("MaterialUOMId", e => e.Read("Secret.View").Readable("Code"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new EntityGridProvider(scopes, catalog).QueryAsync(Grid("MaterialUOM", """["Code"]""", "[]"), CancellationToken.None));
        Assert.Empty(await new EntityOptionsProvider(scopes, catalog).LoadAsync(new DataSourceRequest("en", ["x"], "x", new Dictionary<string, string?> { ["entity"] = "MaterialUOM", ["value"] = "Code", ["label"] = "Code" }), CancellationToken.None));
    }

    [Fact]
    public void The_grid_parameters_are_checked_at_load()
    {
        var grid = new EntityGridProvider(null!, Catalog);
        JsonObject P(string json) => JsonNode.Parse(json)!.AsObject();
        Assert.Empty(grid.ValidateParameters(P("""{"entity":"Warehouse","fields":["Code","Name"],"search":["Name"]}""")));
        Assert.Contains("unknown-entity", grid.ValidateParameters(P("""{"entity":"AspNetUsers","fields":["Id"]}""")).Select(e => e.Code));
        Assert.Contains("unknown-entity", grid.ValidateParameters(null).Select(e => e.Code));
        Assert.Contains("not-readable", grid.ValidateParameters(P("""{"entity":"Warehouse","fields":["Code","Locations"]}""")).Select(e => e.Code));
        Assert.Contains("required", grid.ValidateParameters(P("""{"entity":"Warehouse","fields":[]}""")).Select(e => e.Code));
        Assert.Contains("not-text", grid.ValidateParameters(P("""{"entity":"Warehouse","fields":["Code"],"search":["IsActive"]}""")).Select(e => e.Code));
    }

    // ---- options

    [Fact]
    public async Task The_options_come_from_the_entity_active_rows_only_ordered_by_label()
    {
        var (scopes, db) = Setup(); SeedUnits(db);
        var options = new EntityOptionsProvider(scopes, Catalog);
        var list = await options.LoadAsync(new DataSourceRequest("en", ["x"], "x", new Dictionary<string, string?> { ["entity"] = "MaterialUOM", ["value"] = "MaterialUOMId", ["label"] = "Name", ["active"] = "IsActive" }), CancellationToken.None);
        Assert.Equal(["Kilogram", "Metre"], list.Select(o => o.Label.Resolve(new CultureInfo("en"))));
        Assert.All(list, o => Assert.True(int.TryParse(o.Value, out _)));
        Assert.Contains("not-readable", options.ValidateParameters(JsonNode.Parse("""{"entity":"MaterialUOM","value":"MaterialUOMId","label":"Nope"}""")!.AsObject()).Select(e => e.Code));
        Assert.Contains("not-bool", options.ValidateParameters(JsonNode.Parse("""{"entity":"MaterialUOM","value":"MaterialUOMId","label":"Name","active":"Name"}""")!.AsObject()).Select(e => e.Code));
    }

    // ---- create

    private const string UomParameters = """{"entity":"MaterialUOM","map":{"code":"Code","name":"Name","conversionFactor":"ConversionFactor"},"required":["Code","Name"],"defaults":{"IsActive":true,"CreatedDate":"$now"},"unique":[{"fields":["Code"],"message":{"en":"Code taken.","fa":"کد تکراری است."}}]}""";

    private static ActionInvocation Invoke(params (string, string?)[] values) => new(values.ToDictionary(v => v.Item1, v => v.Item2), "en", ["Admin"], "x", JsonNode.Parse(UomParameters)!.AsObject());

    [Fact]
    public async Task Create_trims_converts_applies_the_defaults_and_refuses_a_duplicate_without_regard_to_case()
    {
        var (scopes, db) = Setup();
        var create = new EntityCreateAction(scopes, Catalog);
        var ok = await create.ExecuteAsync(Invoke(("code", " lt "), ("name", " Litre "), ("conversionFactor", "۱٫۵")), CancellationToken.None);
        Assert.True(ok.Ok, ok.FormError?.ToString());
        using (var ctx = PemsDbContextFactory.CreateInMemory(db))
        {
            var row = await ctx.MaterialUOMs.SingleAsync();
            Assert.Equal(("lt", "Litre", 1.5m, true), (row.Code, row.Name, row.ConversionFactor, row.IsActive));
            Assert.True((DateTime.Now - row.CreatedDate).TotalMinutes < 5);              // "$now"
        }
        var again = await create.ExecuteAsync(Invoke(("code", "LT"), ("name", "Other")), CancellationToken.None);
        Assert.False(again.Ok);
        Assert.Equal("Code taken.", again.FieldErrors!["code"].Resolve(new CultureInfo("en")));
        Assert.Equal("کد تکراری است.", again.FieldErrors["code"].Resolve(new CultureInfo("fa")));
        using var after = PemsDbContextFactory.CreateInMemory(db);
        Assert.Equal(1, await after.MaterialUOMs.CountAsync());
    }

    [Fact]
    public async Task A_missing_required_value_and_a_value_that_does_not_fit_are_refused_on_their_field()
    {
        var (scopes, _) = Setup();
        var create = new EntityCreateAction(scopes, Catalog);
        Assert.Contains("name", (await create.ExecuteAsync(Invoke(("code", "A"), ("name", "  ")), CancellationToken.None)).FieldErrors!.Keys);
        Assert.Contains("conversionFactor", (await create.ExecuteAsync(Invoke(("code", "A"), ("name", "B"), ("conversionFactor", "abc")), CancellationToken.None)).FieldErrors!.Keys);
    }

    [Fact]
    public async Task Without_the_registered_create_permission_nothing_is_written()
    {
        var (scopes, db) = Setup(permission => permission != "InventoryMasterData.Create");
        var refused = await new EntityCreateAction(scopes, Catalog).ExecuteAsync(Invoke(("code", "A"), ("name", "B")), CancellationToken.None);
        Assert.False(refused.Ok);
        using var ctx = PemsDbContextFactory.CreateInMemory(db);
        Assert.Empty(await ctx.MaterialUOMs.ToListAsync());
    }

    [Fact]
    public void The_action_parameters_are_checked_at_load_and_only_registered_writable_properties_can_be_set()
    {
        var create = new EntityCreateAction(null!, Catalog);
        JsonObject P(string json) => JsonNode.Parse(json)!.AsObject();
        Assert.Empty(create.ValidateParameters(P(UomParameters)));
        Assert.Contains("unknown-entity", create.ValidateParameters(P("""{"entity":"UserLogin","map":{"a":"B"}}""")).Select(e => e.Code));
        Assert.Contains("required", create.ValidateParameters(P("""{"entity":"MaterialUOM"}""")).Select(e => e.Code));
        Assert.Contains("not-writable", create.ValidateParameters(P("""{"entity":"MaterialUOM","map":{"id":"MaterialUOMId"}}""")).Select(e => e.Code));   // the key is not writable
        Assert.Contains("not-writable", create.ValidateParameters(P("""{"entity":"MaterialUOM","map":{"code":"Code"},"required":["TenantId"]}""")).Select(e => e.Code));
        Assert.Contains("wrong-type", create.ValidateParameters(P("""{"entity":"MaterialUOM","map":{"code":"Code"},"defaults":{"IsActive":"maybe"}}""")).Select(e => e.Code));
        Assert.Contains("unknown-field", create.ValidateParameters(P("""{"entity":"MaterialUOM","map":{"code":"Code"},"unique":[{"fields":["Ghost"],"message":{"en":"a","fa":"ب"}}]}""")).Select(e => e.Code));
        Assert.Contains("missing-translation", create.ValidateParameters(P("""{"entity":"MaterialUOM","map":{"code":"Code"},"unique":[{"fields":["Code"],"message":{"en":"a"}}]}""")).Select(e => e.Code));
    }

    [Fact]
    public void The_catalog_refuses_a_registration_of_a_property_that_does_not_exist()
        => Assert.Throws<InvalidOperationException>(() => new EntityCatalog().Add<MaterialUOM>("MaterialUOMId", e => e.Readable("Nope")));

    [Fact]
    public void The_kinds_use_the_providers_to_refuse_a_bad_data_source_or_action_when_it_is_loaded()
    {
        var sources = new DataSourceDefinitionKind([new EntityOptionsProvider(null!, Catalog)], [new EntityGridProvider(null!, Catalog)]);
        Assert.Empty(sources.Validate("""{"kind":"grid","provider":"entity","parameters":{"entity":"Warehouse","fields":["Code"]}}""", new DefinitionLimits()));
        Assert.Contains("/parameters/fields", sources.Validate("""{"kind":"grid","provider":"entity","parameters":{"entity":"Warehouse","fields":["Secret"]}}""", new DefinitionLimits()).Select(e => e.Pointer));
        var actions = new ActionDefinitionKind([new EntityCreateAction(null!, Catalog)]);
        Assert.Empty(actions.Validate("""{"kind":"handler","handler":"entity.create","parameters":{"entity":"Warehouse","map":{"code":"Code"}}}""", new DefinitionLimits()));
        Assert.Contains("unknown-entity", actions.Validate("""{"kind":"handler","handler":"entity.create","parameters":{"entity":"Users","map":{"a":"B"}}}""", new DefinitionLimits()).Select(e => e.Code));
    }
}
