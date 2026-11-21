namespace PEMS.Application.Interfaces;

/// <summary>
/// SCRUM-755: the two commands of TaskController (a new personal task, a status step) as operations over the task manager. The actor is the current user
/// (employee, roles, name); the error keys of the task manager are translated. The list (two tabs, paging) and the details page stay on TaskController.
/// </summary>
public interface ITaskItemOperations
{
    [PEMS.Application.Common.PemsOperation("TaskItem", "Create", "Task.Create")]
    Task<(bool Success, string? Error)> CreateAsync(string title, string? description = null, int? assignedToEmployeeId = null, DateTime? dueDate = null, string? priority = null);

    /// <summary>The status step; a task the user may not touch is Forbid (the task manager decides per object), a missing one a message.</summary>
    [PEMS.Application.Common.PemsOperation("TaskItem", "UpdateStatus", "Task.Edit")]
    Task<(bool Success, string? Error)> UpdateStatusAsync(int taskItemId, string status, string? completionNote = null, string? rowVersion = null);
}
