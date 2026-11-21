using System.Text.Json.Nodes;
using PEMS.Application.Common;
using PEMS.Persistence.Seed;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>SCRUM-637: the navigation visibility pages are definitions over operations; the override form is a checkbox matrix of nodes with the columns Show and Hide.</summary>
public sealed class NavigationVisibilityDefinitionsTests
{
    private static readonly OperationCatalog Operations = OperationCatalog.Scan(typeof(PemsOperationAttribute).Assembly);

    private static JsonNode Shipped(string kind, string key) => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, SystemDefinitionSeeder.FolderName, kind, "core", key + ".json")))!["payload"]!;

    [Fact]
    public void The_operations_are_for_administrators_and_the_shipped_sources_validate()
    {
        foreach (var action in new[] { "RoleRows", "UserRows", "Nodes", "OverridesOfRole", "SetRoleOverrides" })
            Assert.Contains("SystemAdmin", Operations.Get("NavigationVisibility", action)!.Roles);
        var reader = new OperationReader(null!, Operations);
        foreach (var key in new[] { "navigation-visibility-role-rows", "navigation-visibility-user-rows" })
            Assert.Empty(reader.Validate((JsonObject)Shipped("data-source", key)["parameters"]!));
        Assert.Empty(new OperationInvokeAction(null!, Operations).ValidateParameters((JsonObject)Shipped("action", "navigation-visibility-edit")["parameters"]!));
    }

    [Fact]
    public void The_override_form_is_a_matrix_of_nodes_with_show_and_hide()
    {
        var field = Shipped("form", "navigation-visibility-edit")["fields"]![0]!;
        Assert.Equal("checkboxMatrix", (string)field["kind"]!);
        Assert.Equal(["Show", "Hide"], field["columns"]!.AsArray().Select(c => (string)c!["value"]!).ToArray());
    }
}
