using PEMS.Application.Definitions;
using PEMS.Domain.Entities;
using PEMS.Persistence.Definitions;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Definitions;

/// <summary>SCRUM-591: the grants of roles to definitions: the permission stays the floor, an allow adds, a deny wins.</summary>
public sealed class DefinitionGrantsTests
{
    private static DefinitionGrantRecord G(string role, string effect) => new("page", "stock.list", role, effect);

    [Theory]
    [InlineData(true, new string[0], new[] { "Planner" }, true)]                                 // no grants: the permission decides
    [InlineData(false, new string[0], new[] { "Planner" }, false)]
    [InlineData(false, new[] { "Planner:allow" }, new[] { "Planner" }, true)]                   // an allow adds for a role without the permission
    [InlineData(true, new[] { "Planner:deny" }, new[] { "Planner" }, false)]                    // a deny closes it for a role that holds the permission
    [InlineData(true, new[] { "Planner:allow", "Viewer:deny" }, new[] { "planner", "Viewer" }, false)]   // a deny of any of the user's roles wins; role names compare without case
    [InlineData(false, new[] { "Viewer:allow" }, new[] { "Planner" }, false)]                   // a grant of another role changes nothing
    public void Decide_follows_permission_allow_and_deny(bool permission, string[] grants, string[] roles, bool expected)
        => Assert.Equal(expected, DefinitionGrantRules.Decide(permission, roles, grants.Select(g => G(g.Split(':')[0], g.Split(':')[1]))));

    [Fact]
    public async Task The_store_reads_only_the_grants_of_the_definition_asked_for()
    {
        var name = "grants-" + Guid.NewGuid();
        using (var db = PemsDbContextFactory.CreateInMemory(name))
        {
            db.DefinitionGrants.AddRange(
                new DefinitionGrant { Kind = "page", DefinitionKey = "stock.list", RoleName = "Planner", Effect = "allow" },
                new DefinitionGrant { Kind = "form", DefinitionKey = "stock.list", RoleName = "Planner", Effect = "deny" },
                new DefinitionGrant { Kind = "page", DefinitionKey = "other", RoleName = "Planner", Effect = "deny" });
            db.SaveChanges();
        }
        using var read = PemsDbContextFactory.CreateInMemory(name);
        var grants = await new EfDefinitionGrantStore(read).ForAsync("page", "stock.list");
        Assert.Equal([("Planner", "allow")], grants.Select(g => (g.RoleName, g.Effect)));
    }

    private sealed class OldStyleAccess : IDefinitionAccess
    {
        public Task<bool> CanOpenAsync(string? permission, IReadOnlyCollection<string> roles, CancellationToken ct = default) => Task.FromResult(permission == "A.View");
    }

    [Fact]
    public async Task An_access_without_grants_answers_the_known_definition_question_with_the_permission_alone()
    {
        IDefinitionAccess access = new OldStyleAccess();
        Assert.True(await access.CanOpenAsync("page", "x", "A.View", ["Planner"]));
        Assert.False(await access.CanOpenAsync("page", "x", "B.View", ["Planner"]));
    }
}
