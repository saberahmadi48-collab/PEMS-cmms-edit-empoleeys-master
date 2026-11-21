using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.Common;
using PEMS.Application.Definitions;
using PEMS.Application.Grids;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Seed;
using PEMS.Tests.TestSupport;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>SCRUM-631: the Authorization report pages are definition pages over read operations; a value typed in a column filter is the parameter of the operation (generic input param:name), and without it nothing is read.</summary>
public sealed class AuthorizationReportDefinitionsTests
{
    private static readonly OperationCatalog Operations = OperationCatalog.Scan(typeof(PemsOperationAttribute).Assembly);
    private static readonly string[] Admin = ["SystemAdmin"];

    private sealed class Access : IDefinitionAccess
    {
        public Task<bool> CanOpenAsync(string? permission, IReadOnlyCollection<string> roles, CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class Reports : IAuthorizationReportService
    {
        public readonly List<int> Asked = [];
        public Task<List<EffectivePermissionRowDto>> EffectiveForRoleAsync(string roleName) => Task.FromResult(new List<EffectivePermissionRowDto> { new() { RoleName = roleName, ResourceName = "Contract", ActionName = "View", Allowed = true } });
        public Task<List<EffectivePermissionRowDto>> EffectiveForUserAsync(int userId)
        { Asked.Add(userId); return Task.FromResult(new List<EffectivePermissionRowDto> { new() { UserId = userId, ResourceName = "Contract", ActionName = "Approve" } }); }
        public Task<List<RoleAssignmentReportRowDto>> AssignmentsOfUserAsync(int userId) => Task.FromResult(new List<RoleAssignmentReportRowDto>());
        public Task<List<AccessSimulationRowDto>> SimulateAsync(int userId, string resourceName, string actionName, string? resourceId = null) => Task.FromResult(new List<AccessSimulationRowDto>());
    }

    private static (OperationGridProvider Grid, Reports Reports) Setup()
    {
        var reports = new Reports();
        var sp = new ServiceCollection().AddScoped(_ => PemsDbContextFactory.CreateInMemory("ar-" + Guid.NewGuid())).AddSingleton<IDefinitionAccess>(new Access())
            .AddSingleton<IAuthorizationReportService>(reports).AddSingleton(Operations).BuildServiceProvider();
        return (new OperationGridProvider(new OperationReader(sp.GetRequiredService<IServiceScopeFactory>(), Operations)), reports);
    }

    private static JsonObject Source(string key) => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, SystemDefinitionSeeder.FolderName, "data-source", "core", key + ".json")))!["payload"]!["parameters"]!.AsObject();

    private static GridDataRequest Ask(string source, params GridFilter[] filters)
    {
        var p = Source(source);
        return new GridDataRequest(new GridRequest(1, 50, [], filters, null), "en", Admin, "hamid", new Dictionary<string, string?> { ["model"] = (string)p["model"]!, ["action"] = (string)p["action"]!, ["args"] = p["args"]!.ToJsonString() });
    }

    [Fact]
    public async Task Nothing_is_read_until_a_value_is_typed_in_the_filter_and_then_the_operation_gets_it()
    {
        var (grid, reports) = Setup();
        var empty = await grid.QueryAsync(Ask("authorization-effective-user-grid"), CancellationToken.None);
        Assert.Equal(0, empty.Total);
        Assert.Empty(reports.Asked);

        var rows = await grid.QueryAsync(Ask("authorization-effective-user-grid", new GridFilter("userId", "eq", "7")), CancellationToken.None);
        Assert.Equal(1, rows.Total);
        Assert.Equal([7], reports.Asked);

        var byRole = await grid.QueryAsync(Ask("authorization-effective-role-grid", new GridFilter("roleName", "eq", "PRC.Manager")), CancellationToken.None);
        Assert.Equal("Contract", ((IDictionary<string, object?>)byRole.Items[0])["resourceName"]);
    }

    [Fact]
    public void The_operations_are_for_administrators_and_the_shipped_sources_pass_the_validation()
    {
        var reader = new OperationReader(null!, Operations);
        foreach (var key in new[] { "authorization-effective-role-grid", "authorization-effective-user-grid", "authorization-user-assignments-grid", "authorization-simulate-grid" })
            Assert.Empty(reader.Validate(Source(key)));
        Assert.All(new[] { "EffectiveForRole", "EffectiveForUser", "AssignmentsOfUser", "Simulate" }, a => Assert.Contains("SystemAdmin", Operations.Get("Authorization", a)!.Roles));
    }
}
