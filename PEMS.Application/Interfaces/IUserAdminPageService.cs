namespace PEMS.Application.Interfaces;

/// <summary>One account in the Users list.</summary>
public class UserRowDto
{
    public int Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Roles { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool LockedOut { get; set; }
    public bool IsAdmin { get; set; }
    public DateTime? LastLoginDate { get; set; }
}

/// <summary>The values of the edit form of one account; Roles are the ticked cells ("Role|Member", comma separated). The password is never read back.</summary>
public class UserFormRowDto
{
    public string Id { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string Roles { get; set; } = string.Empty;
    public string? ExistingEmployeeId { get; set; }
}

public class UserOptionRowDto { public string Value { get; set; } = string.Empty; public string Label { get; set; } = string.Empty; }

/// <summary>One simple fact of the details page (label key, value) so a record view shows an account and its employee link.</summary>
public class UserSummaryRowDto
{
    public int Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool LockedOut { get; set; }
    public DateTime? LastLoginDate { get; set; }
    public string Roles { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Discipline { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public bool HasLinkedEmployee { get; set; }
}

/// <summary>Role-by-role view of the SoD exposure of an account and its recent authorization decisions.</summary>
public class UserSodRowDto { public string PermissionA { get; set; } = string.Empty; public string PermissionB { get; set; } = string.Empty; public string Policy { get; set; } = string.Empty; public bool UserHoldsBoth { get; set; } }

public class UserAuditRowDto
{
    public DateTime Timestamp { get; set; }
    public string ResourceName { get; set; } = string.Empty;
    public string ActionName { get; set; } = string.Empty;
    public string? ResourceId { get; set; }
    public string Decision { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public int UserId { get; set; }
}

/// <summary>Create (Id = 0) and edit input of an account; every id is optional text turned into a number by the engine. Employee: link an existing one, or give a department (and discipline, position) to create one.</summary>
public sealed record UserSaveInput(int Id, string UserName, string Email, string FullName, string? Password, bool IsActive, string? Roles, int? ExistingEmployeeId, int? DepartmentId, int? DisciplineId, int? PositionId);

/// <summary>Success, or Error = a resource key; Detail fills the {0} of the message (an Identity error, a list).</summary>
public sealed record UserSaveResult(bool Success, string? Error, string? Detail = null);

/// <summary>The one-time temporary password of an administrator reset: SecretValue is shown once by the generic secret page; all Secret* texts are resource keys.</summary>
public sealed record UserResetResult(bool Success, string? Error, string? SecretValue = null, string? SecretTitle = null, string? SecretLabel = null, string? SecretNote = null, string? BackUrl = null, string? BackLabel = null);

/// <summary>An exported file (the engine downloads a result with Bytes) or a refusal.</summary>
public sealed record UserExportResult(bool Success, string? Error, byte[]? Bytes = null, string? FileName = null, string? ContentType = null);

/// <summary>SCRUM-650: the Admin Users pages as operations. The rules stay here (Identity validation before any side effect, employee creation with rollback, last active administrator, role diff and synchronisation, password reset with recovery audit, administrator reset); the pages are definitions.</summary>
public interface IUserAdminPageService
{
    [PEMS.Application.Common.PemsOperation("Users", "Rows", Roles = "SystemAdmin")]
    Task<List<UserRowDto>> RowsAsync();

    [PEMS.Application.Common.PemsOperation("Users", "UserOf", Roles = "SystemAdmin")]
    Task<List<UserFormRowDto>> UserOfAsync(int id);

    [PEMS.Application.Common.PemsOperation("Users", "Summary", Roles = "SystemAdmin")]
    Task<List<UserSummaryRowDto>> SummaryAsync(int id);

    [PEMS.Application.Common.PemsOperation("Users", "RoleOptions", Roles = "SystemAdmin")]
    Task<List<UserOptionRowDto>> RoleOptionsAsync();

    [PEMS.Application.Common.PemsOperation("Users", "Create", Roles = "SystemAdmin")]
    Task<UserSaveResult> CreateAsync(UserSaveInput input);

    [PEMS.Application.Common.PemsOperation("Users", "Update", Roles = "SystemAdmin")]
    Task<UserSaveResult> UpdateAsync(UserSaveInput input, int actorId, string? actorName, string? ip);

    [PEMS.Application.Common.PemsOperation("Users", "ToggleActive", Roles = "SystemAdmin")]
    Task<UserSaveResult> ToggleActiveAsync(int id);

    [PEMS.Application.Common.PemsOperation("Users", "ResetAdmin", Roles = "SystemAdmin")]
    Task<UserResetResult> ResetAdminAsync(int id, int actorId, string? ip);

    [PEMS.Application.Common.PemsOperation("Users", "Export", Roles = "SystemAdmin")]
    Task<UserExportResult> ExportAsync(bool onlyActive);

    [PEMS.Application.Common.PemsOperation("Users", "SodRows", Roles = "SystemAdmin")]
    Task<List<UserSodRowDto>> SodRowsAsync(int id);

    [PEMS.Application.Common.PemsOperation("Users", "AuditRows", Roles = "SystemAdmin")]
    Task<List<UserAuditRowDto>> AuditRowsAsync(int userId);
}
