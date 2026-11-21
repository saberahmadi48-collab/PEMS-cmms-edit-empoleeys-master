using PEMS.Application.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>SCRUM-516: shipped reference data: the list is validated when loaded, and the update rule never overwrites what the customer owns or edited.</summary>
public sealed class ReferenceDataTests
{
    private static readonly ReferenceDataKind Kind = new();

    private const string List = """
        { "list": "unit", "owner": "platform", "rows": [
            { "key": "kg", "values": { "name": "Kilogram", "nameFa": "کیلوگرم", "factor": 1 } },
            { "key": "m", "owner": "application", "values": { "name": "Metre", "active": true } } ] }
        """;

    private static IReadOnlyDictionary<string, string?> V(string name, string? note = null)
        => new Dictionary<string, string?> { ["name"] = name, ["note"] = note };

    private static StoredRow Stored(string key, IReadOnlyDictionary<string, string?> values, string owner = "platform", string? hash = "same", bool shipped = true, bool active = true)
        => new(key, owner, values, hash == "same" ? ReferenceDataSync.Fingerprint(values) : hash, active, shipped);

    [Fact]
    public void A_good_list_is_valid_and_its_rows_carry_the_list_owner_unless_they_name_their_own()
    {
        Assert.Empty(Kind.Validate(List, new DefinitionLimits()));
        var rows = ReferenceDataKind.Rows(List);
        Assert.Equal(["kg:platform", "m:application"], rows.Select(r => $"{r.Key}:{r.Owner}"));
        Assert.Equal("کیلوگرم", rows[0].Values["nameFa"]);
        Assert.Equal("1", rows[0].Values["factor"]);
        Assert.Equal("true", rows[1].Values["active"]);
    }

    [Theory]
    [InlineData("""{ "list": "u", "owner": "customer", "rows": [ { "key": "a", "values": { "n": "x" } } ] }""", "/owner", "unknown-owner")]
    [InlineData("""{ "list": "u", "owner": "platform", "rows": [] }""", "/rows", "required")]
    [InlineData("""{ "list": "u", "owner": "platform", "rows": [ { "key": "a", "values": { "n": "x" } }, { "key": "a", "values": { "n": "y" } } ] }""", "/rows/1/key", "duplicate")]
    [InlineData("""{ "list": "u", "owner": "platform", "rows": [ { "key": "a b", "values": { "n": "x" } } ] }""", "/rows/0/key", "invalid-key")]
    [InlineData("""{ "list": "u", "owner": "platform", "rows": [ { "key": "a", "values": { "n": { "x": 1 } } } ] }""", "/rows/0/values/n", "wrong-type")]
    [InlineData("""{ "list": "u", "owner": "platform", "rows": [ { "key": "a" } ] }""", "/rows/0/values", "required")]
    public void A_bad_list_is_refused_with_the_row_and_the_reason(string payload, string pointer, string code)
        => Assert.Contains(Kind.Validate(payload, new DefinitionLimits()), e => e.Pointer == pointer && e.Code == code);

    [Fact]
    public void The_update_rule_adds_updates_what_is_still_ours_and_never_overwrites_what_the_customer_owns_or_edited()
    {
        var shipped = new[]
        {
            new ShippedRow("new", "platform", V("New")),
            new ShippedRow("ours", "platform", V("Ours v2")),
            new ShippedRow("edited", "platform", V("Edited v2")),
            new ShippedRow("theirs", "platform", V("Theirs v2")),
            new ShippedRow("legacy", "platform", V("Legacy v2")),
            new ShippedRow("same", "platform", V("Same")),
        };
        var stored = new[]
        {
            Stored("ours", V("Ours v1")),
            Stored("edited", V("Edited by customer"), hash: ReferenceDataSync.Fingerprint(V("Edited v1"))),
            Stored("theirs", V("Theirs v1"), owner: "application"),
            Stored("legacy", V("Legacy v1"), hash: null),
            Stored("same", V("Same")),
        };

        var plan = ReferenceDataSync.Plan(shipped, stored).ToDictionary(p => p.Key, p => p.Action);
        Assert.Equal(RowAction.Insert, plan["new"]);
        Assert.Equal(RowAction.Update, plan["ours"]);
        Assert.Equal(RowAction.UpstreamChanged, plan["edited"]);
        Assert.Equal(RowAction.UpstreamChanged, plan["theirs"]);
        Assert.Equal(RowAction.UpstreamChanged, plan["legacy"]);
        Assert.Equal(RowAction.Unchanged, plan["same"]);
    }

    [Fact]
    public void A_row_no_longer_shipped_is_retired_never_deleted_and_the_customers_own_rows_are_left()
    {
        var stored = new[]
        {
            Stored("gone", V("Gone")), Stored("already", V("Already"), active: false), Stored("custom", V("Custom"), shipped: false, hash: null), Stored("kept", V("Kept"), owner: "application"),
        };
        var plan = ReferenceDataSync.Plan([], stored);
        Assert.Equal(["gone:Retire"], plan.Select(p => $"{p.Key}:{p.Action}"));
    }

    [Fact]
    public void Changed_fields_are_named_and_the_fingerprint_ignores_the_order_of_the_values()
    {
        var plan = ReferenceDataSync.Plan([new ShippedRow("a", "platform", V("B", "n"))], [Stored("a", V("A", "n"))]).Single();
        Assert.Equal(["name"], plan.ChangedFields);
        Assert.Equal(ReferenceDataSync.Fingerprint(new Dictionary<string, string?> { ["x"] = "1", ["y"] = "2" }), ReferenceDataSync.Fingerprint(new Dictionary<string, string?> { ["y"] = "2", ["x"] = "1" }));
    }
}
