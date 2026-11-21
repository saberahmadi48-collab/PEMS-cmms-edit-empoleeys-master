using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;

namespace PEMS.Web.Services;

/// <summary>SCRUM-682: the workspace DisciplineController showed, as read operations over IDisciplineWorkspaceService (the workspace is read per call; an unknown discipline gives no rows), and its two task writes.</summary>
public sealed class DisciplineWorkspaceOperations(IDisciplineWorkspaceService service, PEMSDbContext context) : IDisciplineWorkspaceOperations
{
    public async Task<List<DisciplineWorkspaceSummaryRow>> SummaryAsync(int id, int? projectId = null)
        => await service.GetWorkspaceAsync(id, projectId) is { } w
            ? [new DisciplineWorkspaceSummaryRow($"{w.Summary.Code} - {w.Summary.Name}", w.Summary.DocumentsCount, w.Summary.PendingDocumentsCount, w.Summary.EquipmentCount, w.Summary.VDRItemsCount, w.Summary.OpenTasksCount, w.Summary.OverdueTasksCount)]
            : [];

    public async Task<List<DisciplineTaskRow>> TasksAsync(int id, int? projectId = null)
        => (await service.GetWorkspaceAsync(id, projectId))?.Tasks.Select(t => new DisciplineTaskRow(t.DisciplineTaskId, t.Type, t.Title, t.ProjectName ?? "", t.ResponsibleName ?? "",
            t.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "", t.Status, t.IsOverdue ? "Yes" : "")).ToList() ?? [];

    public async Task<List<DisciplineDocumentRow>> DocumentsAsync(int id, int? projectId = null)
        => (await service.GetWorkspaceAsync(id, projectId))?.Documents.Select(d => new DisciplineDocumentRow(d.DocumentId, d.DocumentNo, d.Title, d.Status)).ToList() ?? [];

    public async Task<List<DisciplineEquipmentRow>> EquipmentAsync(int id, int? projectId = null)
        => (await service.GetWorkspaceAsync(id, projectId))?.Equipment.Select(e => new DisciplineEquipmentRow(e.EquipmentId, e.TagNumber, e.Name)).ToList() ?? [];

    public async Task<List<DisciplineVdrRow>> VdrItemsAsync(int id, int? projectId = null)
        => (await service.GetWorkspaceAsync(id, projectId))?.VDRItems.Select(v => new DisciplineVdrRow(v.VDRItemId, v.DocumentNumber, v.VendorName, v.WorkflowStatus)).ToList() ?? [];

    public async Task<List<DisciplineMrRow>> MrsAsync(int id, int? projectId = null)
        => (await service.GetWorkspaceAsync(id, projectId))?.MRs.Select(m => new DisciplineMrRow(m.MRNumber, m.Status)).ToList() ?? [];

    public async Task<List<DisciplineChoiceRow>> ProjectsAsync()
        => (await context.Projects.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.ProjectName).Select(x => new { x.ProjectId, x.ProjectName }).ToListAsync())
            .Select(x => new DisciplineChoiceRow(x.ProjectId.ToString(CultureInfo.InvariantCulture), x.ProjectName)).ToList();

    public async Task<List<DisciplineChoiceRow>> EmployeesAsync()
        => (await context.Employees.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.FirstName).ThenBy(x => x.LastName).Select(x => new { x.EmployeeId, Name = x.FirstName + " " + x.LastName }).ToListAsync())
            .Select(x => new DisciplineChoiceRow(x.EmployeeId.ToString(CultureInfo.InvariantCulture), x.Name)).ToList();

    public async Task<List<DisciplineChoiceRow>> DisciplinesAsync()
        => (await context.Disciplines.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new { x.DisciplineId, x.Name }).ToListAsync())
            .Select(x => new DisciplineChoiceRow(x.DisciplineId.ToString(CultureInfo.InvariantCulture), x.Name)).ToList();

    public Task AddTaskAsync(int disciplineId, string title, string type, int? projectId = null, string? description = null, int? responsibleEmployeeId = null, DateTime? dueDate = null)
        => service.AddTaskAsync(disciplineId, projectId, title, description, type, responsibleEmployeeId, dueDate);

    public Task SetTaskStatusAsync(int taskId, string status) => service.SetTaskStatusAsync(taskId, status);
}
