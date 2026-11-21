namespace PEMS.Application.Interfaces;

public class MatrixModuleRowDto { public string Module { get; set; } = string.Empty; }

/// <summary>The cells a role has, as "Module|Action" values joined by commas (the value of a checkbox-matrix form field).</summary>
public class MatrixCellsRowDto
{
    public string RoleName { get; set; } = string.Empty;
    public string Cells { get; set; } = string.Empty;
}

public sealed record MatrixSaveResult(bool Success, string? Error);

/// <summary>SCRUM-641: the permission matrix as operations for a generated page: the module rows, the cells of a role, one save of all cells of a role. The rules stay in IPermissionService.</summary>
public interface IPermissionMatrixPageService
{
    [PEMS.Application.Common.PemsOperation("PermissionMatrix", "Modules", Roles = "SystemAdmin")]
    Task<List<MatrixModuleRowDto>> ModulesAsync();

    [PEMS.Application.Common.PemsOperation("PermissionMatrix", "CellsOfRole", Roles = "SystemAdmin")]
    Task<List<MatrixCellsRowDto>> CellsOfRoleAsync(string roleName);

    [PEMS.Application.Common.PemsOperation("PermissionMatrix", "SetRole", Roles = "SystemAdmin")]
    Task<MatrixSaveResult> SetRoleAsync(string roleName, string? cells);
}
