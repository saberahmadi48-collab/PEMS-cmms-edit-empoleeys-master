using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.Grids;
using PEMS.Persistence.Context;
using PEMS.Tests.TestSupport;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>SCRUM-793: saved views of a list on the server, per user and per role. Real handlers over the in-memory database.</summary>
public sealed class GridViewsTests
{
    private readonly string _database = "views-" + Guid.NewGuid();

    private IServiceScopeFactory Scopes()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => PemsDbContextFactory.CreateInMemory(_database));
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static ActionInvocation Call(string? user, string[] roles, params (string, string?)[] values)
        => new(values.ToDictionary(v => v.Item1, v => v.Item2, StringComparer.Ordinal), "en", roles, user);

    private static (string, string?)[] Save(string name, string scope, string state = """{ "sort": [], "density": "compact" }""")
        => [("viewsKey", "work-orders"), ("name", name), ("scope", scope), ("state", state)];

    private async Task<List<IDictionary<string, object?>>> RowsFor(string user, params string[] roles)
    {
        var request = new GridDataRequest(GridRequest.FromQuery([new("f.viewsKey", "eq:work-orders")]), "en", roles, user, new Dictionary<string, string?>());
        var (items, _) = await new GridViewsProvider(Scopes()).QueryAsync(request, default);
        return items.Cast<IDictionary<string, object?>>().ToList();
    }

    [Fact]
    public async Task A_view_saved_by_one_user_is_available_to_that_user_on_another_device_and_to_nobody_else()
    {
        var save = new GridViewSaveAction(Scopes());
        Assert.True((await save.ExecuteAsync(Call("hamid", ["Planner"], Save("My open", "user")), default)).Ok);
        var mine = await RowsFor("hamid", "Planner");
        var view = Assert.Single(mine, r => (string)r["kind"]! == "view");
        Assert.Equal(("My open", "user"), ((string)view["name"]!, (string)view["scope"]!));
        Assert.DoesNotContain(await RowsFor("saber", "Planner"), r => (string)r["kind"]! == "view");
    }

    [Fact]
    public async Task A_role_view_is_shared_by_the_members_of_the_role_and_a_role_the_user_does_not_hold_is_refused()
    {
        var save = new GridViewSaveAction(Scopes());
        Assert.True((await save.ExecuteAsync(Call("hamid", ["Planner"], Save("Team view", "role:Planner")), default)).Ok);
        var member = await RowsFor("saber", "Planner");
        Assert.Contains(member, r => (string)r["kind"]! == "view" && (string)r["scope"]! == "role:Planner" && (string)r["name"]! == "Team view");
        Assert.DoesNotContain(await RowsFor("other", "Viewer"), r => (string)r["kind"]! == "view");
        var refused = await save.ExecuteAsync(Call("hamid", ["Planner"], Save("Sneaky", "role:Admin")), default);
        Assert.False(refused.Ok);
        Assert.Contains("scope", refused.FieldErrors!.Keys);
    }

    [Fact]
    public async Task The_roles_a_user_holds_are_offered_as_scopes_and_saving_a_name_again_replaces_the_view()
    {
        var save = new GridViewSaveAction(Scopes());
        await save.ExecuteAsync(Call("hamid", ["Planner", "Admin"], Save("v", "user", """{ "density": "compact" }""")), default);
        await save.ExecuteAsync(Call("hamid", ["Planner", "Admin"], Save("v", "user", """{ "density": "comfortable" }""")), default);
        var rows = await RowsFor("hamid", "Planner", "Admin");
        Assert.Equal(2, rows.Count(r => (string)r["kind"]! == "role"));
        var view = Assert.Single(rows, r => (string)r["kind"]! == "view");
        Assert.Contains("comfortable", (string)view["state"]!);
    }

    [Theory]
    [InlineData("", "user", """{ }""")]
    [InlineData("n", "user", "[1]")]
    [InlineData("n", "user", "not json")]
    [InlineData("n", "everyone", """{ }""")]
    public async Task A_view_without_a_name_with_a_state_that_is_not_an_object_or_with_an_unknown_scope_is_refused(string name, string scope, string state)
        => Assert.False((await new GridViewSaveAction(Scopes()).ExecuteAsync(Call("hamid", ["Planner"], Save(name, scope, state)), default)).Ok);

    [Fact]
    public async Task A_user_deletes_their_own_view_and_a_view_of_a_role_they_hold_but_not_the_view_of_another_user()
    {
        var save = new GridViewSaveAction(Scopes());
        await save.ExecuteAsync(Call("hamid", ["Planner"], Save("mine", "user")), default);
        await save.ExecuteAsync(Call("hamid", ["Planner"], Save("team", "role:Planner")), default);
        var delete = new GridViewDeleteAction(Scopes());
        await delete.ExecuteAsync(Call("saber", ["Planner"], ("viewsKey", "work-orders"), ("name", "mine"), ("scope", "user")), default);       // saber's own scope is empty: nothing of hamid's goes
        Assert.Contains(await RowsFor("hamid", "Planner"), r => (string)r["name"]! == "mine" && (string)r["kind"]! == "view");
        Assert.True((await delete.ExecuteAsync(Call("saber", ["Planner"], ("viewsKey", "work-orders"), ("name", "team"), ("scope", "role:Planner")), default)).Ok);
        Assert.DoesNotContain(await RowsFor("hamid", "Planner"), r => (string)r["name"]! == "team" && (string)r["kind"]! == "view");
        Assert.False((await delete.ExecuteAsync(Call("saber", ["Planner"], ("viewsKey", "work-orders"), ("name", "team"), ("scope", "role:Admin")), default)).Ok);
    }
}
