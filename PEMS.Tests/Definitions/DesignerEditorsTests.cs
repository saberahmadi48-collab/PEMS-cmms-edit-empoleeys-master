using System.Text.Json;
using System.Text.Json.Nodes;
using PEMS.Persistence.Services;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>
/// SCRUM-810 (form designer), 811 (operation arguments), 813 (menu tree and separation of duties): the typed editors change the draft the way the stored forms ask, and the pure parts of the providers behave.
/// Real definition service and workbench over the in-memory database.
/// </summary>
public sealed class DesignerEditorsTests
{
    private const string User = "tester";
    private const string Reference = "core:form:guard.form";

    private static async Task<(DefinitionDesigner Designer, DefinitionService Service, int Id)> WorldWithFormAsync()
    {
        var w = new DefinitionTestWorld();
        var (service, _, db) = w.NewService();
        var created = await service.CreateAsync(DefinitionTestWorld.CreateForm("guard.form"), User);
        var workbench = new DefinitionWorkbench(service, new DefinitionImpactService(db, []));
        return (new DefinitionDesigner(service, workbench), service, created.Value!.Summary.Id);
    }

    private static async Task<JsonObject> DraftAsync(DefinitionService service, int id) => (JsonObject)JsonNode.Parse((await service.GetDraftAsync(id)).Value!.PayloadJson)!;

    [Fact]
    public async Task A_field_is_edited_in_place_with_only_the_arguments_given_and_a_dash_removes_a_setting()
    {
        var (designer, service, id) = await WorldWithFormAsync();
        var done = await designer.EditFormFieldAsync(Reference, "tag", "Tag number", "شماره تگ", "text", "true", "values.kind == 'a'", null, null, "2", "30", "^[A-Z]+$", "2", null, "Help", "راهنما", User);
        Assert.True(done.Success, done.Error);
        var field = (JsonObject)((JsonArray)(await DraftAsync(service, id))["fields"]!)[0]!;
        Assert.Equal("Tag number", (string)field["label"]!["en"]!);
        Assert.Equal("شماره تگ", (string)field["label"]!["fa"]!);
        Assert.True((bool)field["required"]!);
        Assert.Equal("values.kind == 'a'", (string)field["visible"]!);
        Assert.Equal((2, 30, 2), ((int)field["minLength"]!, (int)field["maxLength"]!, (int)field["span"]!));
        Assert.Equal("^[A-Z]+$", (string)field["pattern"]!);
        Assert.Equal("Help", (string)field["helpText"]!["en"]!);

        Assert.True((await designer.EditFormFieldAsync(Reference, "tag", null, null, null, null, "-", null, null, null, "-", null, null, null, null, null, User)).Success);
        field = (JsonObject)((JsonArray)(await DraftAsync(service, id))["fields"]!)[0]!;
        Assert.False(field.ContainsKey("visible"));
        Assert.False(field.ContainsKey("maxLength"));
        Assert.Equal(2, (int)field["minLength"]!);                           // not given: unchanged
    }

    [Theory]
    [InlineData("missing", null, "has no field")]
    [InlineData("tag", "maybe", "true or false")]
    public async Task A_bad_field_edit_is_refused_with_the_reason_and_the_draft_is_not_changed(string name, string? required, string reason)
    {
        var (designer, service, id) = await WorldWithFormAsync();
        var before = (await service.GetDraftAsync(id)).Value!.PayloadJson;
        var result = await designer.EditFormFieldAsync(Reference, name, null, null, null, required, null, null, null, null, null, null, null, null, null, null, User);
        Assert.False(result.Success);
        Assert.Contains(reason, result.Error);
        Assert.Equal(before, (await service.GetDraftAsync(id)).Value!.PayloadJson);
    }

    [Fact]
    public async Task The_layout_and_a_rule_of_the_form_are_set_without_json()
    {
        var (designer, service, id) = await WorldWithFormAsync();
        Assert.True((await designer.SetFormLayoutAsync(Reference, "2", "true", "Save it", "ذخیره کن", User)).Success);
        Assert.False((await designer.SetFormLayoutAsync(Reference, "3", null, null, null, User)).Success);
        Assert.True((await designer.AddFormRuleAsync(Reference, "values.a < values.b", "tag", "A before B", "الف قبل از ب", User)).Success);
        Assert.False((await designer.AddFormRuleAsync(Reference, "values.a < values.b", "tag", "", "x", User)).Success);       // a message needs both languages
        var form = await DraftAsync(service, id);
        Assert.Equal(2, (int)form["columns"]!);
        Assert.True((bool)form["warnOnLeave"]!);
        Assert.Equal("Save it", (string)form["submitLabel"]!["en"]!);
        var rule = Assert.Single((JsonArray)form["rules"]!);
        Assert.Equal(("values.a < values.b", "tag"), (((string)rule!["expression"]!), (string)rule["field"]!));
    }

    [Theory]
    [InlineData("actor", "context:userName")]
    [InlineData("userName", "context:userName")]
    [InlineData("roles", "context:roles")]
    [InlineData("employeeId", "context:employeeId")]
    [InlineData("workOrderId", "field:workOrderId")]
    public void The_designer_suggests_the_source_of_an_argument_by_its_name(string argument, string source)
        => Assert.Equal(source, OperationArgumentsGridProvider.Suggest(argument));

    [Fact]
    public void The_menu_tree_lists_children_after_their_parent_by_order_and_shows_an_orphan_at_the_top()
    {
        using var doc = JsonDocument.Parse("""
            [ { "key": "b", "order": 2 }, { "key": "a", "order": 1 }, { "key": "a2", "parent": "a", "order": 2 }, { "key": "a1", "parent": "a", "order": 1 }, { "key": "x", "parent": "gone", "order": 9 } ]
            """);
        var rows = MenuTreeGridProvider.Walk(doc.RootElement);
        Assert.Equal(["a", "a1", "a2", "b", "x"], rows.Select(r => r.Item.GetProperty("key").GetString()));
        Assert.Equal([0, 1, 1, 0, 0], rows.Select(r => r.Depth));
        Assert.Equal([1, 3, 2, 0, 4], rows.Select(r => r.Index));                   // the position in the items list, the id of the move buttons
    }

    [Theory]
    [InlineData(true, true, "one person can design and publish")]
    [InlineData(true, false, "designs only")]
    [InlineData(false, true, "publishes only")]
    [InlineData(false, false, "")]
    public void A_role_that_holds_edit_and_approve_is_flagged_for_separation_of_duties(bool edit, bool approve, string note)
        => Assert.Equal(note, DefinitionSodGridProvider.Separation(edit, approve));

    // ---- SCRUM-809: component instances by id

    private static async Task<(DefinitionDesigner Designer, DefinitionService Service, int Id)> WorldWithPageAsync()
    {
        var w = new DefinitionTestWorld();
        var (service, _, db) = w.NewService();
        var payload = DefinitionTestWorld.Page(("one", "button", """{ "label": { "en": "One" }, "tone": "primary" }"""), ("two", "button", """{ "label": { "en": "Two" }, "tone": "neutral" }"""));
        var created = await service.CreateAsync(new PEMS.Application.Definitions.CreateDefinition("page", "t.page", "core", """{"en":"T","fa":"ت"}""", null, payload, "first"), User);
        Assert.True(created.Success, created.Code);
        return (new DefinitionDesigner(service, new DefinitionWorkbench(service, new DefinitionImpactService(db, []))), service, created.Value!.Summary.Id);
    }

    [Fact]
    public async Task An_option_of_a_component_is_set_and_removed_by_the_id_of_the_component()
    {
        var (designer, service, id) = await WorldWithPageAsync();
        Assert.True((await designer.SetInstanceOptionAsync("core:page:t.page", "two", "tone", "\"danger\"", User)).Success);
        var page = await DraftAsync(service, id);
        Assert.Equal("danger", (string)page["regions"]!["main"]![1]!["options"]!["tone"]!);
        Assert.True((await designer.SetInstanceOptionAsync("core:page:t.page", "two", "tone", "", User)).Success);
        Assert.False(((JsonObject)(await DraftAsync(service, id))["regions"]!["main"]![1]!["options"]!).ContainsKey("tone"));
        Assert.Contains("no component with the id", (await designer.SetInstanceOptionAsync("core:page:t.page", "nope", "tone", "1", User)).Error);
        Assert.Contains("JSON", (await designer.SetInstanceOptionAsync("core:page:t.page", "one", "tone", "{bad", User)).Error);
    }

    [Fact]
    public async Task A_component_moves_to_another_region_at_a_position_the_way_a_drop_does()
    {
        var (designer, service, id) = await WorldWithPageAsync();
        Assert.True((await designer.MoveInstanceAsync("core:page:t.page", "one", "side", 0, User)).Success);
        var page = await DraftAsync(service, id);
        Assert.Equal(["two"], ((JsonArray)page["regions"]!["main"]!).Select(n => (string)n!["id"]!));
        Assert.Equal(["one"], ((JsonArray)page["regions"]!["side"]!).Select(n => (string)n!["id"]!));
        Assert.True((await designer.MoveInstanceAsync("core:page:t.page", "two", "side", 99, User)).Success);          // beyond the end: last
        page = await DraftAsync(service, id);
        Assert.Equal(["one", "two"], ((JsonArray)page["regions"]!["side"]!).Select(n => (string)n!["id"]!));
        Assert.False((await designer.MoveInstanceAsync("core:page:t.page", "ghost", "side", 0, User)).Success);
    }
}
