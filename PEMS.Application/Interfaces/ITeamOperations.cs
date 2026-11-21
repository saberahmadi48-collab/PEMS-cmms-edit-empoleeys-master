namespace PEMS.Application.Interfaces;

public sealed record TeamRow(int TeamId, string Code, string Name, string Group, string Discipline, string Shift, string Supervisor, int Members);

/// <summary>The header of the team page (one row).</summary>
public sealed record TeamProfileRow(string Name, string Code, string Kind, string Discipline, string Supervisor, int Members, int Positions, int VacantPositions);

public sealed record TeamMemberRow(int TeamMemberId, int EmployeeId, string Employee, string Role, string Joined);

public sealed record TeamNodeRow(int TeamId, string Code, string Name, string Kind, string Supervisor, int Members, int Positions, int VacantPositions);

public sealed record TeamPositionRow(int PositionId, string Title, string Description, int SortOrder, string Occupant, string Status, string Since);

public sealed record TeamAssignmentRow(string Position, string Employee, string Started, string Ended, string Notes);

public sealed record TeamChoiceRow(string Value, string Label);

/// <summary>The answer of a team write: <c>Error</c> is the refusal, or the message to show when <c>Success</c>.</summary>
public sealed record TeamResult(bool Success, string? Error);

/// <summary>
/// The Team pages as operations (SCRUM-682's neighbour, SCRUM-757): the team list, the team page (members, sub-teams, positions, assignment history) and the writes of the controller, over the same
/// ITeamService and IOrganizationStructureService. The discipline scope is the old rule: SystemAdmin and EngineeringManager work across disciplines, everybody else only in the discipline of their own
/// employee record (fail closed). The organization chart and its JSON stay in the controller.
/// </summary>
public interface ITeamOperations
{
    [PEMS.Application.Common.PemsOperation("Team", "List", "Team.View")]
    Task<List<TeamRow>> ListAsync();

    [PEMS.Application.Common.PemsOperation("Team", "Profile", "Team.View")]
    Task<List<TeamProfileRow>> ProfileAsync(List<string> userRoles, string userName, int teamId);

    [PEMS.Application.Common.PemsOperation("Team", "Members", "Team.View")]
    Task<List<TeamMemberRow>> MembersAsync(List<string> userRoles, string userName, int teamId);

    [PEMS.Application.Common.PemsOperation("Team", "SubNodes", "Team.View")]
    Task<List<TeamNodeRow>> SubNodesAsync(List<string> userRoles, string userName, int teamId);

    [PEMS.Application.Common.PemsOperation("Team", "Positions", "Team.View")]
    Task<List<TeamPositionRow>> PositionsAsync(List<string> userRoles, string userName, int teamId);

    [PEMS.Application.Common.PemsOperation("Team", "AssignmentHistory", "Team.View")]
    Task<List<TeamAssignmentRow>> AssignmentHistoryAsync(List<string> userRoles, string userName, int teamId);

    [PEMS.Application.Common.PemsOperation("Team", "Groups", "Team.View")]
    Task<List<TeamChoiceRow>> GroupsAsync();

    [PEMS.Application.Common.PemsOperation("Team", "Employees", "Team.View")]
    Task<List<TeamChoiceRow>> EmployeesAsync();

    [PEMS.Application.Common.PemsOperation("Team", "MaintenanceRoles", "Team.View")]
    Task<List<TeamChoiceRow>> MaintenanceRolesAsync();

    [PEMS.Application.Common.PemsOperation("Team", "TeamNodes", "Team.View")]
    Task<List<TeamChoiceRow>> TeamNodesAsync();

    [PEMS.Application.Common.PemsOperation("Team", "VacantPositions", "Team.View")]
    Task<List<TeamChoiceRow>> VacantPositionsAsync();

    [PEMS.Application.Common.PemsOperation("Team", "CreateTeam", "Team.Create")]
    Task<TeamResult> CreateTeamAsync(List<string> userRoles, string userName, string code, string name, int maintenanceGroupId, string? shift = null, int? supervisorEmployeeId = null);

    [PEMS.Application.Common.PemsOperation("Team", "AddMember", "Team.Edit")]
    Task<TeamResult> AddMemberAsync(List<string> userRoles, string userName, int teamId, int employeeId, int roleId);

    [PEMS.Application.Common.PemsOperation("Team", "RemoveMember", "Team.Edit")]
    Task<TeamResult> RemoveMemberAsync(List<string> userRoles, string userName, int teamMemberId);

    [PEMS.Application.Common.PemsOperation("Team", "MoveMember", "Team.Edit")]
    Task<TeamResult> MoveMemberAsync(List<string> userRoles, string userName, int employeeId, int fromTeamId, int toTeamId, int roleId);

    [PEMS.Application.Common.PemsOperation("Team", "CreateSubTeam", "Team.Create")]
    Task<TeamResult> CreateSubTeamAsync(List<string> userRoles, string userName, int maintenanceGroupId, string code, string name, int? parentTeamId = null, string? nodeKind = null, int? supervisorEmployeeId = null);

    [PEMS.Application.Common.PemsOperation("Team", "MoveTeamNode", "Team.Edit")]
    Task<TeamResult> MoveTeamNodeAsync(List<string> userRoles, string userName, int teamId, int? newParentTeamId = null, int? newMaintenanceGroupId = null);

    [PEMS.Application.Common.PemsOperation("Team", "DeactivateTeamNode", "Team.Edit")]
    Task<TeamResult> DeactivateTeamNodeAsync(List<string> userRoles, string userName, int teamId);

    [PEMS.Application.Common.PemsOperation("Team", "CreatePosition", "Team.Edit")]
    Task<TeamResult> CreatePositionAsync(List<string> userRoles, string userName, string title, int sortOrder, int? teamId = null, string? description = null);

    [PEMS.Application.Common.PemsOperation("Team", "UpdatePosition", "Team.Edit")]
    Task<TeamResult> UpdatePositionAsync(List<string> userRoles, string userName, int positionId, string title, int sortOrder, string? description = null);

    [PEMS.Application.Common.PemsOperation("Team", "MovePosition", "Team.Edit")]
    Task<TeamResult> MovePositionAsync(List<string> userRoles, string userName, int positionId, int? newTeamId = null);

    [PEMS.Application.Common.PemsOperation("Team", "DeactivatePosition", "Team.Edit")]
    Task<TeamResult> DeactivatePositionAsync(List<string> userRoles, string userName, int positionId, string? notes = null);

    [PEMS.Application.Common.PemsOperation("Team", "AssignEmployee", "Team.Edit")]
    Task<TeamResult> AssignEmployeeAsync(List<string> userRoles, string userName, int positionId, int employeeId, string? notes = null);

    [PEMS.Application.Common.PemsOperation("Team", "RemoveAssignment", "Team.Edit")]
    Task<TeamResult> RemoveAssignmentAsync(List<string> userRoles, string userName, int positionId, string? notes = null);

    [PEMS.Application.Common.PemsOperation("Team", "MoveEmployee", "Team.Edit")]
    Task<TeamResult> MoveEmployeeAsync(List<string> userRoles, string userName, int fromPositionId, int toPositionId, string? notes = null);
}
