using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Definitions;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Seed;
using PEMS.Tests.TestSupport;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>SCRUM-781 and SCRUM-633: an action refuses without its confirm field and when its precondition operation refuses (generic, by definition); the database update page is built from them and from the generic read operation.</summary>
public sealed class DatabaseUpdateDefinitionsTests
{
    private static readonly OperationCatalog Operations = OperationCatalog.Scan(typeof(PemsOperationAttribute).Assembly);
    private static readonly string[] Admin = ["SystemAdmin"];

    private sealed class Access : IDefinitionAccess
    {
        public Task<bool> CanOpenAsync(string? permission, IReadOnlyCollection<string> roles, CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class Updates : IDatabaseUpdateService
    {
        public int Applied;
        public Task<DatabaseUpdateStatusDto> GetStatusAsync(CancellationToken ct = default)
            => Task.FromResult(new DatabaseUpdateStatusDto { PendingMigrations = ["20261101_A", "20261102_B"], RecentRuns = [new DatabaseUpdateRunDto { Trigger = "Admin", Succeeded = true }] });
        public Task<DatabaseUpdateResultDto> ApplyAsync(string trigger, string? performedBy, CancellationToken ct = default)
        { Applied++; return Task.FromResult(new DatabaseUpdateResultDto { Succeeded = true, Message = "Applied 2 migrations." }); }
    }

    private sealed class Entitlement(bool entitled) : IBuildEntitlementService
    {
        public Task<(bool Success, string? Error)> CheckAsync(CancellationToken ct = default) => Task.FromResult(entitled ? (true, (string?)null) : (false, "Not covered by the licence."));
    }

    private static (IServiceScopeFactory Scopes, Updates Updates) Setup(bool entitled)
    {
        var updates = new Updates();
        var sp = new ServiceCollection().AddScoped(_ => PemsDbContextFactory.CreateInMemory("du-" + Guid.NewGuid())).AddSingleton<IDefinitionAccess>(new Access())
            .AddSingleton<IDatabaseUpdateService>(updates).AddSingleton<IBuildEntitlementService>(new Entitlement(entitled)).AddSingleton(Operations).BuildServiceProvider();
        return (sp.GetRequiredService<IServiceScopeFactory>(), updates);
    }

    private static JsonObject Params(string action) => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, SystemDefinitionSeeder.FolderName, "action", "core", action + ".json")))!["payload"]!["parameters"]!.AsObject();

    private static ActionInvocation Run(params (string, string?)[] values) => new(values.ToDictionary(v => v.Item1, v => v.Item2), "en", Admin, "hamid", Params("database-update-apply"));

    [Fact]
    public async Task Applying_needs_the_backup_confirmation_and_the_licence_entitlement_and_then_runs_the_service()
    {
        var (scopes, updates) = Setup(entitled: true);
        var ops = new OperationInvokeAction(scopes, Operations);
        Assert.False((await ops.ExecuteAsync(Run(("backupConfirmed", "false")), CancellationToken.None)).Ok);
        Assert.Equal(0, updates.Applied);

        var done = await ops.ExecuteAsync(Run(("backupConfirmed", "true")), CancellationToken.None);
        Assert.True(done.Ok, done.FormError?.ToString());
        Assert.Equal(1, updates.Applied);
        Assert.Contains("Applied 2 migrations.", done.Message!.ToString());           // the result message of the service is shown

        var (notEntitled, other) = Setup(entitled: false);
        var refused = await new OperationInvokeAction(notEntitled, Operations).ExecuteAsync(Run(("backupConfirmed", "true")), CancellationToken.None);
        Assert.False(refused.Ok);
        Assert.Contains("Not covered by the licence.", refused.FormError!.ToString());
        Assert.Equal(0, other.Applied);
    }

    [Fact]
    public async Task The_runs_and_the_pending_migrations_are_rows_of_the_read_operation()
    {
        var (scopes, _) = Setup(entitled: true);
        var provider = new OperationGridProvider(new OperationReader(scopes, Operations));
        var runs = await provider.QueryAsync(new GridDataRequest(PEMS.Application.Grids.GridRequest.FromQuery([]), "en", Admin, "hamid",
            new Dictionary<string, string?> { ["model"] = "DatabaseUpdate", ["action"] = "Status", ["path"] = "RecentRuns" }), CancellationToken.None);
        Assert.Equal(1, runs.Total);
        var pending = await provider.QueryAsync(new GridDataRequest(PEMS.Application.Grids.GridRequest.FromQuery([]), "en", Admin, "hamid",
            new Dictionary<string, string?> { ["model"] = "DatabaseUpdate", ["action"] = "Status", ["path"] = "PendingMigrations" }), CancellationToken.None);
        Assert.Equal(2, pending.Total);
        Assert.Equal("20261101_A", ((IDictionary<string, object?>)pending.Items[0])["value"]);
    }

    [Fact]
    public void The_validation_checks_require_and_the_precondition_operation()
    {
        var invoke = new OperationInvokeAction(null!, Operations);
        Assert.Empty(invoke.ValidateParameters(Params("database-update-apply")));
        Assert.Contains(invoke.ValidateParameters((JsonObject)JsonNode.Parse("""{"model":"DatabaseUpdate","action":"Apply","args":{"trigger":"literal:Admin"},"precondition":{"model":"Nope","action":"X"}}""")!), e => e.Pointer.StartsWith("/precondition"));
        Assert.Contains(invoke.ValidateParameters((JsonObject)JsonNode.Parse("""{"model":"DatabaseUpdate","action":"Apply","require":"yes"}""")!), e => e.Pointer == "/require");
    }
}
