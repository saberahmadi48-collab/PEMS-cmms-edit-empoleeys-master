using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
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

/// <summary>The Administration audit pages and the separation-of-duties page (SCRUM-629, 630, 647) run on the generic entity layer with the parameters of their shipped definition files.</summary>
public sealed class AdminAuditDefinitionsTests
{
    private static readonly EntityCatalog Catalog = EntityCatalog.Default();

    private sealed class Access : IDefinitionAccess
    {
        public Task<bool> CanOpenAsync(string? permission, IReadOnlyCollection<string> roles, CancellationToken ct = default) => Task.FromResult(true);
    }

    private static (IServiceScopeFactory Scopes, string Db) Setup()
    {
        var db = "admin-" + Guid.NewGuid();
        var sp = new ServiceCollection().AddScoped(_ => PemsDbContextFactory.CreateInMemory(db)).AddSingleton<IDefinitionAccess>(new Access()).BuildServiceProvider();
        return (sp.GetRequiredService<IServiceScopeFactory>(), db);
    }

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

    private static List<Dictionary<string, object?>> Rows((IReadOnlyList<object> Items, int Total) page) => page.Items.Cast<Dictionary<string, object?>>().ToList();

    [Fact]
    public async Task The_audit_log_grid_is_newest_first_searchable_and_read_only()
    {
        var (scopes, name) = Setup();
        using (var db = PemsDbContextFactory.CreateInMemory(name))
        {
            db.AuditLogs.AddRange(new AuditLog { EntityName = "WorkOrder", EntityKeyValue = "7", Action = "Update", UserName = "hamid", Timestamp = new DateTime(2026, 11, 1, 8, 0, 0), ChangedFieldsSummary = "Status: Open -> Closed" },
                new AuditLog { EntityName = "Permit", EntityKeyValue = "3", Action = "Insert", UserName = "saber", Timestamp = new DateTime(2026, 11, 3, 8, 0, 0), IsAIAssisted = true });
            db.SaveChanges();
        }
        var grid = new EntityGridProvider(scopes, Catalog);
        Assert.Equal(["Permit", "WorkOrder"], Rows(await grid.QueryAsync(Grid("audit-log-grid"), CancellationToken.None)).Select(r => (string)r["entityName"]!));
        Assert.Equal(["WorkOrder"], Rows(await grid.QueryAsync(Grid("audit-log-grid", ("q", "hamid")), CancellationToken.None)).Select(r => (string)r["entityName"]!));
        Assert.Empty(Catalog.Get("AuditLog")!.Writable);
    }

    [Fact]
    public async Task The_authorization_audit_grid_shows_the_user_name_of_each_decision_and_is_read_only()
    {
        var (scopes, name) = Setup();
        using (var db = PemsDbContextFactory.CreateInMemory(name))
        {
            db.Users.Add(new ApplicationUser { Id = 5, UserName = "planner1" });
            db.AuthorizationAuditLogs.AddRange(new AuthorizationAuditLog { UserId = 5, ResourceName = "WorkOrder", ActionName = "Edit", Decision = "Deny", Timestamp = new DateTime(2026, 11, 2, 9, 0, 0) },
                new AuthorizationAuditLog { UserId = null, ResourceName = "Permit", ActionName = "View", Decision = "Allow", Timestamp = new DateTime(2026, 11, 4, 9, 0, 0) });
            db.SaveChanges();
        }
        var rows = Rows(await new EntityGridProvider(scopes, Catalog).QueryAsync(Grid("authorization-audit-grid"), CancellationToken.None));
        Assert.Equal(["Permit", "WorkOrder"], rows.Select(r => (string)r["resourceName"]!));      // newest first
        Assert.Equal("planner1", rows.Single(r => (string)r["resourceName"]! == "WorkOrder")["user"]);
        Assert.Empty(Catalog.Get("AuthorizationAuditLog")!.Writable);
        Assert.Empty(Catalog.Get("ApplicationUser")!.Writable);                               // only the name is readable, for the label; nothing can write an account
        Assert.Equal(["Id", "UserName"], Catalog.Get("ApplicationUser")!.Readable.OrderBy(x => x));   // the key and the name
    }

    private static void SeedPermissions(string name)
    {
        using var db = PemsDbContextFactory.CreateInMemory(name);
        db.Permissions.AddRange(new Permission { PermissionId = 1, Name = "WorkOrder.Create" }, new Permission { PermissionId = 2, Name = "WorkOrder.Approve" }, new Permission { PermissionId = 3, Name = "Permit.Close" });
        db.SaveChanges();
    }

    [Fact]
    public async Task A_separation_policy_is_created_active_and_the_same_pair_is_refused_in_either_order_while_the_first_is_active()
    {
        var (scopes, name) = Setup();
        SeedPermissions(name);
        var create = new EntityCreateAction(scopes, Catalog);
        var first = await create.ExecuteAsync(Invoke("sod-policies-create", ("permissionAId", "1"), ("permissionBId", "2"), ("description", "maker and checker")), CancellationToken.None);
        Assert.True(first.Ok, first.FormError?.ToString());
        using (var db = PemsDbContextFactory.CreateInMemory(name))
        {
            var policy = await db.SoDPolicies.SingleAsync();
            Assert.True(policy.IsActive);
            Assert.NotEqual(default, policy.CreatedDate);
        }
        var reversed = await create.ExecuteAsync(Invoke("sod-policies-create", ("permissionAId", "2"), ("permissionBId", "1")), CancellationToken.None);
        Assert.False(reversed.Ok);
        Assert.Equal("An active policy for this permission pair already exists.", En(reversed.FieldErrors!.Values.Single()));
        Assert.True((await create.ExecuteAsync(Invoke("sod-policies-create", ("permissionAId", "1"), ("permissionBId", "3")), CancellationToken.None)).Ok);   // another pair
    }

    [Fact]
    public async Task A_disabled_separation_policy_does_not_block_a_new_one_for_the_pair_and_a_permission_cannot_conflict_with_itself()
    {
        var (scopes, name) = Setup();
        SeedPermissions(name);
        var create = new EntityCreateAction(scopes, Catalog);
        Assert.True((await create.ExecuteAsync(Invoke("sod-policies-create", ("permissionAId", "1"), ("permissionBId", "2")), CancellationToken.None)).Ok);
        var toggle = new EntityToggleAction(scopes, Catalog);
        Assert.True((await toggle.ExecuteAsync(Invoke("sod-policies-toggle", ("id", "1")), CancellationToken.None)).Ok);
        using (var db = PemsDbContextFactory.CreateInMemory(name)) Assert.False((await db.SoDPolicies.SingleAsync()).IsActive);
        Assert.True((await create.ExecuteAsync(Invoke("sod-policies-create", ("permissionAId", "2"), ("permissionBId", "1")), CancellationToken.None)).Ok);
        var self = await create.ExecuteAsync(Invoke("sod-policies-create", ("permissionAId", "3"), ("permissionBId", "3")), CancellationToken.None);
        Assert.False(self.Ok);
        Assert.Equal("A permission cannot conflict with itself.", En(self.FormError!));
    }

    [Fact]
    public async Task The_separation_policy_grid_shows_both_permission_names()
    {
        var (scopes, name) = Setup();
        SeedPermissions(name);
        using (var db = PemsDbContextFactory.CreateInMemory(name)) { db.SoDPolicies.Add(new SoDPolicy { PermissionAId = 1, PermissionBId = 2, Description = "x" }); db.SaveChanges(); }
        var row = Rows(await new EntityGridProvider(scopes, Catalog).QueryAsync(Grid("sod-policies-grid"), CancellationToken.None)).Single();
        Assert.Equal(("WorkOrder.Create", "WorkOrder.Approve"), (row["permissionA"], row["permissionB"]));
    }

    [Fact]
    public async Task Rows_with_the_same_time_stay_in_a_stable_order_so_no_page_of_the_audit_log_repeats_or_skips_a_row()
    {
        var (scopes, name) = Setup();
        using (var db = PemsDbContextFactory.CreateInMemory(name))
        {
            var same = new DateTime(2026, 11, 1, 8, 0, 0);       // the case the old controller test guarded: many rows saved in the same instant
            for (var i = 0; i < 5; i++) db.AuditLogs.Add(new AuditLog { EntityName = "Vendor", EntityKeyValue = i.ToString(), Action = "Update", UserName = "ali", Timestamp = same });
            db.SaveChanges();
        }
        var grid = new EntityGridProvider(scopes, Catalog);
        var seen = new List<int>();
        for (var page = 1; page <= 3; page++)
            seen.AddRange(Rows(await grid.QueryAsync(Grid("audit-log-grid", ("page", page.ToString()), ("pageSize", "2")), CancellationToken.None)).Select(r => Convert.ToInt32(r["auditLogId"])));
        Assert.Equal(5, seen.Count);
        Assert.Equal(5, seen.Distinct().Count());
        Assert.Equal(seen.OrderByDescending(x => x), seen);     // newest id first within the same time
    }

    [Theory]
    [InlineData("admin-audit-log", "Admin/AuditLog", "audit-log")]
    [InlineData("admin-audit-log", "Admin/AuditLog/Index", "audit-log")]
    [InlineData("admin-authorization-audit", "Admin/AuthorizationAudit/Index", "authorization-audit")]
    [InlineData("admin-sod-policy", "Admin/SoDPolicy/Create", "sod-policies")]
    [InlineData("admin-security-events", "Admin/SecurityEvents", "security-events")]
    public void The_address_of_a_deleted_controller_still_reaches_its_definition_page(string route, string oldAddress, string page)
    {
        var payload = Shipped("route", route);
        Assert.Empty(new RouteDefinitionKind().Validate(payload.ToJsonString(), new DefinitionLimits()));
        Assert.True(new TemplateMatcher(TemplateParser.Parse((string)payload["pattern"]!), new RouteValueDictionary()).TryMatch("/" + oldAddress, new RouteValueDictionary()));
        Assert.Equal("/p/" + page, (string)payload["target"]!);
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, SystemDefinitionSeeder.FolderName, "page", "core", page + ".json")));     // the page the old address leads to ships
    }
}
