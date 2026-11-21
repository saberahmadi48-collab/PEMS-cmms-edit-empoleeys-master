using PEMS.Application.Migration;

namespace PEMS.Tests.Deployment;

/// <summary>SCRUM-583: each feed mode, duplicates, invalid rows and a rerun, on the pure planner (no entity code).</summary>
public sealed class FeedPlannerTests
{
    private static IReadOnlyDictionary<string, string?> Row(string code, string? name = null, string? note = null) => new Dictionary<string, string?> { ["Code"] = code, ["Name"] = name, ["Note"] = note };

    private static Dictionary<string, IReadOnlyDictionary<string, string?>> Existing(params IReadOnlyDictionary<string, string?>[] rows) => rows.ToDictionary(r => FeedPlanner.KeyOf(r, ["Code"]), r => r);

    [Fact]
    public void Append_adds_new_keys_and_skips_existing_ones_without_changing_them()
    {
        var plan = FeedPlanner.Plan("Department", "append", [Row("D1", "One"), Row("D2", "Two")], ["Code"], Existing(Row("d1", "Old")));
        Assert.Equal(new[] { FeedRowAction.Skip, FeedRowAction.Add }, plan.Rows.Select(r => r.Action));   // the key compares without case
    }

    [Fact]
    public void Overwrite_changes_only_rows_that_differ_and_names_the_fields()
    {
        var plan = FeedPlanner.Plan("Department", "overwrite", [Row("D1", "New", "n"), Row("D2", "Same")], ["Code"], Existing(Row("D1", "Old", "n"), Row("D2", "Same")));
        Assert.Equal(FeedRowAction.Change, plan.Rows[0].Action);
        Assert.Equal(new[] { "Name" }, plan.Rows[0].ChangedFields);
        Assert.Equal(FeedRowAction.Skip, plan.Rows[1].Action);
    }

    [Fact]
    public void Merge_fills_only_what_is_empty()
    {
        var plan = FeedPlanner.Plan("Department", "merge", [Row("D1", "Package name", "Package note")], ["Code"], Existing(Row("D1", "Kept name", "")));
        Assert.Equal(FeedRowAction.Change, plan.Rows[0].Action);
        Assert.Equal(new[] { "Note" }, plan.Rows[0].ChangedFields);   // the name the customer has stays
    }

    [Fact]
    public void A_missing_key_a_duplicate_key_and_a_failed_validation_are_refused_with_a_reason()
    {
        var plan = FeedPlanner.Plan("Department", "append", [Row(""), Row("D1", "A"), Row("d1", "B"), Row("D3", "bad")], ["Code"], Existing(),
            r => r["Name"] == "bad" ? "The name is not allowed." : null);
        Assert.Equal(new[] { FeedRowAction.Refuse, FeedRowAction.Add, FeedRowAction.Refuse, FeedRowAction.Refuse }, plan.Rows.Select(r => r.Action));
        Assert.Contains("twice", plan.Rows[2].Error);
        Assert.Equal("The name is not allowed.", plan.Rows[3].Error);
        Assert.True(plan.HasRefusals);
    }

    [Theory]
    [InlineData("append")]
    [InlineData("overwrite")]
    [InlineData("merge")]
    public void A_rerun_after_applying_the_plan_changes_nothing(string mode)
    {
        var rows = new[] { Row("D1", "One", "n1"), Row("D2", "Two", "n2") };
        var first = FeedPlanner.Plan("Department", mode, rows, ["Code"], Existing());
        Assert.All(first.Rows, r => Assert.Equal(FeedRowAction.Add, r.Action));
        var applied = Existing(rows);   // what the database holds after the first run
        var second = FeedPlanner.Plan("Department", mode, rows, ["Code"], applied);
        Assert.All(second.Rows, r => Assert.Equal(FeedRowAction.Skip, r.Action));
    }

    [Fact]
    public void An_unknown_mode_is_a_programming_error() => Assert.Throws<ArgumentException>(() => FeedPlanner.Plan("Department", "delete", [], ["Code"], Existing()));
}
