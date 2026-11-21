using System.Text.Json.Nodes;
using PEMS.Application.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>SCRUM-576 (render plans: one compile per content hash, atomic swap, last good plan) and SCRUM-574 (hydrated results per permission set: no leakage, expiry, invalidation, hit rate).</summary>
public sealed class DefinitionCachesTests
{
    private static PublishedDefinition Def(string payload, string hash, string key = "x.list") => new("page", key, "core", 1, payload, hash);

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    // ---- 576

    [Fact]
    public void A_definition_is_compiled_once_per_content_hash_and_the_plan_equals_the_uncompiled_parse()
    {
        var cache = new RenderPlanCache();
        var def = Def("""{"layout":"list-page","regions":{"main":[{"id":"a","component":"pems-list"}]}}""", "h1");
        var first = cache.Get(def)!;
        var second = cache.Get(def)!;
        Assert.Same(first, second);
        Assert.Equal(1, cache.Compiles);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(def.PayloadJson), first.Instantiate()));      // the plan renders exactly what the uncompiled definition renders
    }

    [Fact]
    public void Every_request_gets_its_own_copy_so_hydrating_one_never_changes_the_plan_or_another_request()
    {
        var cache = new RenderPlanCache();
        var plan = cache.Get(Def("""{"layout":"l","regions":{}}""", "h1"))!;
        var one = (JsonObject)plan.Instantiate();
        one["hydrated"] = "for user one";
        var two = (JsonObject)plan.Instantiate();
        Assert.Null(two["hydrated"]);
        Assert.Null(plan.Template["hydrated"]);
    }

    [Fact]
    public void A_new_version_replaces_the_plan_in_one_step_and_other_definitions_keep_theirs()
    {
        var cache = new RenderPlanCache();
        var other = cache.Get(Def("""{"layout":"a"}""", "o1", "other.page"))!;
        var v1 = cache.Get(Def("""{"layout":"one"}""", "h1"))!;
        var v2 = cache.Get(Def("""{"layout":"two"}""", "h2"))!;
        Assert.NotSame(v1, v2);
        Assert.Equal("two", (string?)v2.Instantiate()["layout"]);
        Assert.Same(v2, cache.Get(Def("""{"layout":"two"}""", "h2")));
        Assert.Same(other, cache.Get(Def("""{"layout":"a"}""", "o1", "other.page")));
        Assert.Equal(3, cache.Compiles);
    }

    [Fact]
    public void An_edit_that_does_not_compile_keeps_the_last_good_plan_and_a_definition_never_seen_gives_none()
    {
        var cache = new RenderPlanCache();
        var good = cache.Get(Def("""{"layout":"good"}""", "h1"))!;
        var broken = cache.Get(Def("{ not json", "h2"));
        Assert.Same(good, broken);                                                          // served from the last good plan
        Assert.Null(new RenderPlanCache().Get(Def("{ not json", "h1")));
        var fixedAgain = cache.Get(Def("""{"layout":"fixed"}""", "h3"))!;
        Assert.Equal("fixed", (string?)fixedAgain.Instantiate()["layout"]);
    }

    [Fact]
    public async Task Many_requests_during_a_swap_see_either_the_old_plan_or_the_new_one_never_a_mix()
    {
        var cache = new RenderPlanCache();
        var v1 = Def("""{"layout":"one"}""", "h1");
        var v2 = Def("""{"layout":"two"}""", "h2");
        cache.Get(v1);
        var seen = await Task.WhenAll(Enumerable.Range(0, 200).Select(i => Task.Run(() => (string?)cache.Get(i % 2 == 0 ? v1 : v2)!.Instantiate()["layout"])));
        Assert.All(seen, layout => Assert.Contains(layout, new[] { "one", "two" }));
    }

    [Fact]
    public async Task The_pipeline_works_on_a_copy_of_the_plan_and_parses_each_version_once()
    {
        var store = new OneDefinition(Def("""{"layout":"list-page","regions":{}}""", "h1"));
        var cache = new RenderPlanCache();
        var pipeline = new DefinitionPipeline(store, new AllowAll(), [], cache);
        var request = new PipelineRequest("page", "x.list", "core", "en", ["Admin"]);
        var first = await pipeline.RunAsync(request);
        ((JsonObject)first.Resolved!.Payload)["touched"] = true;                            // a hydrator changes its private copy
        var second = await pipeline.RunAsync(request);
        Assert.Null(((JsonObject)second.Resolved!.Payload)["touched"]);
        Assert.Equal(1, cache.Compiles);
        Assert.Equal(1, second.Counts["planCompiles"]);
    }

    private sealed class OneDefinition(PublishedDefinition def) : IDefinitionStore
    {
        public Task<PublishedDefinition?> GetPublishedAsync(string kind, string key, string appKey = "core", CancellationToken ct = default) => Task.FromResult<PublishedDefinition?>(def);
    }

    private sealed class AllowAll : IDefinitionAccess
    {
        public Task<bool> CanOpenAsync(string? permission, IReadOnlyCollection<string> roles, CancellationToken ct = default) => Task.FromResult(true);
    }

    // ---- 574

    [Fact]
    public void The_permission_set_is_the_same_for_the_same_roles_in_any_order_and_case_and_different_otherwise()
    {
        Assert.Equal(HydrationCache.PermissionSet(["Admin", "Engineer"]), HydrationCache.PermissionSet(["engineer", " ADMIN ", "admin"]));
        Assert.NotEqual(HydrationCache.PermissionSet(["Admin"]), HydrationCache.PermissionSet(["Admin", "Engineer"]));
        Assert.Equal(HydrationCache.PermissionSet([]), HydrationCache.PermissionSet([" "]));
    }

    [Fact]
    public async Task Users_of_one_permission_set_share_a_result_and_users_of_another_never_see_it()
    {
        var cache = new HydrationCache(new Clock());
        var calls = 0;
        Task<IReadOnlyList<string>> Load() { calls++; return Task.FromResult<IReadOnlyList<string>>(["Engineering"]); }

        var adminKey = HydrationCache.Key("h1", ["Admin"], "en", "options:departments");
        await cache.GetOrAddAsync(adminKey, TimeSpan.FromSeconds(60), Load);
        await cache.GetOrAddAsync(HydrationCache.Key("h1", ["admin"], "en", "options:departments"), TimeSpan.FromSeconds(60), Load);       // same set, other order and case
        Assert.Equal(1, calls);

        await cache.GetOrAddAsync(HydrationCache.Key("h1", ["Auditor"], "en", "options:departments"), TimeSpan.FromSeconds(60), Load);     // another permission set: its own entry
        Assert.Equal(2, calls);
        await cache.GetOrAddAsync(HydrationCache.Key("h1", ["Admin"], "fa", "options:departments"), TimeSpan.FromSeconds(60), Load);       // another language
        Assert.Equal(3, calls);
        Assert.Equal((1L, 3L), (cache.Hits, cache.Misses));
    }

    [Fact]
    public async Task An_entry_expires_after_its_time_to_live_so_a_data_change_shows_within_that_delay()
    {
        var clock = new Clock();
        var cache = new HydrationCache(clock);
        var calls = 0;
        Task<string[]> Load() { calls++; return Task.FromResult(new[] { "v" + calls }); }
        var key = HydrationCache.Key("h1", ["Admin"], "en", "s");
        Assert.Equal("v1", (await cache.GetOrAddAsync(key, TimeSpan.FromSeconds(30), Load))[0]);
        clock.Now = clock.Now.AddSeconds(29);
        Assert.Equal("v1", (await cache.GetOrAddAsync(key, TimeSpan.FromSeconds(30), Load))[0]);
        clock.Now = clock.Now.AddSeconds(2);
        Assert.Equal("v2", (await cache.GetOrAddAsync(key, TimeSpan.FromSeconds(30), Load))[0]);
    }

    [Fact]
    public async Task A_zero_time_to_live_never_caches_and_does_not_count()
    {
        var cache = new HydrationCache();
        var calls = 0;
        Task<string[]> Load() { calls++; return Task.FromResult(new[] { "x" }); }
        await cache.GetOrAddAsync("k", TimeSpan.Zero, Load);
        await cache.GetOrAddAsync("k", TimeSpan.Zero, Load);
        Assert.Equal(2, calls);
        Assert.Equal((0L, 0L), (cache.Hits, cache.Misses));
        Assert.Equal(0, cache.HitRate);
    }

    [Fact]
    public async Task A_changed_definition_has_a_new_hash_and_evict_drops_the_old_ones()
    {
        var cache = new HydrationCache();
        var calls = 0;
        Task<string[]> Load() { calls++; return Task.FromResult(new[] { "x" }); }
        await cache.GetOrAddAsync(HydrationCache.Key("h1", ["Admin"], "en", "s"), TimeSpan.FromMinutes(5), Load);
        await cache.GetOrAddAsync(HydrationCache.Key("h2", ["Admin"], "en", "s"), TimeSpan.FromMinutes(5), Load);       // the new version of the definition
        Assert.Equal(2, calls);
        Assert.Equal(1, cache.Evict("h1"));
        await cache.GetOrAddAsync(HydrationCache.Key("h1", ["Admin"], "en", "s"), TimeSpan.FromMinutes(5), Load);
        Assert.Equal(3, calls);
        Assert.InRange(cache.HitRate, 0, 1);
    }
}
