using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Web.Security;

namespace PEMS.Web.Services;

/// <summary>SCRUM-707, 728: the meeting and inspection controllers over their services.</summary>
public sealed class MeetingInspectionOperations(IMeetingService meetings, ITaskService tasks, IInspectionService inspections, IWorkRequestService workRequests, PEMSDbContext context, ICurrentUserService currentUser) : IMeetingInspectionOperations
{
    public async Task<List<MeetingRow>> MeetingsAsync() => (await meetings.GetAllAsync()).Select(m => new MeetingRow(m.MeetingId, m.Title, m.MeetingDate, m.Location ?? "", m.OrganizerName ?? "", m.ProjectName ?? "", m.Status, m.AttendeeCount, m.ActionItemCount)).ToList();

    public async Task<List<MeetingHeadRow>> MeetingHeadAsync(int id)
    {
        var d = await meetings.GetDetailAsync(id);
        if (d is null) return [];
        var m = d.Meeting;
        return [new MeetingHeadRow(m.MeetingId, m.Title, m.MeetingDate, m.Location ?? "", m.OrganizerName ?? "", m.ProjectName ?? "", m.Status, d.Agenda ?? "", d.MinutesNotes ?? "")];
    }

    public async Task<List<MeetingAttendeeRow>> MeetingAttendeesAsync(int id) => (await meetings.GetDetailAsync(id))?.Attendees.Select(a => new MeetingAttendeeRow(a.MeetingAttendeeId, a.DisplayName, a.IsRequired, a.Attended)).ToList() ?? [];

    public async Task<List<MeetingActionItemRow>> MeetingActionItemsAsync(int id) => (await meetings.GetDetailAsync(id))?.ActionItems.Select(t => new MeetingActionItemRow(t.TaskItemId, t.Title, t.AssignedToEmployeeName ?? "", t.DueDate, t.Priority, t.Status, t.IsOverdue)).ToList() ?? [];

    public async Task<(bool Success, string? Error)> AddMeetingActionItemAsync(int meetingId, string title, int? assignedToEmployeeId = null, DateTime? dueDate = null)
    {
        await tasks.CreateAsync(title, null, assignedToEmployeeId, dueDate, "Normal", "Meeting", meetingId, currentUser.UserId, currentUser.FullName);
        return (true, null);
    }

    public async Task<List<InspectionRow>> InspectionsAsync() => (await inspections.GetPagedFleetAsync(1, 500)).Items
        .Select(i => new InspectionRow(i.InspectionRecordId, i.EquipmentId, i.TagNumber, i.InspectionType, i.InspectionDate, i.Inspector ?? "", i.MeasurementValue, i.MeasurementUnit ?? "", i.LowerLimit, i.UpperLimit, i.ConditionStatus, i.NextInspectionDate, i.DueStatus)).ToList();

    // Alert to work request: a one-click human action over the real work request service, never silent
    public async Task<(bool Success, string? Error)> RaiseWorkRequestAsync(int? employeeId, int inspectionRecordId)
    {
        var record = await context.InspectionRecords.FirstOrDefaultAsync(x => x.InspectionRecordId == inspectionRecordId);
        if (record is null) return (false, "NotFound");
        if (employeeId is not int emp) return (false, "کارمند مرتبط با این کاربر پیدا نشد.");
        var condition = record.LowerLimit.HasValue && record.MeasurementValue < record.LowerLimit
            ? $"کمتر از حد مجاز ({record.MeasurementValue} {record.MeasurementUnit} < {record.LowerLimit} {record.MeasurementUnit})"
            : $"بیشتر از حد مجاز ({record.MeasurementValue} {record.MeasurementUnit} > {record.UpperLimit} {record.MeasurementUnit})";
        var title = $"Inspection Alert - {record.InspectionType} - Equipment #{record.EquipmentId}";
        var reported = $"بازرسی {record.InspectionType} در تاریخ {record.InspectionDate:yyyy-MM-dd}: {condition}.";
        var (success, error, _) = await workRequests.CreateAsync(title, reported, emp, record.EquipmentId, null, null, reported, "High", null);
        return (success, error);
    }
}
