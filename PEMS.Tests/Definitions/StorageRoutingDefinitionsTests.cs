using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.Common;
using PEMS.Application.Definitions;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>SCRUM-649: the storage routing page. The service (StorageRouteService) writes and validates through operations (administrators only); the entity layer lists the rows and loads the edit form; the provider is exposed by name only.</summary>
public sealed class StorageRoutingDefinitionsTests
{
    private static readonly EntityCatalog Entities = EntityCatalog.Default();
    private static readonly OperationCatalog Operations = OperationCatalog.Scan(typeof(PemsOperationAttribute).Assembly);
    private static readonly string[] Admin = ["SystemAdmin"];

    private sealed class Access : IDefinitionAccess
    {
        public Task<bool> CanOpenAsync(string? permission, IReadOnlyCollection<string> roles, CancellationToken ct = default) => Task.FromResult(true);
    }

    private static (IServiceScopeFactory Scopes, string Db) Setup()
    {
        var db = "sr-" + Guid.NewGuid();
        using (var ctx = PemsDbContextFactory.CreateInMemory(db)) { ctx.Set<StorageProvider>().Add(new StorageProvider { StorageProviderId = 3, Name = "Main", Code = "MAIN", RootPath = "/data" }); ctx.SaveChanges(); }
        var sp = new ServiceCollection().AddScoped(_ => PemsDbContextFactory.CreateInMemory(db)).AddSingleton<IDefinitionAccess>(new Access())
            .AddScoped<IStorageRouteService, StorageRouteService>().AddSingleton(Operations).BuildServiceProvider();
        return (sp.GetRequiredService<IServiceScopeFactory>(), db);
    }

    private static JsonObject Params(string action) => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, SystemDefinitionSeeder.FolderName, "action", "core", action + ".json")))!["payload"]!["parameters"]!.AsObject();

    private static ActionInvocation Run(string action, string[] roles, params (string, string?)[] values) => new(values.ToDictionary(v => v.Item1, v => v.Item2), "en", roles, "hamid", Params(action));

    [Fact]
    public async Task A_route_is_created_edited_and_removed_through_the_service_and_the_service_refuses_an_unknown_provider()
    {
        var (scopes, name) = Setup();
        var ops = new OperationInvokeAction(scopes, Operations);
        var refused = await ops.ExecuteAsync(Run("storage-routing-create", Admin, ("module", "VDR"), ("storageProviderId", "999"), ("isEnabled", "true"), ("priority", "1")), CancellationToken.None);
        Assert.False(refused.Ok);
        using (var db = PemsDbContextFactory.CreateInMemory(name)) Assert.Empty(db.Set<StorageRoute>());

        var created = await ops.ExecuteAsync(Run("storage-routing-create", Admin, ("module", "VDR"), ("documentType", ""), ("storageProviderId", "3"), ("isEnabled", "true"), ("isDefault", "true"), ("priority", "1")), CancellationToken.None);
        Assert.True(created.Ok, created.FormError?.ToString());
        int id;
        using (var db = PemsDbContextFactory.CreateInMemory(name)) { var r = await db.Set<StorageRoute>().SingleAsync(); id = r.StorageRouteId; Assert.Equal(("VDR", 3), (r.Module, r.StorageProviderId)); }

        var loaded = await new EntityRecordLoader(scopes, Entities).LoadAsync(Params("storage-routing-edit"), id.ToString(), Admin, CancellationToken.None);
        Assert.Equal(("VDR", "3"), (loaded!["module"], loaded["storageProviderId"]));

        var saved = await ops.ExecuteAsync(Run("storage-routing-edit", Admin, ("id", id.ToString()), ("module", "VDR"), ("storageProviderId", "3"), ("relativeRootPath", "vdr/files"), ("isEnabled", "true"), ("priority", "5")), CancellationToken.None);
        Assert.True(saved.Ok, saved.FormError?.ToString());
        using (var db = PemsDbContextFactory.CreateInMemory(name)) Assert.Equal(("vdr/files", 5), ((await db.Set<StorageRoute>().SingleAsync()).RelativeRootPath, (await db.Set<StorageRoute>().SingleAsync()).Priority));

        Assert.True((await ops.ExecuteAsync(Run("storage-routing-delete", Admin, ("id", id.ToString())), CancellationToken.None)).Ok);
        using (var db = PemsDbContextFactory.CreateInMemory(name)) Assert.False((await db.Set<StorageRoute>().SingleAsync()).IsActive);    // the service removes a route by deactivating it, as before
    }

    [Fact]
    public async Task Only_an_administrator_can_write_and_the_provider_options_never_carry_credentials()
    {
        var (scopes, _) = Setup();
        var refused = await new OperationInvokeAction(scopes, Operations).ExecuteAsync(Run("storage-routing-create", ["Planner"], ("module", "VDR"), ("storageProviderId", "3"), ("isEnabled", "true")), CancellationToken.None);
        Assert.False(refused.Ok);
        Assert.DoesNotContain(Entities.Get("StorageProvider")!.Readable, p => p.Contains("Password", StringComparison.OrdinalIgnoreCase) || p.Contains("Secret", StringComparison.OrdinalIgnoreCase));       // SCRUM-648: the edit form needs host and user name, never the password
        Assert.Empty(Entities.Get("StorageProvider")!.Writable);
        Assert.Empty(Entities.Get("StorageRoute")!.Writable);
    }

    [Theory]
    [InlineData("Admin/StorageRouting")]
    [InlineData("Admin/StorageRouting/Edit/4")]
    public void The_address_of_the_deleted_controller_still_reaches_the_page(string oldAddress)
    {
        var payload = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, SystemDefinitionSeeder.FolderName, "route", "core", "admin-storage-routing.json")))!["payload"]!;
        Assert.Empty(new RouteDefinitionKind().Validate(payload.ToJsonString(), new DefinitionLimits()));
        Assert.True(new Microsoft.AspNetCore.Routing.Template.TemplateMatcher(Microsoft.AspNetCore.Routing.Template.TemplateParser.Parse((string)payload["pattern"]!), new Microsoft.AspNetCore.Routing.RouteValueDictionary())
            .TryMatch("/" + oldAddress, new Microsoft.AspNetCore.Routing.RouteValueDictionary()));
        Assert.Equal("/p/storage-routing", (string)payload["target"]!);
    }
}
