namespace PEMS.Application.Interfaces;

/// <summary>One employee of the list; Status is Active or Inactive.</summary>
public sealed record EmployeeRow(int EmployeeId, string PersonnelCode, string FullName, string Position, string Department, string Discipline, string PersonnelType, string Vendor, string Email, string Phone, string Status);

/// <summary>The profile page header and employment details (one row; a value that is not known is empty).</summary>
public sealed record EmployeeProfileRow(string FullName, string Status, string PersonnelCode, string Position, string OrganizationalPosition, string Department, string Discipline, string TeamSupervisor, string DirectManager,
    string HireDate, string PersonnelType, string Location, string Shift, string Email, string Phone, string HomeAddress, string CurrentTeam, string TeamRole, string MemberSince, string AccountUser, string AccountStatus, string AccountRoles);

public sealed record EmployeeSkillRow(int EmployeeSkillId, string Skill, string Proficiency, string CertifiedDate);

public sealed record EmployeeQualificationRow(int EmployeeQualificationId, string Qualification, string Issuer, string IssueDate, string ExpiryDate, string Expiry);

public sealed record EmployeeCertificationRow(int EmployeeCertificationId, string Certification, string CertificateNumber, string Issuer, string IssueDate, string ExpiryDate, string Expiry);

public sealed record EmployeeLaborRateRow(string RateType, string Rate, string Currency, string EffectiveFrom, string EffectiveTo);

public sealed record EmployeeTeamRow(string Team, string Role, string Supervisor, string JoinedDate, string LeftDate, string Status);

/// <summary>A choice of an employee form (department, discipline, vendor, free position, skill, qualification, certification).</summary>
public sealed record EmployeeChoiceRow(string Value, string Label);

/// <summary>The answer of an employee write: <c>Error</c> is the refusal, or the message to show when <c>Success</c>; <c>SecretValue</c> (with the other Secret* texts) is a one-time password shown once.</summary>
public sealed record EmployeeResult(bool Success, string? Error, string? SecretValue = null, string? SecretTitle = null, string? SecretLabel = null, string? SecretNote = null, string? BackUrl = null, string? BackLabel = null);

/// <summary>The spreadsheet of all employees.</summary>
public sealed record EmployeeExportResult(bool Success, string? Error, byte[] Bytes, string FileName, string ContentType);

/// <summary>
/// The Employee pages as operations (SCRUM-687): the list, the profile with its skills, qualifications, certifications, labor rates and team history, and the writes of the controller (create with an optional
/// system account and a one-time password, the organizational position, activate/deactivate, skills, qualifications, certifications, labor rate, export). Limited to the roles of the old controller.
/// Editing the personal fields is the generic entity.update action with <c>ValidateEdit</c> as its check. The roles of a new account are assigned in Administration, Users (not from this form any more).
/// </summary>
public interface IEmployeeOperations
{
    [PEMS.Application.Common.PemsOperation("Employee", "List", Roles = "SystemAdmin,EngineeringManager")]
    Task<List<EmployeeRow>> ListAsync(string? search = null, int? departmentId = null, int? disciplineId = null);

    [PEMS.Application.Common.PemsOperation("Employee", "Profile", Roles = "SystemAdmin,EngineeringManager")]
    Task<List<EmployeeProfileRow>> ProfileAsync(int id);

    [PEMS.Application.Common.PemsOperation("Employee", "Skills", Roles = "SystemAdmin,EngineeringManager")]
    Task<List<EmployeeSkillRow>> SkillsAsync(int id);

    [PEMS.Application.Common.PemsOperation("Employee", "Qualifications", Roles = "SystemAdmin,EngineeringManager")]
    Task<List<EmployeeQualificationRow>> QualificationsAsync(int id);

    [PEMS.Application.Common.PemsOperation("Employee", "Certifications", Roles = "SystemAdmin,EngineeringManager")]
    Task<List<EmployeeCertificationRow>> CertificationsAsync(int id);

    [PEMS.Application.Common.PemsOperation("Employee", "LaborRates", Roles = "SystemAdmin,EngineeringManager")]
    Task<List<EmployeeLaborRateRow>> LaborRatesAsync(int id);

    [PEMS.Application.Common.PemsOperation("Employee", "TeamHistory", Roles = "SystemAdmin,EngineeringManager")]
    Task<List<EmployeeTeamRow>> TeamHistoryAsync(int id);

    [PEMS.Application.Common.PemsOperation("Employee", "Departments", Roles = "SystemAdmin,EngineeringManager")]
    Task<List<EmployeeChoiceRow>> DepartmentsAsync();

    [PEMS.Application.Common.PemsOperation("Employee", "Disciplines", Roles = "SystemAdmin,EngineeringManager")]
    Task<List<EmployeeChoiceRow>> DisciplinesAsync();

    [PEMS.Application.Common.PemsOperation("Employee", "Vendors", Roles = "SystemAdmin,EngineeringManager")]
    Task<List<EmployeeChoiceRow>> VendorsAsync();

    [PEMS.Application.Common.PemsOperation("Employee", "VacantPositions", Roles = "SystemAdmin,EngineeringManager")]
    Task<List<EmployeeChoiceRow>> VacantPositionsAsync();

    [PEMS.Application.Common.PemsOperation("Employee", "SkillChoices", Roles = "SystemAdmin,EngineeringManager")]
    Task<List<EmployeeChoiceRow>> SkillChoicesAsync();

    [PEMS.Application.Common.PemsOperation("Employee", "QualificationChoices", Roles = "SystemAdmin,EngineeringManager")]
    Task<List<EmployeeChoiceRow>> QualificationChoicesAsync();

    [PEMS.Application.Common.PemsOperation("Employee", "CertificationChoices", Roles = "SystemAdmin,EngineeringManager")]
    Task<List<EmployeeChoiceRow>> CertificationChoicesAsync();

    [PEMS.Application.Common.PemsOperation("Employee", "Create", Roles = "SystemAdmin,EngineeringManager")]
    Task<EmployeeResult> CreateAsync(List<string> userRoles, string firstName, string lastName, int departmentId, int disciplineId, string? personnelCode = null, string? email = null, string? phone = null, string? personnelType = null,
        int? vendorId = null, DateTime? hireDate = null, string? location = null, string? shift = null, string? homeAddress = null, int? positionId = null, bool createUser = false, string? userName = null);

    [PEMS.Application.Common.PemsOperation("Employee", "ValidateEdit", Roles = "SystemAdmin,EngineeringManager")]
    Task<EmployeeResult> ValidateEditAsync(int id, string firstName, string lastName, int departmentId, int disciplineId, string? personnelType = null, int? vendorId = null);

    [PEMS.Application.Common.PemsOperation("Employee", "SetPosition", Roles = "SystemAdmin,EngineeringManager")]
    Task<EmployeeResult> SetPositionAsync(List<string> userRoles, int employeeId, int? positionId = null);

    [PEMS.Application.Common.PemsOperation("Employee", "ToggleActive", Roles = "SystemAdmin,EngineeringManager")]
    Task<EmployeeResult> ToggleActiveAsync(int employeeId);

    [PEMS.Application.Common.PemsOperation("Employee", "AddSkill", Roles = "SystemAdmin,EngineeringManager")]
    Task<EmployeeResult> AddSkillAsync(int employeeId, int skillId, string proficiencyLevel, DateTime? certifiedDate = null);

    [PEMS.Application.Common.PemsOperation("Employee", "RemoveSkill", Roles = "SystemAdmin,EngineeringManager")]
    Task<EmployeeResult> RemoveSkillAsync(int employeeSkillId);

    [PEMS.Application.Common.PemsOperation("Employee", "AddQualification", Roles = "SystemAdmin,EngineeringManager")]
    Task<EmployeeResult> AddQualificationAsync(int employeeId, int qualificationId, DateTime issueDate, string? issuer = null, DateTime? expiryDate = null);

    [PEMS.Application.Common.PemsOperation("Employee", "RemoveQualification", Roles = "SystemAdmin,EngineeringManager")]
    Task<EmployeeResult> RemoveQualificationAsync(int employeeQualificationId);

    [PEMS.Application.Common.PemsOperation("Employee", "AddCertification", Roles = "SystemAdmin,EngineeringManager")]
    Task<EmployeeResult> AddCertificationAsync(int employeeId, int certificationId, DateTime issueDate, string? certificateNumber = null, string? issuer = null, DateTime? expiryDate = null);

    [PEMS.Application.Common.PemsOperation("Employee", "RemoveCertification", Roles = "SystemAdmin,EngineeringManager")]
    Task<EmployeeResult> RemoveCertificationAsync(int employeeCertificationId);

    [PEMS.Application.Common.PemsOperation("Employee", "SetLaborRate", Roles = "SystemAdmin,EngineeringManager")]
    Task<EmployeeResult> SetLaborRateAsync(int employeeId, string rateType, decimal rate, string currency, DateTime effectiveFrom);

    [PEMS.Application.Common.PemsOperation("Employee", "Export", Roles = "SystemAdmin,EngineeringManager")]
    Task<EmployeeExportResult> ExportAsync();
}
