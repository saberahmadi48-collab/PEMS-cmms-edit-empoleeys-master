using Microsoft.AspNetCore.Http;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Workflow;

namespace PEMS.Web.Services;

public sealed class WorkflowManagementPageService(IWorkflowAdministrationService admin, ICurrentUserContext user, IHttpContextAccessor http) : IWorkflowManagementPageService
{
    private WorkflowAdminActor Actor() => new(user.UserId ?? 0, http.HttpContext?.User.Identity?.Name ?? "user#" + (user.UserId ?? 0), user.Roles);

    private static WfResultDto Result(WorkflowAdminResult r)
    {
        if (r.Success) return new WfResultDto(true, null);
        var issues = r.Validation?.Issues.Select(i => WorkflowValidationText.Localize(i.Code, i.Message)).ToList();
        return new WfResultDto(false, issues is { Count: > 0 } ? r.Error + " " + string.Join(" | ", issues) : r.Error);
    }

    private async Task<string?> RowVersionAsync(int versionId) => (await admin.GetDesignerAsync(Actor(), versionId))?.RowVersion;

    private static (int Version, int Id)? Split(string id)
    {
        var p = (id ?? "").Split('-');
        return p.Length == 2 && int.TryParse(p[0], out var v) && int.TryParse(p[1], out var i) ? (v, i) : null;
    }

    public Task<List<WorkflowDefinitionRow>> DefinitionsAsync() => admin.ListAsync(Actor());

    public async Task<List<WfDefinitionRecordDto>> DefinitionRecordAsync(int definitionId)
    {
        var d = await admin.GetDetailsAsync(Actor(), definitionId);
        return d == null ? [] : [new WfDefinitionRecordDto { DefinitionId = d.Definition.DefinitionId, Name = d.Definition.Name, NameFa = d.Definition.NameFa, Description = d.Description, Module = d.Definition.Module, Category = d.Definition.Category, IsActive = d.Definition.IsActive,
            PublishedVersion = d.Definition.PublishedVersion, OpenDraftVersion = d.Definition.OpenDraftVersion, Editable = d.Definition.Editable, EditableReason = d.Definition.EditableReason, TotalInstances = d.TotalInstances }];
    }

    public async Task<List<WfVersionRowDto>> VersionsAsync(int definitionId)
    {
        var d = await admin.GetDetailsAsync(Actor(), definitionId);
        return d == null ? [] : d.Versions.Select(v => new WfVersionRowDto { DefinitionId = definitionId, VersionId = v.VersionId, VersionNumber = v.VersionNumber, Status = v.Status, Stages = v.Stages, Transitions = v.Transitions, PublishedDate = v.PublishedDate, RetiredDate = v.RetiredDate,
            ClonedFromVersion = v.ClonedFromVersion, ChangeSummary = v.ChangeSummary, IsBound = v.IsBound, RunningInstances = v.RunningInstances, TotalInstances = v.TotalInstances }).ToList();
    }

    public async Task<List<WorkflowBindingRow>> BindingsAsync(int? definitionId = null) => (await admin.ListBindingsAsync(Actor())).Where(b => definitionId == null || b.DefinitionId == definitionId).ToList();

    public async Task<List<WfUsageRowDto>> UsageAsync(int definitionId)
    {
        var d = await admin.GetDetailsAsync(Actor(), definitionId);
        return d == null ? [] : d.Usage.Select(u => new WfUsageRowDto { DefinitionId = definitionId, VersionNumber = u.VersionNumber, Running = u.Running, Completed = u.Completed, Cancelled = u.Cancelled, Rejected = u.Rejected }).ToList();
    }

    public async Task<List<WfAuditRowDto>> AuditAsync(int definitionId)
    {
        var d = await admin.GetDetailsAsync(Actor(), definitionId);
        return d == null ? [] : d.Audit.Select(a => new WfAuditRowDto { DefinitionId = definitionId, Timestamp = a.Timestamp, Action = a.Action, Actor = a.Actor, VersionNumber = a.VersionNumber, Previous = a.Previous, Current = a.Current, Details = a.Details }).ToList();
    }

    public async Task<List<WfVersionRecordDto>> VersionRecordAsync(int versionId)
    {
        var m = await admin.GetDesignerAsync(Actor(), versionId);
        return m == null ? [] : [new WfVersionRecordDto { VersionId = versionId, DefinitionId = m.DefinitionId, DefinitionName = m.DefinitionName, VersionNumber = m.VersionNumber, Status = m.Status, CanEditStructure = m.CanEditStructure, ChangeSummary = m.ChangeSummary, LastEditor = m.LastEditor, RunningInstances = m.RunningInstances }];
    }

    public async Task<List<WfStageRowDto>> StagesAsync(int versionId)
    {
        var m = await admin.GetDesignerAsync(Actor(), versionId);
        return m == null ? [] : m.Stages.Select(s => new WfStageRowDto { VersionId = versionId, Key = versionId + "-" + s.StageId, StageId = s.StageId, Name = s.Name, NameFa = s.NameFa, BusinessStatus = s.BusinessStatus, StageOrder = s.StageOrder, StageType = s.StageType, ResponsibleRole = s.ResponsibleRole, ApprovalStrategy = s.ApprovalStrategy, IsMandatory = s.IsMandatory, TimeoutHours = s.TimeoutHours }).OrderBy(s => s.StageOrder).ToList();
    }

    public async Task<List<WfTransitionRowDto>> TransitionsAsync(int versionId)
    {
        var m = await admin.GetDesignerAsync(Actor(), versionId);
        return m == null ? [] : m.Transitions.Select(x => new WfTransitionRowDto { VersionId = versionId, Key = versionId + "-" + x.TransitionId, TransitionId = x.TransitionId, FromStageId = x.FromStageId, ToStageId = x.ToStageId, ActionType = x.ActionType, Label = x.Label, RequiredPermission = x.RequiredPermission, ConditionExpression = x.ConditionExpression, Priority = x.Priority, ForbidInitiator = x.ForbidInitiator }).ToList();
    }

    public async Task<List<WorkflowStageEditModel>> StageOfAsync(string id)
    {
        if (Split(id) is not { } p) return [];
        var s = (await admin.GetDesignerAsync(Actor(), p.Version))?.Stages.FirstOrDefault(x => x.StageId == p.Id);
        return s == null ? [] : [s];
    }

    public async Task<List<WorkflowTransitionEditModel>> TransitionOfAsync(string id)
    {
        if (Split(id) is not { } p) return [];
        var x = (await admin.GetDesignerAsync(Actor(), p.Version))?.Transitions.FirstOrDefault(y => y.TransitionId == p.Id);
        return x == null ? [] : [x];
    }

    public async Task<List<WfBindingImpactRowDto>> BindingImpactAsync(int bindingId, int targetVersionId)
    {
        var i = await admin.GetBindingImpactAsync(Actor(), bindingId, targetVersionId);
        return i == null ? [] : [new WfBindingImpactRowDto { BindingId = bindingId, TargetVersionId = targetVersionId, Definition = i.Definition, CurrentVersion = i.CurrentVersion, TargetVersion = i.TargetVersion, RunningInstances = i.RunningInstances }];
    }

    public async Task<List<WfOptionRowDto>> EmployeeOptionsAsync()
    {
        var any = (await admin.ListAsync(Actor())).FirstOrDefault();
        var versionId = any?.OpenDraftVersion ?? any?.PublishedVersion;
        var m = versionId is int v ? await admin.GetDesignerAsync(Actor(), v) : null;
        return m == null ? [] : m.Employees.Select(e => new WfOptionRowDto { Value = e.Id.ToString(), Label = e.Name }).ToList();
    }

    public async Task<WfResultDto> UpdateDefinitionAsync(int definitionId, string name, string? nameFa, string? description) => Result(await admin.UpdateDefinitionAsync(Actor(), definitionId, name, nameFa, description));

    public async Task<WfResultDto> ArchiveAsync(int definitionId) => Result(await admin.ArchiveDefinitionAsync(Actor(), definitionId));

    public async Task<WfResultDto> CloneAsync(int versionId, string? summary) => Result(await admin.CloneVersionAsync(Actor(), versionId, summary));

    public async Task<WfResultDto> DiscardAsync(int versionId) => Result(await admin.DiscardDraftAsync(Actor(), versionId, await RowVersionAsync(versionId)));

    public async Task<WfResultDto> ValidateAsync(int versionId) => Result(await admin.ValidateAsync(Actor(), versionId, await RowVersionAsync(versionId)));

    public async Task<WfResultDto> PublishAsync(int versionId) => Result(await admin.PublishAsync(Actor(), versionId, await RowVersionAsync(versionId)));

    public async Task<WfResultDto> RetireAsync(int versionId) => Result(await admin.RetireAsync(Actor(), versionId));

    public async Task<WfResultDto> SaveStageAsync(int versionId, WorkflowStageEditModel stage)
    {
        var rowVersion = await RowVersionAsync(versionId);
        return Result(stage.StageId == 0 ? await admin.AddStageAsync(Actor(), versionId, rowVersion, stage) : await admin.UpdateStageAsync(Actor(), versionId, rowVersion, stage));
    }

    public async Task<WfResultDto> SaveStageOfAsync(string id, WorkflowStageEditModel stage)
    {
        if (Split(id) is not { } p) return new WfResultDto(false, "The stage was not found.");
        stage.StageId = p.Id;
        return await SaveStageAsync(p.Version, stage);
    }

    public async Task<WfResultDto> SaveTransitionOfAsync(string id, WorkflowTransitionEditModel transition)
    {
        if (Split(id) is not { } p) return new WfResultDto(false, "The transition was not found.");
        transition.TransitionId = p.Id;
        return await SaveTransitionAsync(p.Version, transition);
    }

    public async Task<WfResultDto> DeleteStageAsync(string id) => Split(id) is { } p ? Result(await admin.RemoveStageAsync(Actor(), p.Version, await RowVersionAsync(p.Version), p.Id)) : new WfResultDto(false, "The stage was not found.");

    public async Task<WfResultDto> SaveTransitionAsync(int versionId, WorkflowTransitionEditModel transition)
    {
        var rowVersion = await RowVersionAsync(versionId);
        return Result(transition.TransitionId == 0 ? await admin.AddTransitionAsync(Actor(), versionId, rowVersion, transition) : await admin.UpdateTransitionAsync(Actor(), versionId, rowVersion, transition));
    }

    public async Task<WfResultDto> DeleteTransitionAsync(string id) => Split(id) is { } p ? Result(await admin.RemoveTransitionAsync(Actor(), p.Version, await RowVersionAsync(p.Version), p.Id)) : new WfResultDto(false, "The transition was not found.");

    public async Task<WfResultDto> ChangeBindingAsync(int bindingId, int targetVersionId, bool confirmed) => Result(await admin.ChangeBindingAsync(Actor(), bindingId, targetVersionId, confirmed));

    public async Task<WfResultDto> DeactivateBindingAsync(int bindingId, bool confirmed) => Result(await admin.DeactivateBindingAsync(Actor(), bindingId, confirmed));
}
