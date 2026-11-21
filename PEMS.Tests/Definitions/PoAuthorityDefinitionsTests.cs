using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
using PEMS.WorkflowPlatform.Interfaces;

namespace PEMS.Tests.Definitions;

/// <summary>SCRUM-643 and SCRUM-775: the PO authority page is a generated definition page; the service keeps its rules. Proves the three generic pieces it needs: a record argument built from the form, a result object turned into the outcome, and a read operation as grid rows and as choices.</summary>
public sealed class PoAuthorityDefinitionsTests
{
    private static readonly OperationCatalog Operations = OperationCatalog.Scan(typeof(PemsOperationAttribute).Assembly);
    private static readonly string[] Admin = ["SystemAdmin"];

    private sealed class Access : IDefinitionAccess
    {
        public Task<bool> CanOpenAsync(string? permission, IReadOnlyCollection<string> roles, CancellationToken ct = default) => Task.FromResult(true);
    }

    private static (IServiceScopeFactory Scopes, string Db) Setup()
    {
        var db = "po-" + Guid.NewGuid();
        using (var ctx = PemsDbContextFactory.CreateInMemory(db)) { ctx.Roles.Add(new ApplicationRole("PRC.Manager") { NormalizedName = "PRC.MANAGER" }); ctx.SaveChanges(); }
        var sp = new ServiceCollection().AddScoped(_ => PemsDbContextFactory.CreateInMemory(db)).AddSingleton<IDefinitionAccess>(new Access())
            .AddSingleton<IPermissionService>(new FakePermissionService()).AddSingleton<IWorkflowRuntime>(new FakeWorkflowRuntime()).AddSingleton(Options.Create(new ProcurementOptions()))
            .AddScoped<IPurchaseOrderService, PurchaseOrderService>().AddScoped<IPurchaseOrderApprovalService, PurchaseOrderApprovalService>().AddSingleton(Operations).BuildServiceProvider();
        return (sp.GetRequiredService<IServiceScopeFactory>(), db);
    }

    private static JsonObject Params(string action) => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, SystemDefinitionSeeder.FolderName, "action", "core", action + ".json")))!["payload"]!["parameters"]!.AsObject();

    private static ActionInvocation Run(string action, string[] roles, params (string, string?)[] values) => new(values.ToDictionary(v => v.Item1, v => v.Item2), "en", roles, "hamid", Params(action));

    [Fact]
    public async Task A_level_is_saved_through_the_service_a_refusal_keeps_the_service_message_and_only_administrators_may()
    {
        var (scopes, name) = Setup();
        var ops = new OperationInvokeAction(scopes, Operations);

        var refused = await ops.ExecuteAsync(Run("po-authority-create", Admin, ("level", "1"), ("currency", "IRR"), ("minAmount", "500"), ("maxAmount", "100"), ("approverRole", "PRC.Manager"), ("isActive", "false")), CancellationToken.None);
        Assert.False(refused.Ok);
        Assert.Contains("Po_Err_RuleRange", refused.FormError!.ToString());                 // the key of the service (no localizer in this test host)

        var created = await ops.ExecuteAsync(Run("po-authority-create", Admin, ("level", "1"), ("currency", "IRR"), ("minAmount", "0"), ("maxAmount", "1000"), ("approverRole", "PRC.Manager"), ("description", "up to 1000"), ("isActive", "false")), CancellationToken.None);
        Assert.True(created.Ok, created.FormError?.ToString());
        int id;
        using (var db = PemsDbContextFactory.CreateInMemory(name)) { var r = await db.Set<PurchaseOrderAuthorityRule>().SingleAsync(); id = r.PurchaseOrderAuthorityRuleId; Assert.Equal((1, 1000m, "up to 1000"), (r.Level, r.MaxAmount, r.Description)); }

        var edited = await ops.ExecuteAsync(Run("po-authority-edit", Admin, ("id", id.ToString()), ("level", "2"), ("currency", "IRR"), ("minAmount", "0"), ("maxAmount", ""), ("approverRole", "PRC.Manager"), ("isActive", "false")), CancellationToken.None);
        Assert.True(edited.Ok, edited.FormError?.ToString());
        using (var db = PemsDbContextFactory.CreateInMemory(name)) { var r = await db.Set<PurchaseOrderAuthorityRule>().SingleAsync(); Assert.Equal((2, (decimal?)null), (r.Level, r.MaxAmount)); }

        Assert.False((await ops.ExecuteAsync(Run("po-authority-create", ["Planner"], ("level", "1"), ("currency", "IRR"), ("minAmount", "0"), ("approverRole", "PRC.Manager")), CancellationToken.None)).Ok);
    }

    [Fact]
    public async Task A_read_operation_is_the_rows_of_a_grid_and_the_choices_of_a_select()
    {
        var (scopes, _) = Setup();
        var ops = new OperationInvokeAction(scopes, Operations);
        Assert.True((await ops.ExecuteAsync(Run("po-authority-create", Admin, ("level", "1"), ("currency", "IRR"), ("minAmount", "0"), ("approverRole", "PRC.Manager"), ("isActive", "false")), CancellationToken.None)).Ok);
        var reader = new OperationReader(scopes, Operations);

        var grid = await new OperationGridProvider(reader).QueryAsync(new GridDataRequest(GridRequest.FromQuery([]), "en", Admin, "hamid",
            new Dictionary<string, string?> { ["model"] = "PoAuthority", ["action"] = "Rules" }), CancellationToken.None);
        Assert.Equal(1, grid.Total);
        Assert.Equal("PRC.Manager", ((IDictionary<string, object?>)grid.Items[0])["approverRole"]);

        var none = await reader.ReadAsync(new Dictionary<string, string?> { ["model"] = "PoAuthority", ["action"] = "Rules" }, "en", ["Planner"], "x", CancellationToken.None);
        Assert.Null(none);                                                                    // the roles of the operation are checked here too

        var options = await new OperationOptionsProvider(reader).LoadAsync(new DataSourceRequest("en", Admin, "hamid", new Dictionary<string, string?> { ["model"] = "PoAuthority", ["action"] = "ApproverRoles" }), CancellationToken.None);
        Assert.NotNull(options);                                                              // a list of text needs no value or label
    }

    [Fact]
    public void The_validation_of_a_read_operation_and_of_a_record_argument_names_the_mistake()
    {
        var reader = new OperationReader(new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), Operations);
        Assert.Contains(reader.Validate((JsonObject)JsonNode.Parse("""{"model":"PoAuthority","action":"Nope"}""")!), e => e.Pointer == "/model");
        Assert.Empty(reader.Validate((JsonObject)JsonNode.Parse("""{"model":"PoAuthority","action":"Rules"}""")!));
        var invoke = new OperationInvokeAction(new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), Operations);
        Assert.Contains(invoke.ValidateParameters((JsonObject)JsonNode.Parse("""{"model":"PoAuthority","action":"Save","args":{"input":{"colour":"field:x"}}}""")!), e => e.Pointer == "/args/input/colour");
        Assert.Empty(invoke.ValidateParameters((JsonObject)JsonNode.Parse("""{"model":"PoAuthority","action":"Save","args":{"input":{"level":"field:level"}}}""")!));
    }
}
