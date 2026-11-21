using PEMS.Application.Definitions;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Definitions;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Definitions;

/// <summary>SCRUM-573 (ADR-0014): one owner per definition kind, a routed store, and the pipeline stages resolve, authorize and hydrate.</summary>
public sealed class DefinitionPipelineTests
{
    private sealed class Stub(string name, PublishedDefinition? hit = null) : IDefinitionStore
    {
        public int Calls;
        public string Name { get; } = name;
        public Task<PublishedDefinition?> GetPublishedAsync(string kind, string key, string appKey = "core", CancellationToken ct = default) { Calls++; return Task.FromResult(hit); }
    }

    private static PublishedDefinition Def(string kind, string key, string payload = "{\"layout\":\"list-page\",\"regions\":{}}") => new(kind, key, "core", 1, payload, "h");

    [Fact]
    public void The_owner_list_of_the_owner_is_the_default_and_unknown_kinds_have_no_owner()
    {
        var o = new DefinitionOwnership();
        foreach (var kind in new[] { "layout", "page", "form", "view", "dashboard", "action", "data-source", "option-set" }) Assert.Equal(DefinitionOwner.Database, o.OwnerOf(kind));   // SCRUM-579: after the cutover
        Assert.Equal(DefinitionOwner.Database, o.OwnerOf("menu"));
        Assert.Equal(DefinitionOwner.Generated, o.OwnerOf("entity"));
        Assert.Equal(DefinitionOwner.Native, o.OwnerOf("workflow-ref"));
        Assert.Equal(DefinitionOwner.None, o.OwnerOf("widget"));
    }

    [Fact]
    public void The_cutover_of_one_kind_is_one_setting_and_a_bad_value_stops_the_start()
    {
        var o = new DefinitionOwnership(new Dictionary<string, string> { ["form"] = "files" });   // SCRUM-579: the database owns the UI kinds by default; one kind can be set to files for development
        Assert.Equal((DefinitionOwner.Files, DefinitionOwner.Database), (o.OwnerOf("form"), o.OwnerOf("page")));
        Assert.Throws<InvalidOperationException>(() => new DefinitionOwnership(new Dictionary<string, string> { ["form"] = "both" }));
    }

    [Fact]
    public async Task The_routed_store_asks_only_the_owner_and_serves_nothing_for_other_kinds()
    {
        var files = new Stub("files", Def("page", "x"));
        var db = new Stub("db", Def("option-set", "y"));
        var routed = new RoutedDefinitionStore(new DefinitionOwnership(new Dictionary<string, string> { ["page"] = "files" }), new Dictionary<DefinitionOwner, IDefinitionStore> { [DefinitionOwner.Files] = files, [DefinitionOwner.Database] = db });   // page set to files for development

        Assert.NotNull(await routed.GetPublishedAsync("page", "x"));
        Assert.NotNull(await routed.GetPublishedAsync("option-set", "y"));
        Assert.Equal((1, 1), (files.Calls, db.Calls));                    // each owner asked once, and only for its own kind
        Assert.Null(await routed.GetPublishedAsync("entity", "z"));       // generated: not served here
        Assert.Null(await routed.GetPublishedAsync("workflow-ref", "z")); // native: own service
        Assert.Null(await routed.GetPublishedAsync("widget", "z"));       // nobody owns it
        Assert.Equal((1, 1), (files.Calls, db.Calls));
    }

    [Fact]
    public void A_file_of_a_kind_that_the_database_owns_is_rejected()
    {
        var folder = Path.Combine(Path.GetTempPath(), "own-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(folder, "option-set", "core"));
        File.WriteAllText(Path.Combine(folder, "option-set", "core", "units.json"),
            "{\"kind\":\"option-set\",\"key\":\"units\",\"title\":{\"fa\":\"a\",\"en\":\"a\"},\"payload\":{}}");
        try
        {
            using var store = new FileDefinitionStore([], new DefinitionLimits(), new DefinitionFileOptions { Folder = folder, FailOnInvalid = false, Watch = false });
            Assert.Contains(store.Problems, p => p.Message.Contains("owned by database") && p.Message.Contains("one owner per kind"));
            Assert.Empty(store.All);
        }
        finally { Directory.Delete(folder, true); }
    }

    private sealed class Access(bool allow) : IDefinitionAccess
    {
        public string? Asked;
        public Task<bool> CanOpenAsync(string? permission, IReadOnlyCollection<string> roles, CancellationToken ct = default) { Asked = permission; return Task.FromResult(allow); }
    }

    private sealed class Hydrator(int order, string id, List<string> trace) : IDefinitionHydrator
    {
        public int Order => order;
        public Task HydrateAsync(ResolvedDefinition d, PipelineRequest r, CancellationToken ct = default) { trace.Add(id); d.Hydrated[id] = r.Culture; return Task.CompletedTask; }
    }

    private static PipelineRequest Request(string key = "items") => new("page", key, "core", "fa", ["Admin"]);

    [Fact]
    public async Task A_missing_definition_is_not_found_and_nothing_else_runs()
    {
        var access = new Access(true);
        var pipeline = new DefinitionPipeline(new Stub("files", null), access, []);
        var result = await pipeline.RunAsync(Request());
        Assert.Equal(PipelineStatus.NotFound, result.Status);
        Assert.Null(access.Asked);
        Assert.Equal(new[] { "resolve" }, result.Timings.Keys);
    }

    [Fact]
    public async Task A_user_without_the_permission_gets_forbidden_before_any_hydration()
    {
        var trace = new List<string>();
        var access = new Access(false);
        var pipeline = new DefinitionPipeline(new Stub("files", Def("page", "items", "{\"layout\":\"list-page\",\"permission\":\"Equipment.View\",\"regions\":{}}")), access, [new Hydrator(1, "a", trace)]);
        var result = await pipeline.RunAsync(Request());
        Assert.Equal(PipelineStatus.Forbidden, result.Status);
        Assert.Equal("Equipment.View", access.Asked);
        Assert.Empty(trace);
        Assert.Null(result.Resolved);
    }

    [Fact]
    public async Task Hydrators_run_in_order_on_a_private_copy_and_the_published_definition_stays_untouched()
    {
        var trace = new List<string>();
        var published = Def("page", "items");
        var pipeline = new DefinitionPipeline(new Stub("files", published), new Access(true), [new Hydrator(20, "second", trace), new Hydrator(10, "first", trace)]);
        var result = await pipeline.RunAsync(Request());
        Assert.True(result.Ok);
        Assert.Equal(new[] { "first", "second" }, trace);
        Assert.Equal("fa", result.Resolved!.Hydrated["first"]);
        Assert.Equal(new[] { "resolve", "authorize", "hydrate" }, result.Timings.Keys);
        result.Resolved.Payload["layout"] = "changed";                                  // the request's copy
        Assert.Contains("list-page", published.PayloadJson);                            // the shared definition is unchanged
        var again = await pipeline.RunAsync(Request());
        Assert.Equal("list-page", (string?)again.Resolved!.Payload["layout"]);
    }

    [Fact]
    public async Task The_permission_stage_reads_module_dot_action_and_a_malformed_one_never_opens_the_page()
    {
        var access = new PermissionDefinitionAccess(new FakePermissionService());
        Assert.True(await access.CanOpenAsync(null, ["Admin"]));
        Assert.True(await access.CanOpenAsync("", ["Admin"]));
        Assert.False(await access.CanOpenAsync("NoDot", ["Admin"]));
        Assert.False(await access.CanOpenAsync(".View", ["Admin"]));
        Assert.False(await access.CanOpenAsync("Equipment.", ["Admin"]));
        Assert.True(await access.CanOpenAsync("Equipment.View", ["Admin"]));     // reaches the permission service
        var denied = new FakePermissionService();
        denied.Denied.Add(("Equipment", "Edit"));
        Assert.False(await new PermissionDefinitionAccess(denied).CanOpenAsync("Equipment.Edit", ["Admin"]));
    }
}
