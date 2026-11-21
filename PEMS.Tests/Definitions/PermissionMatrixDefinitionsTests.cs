using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Definitions;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Seed;
using PEMS.Tests.TestSupport;
using PEMS.Web.Definitions;
using PEMS.Web.Services;

namespace PEMS.Tests.Definitions;

/// <summary>SCRUM-641 (and the generic inputs of SCRUM-782): the permission matrix is a form with a checkbox-matrix field; its cells are loaded by a read operation and saved by one operation, the rules stay in the permission service.</summary>
public sealed class PermissionMatrixDefinitionsTests
{
    private static readonly OperationCatalog Operations = OperationCatalog.Scan(typeof(PemsOperationAttribute).Assembly);
    private static readonly string[] Admin = ["SystemAdmin"];

    private sealed class Access : IDefinitionAccess
    {
        public Task<bool> CanOpenAsync(string? permission, IReadOnlyCollection<string> roles, CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class Permissions : IPermissionService
    {
        public string? SavedRole;
        public List<ModulePermissionInput> Saved = [];
        public Task<bool> HasPermissionAsync(IEnumerable<string> userRoles, string moduleName, string action) => Task.FromResult(true);
        public Task<List<RolePermissionDto>> GetMatrixAsync() => Task.FromResult(new List<RolePermissionDto> { new() { RoleName = "Engineer", ModuleName = "WorkOrder", CanView = true, CanEdit = true }, new() { RoleName = "Other", ModuleName = "Task", CanView = true } });
        public Task SetPermissionsAsync(string roleName, IEnumerable<ModulePermissionInput> permissions) { SavedRole = roleName; Saved = permissions.ToList(); return Task.CompletedTask; }
        public Task SeedDefaultsAsync() => Task.CompletedTask;
    }

    private static (IServiceScopeFactory Scopes, Permissions Permissions) Setup()
    {
        var permissions = new Permissions();
        var sp = new ServiceCollection().AddScoped(_ => PemsDbContextFactory.CreateInMemory("pm-" + Guid.NewGuid())).AddSingleton<IDefinitionAccess>(new Access())
            .AddSingleton<IPermissionService>(permissions).AddScoped<IPermissionMatrixPageService, PermissionMatrixPageService>().AddSingleton(Operations).BuildServiceProvider();
        return (sp.GetRequiredService<IServiceScopeFactory>(), permissions);
    }

    private static JsonObject Params(string action) => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, SystemDefinitionSeeder.FolderName, "action", "core", action + ".json")))!["payload"]!["parameters"]!.AsObject();

    [Fact]
    public async Task The_form_loads_the_cells_of_the_role_and_saving_sets_every_module_of_the_matrix()
    {
        var (scopes, permissions) = Setup();
        var loaded = await new EntityRecordLoader(scopes, EntityCatalog.Default(), new OperationReader(scopes, Operations)).LoadAsync(Params("permission-matrix-edit"), "Engineer", Admin, CancellationToken.None);
        Assert.Equal("WorkOrder|View,WorkOrder|Edit", loaded!["cells"]);

        var done = await new OperationInvokeAction(scopes, Operations).ExecuteAsync(new ActionInvocation(
            new Dictionary<string, string?> { ["id"] = "Engineer", ["cells"] = "WorkOrder|View,WorkOrder|Approve,Nope|View,Task|Bad" }, "en", Admin, "hamid", Params("permission-matrix-edit")), CancellationToken.None);
        Assert.True(done.Ok, done.FormError?.ToString());
        Assert.Equal("Engineer", permissions.SavedRole);
        var workOrder = permissions.Saved.Single(i => i.ModuleName == "WorkOrder");
        Assert.True(workOrder.CanView && workOrder.CanApprove && !workOrder.CanEdit);
        Assert.DoesNotContain(permissions.Saved, i => i.ModuleName == "Nope");           // only the modules of the matrix can be saved
        Assert.False(permissions.Saved.Single(i => i.ModuleName == "Task").CanView);       // a cell that is not ticked is cleared

        var refused = await new OperationInvokeAction(scopes, Operations).ExecuteAsync(new ActionInvocation(
            new Dictionary<string, string?> { ["id"] = "Engineer", ["cells"] = "" }, "en", ["Planner"], "x", Params("permission-matrix-edit")), CancellationToken.None);
        Assert.False(refused.Ok);                                                         // administrators only
    }

    [Fact]
    public void The_shipped_form_has_a_matrix_field_over_the_modules_and_the_ten_actions()
    {
        var form = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, SystemDefinitionSeeder.FolderName, "form", "core", "permission-matrix-edit.json")))!["payload"]!;
        var field = form["fields"]![0]!;
        Assert.Equal("checkboxMatrix", (string)field["kind"]!);
        Assert.Equal("permission-matrix-modules", (string)field["optionsFrom"]!);
        Assert.Equal(10, field["columns"]!.AsArray().Count);
    }
}
