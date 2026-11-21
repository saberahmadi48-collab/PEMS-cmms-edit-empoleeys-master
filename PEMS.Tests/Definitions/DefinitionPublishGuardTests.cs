using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PEMS.Application.Definitions;
using PEMS.Domain.Entities.Definitions;
using PEMS.Persistence.Services;

namespace PEMS.Tests.Definitions;

/// <summary>
/// SCRUM-785, 788, 789: the safe publish of the REST API. A blocking finding stops before publish, a warning needs the acknowledgement, a failed smoke check publishes the previous version again with the summary
/// "auto-rollback: reason", and the automatic rollback stops instead of looping when the previous version is not safe either. Real service and impact check over the in-memory database; only the smoke result is forced.
/// </summary>
public sealed class DefinitionPublishGuardTests
{
    private const string User = "tester";

    /// <summary>The impact check with a version whose smoke check (the check of the published version against itself) is forced to fail.</summary>
    private sealed class ForcedSmoke(IDefinitionImpact inner, params int[] failingVersions) : IDefinitionImpact
    {
        public Task<IReadOnlyList<DefinitionUsage>> UsedByAsync(string kind, string key, CancellationToken ct = default) => inner.UsedByAsync(kind, key, ct);
        public Task<IReadOnlyList<DefinitionTarget>> ReferencesAsync(int id, CancellationToken ct = default) => inner.ReferencesAsync(id, ct);
        public Task<ImpactReport> PrecheckPublishAsync(int id, CancellationToken ct = default) => inner.PrecheckPublishAsync(id, ct);
        public Task<ImpactReport> PrecheckRollbackAsync(int id, int toVersion, CancellationToken ct = default)
            => failingVersions.Contains(toVersion)
                ? Task.FromResult(ImpactReport.From([new ImpactItem("blocked", "/references/data-source/gone", "The data-source 'gone' (options) does not exist or is not published.")]))
                : inner.PrecheckRollbackAsync(id, toVersion, ct);
    }

    private static (DefinitionService Service, DefinitionPublishGuard Guard) Build(DefinitionTestWorld w, params int[] failingSmokeVersions) => Build(w, null, failingSmokeVersions);

    private static (DefinitionService Service, DefinitionPublishGuard Guard) Build(DefinitionTestWorld w, string? protectedDefinitions, params int[] failingSmokeVersions)
    {
        var (service, _, db) = w.NewService();
        IDefinitionImpact impact = new DefinitionImpactService(db, []);
        if (failingSmokeVersions.Length > 0) impact = new ForcedSmoke(impact, failingSmokeVersions);
        var settings = new Dictionary<string, string?> { ["Definitions:ProtectedDefinitions"] = protectedDefinitions };
        var guard = new DefinitionPublishGuard(service, impact, new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), NullLogger<DefinitionPublishGuard>.Instance);
        return (service, guard);
    }

    /// <summary>A form definition with version 1 published and a draft (version 2) holding <paramref name="draftPayload"/>; returns the id and the draft revision.</summary>
    private static async Task<(int Id, long DraftRevision)> PublishedWithDraftAsync(DefinitionTestWorld w, string draftPayload)
    {
        var (s, _, _) = w.NewService();
        var created = await s.CreateAsync(DefinitionTestWorld.CreateForm("guard.form"), User);
        var id = created.Value!.Summary.Id;
        var first = await s.GetDraftAsync(id);
        await s.PublishAsync(id, first.Value!.Info.Revision, User);
        var env = (await s.GetAsync(id)).Value!.Summary.Revision;
        var draft = await s.CreateDraftAsync(id, null, env, User);
        var saved = await s.SaveDraftAsync(id, draftPayload, "edit", draft.Value!.Info.Revision, User);
        return (id, saved.Value!.Info.Revision);
    }

    [Fact]
    public async Task A_safe_change_publishes_and_passes_the_smoke_check()
    {
        var w = new DefinitionTestWorld();
        var (id, revision) = await PublishedWithDraftAsync(w, DefinitionTestWorld.Form.Replace("Item", "Thing"));
        var (service, guard) = Build(w);
        var result = await guard.PublishAsync(id, revision, User, acknowledge: false);
        Assert.Equal(PublishStages.Published, result.Stage);
        Assert.Equal(2, (await service.GetAsync(id)).Value!.Summary.PublishedVersion);
    }

    [Fact]
    public async Task A_change_that_drops_a_field_is_blocked_before_publish_with_the_pointer()
    {
        var w = new DefinitionTestWorld();
        var without = DefinitionTestWorld.Form.Replace("""{"name":"tag","label":{"en":"Tag","fa":"تگ"}}""", """{"name":"other","label":{"en":"Other","fa":"دیگر"}}""");
        var (id, revision) = await PublishedWithDraftAsync(w, without);
        var (service, guard) = Build(w);
        var result = await guard.PublishAsync(id, revision, User, acknowledge: true);
        Assert.Equal(PublishStages.Blocked, result.Stage);
        Assert.Contains(result.Report.Items, i => i.Severity == "blocked" && i.Path == "/fields/tag");
        Assert.Equal(1, (await service.GetAsync(id)).Value!.Summary.PublishedVersion);       // nothing was published
    }

    [Fact]
    public async Task A_change_that_only_warns_needs_the_acknowledgement()
    {
        var w = new DefinitionTestWorld();
        var typed = DefinitionTestWorld.Form.Replace("""{"name":"tag",""", """{"name":"tag","kind":"number",""");
        var (id, revision) = await PublishedWithDraftAsync(w, typed);
        var (service, guard) = Build(w);
        var first = await guard.PublishAsync(id, revision, User, acknowledge: false);
        Assert.Equal(PublishStages.NeedsAcknowledgement, first.Stage);
        Assert.Equal(1, (await service.GetAsync(id)).Value!.Summary.PublishedVersion);
        var second = await guard.PublishAsync(id, revision, User, acknowledge: true);
        Assert.Equal(PublishStages.Published, second.Stage);
        Assert.Equal(2, (await service.GetAsync(id)).Value!.Summary.PublishedVersion);
    }

    [Fact]
    public async Task A_failed_smoke_check_publishes_the_previous_version_again_with_an_auto_rollback_summary()
    {
        var w = new DefinitionTestWorld();
        var (id, revision) = await PublishedWithDraftAsync(w, DefinitionTestWorld.Form.Replace("Item", "Thing"));
        var (service, guard) = Build(w, failingSmokeVersions: [2]);
        var result = await guard.PublishAsync(id, revision, User, acknowledge: false);
        Assert.Equal(PublishStages.RolledBack, result.Stage);
        var detail = (await service.GetAsync(id)).Value!;
        Assert.Equal(3, detail.Summary.PublishedVersion);                                   // version 2 failed; the old payload is published again as version 3
        Assert.StartsWith("auto-rollback: ", detail.Versions.Single(v => v.Number == 3).ChangeSummary);
        Assert.Equal(DefinitionStatus.Retired, detail.Versions.Single(v => v.Number == 2).Status);
    }

    [Fact]
    public async Task The_automatic_rollback_stops_when_the_previous_version_is_not_safe_either()
    {
        var w = new DefinitionTestWorld();
        var (id, revision) = await PublishedWithDraftAsync(w, DefinitionTestWorld.Form.Replace("Item", "Thing"));
        var (service, guard) = Build(w, failingSmokeVersions: [1, 2]);
        var result = await guard.PublishAsync(id, revision, User, acknowledge: false);
        Assert.Equal(PublishStages.RollbackStopped, result.Stage);
        Assert.Contains("not safe either", result.Reason);
        Assert.Equal(2, (await service.GetAsync(id)).Value!.Summary.PublishedVersion);      // no loop, no second rollback
    }

    [Fact]
    public async Task A_protected_definition_is_published_by_a_second_person_not_by_the_author_of_the_draft()
    {
        var w = new DefinitionTestWorld();
        var (id, revision) = await PublishedWithDraftAsync(w, DefinitionTestWorld.Form.Replace("Item", "Thing"));
        var (service, guard) = Build(w, "form:*");
        var alone = await guard.PublishAsync(id, revision, User, acknowledge: false);                 // "tester" made the draft
        Assert.Equal(PublishStages.NeedsSecondApprover, alone.Stage);
        Assert.Equal(1, (await service.GetAsync(id)).Value!.Summary.PublishedVersion);               // nothing was published
        var second = await guard.PublishAsync(id, revision, "saber", acknowledge: false);
        Assert.Equal(PublishStages.Published, second.Stage);
        Assert.Equal(2, (await service.GetAsync(id)).Value!.Summary.PublishedVersion);
    }

    [Fact]
    public async Task A_definition_that_is_not_protected_is_published_by_its_author()
    {
        var w = new DefinitionTestWorld();
        var (id, revision) = await PublishedWithDraftAsync(w, DefinitionTestWorld.Form.Replace("Item", "Thing"));
        var (_, guard) = Build(w, "page:*");
        Assert.Equal(PublishStages.Published, (await guard.PublishAsync(id, revision, User, acknowledge: false)).Stage);
    }

    [Theory]
    [InlineData(null, 3000)]
    [InlineData("abc", 3000)]
    [InlineData("5", 100)]
    [InlineData("500", 500)]
    [InlineData("999999", 60000)]
    public void The_smoke_timeout_comes_from_the_setting_between_100_ms_and_60_s(string? setting, int expected)
        => Assert.Equal(expected, DefinitionPublishGuard.SmokeTimeoutMs(setting));
}
