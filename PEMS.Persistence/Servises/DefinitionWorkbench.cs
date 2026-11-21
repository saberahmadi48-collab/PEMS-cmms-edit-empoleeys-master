using System.Text.Json;
using PEMS.Application.Definitions;
using PEMS.Application.Interfaces;

namespace PEMS.Persistence.Services;

/// <summary>The definition workbench (SCRUM-449): resolves a definition by app, kind and key, takes the latest revisions and runs the definition service; see <see cref="IDefinitionWorkbench"/>.</summary>
public sealed class DefinitionWorkbench(IDefinitionService definitions, IDefinitionImpact impact, Microsoft.Extensions.Configuration.IConfiguration? configuration = null) : IDefinitionWorkbench
{
    public async Task<(bool Success, string? Error)> CreateAsync(string kind, string key, string? appKey, string titleEn, string titleFa, string payloadJson, string? changeSummary, string actor)
    {
        var title = JsonSerializer.Serialize(new Dictionary<string, string> { ["en"] = titleEn?.Trim() ?? "", ["fa"] = titleFa?.Trim() ?? "" });
        var outcome = await definitions.CreateAsync(new CreateDefinition(kind, key, string.IsNullOrWhiteSpace(appKey) ? "core" : appKey.Trim(), title, null, payloadJson, changeSummary), actor);
        return Answer(outcome);
    }

    public async Task<(bool Success, string? Error)> SaveDraftAsync(string kind, string key, string? appKey, string payloadJson, string? changeSummary, string actor)
    {
        var id = await FindAsync(string.IsNullOrWhiteSpace(appKey) ? "core" : appKey.Trim(), kind, key);
        if (id is null) return (false, NotFound(kind, key));
        var draft = await definitions.GetDraftAsync(id.Value);
        if (!draft.Success)
        {
            var detail = await definitions.GetAsync(id.Value);
            if (!detail.Success) return Answer(detail);
            var opened = await definitions.CreateDraftAsync(id.Value, null, detail.Value!.Summary.Revision, actor);
            if (!opened.Success) return Answer(opened);
            draft = opened;
        }
        return Answer(await definitions.SaveDraftAsync(id.Value, payloadJson, changeSummary, draft.Value!.Info.Revision, actor));
    }

    public async Task<(bool Success, string? Error)> ValidateAsync(string reference, string actor)
    {
        var (id, error) = await ResolveAsync(reference);
        if (id is null) return (false, error);
        var draft = await definitions.GetDraftAsync(id.Value);
        if (!draft.Success) return (false, "There is no draft to validate.");
        return Answer(await definitions.ValidateAsync(id.Value, draft.Value!.Info.Revision, actor));
    }

    public async Task<(bool Success, string? Error)> PublishAsync(string reference, string actor)
    {
        var (id, error) = await ResolveAsync(reference);
        if (id is null) return (false, error);
        var draft = await definitions.GetDraftAsync(id.Value);
        if (!draft.Success) return (false, "There is no draft to publish.");
        // SCRUM-808: a protected definition is published by a second person, as in the REST API
        var detail = await definitions.GetAsync(id.Value);
        if (detail.Success && TwoPersonPublish.NeedsSecondApprover(configuration?["Definitions:ProtectedDefinitions"], detail.Value!.Summary.Kind, detail.Value.Summary.Key, draft.Value!.Info.CreatedBy, actor))
            return (false, TwoPersonPublish.Message);
        // SCRUM-450, 457: a draft whose references are missing or unpublished, or that would drop stored data, is stopped with the report; changes that only warn go on
        var report = await impact.PrecheckPublishAsync(id.Value);
        if (report.Blocked) return (false, report.Text());
        // publishing a draft that was not validated validates it first: one step for the designer, the same checks as the REST API
        var validated = await definitions.ValidateAsync(id.Value, draft.Value!.Info.Revision, actor);
        if (!validated.Success) return Answer(validated);
        return Answer(await definitions.PublishAsync(id.Value, validated.Value!.Info.Revision, actor));
    }

    public async Task<(bool Success, string? Error)> RollbackAsync(string reference, int toVersion, string actor)
    {
        var (id, error) = await ResolveAsync(reference);
        if (id is null) return (false, error);
        var detail = await definitions.GetAsync(id.Value);
        if (!detail.Success) return Answer(detail);
        // SCRUM-459: a retired version is published again only if it passes the same check against today's definitions and data shape
        var report = await impact.PrecheckRollbackAsync(id.Value, toVersion);
        if (report.Blocked) return (false, report.Text());
        return Answer(await definitions.RollbackAsync(id.Value, toVersion, detail.Value!.Summary.Revision, actor));
    }

    public async Task<(bool Success, string? Error)> DiscardDraftAsync(string reference, string actor)
    {
        var (id, error) = await ResolveAsync(reference);
        if (id is null) return (false, error);
        var draft = await definitions.GetDraftAsync(id.Value);
        if (!draft.Success) return (false, "There is no draft to discard.");
        return Answer(await definitions.DiscardDraftAsync(id.Value, draft.Value!.Info.Revision, actor));
    }

    /// <summary>"appKey:kind:key" to the definition id.</summary>
    private async Task<(int? Id, string? Error)> ResolveAsync(string reference)
    {
        var parts = (reference ?? "").Split(':', 3);
        if (parts.Length != 3) return (null, "The reference is appKey:kind:key.");
        var id = await FindAsync(parts[0], parts[1], parts[2]);
        return id is null ? (null, NotFound(parts[1], parts[2])) : (id, null);
    }

    private async Task<int?> FindAsync(string appKey, string kind, string key)
    {
        var page = await definitions.ListAsync(new DefinitionListQuery(kind, appKey, key, null, [], 1, 20));
        return page.Items.FirstOrDefault(d => d.Kind == kind && d.Key == key && d.AppKey == appKey)?.Id;
    }

    private static string NotFound(string kind, string key) => $"The definition {kind}/{key} does not exist.";

    private static (bool Success, string? Error) Answer<T>(DefinitionOutcome<T> outcome)
    {
        if (outcome.Success) return (true, null);
        var first = outcome.Errors?.FirstOrDefault();
        var text = first is null ? outcome.Code ?? "The definition could not be saved." : $"{first.Pointer} {first.Message}".Trim();
        return (false, text);
    }
}
