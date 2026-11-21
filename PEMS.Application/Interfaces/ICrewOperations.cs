namespace PEMS.Application.Interfaces;

public sealed record CrewRow(int CrewId, string Code, string Name, string WorkOrder, int MemberCount, string Members);
public sealed record CrewMemberRow(int CrewMemberId, string Crew, string Employee, string Role);

/// <summary>
/// The crews (SCRUM-677) as operations: the crew list with its active members, the member lines, the employee, role and work order choices, and the three commands of CrewController (a new crew, add or reactivate a member,
/// remove a member). A crew tied to a work order goes through the work order section guard (phase, permission) and records its change like before; the guard answer is the error text. Permissions: Crew.View, Crew.Create, Crew.Edit.
/// </summary>
public interface ICrewOperations
{
    [PEMS.Application.Common.PemsOperation("CrewAdmin", "Crews", "Crew.View")]
    Task<List<CrewRow>> CrewsAsync();

    [PEMS.Application.Common.PemsOperation("CrewAdmin", "Members", "Crew.View")]
    Task<List<CrewMemberRow>> MembersAsync();

    [PEMS.Application.Common.PemsOperation("CrewAdmin", "EmployeeChoices", "Crew.View")]
    Task<List<ReportChoiceRow>> EmployeeChoicesAsync();

    [PEMS.Application.Common.PemsOperation("CrewAdmin", "RoleChoices", "Crew.View")]
    Task<List<ReportChoiceRow>> RoleChoicesAsync();

    [PEMS.Application.Common.PemsOperation("CrewAdmin", "WorkOrderChoices", "Crew.View")]
    Task<List<ReportChoiceRow>> WorkOrderChoicesAsync();

    [PEMS.Application.Common.PemsOperation("CrewAdmin", "CreateCrew", "Crew.Create")]
    Task<(bool Success, string? Error)> CreateCrewAsync(int? userId, string? userName, int? employeeId, List<string> userRoles, string? ip, string code, string name, int? workOrderId = null);

    [PEMS.Application.Common.PemsOperation("CrewAdmin", "AddMember", "Crew.Edit")]
    Task<(bool Success, string? Error)> AddMemberAsync(int? userId, string? userName, int? employeeId, List<string> userRoles, string? ip, int crewId, int employeeIdToAdd, int roleId);

    [PEMS.Application.Common.PemsOperation("CrewAdmin", "RemoveMember", "Crew.Edit")]
    Task<(bool Success, string? Error)> RemoveMemberAsync(int? userId, string? userName, int? employeeId, List<string> userRoles, string? ip, int crewMemberId);
}
