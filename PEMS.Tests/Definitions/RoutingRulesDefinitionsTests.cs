using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.Common;
using PEMS.Application.Definitions;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Infrastructure.Cache;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>SCRUM-645: the AI routing rules and confidence thresholds pages. The service writes (it keeps its cache invalidation), the entity only loads the form; administrators only.</summary>
public sealed class RoutingRulesDefinitionsTests
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
        var db = "rr-" + Guid.NewGuid();
        var cache = new MemoryCacheService(new MemoryCache(new MemoryCacheOptions()));
        var sp = new ServiceCollection().AddScoped(_ => PemsDbContextFactory.CreateInMemory(db)).AddSingleton<IDefinitionAccess>(new Access()).AddSingleton<ICacheService>(cache)
            .AddScoped<IRoutingRuleService, RoutingRuleService>().AddSingleton(Operations).BuildServiceProvider();
        return (sp.GetRequiredService<IServiceScopeFactory>(), db);
    }

    private static JsonObject Params(string action) => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, SystemDefinitionSeeder.FolderName, "action", "core", action + ".json")))!["payload"]!["parameters"]!.AsObject();

    private static ActionInvocation Run(string action, string[] roles, params (string, string?)[] values) => new(values.ToDictionary(v => v.Item1, v => v.Item2), "en", roles, "hamid", Params(action));

    [Fact]
    public async Task Saving_a_rule_goes_through_the_service_so_the_routing_cache_is_invalidated_and_only_an_administrator_may_do_it()
    {
        var (scopes, name) = Setup();
        using (var db = PemsDbContextFactory.CreateInMemory(name)) { db.RoutingRules.Add(new RoutingRule { RoutingRuleId = 4, DocumentCategory = "Contract", TargetModule = "Contract", AllowAutoRouting = false }); db.SaveChanges(); }
        using (var scope = scopes.CreateScope()) Assert.Equal("Contract", await scope.ServiceProvider.GetRequiredService<IRoutingRuleService>().GetTargetModuleAsync("Contract"));   // primes the cache

        var edit = new OperationInvokeAction(scopes, Operations);
        var refused = await edit.ExecuteAsync(Run("routing-rules-edit", ["Planner"], ("id", "4"), ("documentCategory", "Contract"), ("targetModule", "CMMS"), ("allowAutoRouting", "true")), CancellationToken.None);
        Assert.False(refused.Ok);

        var saved = await edit.ExecuteAsync(Run("routing-rules-edit", Admin, ("id", "4"), ("documentCategory", "Contract"), ("targetModule", "CMMS"), ("allowAutoRouting", "true"), ("description", "moved")), CancellationToken.None);
        Assert.True(saved.Ok, saved.FormError?.ToString());
        using var after = scopes.CreateScope();
        var service = after.ServiceProvider.GetRequiredService<IRoutingRuleService>();
        Assert.Equal("CMMS", await service.GetTargetModuleAsync("Contract"));            // the cached value was dropped by the service
        Assert.True(await service.AllowsAutoRoutingAsync("Contract"));
    }

    [Fact]
    public async Task The_edit_forms_load_the_stored_row_and_the_confidence_form_shows_the_defaults_until_the_first_save()
    {
        var (scopes, name) = Setup();
        using (var db = PemsDbContextFactory.CreateInMemory(name)) { db.RoutingRules.Add(new RoutingRule { RoutingRuleId = 4, DocumentCategory = "Contract", TargetModule = "VDR", AllowAutoRouting = true, Description = "d" }); db.SaveChanges(); }
        var loader = new EntityRecordLoader(scopes, Entities);
        var rule = await loader.LoadAsync(Params("routing-rules-edit"), "4", Admin, CancellationToken.None);
        Assert.Equal(("Contract", "VDR", "true"), (rule!["documentCategory"], rule["targetModule"], rule["allowAutoRouting"]));
        Assert.Null(await loader.LoadAsync(Params("routing-rules-edit"), "4", ["Planner"], CancellationToken.None));          // not readable by other roles

        var none = await loader.LoadAsync(Params("routing-confidence-edit"), "1", Admin, CancellationToken.None);
        Assert.Equal(("0.85", "0.5"), (none!["highThreshold"], none["mediumThreshold"]));
        var save = await new OperationInvokeAction(scopes, Operations).ExecuteAsync(Run("routing-confidence-edit", Admin, ("id", "1"), ("highThreshold", "0.9"), ("mediumThreshold", "0.6")), CancellationToken.None);
        Assert.True(save.Ok, save.FormError?.ToString());
        var stored = await loader.LoadAsync(Params("routing-confidence-edit"), "1", Admin, CancellationToken.None);
        Assert.Equal(("0.9", "0.6"), (stored!["highThreshold"], stored["mediumThreshold"]));
    }

    [Theory]
    [InlineData("Admin/RoutingRules")]
    [InlineData("Admin/RoutingRules/SetRule")]
    public void The_address_of_the_deleted_controller_still_reaches_the_page(string oldAddress)
    {
        var payload = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, SystemDefinitionSeeder.FolderName, "route", "core", "admin-routing-rules.json")))!["payload"]!;
        Assert.Empty(new RouteDefinitionKind().Validate(payload.ToJsonString(), new DefinitionLimits()));
        Assert.True(new Microsoft.AspNetCore.Routing.Template.TemplateMatcher(Microsoft.AspNetCore.Routing.Template.TemplateParser.Parse((string)payload["pattern"]!), new Microsoft.AspNetCore.Routing.RouteValueDictionary())
            .TryMatch("/" + oldAddress, new Microsoft.AspNetCore.Routing.RouteValueDictionary()));
        Assert.Equal("/p/routing-rules", (string)payload["target"]!);
    }
}
