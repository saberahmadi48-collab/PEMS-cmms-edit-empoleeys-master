using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using static PEMS.Persistence.Workflow.WorkOrderWorkflow;

namespace PEMS.Persistence.Services;

/// <summary>
/// پیاده‌سازی دروازه‌ی بخش‌های فرزند Work Order (بند H25-H31). منطق تصمیم در <see cref="WorkOrderSectionPolicy"/> است؛ این کلاس فقط فاز و مجوز را از
/// دیتابیس/موتور مجوز می‌خواند و ممیزی می‌نویسد. ممیزی بهترین‌تلاش است (شکست ثبت ممیزی هرگز یک جهش ردشده را مجاز نمی‌کند).
/// </summary>
public class WorkOrderSectionGuard : IWorkOrderSectionGuard
{
    private readonly PEMSDbContext _context;
    private readonly IAccessControlService _access;

    public WorkOrderSectionGuard(PEMSDbContext context, IAccessControlService access)
    {
        _context = context;
        _access = access;
    }

    private async Task<(string Status, string? StageName)?> LoadStateAsync(int workOrderId)
    {
        var wo = await _context.WorkOrders.AsNoTracking().Where(w => w.WorkOrderId == workOrderId).Select(w => new { w.Status }).FirstOrDefaultAsync();
        if (wo == null) return null;
        var key = LifecycleKey(workOrderId);
        var stage = await _context.WorkflowInstances.AsNoTracking()
            .Where(i => i.EntityKey == key)
            .OrderByDescending(i => i.StartDate).ThenByDescending(i => i.WorkflowInstanceId)
            .Select(i => i.CurrentStage != null ? i.CurrentStage.Name : null)
            .FirstOrDefaultAsync();
        return (wo.Status, stage);
    }

    private async Task<bool> HasAsync(IReadOnlyList<string> roles, string permission)
    {
        var dot = permission.IndexOf('.');
        return (await _access.AuthorizeForRolesAsync(roles, permission[..dot], permission[(dot + 1)..])).Allowed;
    }

    public async Task<WoGuardResult> AuthorizeAsync(int workOrderId, WoSection section, WoSectionAction action, WoCaller caller, string? reason = null, string? recordRef = null)
    {
        var state = await LoadStateAsync(workOrderId);
        if (state == null)
            return new WoGuardResult(false, "WorkOrderNotFound", "Work Order یافت نشد.", "Work Order not found.", WoPhase.Created, 404);

        var phase = WorkOrderSectionPolicy.ResolvePhase(state.Value.Status, state.Value.StageName);
        var hasSection = await HasAsync(caller.Roles, WorkOrderSectionPolicy.RequiredPermission(section));
        var hasCorrection = await HasAsync(caller.Roles, WorkOrderSectionPolicy.CorrectionPermission);
        var decision = WorkOrderSectionPolicy.Decide(section, action, phase, hasSection, hasCorrection, reason);
        if (decision.Allowed) return new WoGuardResult(true, decision.Code, "", "", phase, 200);

        var http = decision.Code is "MissingPermission" or "MissingCorrectionPermission" ? 403 : 400;
        await LogDeniedAsync(workOrderId, section, action, caller, state.Value, phase, decision, reason, recordRef);
        return new WoGuardResult(false, decision.Code, decision.MessageFa, decision.MessageEn, phase, http);
    }

    private async Task LogDeniedAsync(int workOrderId, WoSection section, WoSectionAction action, WoCaller caller, (string Status, string? StageName) state,
        WoPhase phase, WoSectionDecision decision, string? reason, string? recordRef)
    {
        var terminal = phase is WoPhase.Closed or WoPhase.Cancelled or WoPhase.Rejected;
        try
        {
            _context.WorkOrderChangeLogs.Add(new WorkOrderChangeLog
            {
                WorkOrderId = workOrderId, Section = section.ToString(), Action = action.ToString(), RecordRef = recordRef, Outcome = "Denied", Code = decision.Code,
                WorkOrderStatus = state.Status, WorkflowStage = state.StageName, ChangedByEmployeeId = caller.EmployeeId, ChangedByUserId = caller.UserId,
                ChangedByUserName = caller.UserName, ChangedDate = DateTime.Now, Reason = Trunc(reason, 500),
                IsSecurityEvent = terminal || decision.Code is "MissingPermission" or "MissingCorrectionPermission", IpAddress = caller.IpAddress
            });
            await _context.SaveChangesAsync();
        }
        catch (Exception) { _context.ChangeTracker.Clear(); /* ممیزی بهترین‌تلاش است؛ رد بودن جهش تغییر نمی‌کند */ }

        if (terminal)
        {
            try
            {
                await _access.RecordDenialAsync(caller.UserId, "WorkOrder", $"ChildMutation:{section}.{action}", workOrderId.ToString(), caller.IpAddress,
                    AccessDecisionReason.DeniedByWorkflowState, $"Attempt to change a {phase} Work Order ({decision.Code}).");
            }
            catch (Exception) { _context.ChangeTracker.Clear(); }
        }
    }

    public async Task RecordChangeAsync(int workOrderId, WoSection section, WoSectionAction action, WoCaller caller, string? recordRef, string? reason, string? before, string? after)
    {
        var state = await LoadStateAsync(workOrderId);
        _context.WorkOrderChangeLogs.Add(new WorkOrderChangeLog
        {
            WorkOrderId = workOrderId, Section = section.ToString(), Action = action.ToString(), RecordRef = recordRef, Outcome = "Allowed", Code = "Allowed",
            WorkOrderStatus = state?.Status, WorkflowStage = state?.StageName, ChangedByEmployeeId = caller.EmployeeId, ChangedByUserId = caller.UserId,
            ChangedByUserName = caller.UserName, ChangedDate = DateTime.Now, Reason = Trunc(reason, 500), BeforeValue = before, AfterValue = after, IpAddress = caller.IpAddress
        });
        await _context.SaveChangesAsync();
    }

    public async Task<WoSectionPage?> GetPageAsync(int workOrderId, WoCaller caller)
    {
        var state = await LoadStateAsync(workOrderId);
        if (state == null) return null;
        var phase = WorkOrderSectionPolicy.ResolvePhase(state.Value.Status, state.Value.StageName);
        var hasCorrection = await HasAsync(caller.Roles, WorkOrderSectionPolicy.CorrectionPermission);

        var perms = new Dictionary<string, bool>();
        var access = new Dictionary<WoSection, SectionAccess>();
        var controls = new Dictionary<WoSection, bool>();
        foreach (var section in Enum.GetValues<WoSection>())
        {
            var perm = WorkOrderSectionPolicy.RequiredPermission(section);
            if (!perms.TryGetValue(perm, out var has)) perms[perm] = has = await HasAsync(caller.Roles, perm);
            access[section] = WorkOrderSectionPolicy.AccessFor(section, phase);
            controls[section] = WorkOrderSectionPolicy.ShowsControls(section, phase, has, hasCorrection);
        }

        var canReopen = phase == WoPhase.Closed && await HasAsync(caller.Roles, WorkOrderSectionPolicy.ReopenPermission);
        return new WoSectionPage(phase, state.Value.Status, state.Value.StageName, access, controls, canReopen, hasCorrection);
    }

    private static string? Trunc(string? s, int max) => s == null ? null : (s.Length <= max ? s : s[..max]);
}
