using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Definitions;
using PEMS.Domain.Entities.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>
/// SCRUM-515 (owner decision 2026-11-06): platform fields are ours and read-only for the customer, application fields are the customer's and never touched by an update; the customer wins and an update that
/// would break that stops before it changes anything.
/// </summary>
public sealed class FormOwnershipTests
{
    private static string Field(string name, string kind = "text", bool platform = false, string? label = null)
        => $$"""{"name":"{{name}}","label":{"en":"{{label ?? name}}","fa":"{{label ?? name}}"},"kind":"{{kind}}"{{(platform ? ",\"owner\":\"platform\"" : "")}}}""";

    private static string Form(params string[] fields)
        => $$"""{"id":"item","title":{"en":"Item","fa":"مورد"},"action":"/items","submitLabel":{"en":"Save","fa":"ذخیره"},"fields":[{{string.Join(",", fields)}}]}""";

    private static string[] Names(string payload) => ((JsonArray)JsonNode.Parse(payload)!["fields"]!).Select(f => (string)f!["name"]!).ToArray();

    // ---- the rule for a customer's edit

    [Fact]
    public void A_customer_may_change_application_fields_but_not_touch_platform_fields()
    {
        var shipped = Form(Field("code", platform: true), Field("note"));
        Assert.Empty(FormOwnership.Protect(shipped, Form(Field("code", platform: true), Field("note", label: "My note"), Field("extra"))));      // own fields: free

        var changed = FormOwnership.Protect(shipped, Form(Field("code", platform: true, label: "Mine"), Field("note")));
        Assert.Equal(("/fields/0", "platform-field-changed"), (changed[0].Pointer, changed[0].Code));
        var removed = FormOwnership.Protect(shipped, Form(Field("note")));
        Assert.Equal("platform-field-removed", removed[0].Code);
        var added = FormOwnership.Protect(shipped, Form(Field("code", platform: true), Field("note"), Field("fake", platform: true)));
        Assert.Equal(("/fields/2/owner", "platform-field-added"), (added[0].Pointer, added[0].Code));
    }

    // ---- the merge of an update

    [Fact]
    public void An_update_adds_and_changes_platform_fields_and_leaves_every_application_field_as_the_customer_has_it()
    {
        var customer = Form(Field("code", platform: true), Field("note", label: "My note"), Field("extra"));
        var update = Form(Field("code", platform: true, label: "Code v2"), Field("status", "select", platform: true), Field("note", label: "Shipped note"));
        var merge = FormOwnership.Merge(customer, update);
        Assert.True(merge.Ok);
        Assert.Equal(["code", "note", "extra", "status"], Names(merge.MergedJson!));
        var fields = (JsonArray)JsonNode.Parse(merge.MergedJson!)!["fields"]!;
        Assert.Equal("Code v2", (string)fields[0]!["label"]!["en"]!);           // platform: updated
        Assert.Equal("My note", (string)fields[1]!["label"]!["en"]!);            // application: the customer's text wins over the shipped one
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Field("extra")), fields[2]));
    }

    [Fact]
    public void A_platform_field_the_product_stops_shipping_is_removed_but_an_application_field_never_is()
    {
        var customer = Form(Field("old", platform: true), Field("mine"));
        var merge = FormOwnership.Merge(customer, Form(Field("code", platform: true)));
        Assert.Equal(["mine", "code"], Names(merge.MergedJson!));
    }

    [Fact]
    public void An_update_that_would_collide_with_an_application_field_or_lose_values_is_a_conflict_listed_with_the_field()
    {
        var customer = Form(Field("status"), Field("qty", "number", platform: true), Field("code", platform: true));
        var update = Form(Field("status", "select", platform: true), Field("qty", "text", platform: true), Field("code", "textarea", platform: true));
        var merge = FormOwnership.Merge(customer, update);
        Assert.False(merge.Ok);
        Assert.Null(merge.MergedJson);
        Assert.Equal([("status", "name-taken"), ("qty", "kind-change")], merge.Conflicts.Select(c => (c.Field, c.Code)));   // text to textarea is the same family: no conflict
    }

    // ---- through the service: customised shipped forms

    private static ShippedDefinition Ship(string payload) => new("form", "items.edit", "core", """{"en":"Item","fa":"مورد"}""", null, payload);

    private static async Task<(DefinitionTestWorld World, int Id)> CustomisedAsync(string shipped, string customerPayload)
    {
        var w = new DefinitionTestWorld();
        var (service, _, db) = w.NewService();
        var shippedResult = await service.ShipAsync([Ship(shipped)], "system");
        Assert.True(shippedResult.Ok, string.Join("; ", shippedResult.Problems));
        var def = await db.Definitions.AsNoTracking().SingleAsync();
        var draft = (await service.CreateDraftAsync(def.DefinitionId, null, def.Revision, "customer")).Value!;
        var saved = await service.SaveDraftAsync(def.DefinitionId, customerPayload, "mine", draft.Info.Revision, "customer");
        var validated = await service.ValidateAsync(def.DefinitionId, saved.Value!.Info.Revision, "customer");
        Assert.True(validated.Success, string.Join(";", validated.Errors?.Select(e => e.Pointer + " " + e.Code) ?? []));
        Assert.True((await service.PublishAsync(def.DefinitionId, validated.Value!.Info.Revision, "customer")).Success);
        return (w, def.DefinitionId);
    }

    [Fact]
    public async Task The_service_refuses_an_edit_of_a_platform_field_when_the_draft_is_validated()
    {
        var shipped = Form(Field("code", platform: true), Field("note"));
        var w = new DefinitionTestWorld();
        var (service, _, db) = w.NewService();
        await service.ShipAsync([Ship(shipped)], "system");
        var def = await db.Definitions.AsNoTracking().SingleAsync();
        var draft = (await service.CreateDraftAsync(def.DefinitionId, null, def.Revision, "customer")).Value!;
        var saved = await service.SaveDraftAsync(def.DefinitionId, Form(Field("code", platform: true, label: "Mine"), Field("note")), "x", draft.Info.Revision, "customer");
        var validated = await service.ValidateAsync(def.DefinitionId, saved.Value!.Info.Revision, "customer");
        Assert.Equal(DefinitionFailure.Invalid, validated.Failure);
        Assert.Contains(validated.Errors!, e => e.Code == "platform-field-changed" && e.Pointer == "/fields/0");
    }

    [Fact]
    public async Task A_product_update_reaches_a_customised_form_through_its_platform_fields_and_leaves_the_customers_fields_alone()
    {
        var v1 = Form(Field("code", platform: true), Field("note"));
        var custom = Form(Field("code", platform: true), Field("note", label: "My note"), Field("extra"));
        var (w, id) = await CustomisedAsync(v1, custom);

        var v2 = Form(Field("code", platform: true), Field("status", "number", platform: true), Field("note"));
        var result = await w.NewService().Service.ShipAsync([Ship(v2)], "system");
        Assert.True(result.Ok, string.Join(";", result.Problems));
        Assert.Equal((1, 1), (result.KeptCustomized, result.PlatformFieldsMerged));

        var row = await w.NewContext().Definitions.Include(d => d.Versions).AsNoTracking().SingleAsync(d => d.DefinitionId == id);
        var published = row.Versions.Single(v => v.DefinitionVersionId == row.PublishedVersionId);
        Assert.Equal(["code", "note", "extra", "status"], Names(published.PayloadJson));
        Assert.Contains("My note", published.PayloadJson);
        Assert.Contains("status", published.PayloadJson);
        Assert.True(row.IsCustomized);                                              // the customer's own fields still differ from the shipped form
        Assert.Equal(DefinitionStatus.Retired, row.Versions.Single(v => v.DefinitionVersionId == row.ShippedVersionId).Status);   // the new shipped version is kept beside it
    }

    [Fact]
    public async Task An_update_that_would_break_an_application_field_stops_before_anything_changes_and_says_which_form_and_field()
    {
        var v1 = Form(Field("code", platform: true));
        var custom = Form(Field("code", platform: true), Field("status"));            // the customer made their own "status"
        var (w, id) = await CustomisedAsync(v1, custom);
        var before = await w.NewContext().DefinitionVersions.CountAsync();

        var v2 = Form(Field("code", platform: true), Field("status", "number", platform: true));
        var other = new ShippedDefinition("data-source", "units", "core", """{"en":"U","fa":"U"}""", null, """{"kind":"static","items":[{"value":"1","label":{"en":"a","fa":"a"}}]}""");
        var (service, _, _) = w.NewService();
        var result = await service.ShipAsync([Ship(v2), other], "system");

        Assert.False(result.Ok);
        Assert.Equal((0, 0, 0, 0), (result.Created, result.Upgraded, result.KeptCustomized, result.PlatformFieldsMerged));
        var problem = Assert.Single(result.Problems);
        Assert.Contains("form/core/items.edit", problem);
        Assert.Contains("'status'", problem);
        Assert.Contains("name-taken", problem);
        Assert.Equal(before, await w.NewContext().DefinitionVersions.CountAsync());   // nothing was written, not even the unrelated data source of the same update
        Assert.True(id > 0);
    }
}
