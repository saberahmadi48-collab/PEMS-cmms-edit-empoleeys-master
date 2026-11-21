using PEMS.Domain.Entities;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Cmms;

/// <summary>SCRUM-513: the tree picker reads one level at a time (children of a parent, or the roots), a search over the whole tree, 20 nodes a page with hasMore.</summary>
public class FunctionalLocationTreeNodesTests
{
    private static async Task<FunctionalLocationService> ServiceAsync(string name)
    {
        var ctx = PemsDbContextFactory.CreateInMemory("FLTree_" + name);
        ctx.FunctionalLocations.AddRange(
            new FunctionalLocation { FunctionalLocationId = 1, Code = "U100", Name = "Unit 100", Plant = "Main", Area = "North", IsActive = true },
            new FunctionalLocation { FunctionalLocationId = 2, Code = "U100-CW", Name = "Cooling water", ParentId = 1, IsActive = true },
            new FunctionalLocation { FunctionalLocationId = 3, Code = "U100-CW-PUMP", Name = "Pump station", ParentId = 2, IsActive = true },
            new FunctionalLocation { FunctionalLocationId = 4, Code = "U100-OLD", Name = "Retired", ParentId = 1, IsActive = false },
            new FunctionalLocation { FunctionalLocationId = 5, Code = "U200", Name = "Unit 200", IsActive = true });
        for (var i = 0; i < 25; i++)
            ctx.FunctionalLocations.Add(new FunctionalLocation { FunctionalLocationId = 100 + i, Code = $"B-{i:D2}", Name = "Bulk " + i, ParentId = 5, IsActive = true });
        await ctx.SaveChangesAsync();
        return new FunctionalLocationService(ctx);
    }

    [Fact]
    public async Task The_roots_come_first_with_their_children_flag_and_inactive_nodes_are_hidden()
    {
        var s = await ServiceAsync(nameof(The_roots_come_first_with_their_children_flag_and_inactive_nodes_are_hidden));
        var roots = await s.GetTreeNodesAsync(null, null, 1, 20);
        Assert.Equal(new[] { "U100 - Unit 100", "U200 - Unit 200" }, roots.Items.Select(i => i.Label));
        Assert.Equal("Main / North", roots.Items[0].Sub);
        Assert.All(roots.Items, i => Assert.True(i.HasChildren));
        var children = await s.GetTreeNodesAsync(1, null, 1, 20);
        Assert.Equal(new[] { "U100-CW - Cooling water" }, children.Items.Select(i => i.Label));   // the retired one is not offered
        Assert.True(children.Items[0].HasChildren);
        Assert.False((await s.GetTreeNodesAsync(2, null, 1, 20)).Items.Single().HasChildren);
    }

    [Fact]
    public async Task A_level_is_paged_by_twenty_with_a_has_more_flag()
    {
        var s = await ServiceAsync(nameof(A_level_is_paged_by_twenty_with_a_has_more_flag));
        var first = await s.GetTreeNodesAsync(5, null, 1, 20);
        var second = await s.GetTreeNodesAsync(5, null, 2, 20);
        Assert.Equal((20, true), (first.Items.Count, first.HasMore));
        Assert.Equal((5, false), (second.Items.Count, second.HasMore));
        Assert.Empty(first.Items.Select(i => i.Id).Intersect(second.Items.Select(i => i.Id)));
    }

    [Fact]
    public async Task A_search_of_two_characters_or_more_is_flat_over_the_whole_tree_and_a_single_character_is_ignored()
    {
        var s = await ServiceAsync(nameof(A_search_of_two_characters_or_more_is_flat_over_the_whole_tree_and_a_single_character_is_ignored));
        var hits = await s.GetTreeNodesAsync(null, "pump", 1, 20);
        Assert.Equal(new[] { "U100-CW-PUMP - Pump station" }, hits.Items.Select(i => i.Label));
        Assert.All(hits.Items, i => Assert.False(i.HasChildren));
        var one = await s.GetTreeNodesAsync(null, "U", 1, 20);
        Assert.Equal(2, one.Items.Count);   // one character: the roots, not a search
    }
}
