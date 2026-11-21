using System.Text.Json.Nodes;
using PEMS.Application.Common;
using PEMS.Persistence.Seed;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>SCRUM-682: the Discipline master-data page and the workspace list are definitions over service operations; the permissions of the old controller are kept by the operations and the catalog, and every shipped definition names a registered operation.</summary>
public sealed class DisciplineDefinitionsTests
{
    private static readonly OperationCatalog Operations = OperationCatalog.Scan(typeof(PemsOperationAttribute).Assembly);

    private static JsonObject Shipped(string kind, string key)
        => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, SystemDefinitionSeeder.FolderName, kind, "core", key + ".json")))!["payload"]!;

    [Theory]
    [InlineData("DisciplineMaster", "List", "DisciplineMaster.View")]
    [InlineData("DisciplineMaster", "Create", "DisciplineMaster.Create")]
    [InlineData("DisciplineMaster", "Update", "DisciplineMaster.Edit")]
    [InlineData("DisciplineMaster", "SetActive", "DisciplineMaster.Edit")]
    [InlineData("DisciplineWorkspace", "Workspaces", "DisciplineWorkspace.View")]
    [InlineData("DisciplineWorkspace", "Summary", "DisciplineWorkspace.View")]
    public void The_operations_keep_the_permission_the_controller_checked(string model, string action, string permission)
        => Assert.Equal(permission, Operations.Get(model, action)?.Permission);

    [Fact]
    public void The_catalog_loads_the_edit_form_with_the_same_master_data_permissions()
    {
        var discipline = EntityCatalog.Default().Get("Discipline")!;
        Assert.Equal(("DisciplineMaster.View", "DisciplineMaster.Create", "DisciplineMaster.Edit"), (discipline.ReadPermission, discipline.CreatePermission, discipline.EditPermission));
    }

    [Theory]
    [InlineData("action", "disciplines-create", "Create")]
    [InlineData("action", "disciplines-edit", "Update")]
    [InlineData("action", "disciplines-activate", "SetActive")]
    [InlineData("action", "disciplines-deactivate", "SetActive")]
    public void The_shipped_actions_run_the_operations_and_their_arguments_are_parameters_of_the_service_method(string kind, string key, string action)
    {
        var payload = Shipped(kind, key);
        Assert.Equal("operation.invoke", (string?)payload["handler"]);
        var parameters = payload["parameters"]!.AsObject();
        Assert.Equal(action, (string?)parameters["action"]);
        var names = Operations.Get("DisciplineMaster", action)!.Method.GetParameters().Select(p => p.Name!).ToHashSet();
        Assert.All(parameters["args"]!.AsObject().Select(a => a.Key), name => Assert.Contains(name, names));
    }

    [Fact]
    public void The_grids_read_through_registered_operations()
    {
        foreach (var (key, model, action) in new[] { ("disciplines-grid", "DisciplineMaster", "List"), ("discipline-workspaces-grid", "DisciplineWorkspace", "Workspaces") })
        {
            var parameters = Shipped("data-source", key)["parameters"]!.AsObject();
            Assert.Equal((model, action), ((string?)parameters["model"], (string?)parameters["action"]));
            Assert.NotNull(Operations.Get(model, action));
        }
    }
}
