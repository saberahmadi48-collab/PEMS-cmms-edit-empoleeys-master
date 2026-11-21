using Microsoft.Extensions.Localization;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Web.Resources;
using PEMS.Web.Security;

namespace PEMS.Web.Services;

/// <summary>SCRUM-755: the create and status commands of TaskController (same service, same messages).</summary>
public sealed class TaskItemOperations(ITaskManagerService manager, ICurrentUserService currentUser, IStringLocalizer<SharedResource> localizer) : ITaskItemOperations
{
    private Task<TaskActor> ActorAsync() => manager.ResolveActorAsync(currentUser.UserId, currentUser.Roles, currentUser.FullName);

    public async Task<(bool Success, string? Error)> CreateAsync(string title, string? description = null, int? assignedToEmployeeId = null, DateTime? dueDate = null, string? priority = null)
    {
        var result = await manager.CreateAsync(new WorkItemCreateInput
        {
            Title = title ?? "",
            Description = description,
            AssigneeEmployeeId = assignedToEmployeeId,
            DueDate = dueDate,
            Priority = string.IsNullOrWhiteSpace(priority) ? null : priority
        }, await ActorAsync());
        return result.Success ? (true, null) : (false, localizer[result.ErrorKey!].Value);
    }

    public async Task<(bool Success, string? Error)> UpdateStatusAsync(int taskItemId, string status, string? completionNote = null, string? rowVersion = null)
    {
        var result = await manager.TransitionAsync(taskItemId, null, status, completionNote, rowVersion, await ActorAsync());
        if (result.Success) return (true, null);
        if (result.ErrorKey == "Tm_Err_NotAuthorized") throw new UnauthorizedAccessException();
        return (false, localizer[result.ErrorKey!].Value);
    }
}
