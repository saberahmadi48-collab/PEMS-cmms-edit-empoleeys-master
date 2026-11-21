namespace PEMS.Application.Interfaces;

/// <summary>One effective permission of a role or a user (the echo of the asked role or user id lets a grid filter on it).</summary>
public class EffectivePermissionRowDto
{
    public string? RoleName { get; set; }
    public int? UserId { get; set; }
    public string ResourceName { get; set; } = string.Empty;
    public string ActionName { get; set; } = string.Empty;
    public bool Allowed { get; set; }
    public bool IsDestructive { get; set; }
    public string? Detail { get; set; }
}

public class RoleAssignmentReportRowDto
{
    public int UserId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public string ScopeType { get; set; } = string.Empty;
    public string? ScopeValue { get; set; }
    public string AssignmentType { get; set; } = string.Empty;
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
}

public class AccessSimulationRowDto
{
    public int UserId { get; set; }
    public string ResourceName { get; set; } = string.Empty;
    public string ActionName { get; set; } = string.Empty;
    public bool Allowed { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public string? MatchedRoleName { get; set; }
    public string? MatchedScopeType { get; set; }
    public string? MatchedScopeValue { get; set; }
}

/// <summary>SCRUM-631: the diagnostic reports of the Admin Authorization pages (effective permissions of a role or a user, the permission simulator) as read operations, so the pages are definitions. Administrators only.</summary>
public interface IAuthorizationReportService
{
    [PEMS.Application.Common.PemsOperation("Authorization", "EffectiveForRole", Roles = "SystemAdmin")]
    Task<List<EffectivePermissionRowDto>> EffectiveForRoleAsync(string roleName);

    [PEMS.Application.Common.PemsOperation("Authorization", "EffectiveForUser", Roles = "SystemAdmin")]
    Task<List<EffectivePermissionRowDto>> EffectiveForUserAsync(int userId);

    [PEMS.Application.Common.PemsOperation("Authorization", "AssignmentsOfUser", Roles = "SystemAdmin")]
    Task<List<RoleAssignmentReportRowDto>> AssignmentsOfUserAsync(int userId);

    /// <summary>The real engine (Role, Scope, SoD) asked once; it writes one audit row, as the old simulator did.</summary>
    [PEMS.Application.Common.PemsOperation("Authorization", "Simulate", Roles = "SystemAdmin")]
    Task<List<AccessSimulationRowDto>> SimulateAsync(int userId, string resourceName, string actionName, string? resourceId = null);
}
