namespace PEMS.Application.Interfaces;

public sealed record ShiftHandoverRow(int ShiftHandoverId, string Outgoing, string Incoming, string ShiftLabel, DateTime HandoverDate, string Status, DateTime? PreparedDate, DateTime? AcknowledgedDate, DateTime? CompletedDate, int ItemCount);
public sealed record ShiftHandoverHeadRow(int ShiftHandoverId, string Outgoing, string Incoming, string ShiftLabel, DateTime HandoverDate, string Status, string Notes, DateTime? PreparedDate, DateTime? AcknowledgedDate, DateTime? CompletedDate);
public sealed record ShiftHandoverItemRow(string ItemType, string ReferenceType, int? ReferenceId, string Description);
public sealed record ShiftHandoverHistoryRow(string FromStatus, string ToStatus, string ChangedBy, DateTime ChangedDate, string Reason);

/// <summary>
/// Shift handovers (SCRUM-749) as read operations (list with a status filter, head, the carried items and the status history, the people choices). The create, prepare, acknowledge and complete commands are attributes on
/// IShiftHandoverService (permissions ShiftHandover.Create, Prepare, Acknowledge, Complete, as before).
/// </summary>
public interface IShiftHandoverOperations
{
    [PEMS.Application.Common.PemsOperation("ShiftHandoverPage", "List", "ShiftHandover.View")]
    Task<List<ShiftHandoverRow>> ListAsync(string? status = null);

    [PEMS.Application.Common.PemsOperation("ShiftHandoverPage", "Head", "ShiftHandover.View")]
    Task<List<ShiftHandoverHeadRow>> HeadAsync(int id);

    [PEMS.Application.Common.PemsOperation("ShiftHandoverPage", "Items", "ShiftHandover.View")]
    Task<List<ShiftHandoverItemRow>> ItemsAsync(int id);

    [PEMS.Application.Common.PemsOperation("ShiftHandoverPage", "History", "ShiftHandover.View")]
    Task<List<ShiftHandoverHistoryRow>> HistoryAsync(int id);

    [PEMS.Application.Common.PemsOperation("ShiftHandoverPage", "EmployeeChoices", "ShiftHandover.View")]
    Task<List<ReportChoiceRow>> EmployeeChoicesAsync();
}
