using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>
/// Discipline Workspace طبق سند راهنما: دیتابیس مشترک، فقط نمای فیلترشده به‌ازای هر Discipline
/// (Tasks/Activities/Deliverables + KPI + Dashboard).
/// </summary>
public interface IDisciplineWorkspaceService
{
    [PEMS.Application.Common.PemsOperation("DisciplineWorkspace", "Workspaces", "DisciplineWorkspace.View")]   // SCRUM-682 (the list of workspaces; Summary is the one-row totals of the workspace page)
    Task<List<DisciplineSummaryDto>> GetSummaryListAsync();

    Task<DisciplineWorkspaceDto?> GetWorkspaceAsync(int disciplineId, int? projectId = null);

    Task<int> AddTaskAsync(int disciplineId, int? projectId, string title, string? description,
        string type, int? responsibleEmployeeId, DateTime? dueDate);

    Task SetTaskStatusAsync(int taskId, string status);
}
