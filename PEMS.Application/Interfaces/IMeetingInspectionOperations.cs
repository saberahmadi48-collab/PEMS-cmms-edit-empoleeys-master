namespace PEMS.Application.Interfaces;

public sealed record MeetingRow(int MeetingId, string Title, DateTime MeetingDate, string Location, string Organizer, string Project, string Status, int AttendeeCount, int ActionItemCount);
public sealed record MeetingHeadRow(int MeetingId, string Title, DateTime MeetingDate, string Location, string Organizer, string Project, string Status, string Agenda, string MinutesNotes);
public sealed record MeetingAttendeeRow(int Id, string Name, bool IsRequired, bool Attended);
public sealed record MeetingActionItemRow(int Id, string Title, string AssignedTo, DateTime? DueDate, string Priority, string Status, bool IsOverdue);
public sealed record InspectionRow(int InspectionRecordId, int EquipmentId, string TagNumber, string InspectionType, DateTime InspectionDate, string Inspector, decimal? MeasurementValue, string MeasurementUnit, decimal? LowerLimit, decimal? UpperLimit,
    string ConditionStatus, DateTime? NextInspectionDate, string DueStatus);

/// <summary>
/// Meetings (SCRUM-728) and inspections (SCRUM-707) as operations: the meeting list, head, attendees and action items, a new meeting and a meeting action item (a task related to the meeting); the fleet inspection list,
/// a new inspection record (service attribute) and the one-click work request raised from an inspection that is out of its limits. Same services as the old controllers.
/// </summary>
public interface IMeetingInspectionOperations
{
    [PEMS.Application.Common.PemsOperation("Meeting", "List")]
    Task<List<MeetingRow>> MeetingsAsync();

    [PEMS.Application.Common.PemsOperation("Meeting", "Head")]
    Task<List<MeetingHeadRow>> MeetingHeadAsync(int id);

    [PEMS.Application.Common.PemsOperation("Meeting", "Attendees")]
    Task<List<MeetingAttendeeRow>> MeetingAttendeesAsync(int id);

    [PEMS.Application.Common.PemsOperation("Meeting", "ActionItems")]
    Task<List<MeetingActionItemRow>> MeetingActionItemsAsync(int id);

    [PEMS.Application.Common.PemsOperation("Meeting", "AddActionItem")]
    Task<(bool Success, string? Error)> AddMeetingActionItemAsync(int meetingId, string title, int? assignedToEmployeeId = null, DateTime? dueDate = null);

    [PEMS.Application.Common.PemsOperation("Inspection", "Fleet", "Inspection.View")]
    Task<List<InspectionRow>> InspectionsAsync();

    [PEMS.Application.Common.PemsOperation("Inspection", "RaiseWorkRequest", "WorkRequest.Create")]
    Task<(bool Success, string? Error)> RaiseWorkRequestAsync(int? employeeId, int inspectionRecordId);
}
