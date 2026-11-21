using System.Globalization;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Application.Organization;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Web.Helpers;

namespace PEMS.Web.Services;

/// <summary>SCRUM-687: what EmployeeController read and did, as operations over the same services and rules (duplicate personnel code, personnel type and vendor, active department and discipline, the one-time password of a new account, the position only for SystemAdmin).</summary>
public sealed class EmployeeOperations(PEMSDbContext context, UserManager<ApplicationUser> users, IEmployeeLaborRateService laborRates, IEmployeeSkillService skills, IUserRoleAssignmentSynchronizer roleSync,
    IOrganizationHierarchyService hierarchy) : IEmployeeOperations
{
    private static readonly string[] PersonnelTypes = ["Internal", "Contractor", "External", "Temporary"];
    private const int MaxRows = 5000;

    private static string Date(DateTime? value) => value.HasValue ? value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "";
    private static string Dash(string? value) => string.IsNullOrWhiteSpace(value) ? "" : value;

    private static string ExpiryStatus(DateTime? expiry)
        => !expiry.HasValue ? "Unknown" : expiry.Value.Date < DateTime.Today ? "Overdue" : expiry.Value.Date <= DateTime.Today.AddDays(30) ? "Due soon" : "OK";

    public async Task<List<EmployeeRow>> ListAsync(string? search = null, int? departmentId = null, int? disciplineId = null)
    {
        var query = context.Employees.AsNoTracking().AsQueryable();
        if (departmentId.HasValue) query = query.Where(x => x.DepartmentId == departmentId.Value);
        if (disciplineId.HasValue) query = query.Where(x => x.DisciplineId == disciplineId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(x => x.FirstName.ToLower().Contains(term) || x.LastName.ToLower().Contains(term) || (x.PersonnelCode != null && x.PersonnelCode.ToLower().Contains(term)) || (x.Email != null && x.Email.ToLower().Contains(term)));
        }
        var rows = await query.OrderBy(x => x.FirstName).ThenBy(x => x.LastName).ThenBy(x => x.EmployeeId).Take(MaxRows)
            .Select(x => new
            {
                x.EmployeeId, x.PersonnelCode, x.FirstName, x.LastName, x.Position, Department = x.Department != null ? x.Department.Name : "", Discipline = x.Discipline != null ? x.Discipline.Name : "",
                x.PersonnelType, Vendor = x.VendorRef != null ? x.VendorRef.Name : "", x.Email, x.Phone, x.IsActive
            }).ToListAsync();
        return rows.Select(x => new EmployeeRow(x.EmployeeId, x.PersonnelCode ?? "", x.FirstName + " " + x.LastName, x.Position ?? "", x.Department, x.Discipline, x.PersonnelType, x.Vendor, x.Email ?? "", x.Phone ?? "", x.IsActive ? "Active" : "Inactive")).ToList();
    }

    public async Task<List<EmployeeProfileRow>> ProfileAsync(int id)
    {
        var e = await context.Employees.AsNoTracking().Include(x => x.Department).Include(x => x.Discipline).Include(x => x.VendorRef).FirstOrDefaultAsync(x => x.EmployeeId == id);
        if (e is null) return [];
        var org = await new PEMS.Persistence.Services.OrganizationResolver(context).GetContextAsync(id);
        var direct = await hierarchy.GetDirectManagerAsync(id);
        var membership = await context.TeamMembers.AsNoTracking().Include(x => x.Team!).ThenInclude(t => t.SupervisorEmployee).Include(x => x.Role)
            .Where(x => x.EmployeeId == id && x.IsActive).OrderByDescending(x => x.JoinedDate).FirstOrDefaultAsync();
        var account = await context.Users.AsNoTracking().Where(u => u.EmployeeId == id).Select(u => new { u.Id, u.UserName, u.IsActive }).FirstOrDefaultAsync();
        var roles = account is null ? [] : await (from ur in context.UserRoles join r in context.Roles on ur.RoleId equals r.Id where ur.UserId == account.Id orderby r.Name select r.Name!).ToListAsync();
        var supervisor = membership?.Team?.SupervisorEmployee;
        return [new EmployeeProfileRow(e.FirstName + " " + e.LastName, e.IsActive ? "Active" : "Inactive", e.PersonnelCode ?? "", Dash(e.Position), Dash(org?.PrimaryPositionTitle), Dash(e.Department?.Name), Dash(e.Discipline?.Name),
            supervisor is null ? "" : supervisor.FirstName + " " + supervisor.LastName, direct.IssueCode is null ? string.Join(", ", direct.Managers.Select(m => m.Name)) : "", Date(e.HireDate),
            e.PersonnelType + (e.PersonnelType != "Internal" && e.VendorRef != null ? " (" + e.VendorRef.Name + ")" : ""), Dash(e.Location), Dash(e.Shift), Dash(e.Email), Dash(e.Phone), Dash(e.HomeAddress),
            Dash(membership?.Team?.Name), Dash(membership?.Role?.Name), membership is null ? "" : Date(membership.JoinedDate), account?.UserName ?? "", account is null ? "" : account.IsActive ? "Active" : "Inactive", string.Join(", ", roles))];
    }

    public async Task<List<EmployeeSkillRow>> SkillsAsync(int id)
        => (await context.EmployeeSkills.AsNoTracking().Include(x => x.Skill).Where(x => x.EmployeeId == id && x.IsActive).OrderBy(x => x.Skill!.Name).ToListAsync())
            .Select(x => new EmployeeSkillRow(x.EmployeeSkillId, x.Skill?.Name ?? "", x.ProficiencyLevel, Date(x.CertifiedDate))).ToList();

    public async Task<List<EmployeeQualificationRow>> QualificationsAsync(int id)
        => (await context.EmployeeQualifications.AsNoTracking().Include(x => x.Qualification).Where(x => x.EmployeeId == id && x.IsActive).OrderByDescending(x => x.IssueDate).ToListAsync())
            .Select(x => new EmployeeQualificationRow(x.EmployeeQualificationId, x.Qualification?.Name ?? "", x.Issuer ?? "", Date(x.IssueDate), Date(x.ExpiryDate), ExpiryStatus(x.ExpiryDate))).ToList();

    public async Task<List<EmployeeCertificationRow>> CertificationsAsync(int id)
        => (await context.EmployeeCertifications.AsNoTracking().Include(x => x.Certification).Where(x => x.EmployeeId == id && x.IsActive).OrderByDescending(x => x.IssueDate).ToListAsync())
            .Select(x => new EmployeeCertificationRow(x.EmployeeCertificationId, x.Certification?.Name ?? "", x.CertificateNumber ?? "", x.Issuer ?? "", Date(x.IssueDate), Date(x.ExpiryDate), ExpiryStatus(x.ExpiryDate))).ToList();

    public async Task<List<EmployeeLaborRateRow>> LaborRatesAsync(int id)
        => (await laborRates.GetHistoryAsync(id)).Select(x => new EmployeeLaborRateRow(x.RateType, x.Rate.ToString("N2", CultureInfo.InvariantCulture), x.Currency, Date(x.EffectiveFrom), Date(x.EffectiveTo))).ToList();

    public async Task<List<EmployeeTeamRow>> TeamHistoryAsync(int id)
        => (await context.TeamMembers.AsNoTracking().Include(x => x.Team!).ThenInclude(t => t.SupervisorEmployee).Include(x => x.Role).Where(x => x.EmployeeId == id).OrderByDescending(x => x.JoinedDate).ToListAsync())
            .Select(x => new EmployeeTeamRow(x.Team?.Name ?? "", x.Role?.Name ?? "", x.Team?.SupervisorEmployee is { } s ? s.FirstName + " " + s.LastName : "", Date(x.JoinedDate), Date(x.LeftDate), x.IsActive ? "Active" : "Left")).ToList();

    private static string Id(int value) => value.ToString(CultureInfo.InvariantCulture);

    public async Task<List<EmployeeChoiceRow>> DepartmentsAsync()
        => (await context.Departments.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new { x.DepartmentId, x.Name }).ToListAsync()).Select(x => new EmployeeChoiceRow(Id(x.DepartmentId), x.Name)).ToList();

    public async Task<List<EmployeeChoiceRow>> DisciplinesAsync()
        => (await context.Disciplines.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new { x.DisciplineId, x.Name }).ToListAsync()).Select(x => new EmployeeChoiceRow(Id(x.DisciplineId), x.Name)).ToList();

    public async Task<List<EmployeeChoiceRow>> VendorsAsync()
        => (await context.Vendors.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new { x.VendorId, x.Name }).ToListAsync()).Select(x => new EmployeeChoiceRow(Id(x.VendorId), x.Name)).ToList();

    public async Task<List<EmployeeChoiceRow>> VacantPositionsAsync()
        => (await hierarchy.GetVacantPositionsAsync()).Select(x => new EmployeeChoiceRow(Id(x.PositionId), x.Label)).ToList();

    public async Task<List<EmployeeChoiceRow>> SkillChoicesAsync()
        => (await context.Skills.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new { x.SkillId, x.Name }).ToListAsync()).Select(x => new EmployeeChoiceRow(Id(x.SkillId), x.Name)).ToList();

    public async Task<List<EmployeeChoiceRow>> QualificationChoicesAsync()
        => (await context.Qualifications.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new { x.QualificationId, x.Name }).ToListAsync()).Select(x => new EmployeeChoiceRow(Id(x.QualificationId), x.Name)).ToList();

    public async Task<List<EmployeeChoiceRow>> CertificationChoicesAsync()
        => (await context.Certifications.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new { x.CertificationId, x.Name }).ToListAsync()).Select(x => new EmployeeChoiceRow(Id(x.CertificationId), x.Name)).ToList();

    /// <summary>The checks shared by create and edit: the personnel type, the vendor of non-internal personnel, and an active department and discipline (unchanged ones of the current employee are accepted).</summary>
    private async Task<string?> CheckAsync(string? personnelType, int? vendorId, int departmentId, int disciplineId, Employee? current)
    {
        var type = string.IsNullOrWhiteSpace(personnelType) ? "Internal" : personnelType;
        if (!PersonnelTypes.Contains(type, StringComparer.Ordinal)) return "نوع پرسنل نامعتبره.";
        if (type != "Internal" && !vendorId.HasValue) return "برای پرسنل غیر-Internal، انتخاب Vendor الزامی است.";
        if (type == "Internal" && vendorId.HasValue) return "برای پرسنل Internal نباید Vendor انتخاب شود؛ آن را خالی کنید.";
        if (!await context.Departments.AnyAsync(d => d.DepartmentId == departmentId && (d.IsActive || (current != null && current.DepartmentId == departmentId)))) return "دپارتمان نامعتبر یا غیرفعال است.";
        if (!await context.Disciplines.AnyAsync(d => d.DisciplineId == disciplineId && (d.IsActive || (current != null && current.DisciplineId == disciplineId)))) return "دیسیپلین نامعتبر یا غیرفعال است (مقدار 0/ناموجود پذیرفته نمی‌شود).";
        return null;
    }

    public async Task<EmployeeResult> ValidateEditAsync(int id, string firstName, string lastName, int departmentId, int disciplineId, string? personnelType = null, int? vendorId = null)
    {
        var employee = await context.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.EmployeeId == id);
        if (employee is null) return new(false, "کارمند پیدا نشد.");
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName)) return new(false, "First and last name are required");
        var error = await CheckAsync(personnelType, vendorId, departmentId, disciplineId, employee);
        return error is null ? new(true, null) : new(false, error);
    }

    public async Task<EmployeeResult> CreateAsync(List<string> userRoles, string firstName, string lastName, int departmentId, int disciplineId, string? personnelCode = null, string? email = null, string? phone = null, string? personnelType = null,
        int? vendorId = null, DateTime? hireDate = null, string? location = null, string? shift = null, string? homeAddress = null, int? positionId = null, bool createUser = false, string? userName = null)
    {
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName)) return new(false, "First and last name are required");
        var error = await CheckAsync(personnelType, vendorId, departmentId, disciplineId, null);
        if (error != null) return new(false, error);
        var code = personnelCode?.Trim() ?? "";
        if (code != "" && await context.Employees.AnyAsync(x => x.PersonnelCode == code)) return new(false, $"کد پرسنلی «{code}» قبلاً برای یک کارمند دیگر ثبت شده است.");
        if (createUser)
        {
            if (string.IsNullOrWhiteSpace(userName)) return new(false, "برای ساخت یوزر، نام کاربری الزامی است.");
            if (await users.FindByNameAsync(userName.Trim()) != null) return new(false, $"نام کاربری «{userName}» قبلاً استفاده شده است.");
        }

        var type = string.IsNullOrWhiteSpace(personnelType) ? "Internal" : personnelType;
        var employee = new Employee
        {
            PersonnelCode = code, FirstName = firstName.Trim(), LastName = lastName.Trim(), Email = email, Phone = phone, DepartmentId = departmentId, DisciplineId = disciplineId, PersonnelType = type,
            VendorId = type == "Internal" ? null : vendorId, IsActive = true, CreatedDate = DateTime.Now, HireDate = hireDate, Location = location, Shift = shift, HomeAddress = homeAddress
        };
        await context.Employees.AddAsync(employee);
        await context.SaveChangesAsync();

        var warning = "";
        if (positionId.HasValue) warning = await ApplyPositionAsync(userRoles, employee, positionId);

        if (!createUser || string.IsNullOrWhiteSpace(userName)) return new(true, "Employee added" + (warning == "" ? "" : " " + warning));

        var account = new ApplicationUser { UserName = userName.Trim(), Email = email, EmailConfirmed = true, FullName = $"{employee.FirstName} {employee.LastName}", IsActive = true, EmployeeId = employee.EmployeeId, MustChangePassword = true, CreatedDate = DateTime.Now };
        var password = PEMS.Shared.Security.TemporaryPasswordGenerator.Generate();   // SCRUM-17: a random one-time password per user, shown once, never a fixed default
        var created = await users.CreateAsync(account, password);
        if (!created.Succeeded) return new(false, "کارمند ساخته شد ولی ساخت یوزر ناموفق بود: " + string.Join(", ", created.Errors.Select(x => x.Description)));
        await roleSync.SyncAsync(account.Id);
        return new(true, $"کارمند و یوزر «{userName}» ساخته شد. نقش‌ها را در مدیریت > کاربران تخصیص دهید." + (warning == "" ? "" : " " + warning), password,
            "Employee and account created", "One-time password", "This password is shown only now and must be changed at the first sign-in.", "/p/employees", "Back to employees");
    }

    /// <summary>The organizational position (only SystemAdmin); a failure is a warning text, the employee stays as it is.</summary>
    private async Task<string> ApplyPositionAsync(List<string> roles, Employee employee, int? positionId)
    {
        if (!roles.Contains(SystemRoles.SystemAdmin)) return "تغییر سمت سازمانی فقط برای SystemAdmin مجاز است.";
        var result = await hierarchy.SetPrimaryPositionAsync(employee.EmployeeId, positionId);
        if (!result.Success) return "سمت سازمانی تخصیص نیافت: " + (result.Detail ?? result.ErrorKey ?? "");
        if (positionId.HasValue)
        {
            var title = await context.Positions.AsNoTracking().Where(p => p.PositionId == positionId.Value).Select(p => p.Title).FirstOrDefaultAsync();
            if (title != null && employee.Position != title) { employee.Position = title; await context.SaveChangesAsync(); }
        }
        return "";
    }

    public async Task<EmployeeResult> SetPositionAsync(List<string> userRoles, int employeeId, int? positionId = null)
    {
        var employee = await context.Employees.FirstOrDefaultAsync(x => x.EmployeeId == employeeId);
        if (employee is null) return new(false, "کارمند پیدا نشد.");
        var warning = await ApplyPositionAsync(userRoles, employee, positionId);
        return warning == "" ? new(true, "Position updated") : new(false, warning);
    }

    public async Task<EmployeeResult> ToggleActiveAsync(int employeeId)
    {
        var employee = await context.Employees.FirstOrDefaultAsync(x => x.EmployeeId == employeeId);
        if (employee is null) return new(false, "کارمند پیدا نشد.");
        employee.IsActive = !employee.IsActive;
        await context.SaveChangesAsync();
        return new(true, employee.IsActive ? "Employee reactivated" : "Employee deactivated");
    }

    public async Task<EmployeeResult> AddSkillAsync(int employeeId, int skillId, string proficiencyLevel, DateTime? certifiedDate = null)
    {
        try { await skills.AddOrReactivateAsync(employeeId, skillId, proficiencyLevel, certifiedDate); return new(true, "مهارت اضافه شد."); }
        catch (Exception ex) { return new(false, ex.Message); }
    }

    public async Task<EmployeeResult> RemoveSkillAsync(int employeeSkillId)
    {
        try { await skills.RemoveAsync(employeeSkillId); return new(true, "مهارت حذف شد."); }
        catch (Exception ex) { return new(false, ex.Message); }
    }

    public async Task<EmployeeResult> AddQualificationAsync(int employeeId, int qualificationId, DateTime issueDate, string? issuer = null, DateTime? expiryDate = null)
    {
        await context.EmployeeQualifications.AddAsync(new EmployeeQualification { EmployeeId = employeeId, QualificationId = qualificationId, Issuer = issuer, IssueDate = issueDate, ExpiryDate = expiryDate, IsActive = true, CreatedDate = DateTime.Now });
        await context.SaveChangesAsync();
        return new(true, "صلاحیت ثبت شد.");
    }

    public async Task<EmployeeResult> RemoveQualificationAsync(int employeeQualificationId)
    {
        var row = await context.EmployeeQualifications.FirstOrDefaultAsync(x => x.EmployeeQualificationId == employeeQualificationId);
        if (row is null) return new(false, "مورد پیدا نشد.");
        row.IsActive = false;
        await context.SaveChangesAsync();
        return new(true, "صلاحیت حذف شد.");
    }

    public async Task<EmployeeResult> AddCertificationAsync(int employeeId, int certificationId, DateTime issueDate, string? certificateNumber = null, string? issuer = null, DateTime? expiryDate = null)
    {
        await context.EmployeeCertifications.AddAsync(new EmployeeCertification { EmployeeId = employeeId, CertificationId = certificationId, CertificateNumber = certificateNumber, Issuer = issuer, IssueDate = issueDate, ExpiryDate = expiryDate, IsActive = true, CreatedDate = DateTime.Now });
        await context.SaveChangesAsync();
        return new(true, "گواهینامه ثبت شد.");
    }

    public async Task<EmployeeResult> RemoveCertificationAsync(int employeeCertificationId)
    {
        var row = await context.EmployeeCertifications.FirstOrDefaultAsync(x => x.EmployeeCertificationId == employeeCertificationId);
        if (row is null) return new(false, "مورد پیدا نشد.");
        row.IsActive = false;
        await context.SaveChangesAsync();
        return new(true, "گواهینامه حذف شد.");
    }

    public async Task<EmployeeResult> SetLaborRateAsync(int employeeId, string rateType, decimal rate, string currency, DateTime effectiveFrom)
    {
        try { await laborRates.SetRateAsync(employeeId, rateType, rate, currency, effectiveFrom); return new(true, "نرخ دستمزد ثبت شد."); }
        catch (Exception ex) { return new(false, ex.Message); }
    }

    public async Task<EmployeeExportResult> ExportAsync()
    {
        var employees = await context.Employees.AsNoTracking().Include(x => x.Department).Include(x => x.Discipline).OrderBy(x => x.FirstName).ToListAsync();
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Employees");
        sheet.AddStyledHeaderRow("PersonnelCode", "FirstName", "LastName", "Position", "Email", "Phone", "Department", "Discipline", "IsActive");
        var row = 2;
        foreach (var e in employees)
        {
            sheet.Cell(row, 1).Value = e.PersonnelCode;
            sheet.Cell(row, 2).Value = e.FirstName;
            sheet.Cell(row, 3).Value = e.LastName;
            sheet.Cell(row, 4).Value = e.Position;
            sheet.Cell(row, 5).Value = e.Email;
            sheet.Cell(row, 6).Value = e.Phone;
            sheet.Cell(row, 7).Value = e.Department?.Name;
            sheet.Cell(row, 8).Value = e.Discipline?.Name;
            sheet.Cell(row, 9).Value = e.IsActive;
            row++;
        }
        sheet.FinalizeExportSheet();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return new EmployeeExportResult(true, null, stream.ToArray(), $"Employees_{DateTime.Now:yyyyMMdd}.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }
}
