using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.Common;
using PEMS.Application.Definitions;
using PEMS.Domain.Entities;
using PEMS.Persistence.Seed;
using PEMS.Tests.TestSupport;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

public interface IRolesTestService
{
    [PemsOperation("RolesTest", "Admin", Roles = "SystemAdmin, Auditor")]
    Task<bool> AdminAsync(int id);

    [PemsOperation("RolesTest", "Open")]
    Task<bool> OpenAsync(int id);

    [PemsOperation("RolesTest", "Check")]
    Task<(bool Success, string? Error)> CheckAsync(string name);
}

public sealed class RolesTestService : IRolesTestService
{
    public static string? Last;
    public Task<bool> AdminAsync(int id) { Last = "admin " + id; return Task.FromResult(true); }
    public Task<bool> OpenAsync(int id) { Last = "open " + id; return Task.FromResult(true); }
    public Task<(bool Success, string? Error)> CheckAsync(string name) => Task.FromResult(name == "Bad" ? (false, "The service refuses this name.") : (true, (string?)null));
}

/// <summary>SCRUM-773 (an operation names the roles that may run it) and SCRUM-774 (an entity create or update runs a validation operation before it writes).</summary>
public sealed class OperationRolesAndValidationTests
{
    private static readonly OperationCatalog Operations = OperationCatalog.Scan(typeof(OperationRolesAndValidationTests).Assembly);
    private static readonly EntityCatalog Entities = EntityCatalog.Default();

    private sealed class Access : IDefinitionAccess
    {
        public Task<bool> CanOpenAsync(string? permission, IReadOnlyCollection<string> roles, CancellationToken ct = default) => Task.FromResult(true);
    }

    private static (IServiceScopeFactory Scopes, string Db) Setup()
    {
        var db = "ops-" + Guid.NewGuid();
        var sp = new ServiceCollection().AddScoped(_ => PemsDbContextFactory.CreateInMemory(db)).AddSingleton<IDefinitionAccess>(new Access())
            .AddScoped<IRolesTestService, RolesTestService>().AddSingleton(Operations).BuildServiceProvider();
        return (sp.GetRequiredService<IServiceScopeFactory>(), db);
    }

    private static JsonObject Shipped(string kind, string key)
        => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, SystemDefinitionSeeder.FolderName, kind, "core", key + ".json")))!["payload"]!;

    private static string En(PEMS.UI.Abstractions.TranslatableText t) => t.Resolve(new CultureInfo("en"));

    private static ActionInvocation Run(string action, string[] roles, JsonObject? parameters = null, params (string, string?)[] values)
        => new(values.ToDictionary(v => v.Item1, v => v.Item2), "en", roles, "hamid", parameters ?? new JsonObject { ["model"] = "RolesTest", ["action"] = action, ["args"] = new JsonObject { ["id"] = "field:id" } });

    [Theory]
    [InlineData("SystemAdmin", true)]
    [InlineData("auditor", true)]                       // roles are compared without case
    [InlineData("Planner", false)]
    public async Task An_operation_with_roles_runs_only_for_a_caller_who_holds_one_of_them(string role, bool allowed)
    {
        var (scopes, _) = Setup();
        var outcome = await new OperationInvokeAction(scopes, Operations).ExecuteAsync(Run("Admin", [role], null, ("id", "5")), CancellationToken.None);
        Assert.Equal(allowed, outcome.Ok);
        if (!allowed) Assert.Equal("You may not do this.", En(outcome.FormError!));
    }

    [Fact]
    public async Task An_operation_without_roles_still_runs_for_any_role_and_the_roles_are_described_for_the_designers()
    {
        var (scopes, _) = Setup();
        Assert.True((await new OperationInvokeAction(scopes, Operations).ExecuteAsync(Run("Open", ["Planner"], null, ("id", "1")), CancellationToken.None)).Ok);
        var described = Operations.Describe().Single(o => o.Model == "RolesTest" && o.Action == "Admin");
        Assert.Equal(["SystemAdmin", "Auditor"], described.Roles);
        Assert.Empty(Operations.Describe().Single(o => o.Model == "RolesTest" && o.Action == "Open").Roles!);
    }

    private static JsonObject WithValidation(string action)
    {
        var parameters = (JsonObject)Shipped("action", action)["parameters"]!.DeepClone();
        parameters["validate"] = JsonNode.Parse("""{"model":"RolesTest","action":"Check","args":{"name":"field:name"}}""");
        return parameters;
    }

    [Fact]
    public async Task A_create_that_the_validation_operation_refuses_writes_nothing_and_shows_the_service_message()
    {
        var (scopes, name) = Setup();
        var create = new EntityCreateAction(scopes, Entities);
        var bad = await create.ExecuteAsync(Run("x", ["SystemAdmin"], WithValidation("departments-create"), ("code", "ENG"), ("name", "Bad")), CancellationToken.None);
        Assert.False(bad.Ok);
        Assert.Equal("The service refuses this name.", En(bad.FormError!));
        using (var db = PemsDbContextFactory.CreateInMemory(name)) Assert.Empty(db.Departments);

        var fine = await create.ExecuteAsync(Run("x", ["SystemAdmin"], WithValidation("departments-create"), ("code", "ENG"), ("name", "Fine")), CancellationToken.None);
        Assert.True(fine.Ok, fine.FormError?.ToString());
        using (var db = PemsDbContextFactory.CreateInMemory(name)) Assert.Equal("Fine", (await db.Departments.SingleAsync()).Name);
    }

    [Fact]
    public async Task An_update_that_the_validation_operation_refuses_leaves_the_stored_row_as_it_was()
    {
        var (scopes, name) = Setup();
        using (var db = PemsDbContextFactory.CreateInMemory(name)) { db.Departments.Add(new Department { DepartmentId = 4, Code = "OLD", Name = "Original" }); db.SaveChanges(); }
        var update = new EntityUpdateAction(scopes, Entities);
        var bad = await update.ExecuteAsync(Run("x", ["SystemAdmin"], WithValidation("departments-edit"), ("id", "4"), ("code", "OLD"), ("name", "Bad")), CancellationToken.None);
        Assert.False(bad.Ok);
        using (var db = PemsDbContextFactory.CreateInMemory(name)) Assert.Equal("Original", (await db.Departments.SingleAsync()).Name);

        Assert.True((await update.ExecuteAsync(Run("x", ["SystemAdmin"], WithValidation("departments-edit"), ("id", "4"), ("code", "OLD"), ("name", "Renamed")), CancellationToken.None)).Ok);
        using (var db = PemsDbContextFactory.CreateInMemory(name)) Assert.Equal("Renamed", (await db.Departments.SingleAsync()).Name);
    }

    [Fact]
    public void A_validate_setting_that_is_not_an_operation_object_is_refused_at_load_and_an_unknown_operation_is_refused_at_run_time()
    {
        var (scopes, _) = Setup();
        var parameters = (JsonObject)Shipped("action", "departments-create")["parameters"]!.DeepClone();
        parameters["validate"] = "RolesTest.Check";
        Assert.Contains(new EntityCreateAction(scopes, Entities).ValidateParameters(parameters), e => e.Pointer == "/validate");
        parameters["validate"] = JsonNode.Parse("""{"model":"RolesTest","action":"Check"}""");
        Assert.DoesNotContain(new EntityCreateAction(scopes, Entities).ValidateParameters(parameters), e => e.Pointer == "/validate");
    }
}
