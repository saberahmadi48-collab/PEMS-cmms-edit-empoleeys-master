using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using PEMS.Application.Definitions;
using PEMS.Persistence.Context;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.UI.Manifest;
using PEMS.UI.Rendering;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>The real service, store and kinds over the in-memory database; one "world" = one database, as many contexts (users) as needed.</summary>
public sealed class DefinitionTestWorld
{
    public const string Form = """{"id":"item","title":{"en":"Item","fa":"مورد"},"action":"/items","fields":[{"name":"tag","label":{"en":"Tag","fa":"تگ"}}],"submitLabel":{"en":"Save","fa":"ذخیره"}}""";

    private readonly string _database = "defs-" + Guid.NewGuid();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    public DefinitionLimits Limits { get; init; } = new();

    public PEMSDbContext NewContext() => PemsDbContextFactory.CreateInMemory(_database);

    public (DefinitionService Service, DefinitionStore Store, PEMSDbContext Db) NewService()
    {
        var db = NewContext();
        var registry = new PemsComponentRegistry();
        var kinds = new IDefinitionKind[] { new FormDefinitionKind(), new PageDefinitionKind(registry, new ComponentManifestCatalog(registry)) };
        var store = new DefinitionStore(db, _cache);
        return (new DefinitionService(db, kinds, Limits, store), store, db);
    }

    public static CreateDefinition CreateForm(string key = "items.new", string? payload = null) =>
        new("form", key, "core", """{"en":"Item form","fa":"فرم مورد"}""", null, payload ?? Form, "first");

    public static string Page(params (string Id, string Component, string Options)[] instances)
    {
        var list = new JsonArray();
        foreach (var (id, component, options) in instances)
            list.Add(new JsonObject { ["id"] = id, ["component"] = component, ["options"] = JsonNode.Parse(options) });
        return new JsonObject { ["layout"] = "list-page", ["regions"] = new JsonObject { ["main"] = list } }.ToJsonString();
    }
}
