using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.Common;
using PEMS.Application.Definitions;
using PEMS.Application.Grids;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>SCRUM-632: the contract approval authority page runs on the generic entity layer; the rules of ContractApprovalAuthorityRuleService stay, as the validation operation of the create and edit actions (SCRUM-774).</summary>
public sealed class ContractApprovalAuthorityDefinitionsTests
{
    private static readonly EntityCatalog Entities = EntityCatalog.Default();
    private static readonly OperationCatalog Operations = OperationCatalog.Scan(typeof(PemsOperationAttribute).Assembly);

    private sealed class Access : IDefinitionAccess
    {
        public Task<bool> CanOpenAsync(string? permission, IReadOnlyCollection<string> roles, CancellationToken ct = default) => Task.FromResult(true);
    }

    private static (IServiceScopeFactory Scopes, string Db) Setup()
    {
        var db = "caa-" + Guid.NewGuid();
        var sp = new ServiceCollection().AddScoped(_ => PemsDbContextFactory.CreateInMemory(db)).AddSingleton<IDefinitionAccess>(new Access())
            .AddScoped<IContractApprovalAuthorityRuleService, ContractApprovalAuthorityRuleService>().AddSingleton(Operations).BuildServiceProvider();
        return (sp.GetRequiredService<IServiceScopeFactory>(), db);
    }

    private static JsonObject Shipped(string kind, string key)
        => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, SystemDefinitionSeeder.FolderName, kind, "core", key + ".json")))!["payload"]!;

    private static ActionInvocation Invoke(string action, string[] roles, params (string, string?)[] values)
        => new(values.ToDictionary(v => v.Item1, v => v.Item2), "en", roles, "hamid", Shipped("action", action)["parameters"]!.AsObject());

    private static readonly string[] Admin = ["SystemAdmin"];

    private static async Task<ActionOutcome> Create(IServiceScopeFactory scopes, params (string, string?)[] values)
        => await new EntityCreateAction(scopes, Entities).ExecuteAsync(Invoke("contract-approval-authority-create", Admin, values), CancellationToken.None);

    [Theory]
    [InlineData("Admin/ContractApprovalAuthority")]
    [InlineData("Admin/ContractApprovalAuthority/Index")]
    public void The_address_of_the_deleted_controller_still_reaches_the_page(string oldAddress)
    {
        var payload = Shipped("route", "admin-contract-approval-authority");
        Assert.Empty(new RouteDefinitionKind().Validate(payload.ToJsonString(), new DefinitionLimits()));
        Assert.True(new Microsoft.AspNetCore.Routing.Template.TemplateMatcher(Microsoft.AspNetCore.Routing.Template.TemplateParser.Parse((string)payload["pattern"]!), new Microsoft.AspNetCore.Routing.RouteValueDictionary())
            .TryMatch("/" + oldAddress, new Microsoft.AspNetCore.Routing.RouteValueDictionary()));
        Assert.Equal("/p/contract-approval-authority", (string)payload["target"]!);
    }

    [Fact]
    public async Task A_valid_rule_is_created_active_and_a_blank_class_is_stored_as_none()
    {
        var (scopes, name) = Setup();
        var ok = await Create(scopes, ("minValue", "0"), ("maxValue", "100000"), ("criticalityClass", ""), ("requiredApproverRole", SystemRoles.ContractManager), ("priority", "2"), ("description", "small contracts"));
        Assert.True(ok.Ok, ok.FormError?.ToString());
        using var db = PemsDbContextFactory.CreateInMemory(name);
        var rule = await db.ContractApprovalAuthorityRules.SingleAsync();
        Assert.Equal((0m, 100000m, null, SystemRoles.ContractManager, 2, true), (rule.MinValue, rule.MaxValue, rule.CriticalityClass, rule.RequiredApproverRole, rule.Priority, rule.IsActive));
    }

    [Theory]
    [InlineData("NotARealRole", "0", "10")]      // unknown role
    [InlineData("ContractManager", "500", "10")] // floor above ceiling
    public async Task A_rule_the_service_refuses_is_not_written_and_shows_the_service_message(string role, string min, string max)
    {
        var (scopes, name) = Setup();
        var refused = await Create(scopes, ("minValue", min), ("maxValue", max), ("requiredApproverRole", role), ("priority", "0"));
        Assert.False(refused.Ok);
        Assert.False(string.IsNullOrWhiteSpace(refused.FormError!.Resolve(new CultureInfo("en"))));
        using var db = PemsDbContextFactory.CreateInMemory(name);
        Assert.Empty(db.ContractApprovalAuthorityRules);
    }

    [Fact]
    public async Task An_identical_active_rule_is_refused_on_create_but_an_edit_does_not_collide_with_itself()
    {
        var (scopes, name) = Setup();
        Assert.True((await Create(scopes, ("minValue", "0"), ("maxValue", "100"), ("criticalityClass", "A"), ("requiredApproverRole", SystemRoles.ContractManager), ("priority", "0"))).Ok);
        Assert.False((await Create(scopes, ("minValue", "0"), ("maxValue", "100"), ("criticalityClass", "A"), ("requiredApproverRole", SystemRoles.EngineeringManager), ("priority", "1"))).Ok);

        var edit = new EntityUpdateAction(scopes, Entities);
        var saved = await edit.ExecuteAsync(Invoke("contract-approval-authority-edit", Admin, ("id", "1"), ("minValue", "0"), ("maxValue", "100"), ("criticalityClass", "A"),
            ("requiredApproverRole", SystemRoles.EngineeringManager), ("priority", "3"), ("description", "changed")), CancellationToken.None);
        Assert.True(saved.Ok, saved.FormError?.ToString());
        using var db = PemsDbContextFactory.CreateInMemory(name);
        var rule = await db.ContractApprovalAuthorityRules.SingleAsync();
        Assert.Equal((SystemRoles.EngineeringManager, 3, "changed"), (rule.RequiredApproverRole, rule.Priority, rule.Description));
    }

    [Fact]
    public async Task An_edit_the_service_refuses_leaves_the_stored_rule_unchanged_and_a_rule_can_be_disabled_and_enabled()
    {
        var (scopes, name) = Setup();
        Assert.True((await Create(scopes, ("minValue", "0"), ("maxValue", "100"), ("requiredApproverRole", SystemRoles.ContractManager), ("priority", "0"))).Ok);
        var edit = new EntityUpdateAction(scopes, Entities);
        var refused = await edit.ExecuteAsync(Invoke("contract-approval-authority-edit", Admin, ("id", "1"), ("minValue", "900"), ("maxValue", "100"), ("requiredApproverRole", SystemRoles.ContractManager), ("priority", "0")), CancellationToken.None);
        Assert.False(refused.Ok);
        using (var db = PemsDbContextFactory.CreateInMemory(name)) Assert.Equal(0m, (await db.ContractApprovalAuthorityRules.SingleAsync()).MinValue);

        var toggle = new EntityToggleAction(scopes, Entities);
        Assert.True((await toggle.ExecuteAsync(Invoke("contract-approval-authority-toggle", Admin, ("id", "1")), CancellationToken.None)).Ok);
        using (var db = PemsDbContextFactory.CreateInMemory(name)) Assert.False((await db.ContractApprovalAuthorityRules.SingleAsync()).IsActive);
    }

    [Fact]
    public async Task Only_an_administrator_can_write_and_the_grid_lists_by_priority_then_newest()
    {
        var (scopes, name) = Setup();
        var planner = await new EntityCreateAction(scopes, Entities).ExecuteAsync(Invoke("contract-approval-authority-create", ["Planner"], ("requiredApproverRole", SystemRoles.ContractManager), ("priority", "0")), CancellationToken.None);
        Assert.False(planner.Ok);                                                  // the entity roles, as [Authorize(Roles = SystemAdmin)] was
        Assert.True((await Create(scopes, ("minValue", "0"), ("maxValue", "1"), ("requiredApproverRole", SystemRoles.ContractManager), ("priority", "5"))).Ok);
        Assert.True((await Create(scopes, ("minValue", "2"), ("maxValue", "3"), ("requiredApproverRole", SystemRoles.ContractManager), ("priority", "1"))).Ok);
        Assert.True((await Create(scopes, ("minValue", "4"), ("maxValue", "5"), ("requiredApproverRole", SystemRoles.ContractManager), ("priority", "1"))).Ok);

        var p = Shipped("data-source", "contract-approval-authority-grid")["parameters"]!.AsObject();
        var request = new GridDataRequest(GridRequest.FromQuery([]), "en", Admin, "x", p.ToDictionary(x => x.Key, x => (string?)(x.Value is JsonValue v && v.TryGetValue<string>(out var s) ? s : x.Value!.ToJsonString())));
        var rows = (await new EntityGridProvider(scopes, Entities).QueryAsync(request, CancellationToken.None)).Items.Cast<Dictionary<string, object?>>().ToList();
        Assert.Equal([3, 2, 1], rows.Select(r => Convert.ToInt32(r["contractApprovalAuthorityRuleId"])));   // priority 1 (newest first), then priority 5
    }
}
