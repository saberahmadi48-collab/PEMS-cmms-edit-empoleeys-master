using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PEMS.Application.Definitions;

namespace PEMS.Persistence.Services;

// SCRUM-785, 788, 789: the safe publish. One orchestration for the REST API: the precheck before publish (references must exist and be published; blocked stops with the pointers), the explicit acknowledgement of
// changes that only warn, the smoke check after publish through the same checks the runtime relies on, and the automatic republish of the previous version when the smoke check fails (change summary
// "auto-rollback: reason", audited and stamped like any rollback). The automatic rollback never loops: when the previous version is not safe either it stops and says so (alert in the log).

/// <summary>What the guarded publish did: <see cref="Stage"/> is one of the <see cref="PublishStages"/> constants.</summary>
public sealed record GuardedPublish(string Stage, ImpactReport Report, DefinitionOutcome<DefinitionDetail>? Outcome, string? Reason = null);

public static class PublishStages
{
    /// <summary>The precheck found a blocking reason; nothing was published.</summary>
    public const string Blocked = "blocked";
    /// <summary>The change only warns (data shape, type changes); publish again with acknowledge to go on; nothing was published.</summary>
    public const string NeedsAcknowledgement = "needs-acknowledgement";
    /// <summary>The definition is protected (<c>Definitions:ProtectedDefinitions</c>) and the publisher made the draft: another approver must publish it (SCRUM-808); nothing was published.</summary>
    public const string NeedsSecondApprover = "needs-second-approver";
    /// <summary>The service refused the publish (stale, invalid); <see cref="GuardedPublish.Outcome"/> says why.</summary>
    public const string Failed = "failed";
    /// <summary>Published and the smoke check passed.</summary>
    public const string Published = "published";
    /// <summary>Published, the smoke check failed, the previous version was published again.</summary>
    public const string RolledBack = "rolled-back";
    /// <summary>Published, the smoke check failed and the previous version is not safe either: nothing more was changed, an alert was logged.</summary>
    public const string RollbackStopped = "rollback-stopped";
}

public sealed class DefinitionPublishGuard(IDefinitionService definitions, IDefinitionImpact impact, IConfiguration configuration, ILogger<DefinitionPublishGuard> log)
{
    public const int DefaultSmokeTimeoutMs = 3000;

    /// <summary>The smoke timeout from <c>Definitions:SmokeTimeoutMs</c>, kept between 100 ms and 60 s; a missing or invalid value is the default.</summary>
    public static int SmokeTimeoutMs(string? setting) => int.TryParse(setting, out var ms) ? Math.Clamp(ms, 100, 60_000) : DefaultSmokeTimeoutMs;

    public async Task<GuardedPublish> PublishAsync(int id, long expectedDraftRevision, string actor, bool acknowledge, CancellationToken ct = default)
    {
        var second = await SecondApproverAsync(id, actor, ct);
        if (second != null) return new GuardedPublish(PublishStages.NeedsSecondApprover, ImpactReport.From([]), null, second);
        var report = await impact.PrecheckPublishAsync(id, ct);
        if (report.Blocked) return new GuardedPublish(PublishStages.Blocked, report, null, report.Text());
        if (report.Verdict == "warnings" && !acknowledge) return new GuardedPublish(PublishStages.NeedsAcknowledgement, report, null, report.Text());

        var outcome = await definitions.PublishAsync(id, expectedDraftRevision, actor, ct);
        if (!outcome.Success) return new GuardedPublish(PublishStages.Failed, report, outcome);

        var failure = await SmokeAsync(id, outcome.Value!.Summary.PublishedVersion, ct);
        if (failure is null) return new GuardedPublish(PublishStages.Published, report, outcome);
        return await AutoRollbackAsync(id, outcome, report, actor, failure, ct);
    }

    /// <summary>The reason a protected definition may not be published by this person (they made the draft), or null (SCRUM-808).</summary>
    public async Task<string?> SecondApproverAsync(int id, string actor, CancellationToken ct = default)
    {
        var setting = configuration["Definitions:ProtectedDefinitions"];
        if (string.IsNullOrWhiteSpace(setting)) return null;
        var detail = await definitions.GetAsync(id, ct);
        if (!detail.Success) return null;
        var draft = await definitions.GetDraftAsync(id, ct);
        if (!draft.Success) return null;
        return TwoPersonPublish.NeedsSecondApprover(setting, detail.Value!.Summary.Kind, detail.Value.Summary.Key, draft.Value!.Info.CreatedBy, actor) ? TwoPersonPublish.Message : null;
    }

    /// <summary>
    /// The smoke check of the published version: its references against today's definitions, through the same impact check the precheck uses, inside the timeout. A finding that blocks, an exception or the timeout is a failure
    /// (the reason is returned); null means the version passed.
    /// </summary>
    public async Task<string?> SmokeAsync(int id, int? publishedVersion, CancellationToken ct = default)
    {
        if (publishedVersion is null) return "there is no published version";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(SmokeTimeoutMs(configuration["Definitions:SmokeTimeoutMs"]));
        try
        {
            var check = await impact.PrecheckRollbackAsync(id, publishedVersion.Value, timeout.Token);
            return check.Blocked ? check.Text() : null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return "the smoke check did not finish in time"; }
        catch (Exception ex) { return "the smoke check failed: " + ex.Message; }
    }

    private async Task<GuardedPublish> AutoRollbackAsync(int id, DefinitionOutcome<DefinitionDetail> published, ImpactReport report, string actor, string reason, CancellationToken ct)
    {
        var detail = await definitions.GetAsync(id, ct);
        if (!detail.Success) return new GuardedPublish(PublishStages.RollbackStopped, report, published, reason);
        var current = detail.Value!.Summary.PublishedVersion;
        var previous = detail.Value.Versions.Where(v => v.Status == PEMS.Domain.Entities.Definitions.DefinitionStatus.Retired && v.Number < (current ?? int.MaxValue))
            .OrderByDescending(v => v.Number).Select(v => (int?)v.Number).FirstOrDefault();
        if (previous is null)
        {
            log.LogError("ALERT definition {Id}: the smoke check of the published version failed ({Reason}) and there is no previous version to publish again.", id, reason);
            return new GuardedPublish(PublishStages.RollbackStopped, report, published, reason + "; there is no previous version");
        }

        // the loop guard: the previous version must pass the same check, otherwise nothing more is changed
        var safe = await impact.PrecheckRollbackAsync(id, previous.Value, ct);
        if (safe.Blocked)
        {
            log.LogError("ALERT definition {Id}: the smoke check failed ({Reason}) and version {Previous} is not safe either ({Why}); no automatic rollback.", id, reason, previous, safe.Text());
            return new GuardedPublish(PublishStages.RollbackStopped, report, published, $"{reason}; the previous version {previous} is not safe either: {safe.Text()}");
        }

        var rolled = await definitions.RollbackAsync(id, previous.Value, detail.Value.Summary.Revision, actor, ct, "auto-rollback: " + Short(reason));
        if (!rolled.Success)
        {
            log.LogError("ALERT definition {Id}: automatic rollback to version {Previous} was refused ({Code}).", id, previous, rolled.Code);
            return new GuardedPublish(PublishStages.RollbackStopped, report, published, $"{reason}; the automatic rollback was refused ({rolled.Code})");
        }
        log.LogWarning("Definition {Id}: version {Current} failed the smoke check ({Reason}); version {Previous} was published again.", id, current, reason, previous);
        return new GuardedPublish(PublishStages.RolledBack, report, rolled, reason);
    }

    private static string Short(string reason) => reason.Length <= 300 ? reason : reason[..300];
}
