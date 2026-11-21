using PEMS.Application.Organization;

namespace PEMS.Web.Services;

public sealed class OrganizationPageService(IOrganizationHierarchyService hierarchy, IOrganizationValidationService validation, IOrganizationConsistencyService consistency) : IOrganizationPageService
{
    public async Task<List<OrganizationPositionRow>> PositionRowsAsync()
    {
        var rows = await hierarchy.GetPositionRowsAsync();
        var issues = await validation.ValidateAllAsync();
        foreach (var r in rows) r.IssueCount = issues.Count(i => i.PositionId == r.PositionId && i.Severity != "P3");
        return rows;
    }

    public async Task<List<OrganizationIssue>> IssuesAsync() => (await validation.ValidateAllAsync()).ToList();

    public Task<List<OrganizationBlockedRequest>> BlockedAsync() => hierarchy.GetBlockedRequestsAsync();

    public async Task<List<OrgReadinessRowDto>> ReadinessAsync()
    {
        var r = await hierarchy.GetReadinessAsync();
        return [new OrgReadinessRowDto { ActivePositions = r.ActivePositions, PositionsWithParent = r.PositionsWithParent, RootPositions = r.RootPositions, VacantPositions = r.VacantPositions, ActivePositionsWithoutDepartment = r.ActivePositionsWithoutDepartment,
            ActiveEmployees = r.ActiveEmployees, ActiveEmployeesWithPrimaryPosition = r.ActiveEmployeesWithPrimaryPosition, ActiveEmployeesWithoutPersonnelCode = r.ActiveEmployeesWithoutPersonnelCode, ActiveUsersWithoutPrimaryPosition = r.ActiveUsersWithoutPrimaryPosition }];
    }

    public async Task<List<EmployeeDepartmentConsistencyRow>> DepartmentConsistencyAsync() => (await consistency.GetDepartmentConsistencyAsync()).ToList();

    public async Task<List<LegacyPositionRow>> LegacyPositionsAsync() => (await consistency.GetLegacyPositionConflictsAsync()).ToList();

    public async Task<List<OrganizationChartNodeDto>> ChartRowsAsync() => (await hierarchy.GetChartAsync()).Nodes;

    public async Task<List<OrgOptionRowDto>> DepartmentOptionsAsync() => (await hierarchy.GetActiveDepartmentsAsync()).Select(d => new OrgOptionRowDto { Value = d.DepartmentId.ToString(), Label = d.Name }).ToList();

    public async Task<List<OrgOptionRowDto>> EmployeeOptionsAsync() => (await hierarchy.GetActiveEmployeesAsync()).Select(e => new OrgOptionRowDto { Value = e.EmployeeId.ToString(), Label = e.Label }).ToList();

    public async Task<List<OrgOptionRowDto>> PositionOptionsAsync() => (await hierarchy.GetPositionRowsAsync()).Where(p => p.IsActive).Select(p => new OrgOptionRowDto { Value = p.PositionId.ToString(), Label = (p.PositionCode is null ? "" : p.PositionCode + " - ") + p.Title }).ToList();

    public async Task<List<OrgPositionFormRowDto>> PositionOfAsync(int positionId)
    {
        var p = (await hierarchy.GetPositionRowsAsync()).FirstOrDefault(x => x.PositionId == positionId);
        return p == null ? [] : [new OrgPositionFormRowDto { Title = p.Title, PositionCode = p.PositionCode, DepartmentId = p.DepartmentId?.ToString(), Description = p.Description }];
    }

    public async Task<List<OrgHierarchyFormRowDto>> HierarchyOfAsync(int positionId)
    {
        var p = (await hierarchy.GetPositionRowsAsync()).FirstOrDefault(x => x.PositionId == positionId);
        return p == null ? [] : [new OrgHierarchyFormRowDto { PositionCode = p.PositionCode, ParentPositionId = p.ParentPositionId?.ToString() }];
    }

    public async Task<List<OrgAssignmentFormRowDto>> AssignmentOfAsync(int positionId)
    {
        var p = (await hierarchy.GetPositionRowsAsync()).FirstOrDefault(x => x.PositionId == positionId && x.AssignmentId != null);
        return p == null ? [] : [new OrgAssignmentFormRowDto { IsPrimary = p.HolderIsPrimary, StartDate = (p.StartDate ?? DateTime.Today).ToString("yyyy-MM-dd"), EndDate = p.EndDate?.ToString("yyyy-MM-dd") }];
    }

    public async Task<OrgOpResult> UpdateAssignmentOfPositionAsync(int positionId, bool isPrimary, DateTime startDate, DateTime? endDate)
    {
        var p = (await hierarchy.GetPositionRowsAsync()).FirstOrDefault(x => x.PositionId == positionId && x.AssignmentId != null);
        return p?.AssignmentId is int id ? await hierarchy.UpdateAssignmentAsync(id, isPrimary, startDate, endDate) : OrgOpResult.Fail("Org_Err_Generic");
    }

    public async Task<OrgOpResult> EndAssignmentOfPositionAsync(int positionId, string? notes)
    {
        var p = (await hierarchy.GetPositionRowsAsync()).FirstOrDefault(x => x.PositionId == positionId && x.AssignmentId != null);
        return p?.AssignmentId is int id ? await hierarchy.EndAssignmentAsync(id, notes) : OrgOpResult.Fail("Org_Err_Generic");
    }
}
