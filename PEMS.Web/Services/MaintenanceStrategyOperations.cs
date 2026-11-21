using Microsoft.Extensions.Localization;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Web.Resources;
using PEMS.Web.Security;

namespace PEMS.Web.Services;

/// <summary>SCRUM-725: the lifecycle and assignment commands of MaintenanceStrategyController, as operations (same services, same messages).</summary>
public sealed class MaintenanceStrategyOperations(PEMSDbContext context, IMaintenanceStrategyCommandService commands, IStrategyAssignmentService assignments, ICurrentUserService currentUser,
    IStringLocalizer<SharedResource> localizer) : IMaintenanceStrategyOperations
{
    private Task<StrategyActor> ActorAsync() => context.ResolveStrategyActorAsync(currentUser);

    private (bool Success, string? Error) Outcome(OpResult result)
        => result.Success ? (true, null) : (false, string.Join(" | ", localizer.TranslateAll(result.Errors)));

    public async Task<(bool Success, string? Error)> SubmitAsync(int id) => Outcome(await commands.SubmitForReviewAsync(id, await ActorAsync()));

    public async Task<(bool Success, string? Error)> ReviewAsync(int id, bool approve, string? comment = null) => Outcome(await commands.ReviewAsync(id, approve, comment, await ActorAsync()));

    public async Task<(bool Success, string? Error)> ActivateAsync(int id, bool moveAssignments = false) => Outcome(await commands.ActivateAsync(id, moveAssignments, await ActorAsync()));

    public async Task<(bool Success, string? Error)> RetireAsync(int id, string? comment = null) => Outcome(await commands.RetireAsync(id, comment, await ActorAsync()));

    public async Task<(bool Success, string? Error)> AssignAsync(int id, int equipmentId)
    {
        var result = await assignments.AssignAsync(id, new List<int> { equipmentId }, await ActorAsync());
        if (result.Errors.Count > 0) return (false, string.Join(" | ", localizer.TranslateAll(result.Errors)));
        if (result.Assigned > 0) return (true, null);
        var parts = new List<string>();
        if (result.SkippedAlreadyAssigned > 0) parts.Add(localizer["Rcm_Info_SkippedAlready", result.SkippedAlreadyAssigned].Value);
        if (result.SkippedConflict > 0) parts.Add(localizer["Rcm_Info_SkippedConflict", result.SkippedConflict].Value);
        if (result.SkippedCriticality > 0) parts.Add(localizer["Rcm_Info_SkippedCriticality", result.SkippedCriticality].Value);
        if (result.SkippedNotFound > 0) parts.Add(localizer["Rcm_Info_SkippedNotFound", result.SkippedNotFound].Value);
        return (false, parts.Count == 0 ? localizer["Rcm_Info_AssignedCount", 0].Value : string.Join(" · ", parts));
    }

    public async Task<(bool Success, string? Error)> UnassignAsync(int assignmentId, string? comment = null) => Outcome(await assignments.UnassignAsync(assignmentId, comment, await ActorAsync()));

    public async Task<(bool Success, string? Error)> RequestOverrideAsync(int assignmentId, int? overrideIntervalDays = null, string? overrideTaskNote = null, string? overrideJustification = null)
        => Outcome(await assignments.RequestOverrideAsync(assignmentId, overrideIntervalDays, overrideTaskNote, overrideJustification, await ActorAsync()));

    public async Task<(bool Success, string? Error)> DecideOverrideAsync(int assignmentId, bool approve, string? comment = null) => Outcome(await assignments.DecideOverrideAsync(assignmentId, approve, comment, await ActorAsync()));
}
