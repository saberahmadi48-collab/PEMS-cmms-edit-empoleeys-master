using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.DTOs;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Seed;
using PEMS.Persistence.Services;
using PEMS.Persistence.Workflow;
using PEMS.Tests.TestSupport;
using PEMS.WorkflowPlatform.Entities;
using PEMS.WorkflowPlatform.Interfaces;
using PEMS.WorkflowPlatform.Services;

namespace PEMS.Tests.Workflow;

/// <summary>
/// SCRUM-103: every workflow PEMS code drives is editable in Workflow Management and cannot be broken by an edit.
/// All module seeders run on one InMemory database (the startup order), then the binding backfill; the validator gets the
/// app's action registry and the module contracts. Behaviour must match the old flows: the binding resolves to the same
/// definition and version the legacy Module+Category path picks, and a running instance keeps its version.
/// </summary>
public class PemsWorkflowContractTests : IDisposable
{
    private readonly PEMSDbContext _ctx = PemsDbContextFactory.CreateInMemory("wf-contracts-" + Guid.NewGuid());
    private readonly IServiceProvider _sp;
    private readonly WorkflowAdministrationService _admin;
    private readonly WorkflowAdminActor _actor = new(101, "Admin One", new[] { SystemRoles.SystemAdmin });
    private readonly WorkflowAdminActor _publisher = new(102, "Admin Two", new[] { SystemRoles.SystemAdmin });

    public PemsWorkflowContractTests()
    {
        _ctx.Roles.Add(new ApplicationRole { Name = SystemRoles.SystemAdmin, NormalizedName = SystemRoles.SystemAdmin.ToUpperInvariant() });
        _ctx.SaveChanges();
        _sp = new ServiceCollection().AddSingleton(_ctx).AddLogging().BuildServiceProvider();
        SeedAllAsync(_sp).GetAwaiter().GetResult();

        var validator = NewValidator(_ctx);
        _admin = new WorkflowAdministrationService(_ctx, new AccessControlService(_ctx), new WorkflowDefinitionService(_ctx, validator),
            new WorkflowBindingService(_ctx), PemsWorkflowContracts.CreateRegistry());
    }

    public void Dispose() => _ctx.Dispose();

    /// <summary>Same workflow seeders, same order as Program.cs, then the SCRUM-103 binding backfill.</summary>
    private static async Task SeedAllAsync(IServiceProvider sp)
    {
        await PartCatalogueSeeder.SeedAsync(sp.GetRequiredService<PEMSDbContext>(), importExcel: false);   // the workflow steps the next seeder reads
        await PartCatalogueWorkflowSeeder.SeedAsync(sp);
        await EngineeringDocumentWorkflowSeeder.SeedAsync(sp);
        await VdrWorkflowSeeder.SeedAsync(sp);
        await TechnicalQueryWorkflowSeeder.SeedAsync(sp);
        await MOCWorkflowSeeder.SeedAsync(sp);
        await WorkOrderReviewWorkflowSeeder.SeedAsync(sp);
        await WorkRequestWorkflowSeeder.SeedAsync(sp);
        await WorkOrderLifecycleWorkflowSeeder.SeedAsync(sp);
        await WorkOrderLifecycleWorkflowV2Seeder.SeedAsync(sp);
        await WorkflowManagementSeeder.SeedAsync(sp);
        await NcrApprovalWorkflowSeeder.SeedAsync(sp);
        await IrnDispositionWorkflowSeeder.SeedAsync(sp);
        await MaintenanceStrategyWorkflowSeeder.SeedAsync(sp);
        await ContractWorkflowSeeder.SeedAsync(sp);
        await PermitApprovalWorkflowSeeder.SeedAsync(sp);
        await WarehouseIntakeWorkflowSeeder.SeedAsync(sp);
        await PreProcurementWorkflowSeeder.SeedAsync(sp);
        await PurchaseOrderApprovalWorkflowSeeder.SeedAsync(sp);
        await IrnHoldResolutionWorkflowSeeder.SeedAsync(sp);
        await WorkflowBindingBackfillSeeder.SeedAsync(sp);
    }

    private static WorkflowVersionValidator NewValidator(PEMSDbContext ctx)
        => new(ctx, PemsWorkflowContracts.CreateActionRegistry(), new WorkflowConditionEvaluator(PartCatalogueWorkflow.Fields), null,
            PemsWorkflowContracts.CreateRegistry());   // as registered in Program.cs (the reference catalogue is left out: no roles in this database)

    public static IEnumerable<object[]> Contracts() => PemsWorkflowContracts.All.Select(c => new object[] { c.Module, c.Category });

    private static WorkflowModuleContract Contract(string module, string category)
        => PemsWorkflowContracts.All.Single(c => c.Module == module && c.Category == category);

    /// <summary>What the legacy path (Module + Category, no binding) starts today: the single active definition, its newest Published version.</summary>
    private async Task<WorkflowVersion> LegacyChoiceAsync(WorkflowModuleContract c)
    {
        var def = await _ctx.WorkflowDefinitions.SingleAsync(d => d.IsActive && d.Module == c.Module && d.Category == c.Category);
        return await _ctx.WorkflowVersions.Where(v => v.WorkflowDefinitionId == def.WorkflowDefinitionId && v.Status == WorkflowVersionStatus.Published)
            .OrderByDescending(v => v.VersionNumber).FirstAsync();
    }

    [Fact]
    public void There_is_one_contract_per_module_workflow_and_sixteen_in_all()
    {
        Assert.Equal(17, PemsWorkflowContracts.All.Count);   // SCRUM-472 warehouse intake, SCRUM-471 pre-procurement, SCRUM-470 PO approval, SCRUM-473 IRN hold
        Assert.Equal(17, PemsWorkflowContracts.All.Select(c => (c.Module, c.Category)).Distinct().Count());
        Assert.Equal(17, PemsWorkflowContracts.CreateRegistry().All().Count);
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public async Task The_seeded_definition_meets_its_module_contract(string module, string category)
    {
        var version = await LegacyChoiceAsync(Contract(module, category));

        var result = await NewValidator(_ctx).ValidateAsync(version.WorkflowVersionId);

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public async Task Each_module_workflow_has_a_binding_that_starts_the_same_version_as_before(string module, string category)
    {
        var c = Contract(module, category);
        var legacy = await LegacyChoiceAsync(c);

        var binding = Assert.Single(await _ctx.WorkflowBindings.Where(b => b.IsActive && b.Module == module && b.EntityType == c.EntityType && b.Context == category).ToListAsync());
        var resolved = await new WorkflowBindingService(_ctx).ResolveAsync(module, c.EntityType, category);

        Assert.NotNull(resolved);
        Assert.Equal(legacy.WorkflowDefinitionId, resolved!.Definition.WorkflowDefinitionId);
        Assert.Equal(legacy.WorkflowVersionId, resolved.Version.WorkflowVersionId);
        Assert.Equal(legacy.WorkflowDefinitionId, binding.WorkflowDefinitionId);
    }

    [Fact]
    public async Task Binding_backfill_is_idempotent_and_never_recreates_a_binding_an_administrator_turned_off()
    {
        var before = await _ctx.WorkflowBindings.CountAsync();
        var moc = await _ctx.WorkflowBindings.SingleAsync(b => b.Module == MOCWorkflowSeeder.Module);
        moc.IsActive = false;
        await _ctx.SaveChangesAsync();

        await WorkflowBindingBackfillSeeder.SeedAsync(_sp);

        Assert.Equal(before, await _ctx.WorkflowBindings.CountAsync());
        Assert.False((await _ctx.WorkflowBindings.SingleAsync(b => b.Module == MOCWorkflowSeeder.Module)).IsActive);
    }

    [Fact]
    public async Task Every_module_workflow_is_editable_and_says_which_contract_it_keeps()
    {
        var rows = await _admin.ListAsync(_actor);
        var moduleRows = rows.Where(r => PemsWorkflowContracts.All.Any(c => c.Module == r.Module && c.Category == r.Category) && r.IsActive).ToList();

        Assert.Equal(17, moduleRows.Count);
        Assert.All(moduleRows, r => Assert.True(r.Editable, $"{r.Module}/{r.Category}: {r.EditableReason}"));
        var doc = moduleRows.Single(r => r.Module == EngineeringDocumentWorkflowSeeder.Module);
        Assert.Contains("IDC (Parallel)", doc.EditableReason);
    }

    [Fact]
    public async Task Entity_types_match_what_the_module_code_passes_to_the_engine()
    {
        static string Const(Type t, string name) => (string)t.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)!.GetRawConstantValue()!;

        Assert.Equal(Const(typeof(DocumentWorkflowService), "BusinessObjectType"), Contract("EngineeringDocument", "Approval").EntityType);
        Assert.Equal(Const(typeof(VdrWorkflowService), "BusinessObjectType"), Contract("VendorDocument", "Review").EntityType);
        Assert.Equal(Const(typeof(MOCService), "BusinessObjectType"), Contract("MOC", "ChangeApproval").EntityType);
        Assert.Equal(Const(typeof(TechnicalQueryService), "BusinessObjectType"), Contract("TechnicalQuery", "Response").EntityType);
        Assert.Equal(Const(typeof(WorkOrderReviewService), "BusinessObjectType"), Contract("WorkOrder", "MultiDisciplineReview").EntityType);
        Assert.Equal(Const(typeof(PEMS.Web.Controllers.QCController), "NcrBusinessObjectType"), Contract("NCR", "CriticalApproval").EntityType);
        await Task.CompletedTask;
    }

    // ---------------- an edit that would break the module is refused; other edits pass ----------------

    private async Task<int> CloneAsync(string module, string category)
    {
        var published = await LegacyChoiceAsync(Contract(module, category));
        var r = await _admin.CloneVersionAsync(_actor, published.WorkflowVersionId, "SCRUM-103 test");
        Assert.True(r.Success, r.Error);
        return r.Id!.Value;
    }

    private async Task<WorkflowStage> StageAsync(int versionId, string name)
        => await _ctx.WorkflowStages.AsNoTracking().SingleAsync(s => s.WorkflowVersionId == versionId && s.Name == name);

    private static WorkflowStageEditModel Edit(WorkflowStage s) => new()
    {
        StageId = s.WorkflowStageId, Name = s.Name, NameFa = s.NameFa, BusinessStatus = s.BusinessStatus, StageOrder = s.StageOrder,
        StageType = s.StageType, ResponsibleRole = s.ResponsibleRole, ResponsibleEmployeeId = s.ResponsibleEmployeeId,
        ApprovalStrategy = s.ApprovalStrategy, MinimumApprovalCount = s.MinimumApprovalCount, IsMandatory = s.IsMandatory, TimeoutHours = s.TimeoutHours
    };

    private async Task<IReadOnlyList<string>> ContractErrorsAsync(int versionId)
        => (await NewValidator(_ctx).ValidateAsync(versionId)).Errors.Where(e => e.Code.StartsWith("CONTRACT_")).Select(e => e.Code).ToList();

    [Fact]
    public async Task Renaming_a_stage_the_module_reads_is_refused()
    {
        var v = await CloneAsync("EngineeringDocument", "Approval");
        var idc = Edit(await StageAsync(v, "IDC"));
        idc.Name = "Interdisciplinary";
        Assert.True((await _admin.UpdateStageAsync(_actor, v, null, idc)).Success);

        Assert.Contains("CONTRACT_STAGE_MISSING", await ContractErrorsAsync(v));
        Assert.False((await _admin.ValidateAsync(_actor, v, null)).Success);
    }

    [Fact]
    public async Task Making_a_dynamic_participant_stage_sequential_is_refused()
    {
        var v = await CloneAsync("VendorDocument", "Review");
        var review = Edit(await StageAsync(v, "DisciplineReview"));
        review.StageType = "Sequential";
        review.ResponsibleRole = "Engineer";
        Assert.True((await _admin.UpdateStageAsync(_actor, v, null, review)).Success);

        Assert.Contains("CONTRACT_STAGE_TYPE", await ContractErrorsAsync(v));
    }

    [Fact]
    public async Task Removing_an_action_or_a_hand_over_the_module_executes_is_refused()
    {
        var doc = await CloneAsync("EngineeringDocument", "Approval");
        var issue = await _ctx.WorkflowTransitions.AsNoTracking().SingleAsync(t => t.WorkflowVersionId == doc && t.ActionType == "Issue");
        Assert.True((await _admin.RemoveTransitionAsync(_actor, doc, null, issue.WorkflowTransitionId)).Success);
        Assert.Contains("CONTRACT_ACTION_MISSING", await ContractErrorsAsync(doc));

        var moc = await CloneAsync("MOC", "ChangeApproval");
        var approval = await StageAsync(moc, "Approval");
        var senior = await StageAsync(moc, "SeniorApproval");
        var handOver = await _ctx.WorkflowTransitions.AsNoTracking()
            .SingleAsync(t => t.WorkflowVersionId == moc && t.FromStageId == approval.WorkflowStageId && t.ToStageId == senior.WorkflowStageId);
        Assert.True((await _admin.UpdateTransitionAsync(_actor, moc, null, new WorkflowTransitionEditModel
        {
            TransitionId = handOver.WorkflowTransitionId, FromStageId = approval.WorkflowStageId, ToStageId = (await StageAsync(moc, "Closure")).WorkflowStageId,
            ActionType = "Approve", Priority = handOver.Priority
        })).Success);
        Assert.Contains("CONTRACT_TRANSITION_MISSING", await ContractErrorsAsync(moc));
    }

    [Fact]
    public async Task A_stage_without_BusinessStatus_is_refused_where_the_status_comes_from_the_stage()
    {
        var v = await CloneAsync("WorkRequest", "Standard");
        var first = await _ctx.WorkflowStages.AsNoTracking().Where(s => s.WorkflowVersionId == v).OrderBy(s => s.StageOrder).FirstAsync();
        var edit = Edit(first);
        edit.BusinessStatus = null;
        Assert.True((await _admin.UpdateStageAsync(_actor, v, null, edit)).Success);

        Assert.Contains("CONTRACT_BUSINESS_STATUS_MISSING", await ContractErrorsAsync(v));
    }

    [Fact]
    public async Task Edits_outside_the_contract_validate_publish_and_leave_running_instances_on_their_version()
    {
        var c = Contract("NCR", "CriticalApproval");
        var v1 = await LegacyChoiceAsync(c);
        var runtime = new WorkflowRuntimeService(_ctx, new WorkflowBindingService(_ctx));
        var running = await runtime.StartInstanceAsync(c.Module, c.EntityType, 501, c.Category, null, null);

        // a typical BPMS edit: labels, SLA, participants and an extra review stage before the QA approval
        var v2 = await CloneAsync("NCR", "CriticalApproval");
        var qa = Edit(await StageAsync(v2, "QAApproval"));
        qa.NameFa = "تأیید تضمین کیفیت";
        qa.TimeoutHours = 48;
        Assert.True((await _admin.UpdateStageAsync(_actor, v2, null, qa)).Success);
        Assert.Empty(await ContractErrorsAsync(v2));

        var validated = await _admin.ValidateAsync(_actor, v2, null);
        Assert.True(validated.Success, validated.Error + " " + string.Join("; ", validated.Validation?.Errors.Select(e => e.Code) ?? Array.Empty<string>()));
        var published = await _admin.PublishAsync(_publisher, v2, null);
        Assert.True(published.Success, published.Error);

        // the binding stays pinned until an administrator moves it: new instances still start on v1
        var stillOld = await runtime.StartInstanceAsync(c.Module, c.EntityType, 502, c.Category, null, null);
        Assert.Equal(v1.WorkflowVersionId, (await _ctx.WorkflowInstances.FindAsync(stillOld))!.WorkflowVersionId);

        var binding = await _ctx.WorkflowBindings.SingleAsync(b => b.Module == c.Module && b.EntityType == c.EntityType);
        Assert.True((await _admin.ChangeBindingAsync(_actor, binding.WorkflowBindingId, v2, confirmed: true)).Success);
        var onNew = await runtime.StartInstanceAsync(c.Module, c.EntityType, 503, c.Category, null, null);

        Assert.Equal(v2, (await _ctx.WorkflowInstances.FindAsync(onNew))!.WorkflowVersionId);
        Assert.Equal(v1.WorkflowVersionId, (await _ctx.WorkflowInstances.FindAsync(running))!.WorkflowVersionId);
    }
}
