using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.Definitions;
using PEMS.Application.Grids;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Seed;
using PEMS.Tests.TestSupport;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>The W1 pilot pages (Departments, Roles, Security events) run on the generic entity layer with the parameters of their shipped definition files: no handler or provider of their own.</summary>
public sealed class RolesDefinitionsTests
{
    private static readonly EntityCatalog Catalog = EntityCatalog.Default();

    private sealed class Access : IDefinitionAccess
    {
        public Task<bool> CanOpenAsync(string? permission, IReadOnlyCollection<string> roles, CancellationToken ct = default) => Task.FromResult(true);
    }

    private static (IServiceScopeFactory Scopes, string Db) Setup()
    {
        var db = "w1-" + Guid.NewGuid();
        var sp = new ServiceCollection().AddScoped(_ => PemsDbContextFactory.CreateInMemory(db)).AddSingleton<IDefinitionAccess>(new Access()).BuildServiceProvider();
        return (sp.GetRequiredService<IServiceScopeFactory>(), db);
    }

    /// <summary>The payload of a shipped definition file, as the engine reads it.</summary>
    private static JsonObject Shipped(string kind, string key)
        => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, SystemDefinitionSeeder.FolderName, kind, "core", key + ".json")))!["payload"]!;

    private static GridDataRequest Grid(string dataSource, params (string, string)[] query)
    {
        var p = Shipped("data-source", dataSource)["parameters"]!.AsObject();
        return new GridDataRequest(GridRequest.FromQuery(query.Select(q => new KeyValuePair<string, string?>(q.Item1, q.Item2))), "en", ["SystemAdmin"], "x",
            p.ToDictionary(x => x.Key, x => (string?)(x.Value is JsonValue v && v.TryGetValue<string>(out var s) ? s : x.Value!.ToJsonString())));
    }

    private static ActionInvocation Invoke(string action, params (string, string?)[] values)
        => new(values.ToDictionary(v => v.Item1, v => v.Item2), "en", ["SystemAdmin"], "hamid", Shipped("action", action)["parameters"]!.AsObject());

    private static string En(PEMS.UI.Abstractions.TranslatableText t) => t.Resolve(new CultureInfo("en"));

    // ---- roles

    [Fact]
    public async Task The_roles_grid_counts_users_and_active_permissions_and_sorts_as_the_definition_says()
    {
        var (scopes, name) = Setup();
        using (var db = PemsDbContextFactory.CreateInMemory(name))
        {
            db.Roles.AddRange(new ApplicationRole("Planner") { NormalizedName = "PLANNER", Id = 2, SortOrder = 5 }, new ApplicationRole("Inspector") { NormalizedName = "INSPECTOR", Id = 3, SortOrder = 9, Description = "Field inspections" },
                new ApplicationRole("SystemAdmin") { NormalizedName = "SYSTEMADMIN", Id = 1, SortOrder = 1, IsSystemRole = true });
            db.UserRoles.AddRange(new IdentityUserRole<int> { UserId = 10, RoleId = 3 }, new IdentityUserRole<int> { UserId = 11, RoleId = 3 });
            db.RolePermissionGrants.AddRange(new RolePermissionGrant { RoleId = 3, PermissionId = 1, IsActive = true }, new RolePermissionGrant { RoleId = 3, PermissionId = 2, IsActive = false });
            db.SaveChanges();
        }
        var grid = new EntityGridProvider(scopes, Catalog);
        var (items, total) = await grid.QueryAsync(Grid("roles-grid"), CancellationToken.None);
        Assert.Equal(3, total);
        var rows = items.Cast<Dictionary<string, object?>>().ToList();
        Assert.Equal(["SystemAdmin", "Planner", "Inspector"], rows.Select(r => (string)r["name"]!));      // SortOrder, then Name
        var inspector = rows.Single(r => (string)r["name"]! == "Inspector");
        Assert.Equal((2, 1), (inspector["users"], inspector["permissions"]));                              // users, and only the active grants
        Assert.Equal(0, rows.Single(r => (string)r["name"]! == "Planner")["users"]);
        Assert.Equal(["Inspector"], (await grid.QueryAsync(Grid("roles-grid", ("q", "field")), CancellationToken.None)).Items.Cast<Dictionary<string, object?>>().Select(r => (string)r["name"]!));
    }

    [Fact]
    public async Task Creating_a_role_normalizes_the_name_refuses_a_duplicate_and_redirects_to_its_permissions()
    {
        var (scopes, name) = Setup();
        var create = new EntityCreateAction(scopes, Catalog);
        var ok = await create.ExecuteAsync(Invoke("roles-create", ("name", " Auditor "), ("displayName", "Auditor role"), ("description", "")), CancellationToken.None);
        Assert.True(ok.Ok, ok.FormError?.ToString() + string.Join(",", ok.FieldErrors?.Keys ?? []));
        Assert.Equal("/Admin/PermissionMatrix#role-Auditor", ok.Redirect);
        using (var db = PemsDbContextFactory.CreateInMemory(name))
        {
            var role = await db.Roles.SingleAsync();
            Assert.Equal(("Auditor", "AUDITOR"), (role.Name, role.NormalizedName));
            Assert.False(string.IsNullOrEmpty(role.ConcurrencyStamp));
            Assert.Empty(await db.RolePermissionGrants.ToListAsync());         // Default-Deny
        }
        var again = await create.ExecuteAsync(Invoke("roles-create", ("name", "auditor")), CancellationToken.None);
        Assert.Equal("A role with this name already exists.", En(again.FieldErrors!["name"]));
        Assert.False((await create.ExecuteAsync(Invoke("roles-create", ("name", " ")), CancellationToken.None)).Ok);
    }

    [Fact]
    public async Task A_system_role_cannot_be_deleted_by_the_rule_of_the_definition_and_another_role_can()
    {
        var (scopes, name) = Setup();
        using (var db = PemsDbContextFactory.CreateInMemory(name))
        {
            db.Roles.AddRange(new ApplicationRole("SystemAdmin") { NormalizedName = "SYSTEMADMIN", Id = 1 }, new ApplicationRole("Reviewer") { NormalizedName = "REVIEWER", Id = 2, IsSystemRole = true }, new ApplicationRole("Custom") { NormalizedName = "CUSTOM", Id = 3 });
            db.SaveChanges();
        }
        var delete = new EntityDeleteAction(scopes, Catalog);
        var byName = await delete.ExecuteAsync(Invoke("roles-delete", ("id", "1")), CancellationToken.None);
        var byFlag = await delete.ExecuteAsync(Invoke("roles-delete", ("id", "2")), CancellationToken.None);
        Assert.False(byName.Ok); Assert.False(byFlag.Ok);
        Assert.Equal("System roles cannot be deleted.", En(byName.FormError!));
        Assert.True((await delete.ExecuteAsync(Invoke("roles-delete", ("id", "3")), CancellationToken.None)).Ok);
        Assert.False((await delete.ExecuteAsync(Invoke("roles-delete", ("id", "99")), CancellationToken.None)).Ok);       // no longer exists
        using var after = PemsDbContextFactory.CreateInMemory(name);
        Assert.Equal(["Reviewer", "SystemAdmin"], after.Roles.Select(r => r.Name!).OrderBy(x => x).ToList());
    }

    // ---- security events

    [Fact]
    public async Task The_security_events_grid_is_newest_first_without_a_sort_and_nothing_lets_a_definition_write_it()
    {
        var (scopes, name) = Setup();
        using (var db = PemsDbContextFactory.CreateInMemory(name))
        {
            db.SecurityEvents.AddRange(new SecurityEvent { EventType = "Reset", Method = "RecoveryCode", Result = "Success", OccurredAtUtc = new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc) },
                new SecurityEvent { EventType = "Reset", Method = "BreakGlass", Result = "Failure", FailureReason = "expired", OccurredAtUtc = new DateTime(2026, 11, 3, 8, 0, 0, DateTimeKind.Utc) });
            db.SaveChanges();
        }
        var grid = new EntityGridProvider(scopes, Catalog);
        Assert.Equal(["BreakGlass", "RecoveryCode"], (await grid.QueryAsync(Grid("security-events-grid"), CancellationToken.None)).Items.Cast<Dictionary<string, object?>>().Select(r => (string)r["method"]!));
        Assert.Equal(["BreakGlass"], (await grid.QueryAsync(Grid("security-events-grid", ("q", "expired")), CancellationToken.None)).Items.Cast<Dictionary<string, object?>>().Select(r => (string)r["method"]!));
        Assert.Empty(Catalog.Get("SecurityEvent")!.Writable);
    }

    // ---- departments: create, edit (the form loads the row), toggle

    [Fact]
    public async Task Departments_create_edit_and_toggle_run_on_the_generic_handlers_with_the_shipped_parameters()
    {
        var (scopes, name) = Setup();
        var create = new EntityCreateAction(scopes, Catalog);
        Assert.True((await create.ExecuteAsync(Invoke("departments-create", ("code", "ENG"), ("name", "Engineering"), ("description", "d")), CancellationToken.None)).Ok);
        Assert.True((await create.ExecuteAsync(Invoke("departments-create", ("code", ""), ("name", "Maintenance")), CancellationToken.None)).Ok);          // an empty code is allowed
        Assert.True((await create.ExecuteAsync(Invoke("departments-create", ("code", ""), ("name", "Operations")), CancellationToken.None)).Ok);          // and is not a duplicate of the other empty one
        var sameName = await create.ExecuteAsync(Invoke("departments-create", ("name", "engineering")), CancellationToken.None);
        Assert.Equal("A department with this name already exists.", En(sameName.FieldErrors!["name"]));
        var sameCode = await create.ExecuteAsync(Invoke("departments-create", ("code", "eng"), ("name", "Other")), CancellationToken.None);
        Assert.Equal("A department with this code already exists.", En(sameCode.FieldErrors!["code"]));

        int id;
        using (var db = PemsDbContextFactory.CreateInMemory(name)) { var row = await db.Departments.SingleAsync(d => d.Name == "Engineering"); id = row.DepartmentId; Assert.True(row.IsActive); }

        // the edit form loads the row through the action's own map
        var loader = new EntityRecordLoader(scopes, Catalog);
        var edit = Shipped("action", "departments-edit")["parameters"]!.AsObject();
        var values = await loader.LoadAsync(edit, id.ToString(), ["SystemAdmin"], CancellationToken.None);
        Assert.Equal(("ENG", "Engineering", "d"), (values!["code"], values["name"], values["description"]));
        Assert.Null(await loader.LoadAsync(edit, "999", ["SystemAdmin"], CancellationToken.None));
        Assert.Null(await loader.LoadAsync(edit, id.ToString(), ["Viewer"], CancellationToken.None));                           // the catalog's roles

        var update = new EntityUpdateAction(scopes, Catalog);
        Assert.True((await update.ExecuteAsync(Invoke("departments-edit", ("id", id.ToString()), ("code", "ENG"), ("name", "Engineering dept"), ("description", "new")), CancellationToken.None)).Ok);   // not a duplicate of itself
        var clash = await update.ExecuteAsync(Invoke("departments-edit", ("id", id.ToString()), ("name", "Maintenance")), CancellationToken.None);
        Assert.Contains("name", clash.FieldErrors!.Keys);
        Assert.False((await update.ExecuteAsync(Invoke("departments-edit", ("id", "999"), ("name", "X")), CancellationToken.None)).Ok);
        using (var db = PemsDbContextFactory.CreateInMemory(name))
        {
            var row = await db.Departments.SingleAsync(d => d.DepartmentId == id);
            Assert.Equal(("Engineering dept", "new"), (row.Name, row.Description));
        }

        var toggle = new EntityToggleAction(scopes, Catalog);
        Assert.True((await toggle.ExecuteAsync(Invoke("departments-toggle", ("id", id.ToString())), CancellationToken.None)).Ok);
        using (var db = PemsDbContextFactory.CreateInMemory(name)) Assert.False((await db.Departments.SingleAsync(d => d.DepartmentId == id)).IsActive);
        Assert.True((await toggle.ExecuteAsync(Invoke("departments-toggle", ("id", id.ToString())), CancellationToken.None)).Ok);
        using (var db = PemsDbContextFactory.CreateInMemory(name)) Assert.True((await db.Departments.SingleAsync(d => d.DepartmentId == id)).IsActive);
    }

    [Fact]
    public async Task The_departments_grid_counts_the_active_employees_of_each_row()
    {
        var (scopes, name) = Setup();
        int a;
        using (var db = PemsDbContextFactory.CreateInMemory(name))
        {
            var d1 = new Department { Code = "A", Name = "Alpha", IsActive = true }; var d2 = new Department { Code = "B", Name = "Beta", IsActive = true };
            db.Departments.AddRange(d1, d2); db.SaveChanges(); a = d1.DepartmentId;
            db.Employees.AddRange(new Employee { DepartmentId = a, IsActive = true, FirstName = "x", LastName = "y" }, new Employee { DepartmentId = a, IsActive = true, FirstName = "x", LastName = "z" }, new Employee { DepartmentId = a, IsActive = false, FirstName = "x", LastName = "w" });
            db.SaveChanges();
        }
        var rows = (await new EntityGridProvider(scopes, Catalog).QueryAsync(Grid("departments-grid"), CancellationToken.None)).Items.Cast<Dictionary<string, object?>>().ToList();
        Assert.Equal(["Alpha", "Beta"], rows.Select(r => (string)r["name"]!));      // default sort: Name
        Assert.Equal((2, 0), (rows[0]["employees"], rows[1]["employees"]));
    }

    [Fact]
    public async Task The_catalog_roles_are_enforced_by_the_code_not_by_the_definition()
    {
        var (scopes, db) = Setup();
        var refused = await new EntityCreateAction(scopes, Catalog).ExecuteAsync(Invoke("departments-create", ("name", "Sneaky")) with { Roles = ["Viewer"] }, CancellationToken.None);   // not an organization role
        Assert.False(refused.Ok);
        using var ctx = PemsDbContextFactory.CreateInMemory(db);
        Assert.Empty(await ctx.Departments.ToListAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new EntityGridProvider(scopes, Catalog).QueryAsync(Grid("roles-grid") with { Roles = ["Viewer"] }, CancellationToken.None));
    }
}
