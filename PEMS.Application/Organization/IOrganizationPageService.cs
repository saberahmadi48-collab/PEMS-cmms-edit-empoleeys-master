namespace PEMS.Application.Organization;

public class OrgOptionRowDto { public string Value { get; set; } = string.Empty; public string Label { get; set; } = string.Empty; }

public class OrgPositionFormRowDto { public string Title { get; set; } = string.Empty; public string? PositionCode { get; set; } public string? DepartmentId { get; set; } public string? Description { get; set; } }

public class OrgHierarchyFormRowDto { public string? PositionCode { get; set; } public string? ParentPositionId { get; set; } }

public class OrgAssignmentFormRowDto { public bool IsPrimary { get; set; } public string StartDate { get; set; } = string.Empty; public string? EndDate { get; set; } }

public class OrgReadinessRowDto
{
    public string Id { get; set; } = "organization";
    public int ActivePositions { get; set; } public int PositionsWithParent { get; set; } public int RootPositions { get; set; } public int VacantPositions { get; set; } public int ActivePositionsWithoutDepartment { get; set; }
    public int ActiveEmployees { get; set; } public int ActiveEmployeesWithPrimaryPosition { get; set; } public int ActiveEmployeesWithoutPersonnelCode { get; set; } public int ActiveUsersWithoutPrimaryPosition { get; set; }
}

/// <summary>SCRUM-639: the read side of the Admin Organization pages as operations (positions with their issue counts, the validation issues, the blocked requests, the consistency reports, the chart rows, options and the values of the edit forms). The rules stay in the organization services.</summary>
public interface IOrganizationPageService
{
    [PEMS.Application.Common.PemsOperation("Organization", "PositionRows", Roles = "SystemAdmin")]
    Task<List<OrganizationPositionRow>> PositionRowsAsync();

    [PEMS.Application.Common.PemsOperation("Organization", "Issues", Roles = "SystemAdmin")]
    Task<List<OrganizationIssue>> IssuesAsync();

    [PEMS.Application.Common.PemsOperation("Organization", "Blocked", Roles = "SystemAdmin")]
    Task<List<OrganizationBlockedRequest>> BlockedAsync();

    [PEMS.Application.Common.PemsOperation("Organization", "Readiness", Roles = "SystemAdmin")]
    Task<List<OrgReadinessRowDto>> ReadinessAsync();

    [PEMS.Application.Common.PemsOperation("Organization", "DepartmentConsistency", Roles = "SystemAdmin")]
    Task<List<EmployeeDepartmentConsistencyRow>> DepartmentConsistencyAsync();

    [PEMS.Application.Common.PemsOperation("Organization", "LegacyPositions", Roles = "SystemAdmin")]
    Task<List<LegacyPositionRow>> LegacyPositionsAsync();

    [PEMS.Application.Common.PemsOperation("Organization", "ChartRows", Roles = "SystemAdmin")]
    Task<List<OrganizationChartNodeDto>> ChartRowsAsync();

    [PEMS.Application.Common.PemsOperation("Organization", "DepartmentOptions", Roles = "SystemAdmin")]
    Task<List<OrgOptionRowDto>> DepartmentOptionsAsync();

    [PEMS.Application.Common.PemsOperation("Organization", "EmployeeOptions", Roles = "SystemAdmin")]
    Task<List<OrgOptionRowDto>> EmployeeOptionsAsync();

    [PEMS.Application.Common.PemsOperation("Organization", "PositionOptions", Roles = "SystemAdmin")]
    Task<List<OrgOptionRowDto>> PositionOptionsAsync();

    [PEMS.Application.Common.PemsOperation("Organization", "PositionOf", Roles = "SystemAdmin")]
    Task<List<OrgPositionFormRowDto>> PositionOfAsync(int positionId);

    [PEMS.Application.Common.PemsOperation("Organization", "HierarchyOf", Roles = "SystemAdmin")]
    Task<List<OrgHierarchyFormRowDto>> HierarchyOfAsync(int positionId);

    [PEMS.Application.Common.PemsOperation("Organization", "AssignmentOf", Roles = "SystemAdmin")]
    Task<List<OrgAssignmentFormRowDto>> AssignmentOfAsync(int positionId);

    /// <summary>The grid row action link only carries the id of the row (the position), so the assignment is addressed by the position it fills.</summary>
    [PEMS.Application.Common.PemsOperation("Organization", "UpdateAssignmentOfPosition", Roles = "SystemAdmin")]
    Task<OrgOpResult> UpdateAssignmentOfPositionAsync(int positionId, bool isPrimary, DateTime startDate, DateTime? endDate);

    [PEMS.Application.Common.PemsOperation("Organization", "EndAssignmentOfPosition", Roles = "SystemAdmin")]
    Task<OrgOpResult> EndAssignmentOfPositionAsync(int positionId, string? notes);
}
