namespace PEMS.Application.Interfaces;

/// <summary>One waiting approval of the My Work page (SCRUM-730): the labels are in the language of the user, <c>RecordPath</c> is the page of the record ("" when it has none).</summary>
public sealed record MyWorkRow(int WorkflowInstanceStageId, string Module, string ModuleLabel, string Stage, bool IsPersonal, int RecordId, string RecordPath, DateTime AssignedDate, DateTime? DueDate, bool IsOverdue);

/// <summary>The approvals waiting for the signed-in user as rows of a grid; the identity comes from the context (employee and roles of the request), never from an argument of the page.</summary>
public interface IMyWorkOperations
{
    [PEMS.Application.Common.PemsOperation("MyWork", "Approvals")]
    Task<List<MyWorkRow>> ApprovalsAsync(int? employeeId = null, IReadOnlyList<string>? roles = null, string? culture = null);
}
