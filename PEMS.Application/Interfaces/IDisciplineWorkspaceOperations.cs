namespace PEMS.Application.Interfaces;

/// <summary>The totals of one discipline workspace (one row; Title is "code - name").</summary>
public sealed record DisciplineWorkspaceSummaryRow(string Title, int Documents, int Pending, int Equipment, int VdrItems, int OpenTasks, int OverdueTasks);

/// <summary>One task, activity or deliverable of a workspace.</summary>
public sealed record DisciplineTaskRow(int TaskId, string Type, string Title, string Project, string Responsible, string Due, string Status, string Overdue);

public sealed record DisciplineDocumentRow(int DocumentId, string DocumentNo, string Title, string Status);

public sealed record DisciplineEquipmentRow(int EquipmentId, string TagNumber, string Name);

public sealed record DisciplineVdrRow(int VdrItemId, string DocumentNumber, string Vendor, string Status);

public sealed record DisciplineMrRow(string MrNumber, string Status);

/// <summary>A choice of a workspace filter or form (project, employee, discipline).</summary>
public sealed record DisciplineChoiceRow(string Value, string Label);

/// <summary>The discipline workspace page as operations (SCRUM-682): the same IDisciplineWorkspaceService views filtered by discipline and project, and the two task writes (permission DisciplineWorkspace.Create and .Edit as the controller checked).</summary>
public interface IDisciplineWorkspaceOperations
{
    [PEMS.Application.Common.PemsOperation("DisciplineWorkspace", "Summary", "DisciplineWorkspace.View")]
    Task<List<DisciplineWorkspaceSummaryRow>> SummaryAsync(int id, int? projectId = null);

    [PEMS.Application.Common.PemsOperation("DisciplineWorkspace", "Tasks", "DisciplineWorkspace.View")]
    Task<List<DisciplineTaskRow>> TasksAsync(int id, int? projectId = null);

    [PEMS.Application.Common.PemsOperation("DisciplineWorkspace", "Documents", "DisciplineWorkspace.View")]
    Task<List<DisciplineDocumentRow>> DocumentsAsync(int id, int? projectId = null);

    [PEMS.Application.Common.PemsOperation("DisciplineWorkspace", "Equipment", "DisciplineWorkspace.View")]
    Task<List<DisciplineEquipmentRow>> EquipmentAsync(int id, int? projectId = null);

    [PEMS.Application.Common.PemsOperation("DisciplineWorkspace", "VdrItems", "DisciplineWorkspace.View")]
    Task<List<DisciplineVdrRow>> VdrItemsAsync(int id, int? projectId = null);

    [PEMS.Application.Common.PemsOperation("DisciplineWorkspace", "Mrs", "DisciplineWorkspace.View")]
    Task<List<DisciplineMrRow>> MrsAsync(int id, int? projectId = null);

    [PEMS.Application.Common.PemsOperation("DisciplineWorkspace", "Projects", "DisciplineWorkspace.View")]
    Task<List<DisciplineChoiceRow>> ProjectsAsync();

    [PEMS.Application.Common.PemsOperation("DisciplineWorkspace", "Employees", "DisciplineWorkspace.View")]
    Task<List<DisciplineChoiceRow>> EmployeesAsync();

    [PEMS.Application.Common.PemsOperation("DisciplineWorkspace", "Disciplines", "DisciplineWorkspace.View")]
    Task<List<DisciplineChoiceRow>> DisciplinesAsync();

    [PEMS.Application.Common.PemsOperation("DisciplineWorkspace", "AddTask", "DisciplineWorkspace.Create")]
    Task AddTaskAsync(int disciplineId, string title, string type, int? projectId = null, string? description = null, int? responsibleEmployeeId = null, DateTime? dueDate = null);

    [PEMS.Application.Common.PemsOperation("DisciplineWorkspace", "SetTaskStatus", "DisciplineWorkspace.Edit")]
    Task SetTaskStatusAsync(int taskId, string status);
}
