using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Web.Services;

/// <summary>SCRUM-723: the lists and the 29 commands of MaintenanceMasterDataController, moved as they were (same validation, same messages); the permission is the floor of the operation.</summary>
public sealed class MaintenanceMasterDataOperations(PEMSDbContext _context, IOrganizationStructureService _organizationService) : IMaintenanceMasterDataOperations
{
    private static string S(int id) => id.ToString(CultureInfo.InvariantCulture);

    public async Task<List<MasterDataRow>> GroupsAsync() => (await _context.MaintenanceGroups.AsNoTracking().Include(x => x.Discipline).Where(x => x.IsActive).OrderBy(x => x.Name).ThenBy(x => x.MaintenanceGroupId).ToListAsync()).Select(x => new MasterDataRow(x.MaintenanceGroupId, x.Code, x.Name, x.Discipline?.Name ?? "", x.IsActive)).ToList();
    public async Task<List<MasterDataRow>> SkillsAsync() => (await _context.Skills.AsNoTracking().Include(x => x.Discipline).Where(x => x.IsActive).OrderBy(x => x.Name).ThenBy(x => x.SkillId).ToListAsync()).Select(x => new MasterDataRow(x.SkillId, "", x.Name, x.Discipline?.Name ?? "", x.IsActive)).ToList();
    public async Task<List<MasterDataRow>> QualificationsAsync() => (await _context.Qualifications.AsNoTracking().Include(x => x.Discipline).Where(x => x.IsActive).OrderBy(x => x.Name).ThenBy(x => x.QualificationId).ToListAsync()).Select(x => new MasterDataRow(x.QualificationId, "", x.Name, x.Discipline?.Name ?? "", x.IsActive)).ToList();
    public async Task<List<MasterDataRow>> CertificationsAsync() => (await _context.Certifications.AsNoTracking().Include(x => x.Discipline).Where(x => x.IsActive).OrderBy(x => x.Name).ThenBy(x => x.CertificationId).ToListAsync()).Select(x => new MasterDataRow(x.CertificationId, "", x.Name, x.Discipline?.Name ?? "", x.IsActive)).ToList();
    public async Task<List<MasterDataRow>> MaintenanceTypesAsync() => (await _context.MaintenanceTypes.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name).ToListAsync()).Select(x => new MasterDataRow(x.MaintenanceTypeId, x.Code, x.Name, x.NameFa ?? "", x.IsActive)).ToList();
    public async Task<List<MasterDataRow>> StrategyTypesAsync() => (await _context.MaintenanceStrategyTypes.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name).ToListAsync()).Select(x => new MasterDataRow(x.MaintenanceStrategyTypeId, x.Code, x.Name, x.NameFa ?? "", x.IsActive)).ToList();
    public async Task<List<MasterDataRow>> EquipmentClassesAsync() => (await _context.EquipmentClasses.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name).ToListAsync()).Select(x => new MasterDataRow(x.EquipmentClassId, x.Code, x.Name, x.NameFa ?? "", x.IsActive)).ToList();
    public async Task<List<MasterDataRow>> StandardsAsync() => (await _context.StandardReferences.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Organization).ThenBy(x => x.StandardNumber).ToListAsync()).Select(x => new MasterDataRow(x.StandardReferenceId, x.Code, x.Title, x.Organization + " " + x.StandardNumber, x.IsActive)).ToList();
    public async Task<List<MasterDataRow>> ActivitiesAsync() => (await _context.MaintenanceActivities.AsNoTracking().Include(x => x.MaintenanceGroup).Include(x => x.MaintenanceType).Where(x => x.IsActive).OrderBy(x => x.MaintenanceGroup!.Name).ThenBy(x => x.DisplayOrder).ThenBy(x => x.Name).ToListAsync())
        .Select(x => new MasterDataRow(x.MaintenanceActivityId, x.Code, x.Name, (x.MaintenanceGroup?.Name + " / " + x.MaintenanceType?.Name).Trim(' ', '/'), x.IsActive)).ToList();
    public async Task<List<MasterDataRow>> FailureModesAsync() => (await _context.FailureModes.AsNoTracking().Include(x => x.EquipmentClass).Include(x => x.FailureEffect).Include(x => x.FailureConsequence).Where(x => x.IsActive).OrderBy(x => x.EquipmentClass!.Name).ThenBy(x => x.DisplayOrder).ThenBy(x => x.Name).ToListAsync())
        .Select(x => new MasterDataRow(x.FailureModeId, x.Code, x.Name, (x.EquipmentClass?.Name + " / " + x.FailureEffect?.Name + " / " + x.FailureConsequence?.Name).Trim(' ', '/'), x.IsActive)).ToList();
    public async Task<List<MasterDataRow>> PmTemplatesAsync() => (await _context.PMTemplates.AsNoTracking().Include(x => x.EquipmentClass).Include(x => x.MaintenanceStrategyType).Where(x => x.IsActive).OrderBy(x => x.EquipmentClass!.Name).ThenBy(x => x.DisplayOrder).ThenBy(x => x.Name).ToListAsync())
        .Select(x => new MasterDataRow(x.PMTemplateId, x.Code, x.Name, (x.EquipmentClass?.Name + " / " + x.MaintenanceStrategyType?.Name).Trim(' ', '/'), x.IsActive)).ToList();
    public async Task<List<MasterDataRow>> EvaluationProfilesAsync() => (await _context.EvaluationProfiles.AsNoTracking().Include(x => x.StandardReference).Include(x => x.EquipmentClass).Include(x => x.Limits).OrderBy(x => x.VariableType).ThenBy(x => x.Code).ToListAsync())
        .Select(x => new MasterDataRow(x.EvaluationProfileId, x.Code, x.Name, x.VariableType + " / " + x.StandardReference?.Code + " / " + x.EquipmentClass?.Name + " / " + x.Limits.Count + " limits", x.IsActive)).ToList();
    public async Task<List<MasterDataRow>> EvaluationLimitsAsync() => (await _context.EvaluationLimits.AsNoTracking().Include(x => x.EvaluationProfile).OrderBy(x => x.EvaluationProfileId).ThenBy(x => x.EvaluationLimitId).ToListAsync())
        .Select(x => new MasterDataRow(x.EvaluationLimitId, x.EvaluationProfile?.Code ?? "", x.Status, (x.LowInclusive ? "[" : "(") + x.LowValue + " .. " + x.HighValue + (x.HighInclusive ? "]" : ")"), x.IsActive)).ToList();
    public async Task<List<MasterDataRow>> ConditionPrioritiesAsync() => (await _context.ConditionPriorityMappings.AsNoTracking().OrderBy(x => x.Condition).ToListAsync()).Select(x => new MasterDataRow(x.ConditionPriorityMappingId, x.Condition, x.Priority, "", x.IsActive)).ToList();

    public async Task<List<ReportChoiceRow>> DisciplinesChoicesAsync() => (await _context.Disciplines.AsNoTracking().Where(x => x.IsActive).Select(x => new { x.DisciplineId, x.Name }).ToListAsync()).Select(x => new ReportChoiceRow(S(x.DisciplineId), x.Name)).ToList();
    public async Task<List<ReportChoiceRow>> GroupsChoicesAsync() => (await _context.MaintenanceGroups.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new { x.MaintenanceGroupId, x.Code, x.Name }).ToListAsync()).Select(x => new ReportChoiceRow(S(x.MaintenanceGroupId), x.Code + " " + x.Name)).ToList();
    public async Task<List<ReportChoiceRow>> MaintenanceTypesChoicesAsync() => (await _context.MaintenanceTypes.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new { x.MaintenanceTypeId, x.Name }).ToListAsync()).Select(x => new ReportChoiceRow(S(x.MaintenanceTypeId), x.Name)).ToList();
    public async Task<List<ReportChoiceRow>> StandardsChoicesAsync() => (await _context.StandardReferences.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Code).Select(x => new { x.StandardReferenceId, x.Code, x.Title }).ToListAsync()).Select(x => new ReportChoiceRow(S(x.StandardReferenceId), x.Code + " " + x.Title)).ToList();
    public async Task<List<ReportChoiceRow>> EquipmentClassesChoicesAsync() => (await _context.EquipmentClasses.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new { x.EquipmentClassId, x.Name }).ToListAsync()).Select(x => new ReportChoiceRow(S(x.EquipmentClassId), x.Name)).ToList();
    public async Task<List<ReportChoiceRow>> ProfilesChoicesAsync() => (await _context.EvaluationProfiles.AsNoTracking().OrderBy(x => x.Code).Select(x => new { x.EvaluationProfileId, x.Code, x.Name }).ToListAsync()).Select(x => new ReportChoiceRow(S(x.EvaluationProfileId), x.Code + " " + x.Name)).ToList();

    public async Task<(bool Success, string? Error)> CreateGroupAsync(string code, string name, int disciplineId, string? description)
    {

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(code))
        {
            return (false, "کد و نام گروه تعمیراتی الزامی هستند.");
        }

        var duplicate = await _context.MaintenanceGroups.AnyAsync(x => x.Name == name.Trim() || x.Code == code.Trim());
        if (duplicate)
        {
            return (false, "گروه تعمیراتی با همین کد یا نام از قبل وجود دارد.");
        }

        await _context.MaintenanceGroups.AddAsync(new MaintenanceGroup
        {
            Code = code.Trim(),
            Name = name.Trim(),
            DisciplineId = disciplineId,
            Description = description,
            IsActive = true,
            CreatedDate = DateTime.Now
        });

        await _context.SaveChangesAsync();

        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ToggleGroupActiveAsync(int id)
    {

        var group = await _context.MaintenanceGroups.FirstOrDefaultAsync(x => x.MaintenanceGroupId == id);
        if (group == null) return (false, "NotFound");

        group.IsActive = !group.IsActive;
        await _context.SaveChangesAsync();

        return (true, null);
    }

    public async Task<(bool Success, string? Error)> CreateSubGroupAsync(string code, string name, int disciplineId, int parentMaintenanceGroupId, string? description)
    {

        try
        {
            await _organizationService.CreateGroupAsync(code, name, disciplineId, parentMaintenanceGroupId, description);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<(bool Success, string? Error)> MoveGroupAsync(int maintenanceGroupId, int? newParentMaintenanceGroupId)
    {

        try
        {
            await _organizationService.MoveGroupAsync(maintenanceGroupId, newParentMaintenanceGroupId);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<(bool Success, string? Error)> CreateSkillAsync(string name, int? disciplineId, string? description)
    {

        if (string.IsNullOrWhiteSpace(name))
        {
            return (false, "نام مهارت الزامی است.");
        }

        var duplicate = await _context.Skills.AnyAsync(x => x.Name == name.Trim());
        if (duplicate)
        {
            return (false, "مهارتی با همین نام از قبل وجود دارد.");
        }

        await _context.Skills.AddAsync(new Skill
        {
            Name = name.Trim(),
            DisciplineId = disciplineId,
            Description = description,
            IsActive = true,
            CreatedDate = DateTime.Now
        });

        await _context.SaveChangesAsync();

        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ToggleSkillActiveAsync(int id)
    {

        var skill = await _context.Skills.FirstOrDefaultAsync(x => x.SkillId == id);
        if (skill == null) return (false, "NotFound");

        skill.IsActive = !skill.IsActive;
        await _context.SaveChangesAsync();

        return (true, null);
    }

    public async Task<(bool Success, string? Error)> CreateQualificationAsync(string name, int? disciplineId, string? description)
    {

        if (string.IsNullOrWhiteSpace(name))
        {
            return (false, "نام صلاحیت الزامی است.");
        }

        if (await _context.Qualifications.AnyAsync(x => x.Name == name.Trim()))
        {
            return (false, "صلاحیتی با همین نام از قبل وجود دارد.");
        }

        await _context.Qualifications.AddAsync(new Qualification
        {
            Name = name.Trim(),
            DisciplineId = disciplineId,
            Description = description,
            IsActive = true,
            CreatedDate = DateTime.Now
        });

        await _context.SaveChangesAsync();

        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ToggleQualificationActiveAsync(int id)
    {

        var qualification = await _context.Qualifications.FirstOrDefaultAsync(x => x.QualificationId == id);
        if (qualification == null) return (false, "NotFound");

        qualification.IsActive = !qualification.IsActive;
        await _context.SaveChangesAsync();

        return (true, null);
    }

    public async Task<(bool Success, string? Error)> CreateCertificationAsync(string name, int? disciplineId, string? description)
    {

        if (string.IsNullOrWhiteSpace(name))
        {
            return (false, "نام گواهینامه الزامی است.");
        }

        if (await _context.Certifications.AnyAsync(x => x.Name == name.Trim()))
        {
            return (false, "گواهینامه‌ای با همین نام از قبل وجود دارد.");
        }

        await _context.Certifications.AddAsync(new Certification
        {
            Name = name.Trim(),
            DisciplineId = disciplineId,
            Description = description,
            IsActive = true,
            CreatedDate = DateTime.Now
        });

        await _context.SaveChangesAsync();

        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ToggleCertificationActiveAsync(int id)
    {

        var certification = await _context.Certifications.FirstOrDefaultAsync(x => x.CertificationId == id);
        if (certification == null) return (false, "NotFound");

        certification.IsActive = !certification.IsActive;
        await _context.SaveChangesAsync();

        return (true, null);
    }

    public async Task<(bool Success, string? Error)> CreateMaintenanceTypeAsync(string code, string name, string? nameFa, string? description)
    {

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(code))
        {
            return (false, "کد و نام Maintenance Type الزامی هستند.");
        }

        if (await _context.MaintenanceTypes.AnyAsync(x => x.Code == code.Trim()))
        {
            return (false, "Maintenance Type ای با همین کد از قبل وجود دارد.");
        }

        await _context.MaintenanceTypes.AddAsync(new MaintenanceType
        {
            Code = code.Trim(),
            Name = name.Trim(),
            NameFa = nameFa,
            Description = description,
            IsSystemDefault = false,
            IsActive = true,
            CreatedDate = DateTime.Now
        });

        await _context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ToggleMaintenanceTypeActiveAsync(int id)
    {

        var item = await _context.MaintenanceTypes.FirstOrDefaultAsync(x => x.MaintenanceTypeId == id);
        if (item == null) return (false, "NotFound");

        item.IsActive = !item.IsActive;
        await _context.SaveChangesAsync();

        return (true, null);
    }

    public async Task<(bool Success, string? Error)> CreateMaintenanceStrategyTypeAsync(string code, string name, string? nameFa, string? description)
    {

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(code))
        {
            return (false, "کد و نام Strategy Type الزامی هستند.");
        }

        if (await _context.MaintenanceStrategyTypes.AnyAsync(x => x.Code == code.Trim()))
        {
            return (false, "Strategy Type ای با همین کد از قبل وجود دارد.");
        }

        await _context.MaintenanceStrategyTypes.AddAsync(new MaintenanceStrategyType
        {
            Code = code.Trim(),
            Name = name.Trim(),
            NameFa = nameFa,
            Description = description,
            IsSystemDefault = false,
            IsActive = true,
            CreatedDate = DateTime.Now
        });

        await _context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ToggleMaintenanceStrategyTypeActiveAsync(int id)
    {

        var item = await _context.MaintenanceStrategyTypes.FirstOrDefaultAsync(x => x.MaintenanceStrategyTypeId == id);
        if (item == null) return (false, "NotFound");

        item.IsActive = !item.IsActive;
        await _context.SaveChangesAsync();

        return (true, null);
    }

    public async Task<(bool Success, string? Error)> CreateEquipmentClassAsync(string code, string name, string? nameFa, string? description)
    {

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(code))
        {
            return (false, "کد و نام Equipment Class الزامی هستند.");
        }

        if (await _context.EquipmentClasses.AnyAsync(x => x.Code == code.Trim()))
        {
            return (false, "Equipment Class ای با همین کد از قبل وجود دارد.");
        }

        await _context.EquipmentClasses.AddAsync(new EquipmentClass
        {
            Code = code.Trim(),
            Name = name.Trim(),
            NameFa = nameFa,
            Description = description,
            IsSystemDefault = false,
            IsActive = true,
            CreatedDate = DateTime.Now
        });

        await _context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ToggleEquipmentClassActiveAsync(int id)
    {

        var item = await _context.EquipmentClasses.FirstOrDefaultAsync(x => x.EquipmentClassId == id);
        if (item == null) return (false, "NotFound");

        item.IsActive = !item.IsActive;
        await _context.SaveChangesAsync();

        return (true, null);
    }

    public async Task<(bool Success, string? Error)> CreateStandardReferenceAsync(string code, string organization, string standardNumber, string title, string? category, string? description)
    {

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(organization) ||
            string.IsNullOrWhiteSpace(standardNumber) || string.IsNullOrWhiteSpace(title))
        {
            return (false, "کد، سازمان، شماره استاندارد و عنوان الزامی هستند.");
        }

        // طبق بند ۱۹: «duplicate standard references are prohibited»
        if (await _context.StandardReferences.AnyAsync(x => x.Code == code.Trim() ||
            (x.Organization == organization.Trim() && x.StandardNumber == standardNumber.Trim())))
        {
            return (false, "این استاندارد (کد یا ترکیب سازمان/شماره) از قبل ثبت شده است.");
        }

        await _context.StandardReferences.AddAsync(new StandardReference
        {
            Code = code.Trim(),
            Organization = organization.Trim(),
            StandardNumber = standardNumber.Trim(),
            Title = title.Trim(),
            Category = category,
            Description = description,
            IsSystemDefault = false,
            IsActive = true,
            CreatedDate = DateTime.Now
        });

        await _context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ToggleStandardReferenceActiveAsync(int id)
    {

        var item = await _context.StandardReferences.FirstOrDefaultAsync(x => x.StandardReferenceId == id);
        if (item == null) return (false, "NotFound");

        item.IsActive = !item.IsActive;
        await _context.SaveChangesAsync();

        return (true, null);
    }

    public async Task<(bool Success, string? Error)> CreateMaintenanceActivityAsync(string code, string name, string? nameFa, int maintenanceGroupId, int? maintenanceTypeId, string? description)
    {

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(code))
        {
            return (false, "کد و نام Maintenance Activity الزامی هستند.");
        }

        // طبق بند ۱۹: «orphan Maintenance Activities are prohibited» - Group باید معتبر و فعال باشد
        var groupExists = await _context.MaintenanceGroups.AnyAsync(x => x.MaintenanceGroupId == maintenanceGroupId && x.IsActive);
        if (!groupExists)
        {
            return (false, "گروه تعمیراتی انتخاب‌شده معتبر یا فعال نیست.");
        }

        if (await _context.MaintenanceActivities.AnyAsync(x => x.Code == code.Trim()))
        {
            return (false, "Maintenance Activity ای با همین کد از قبل وجود دارد.");
        }

        await _context.MaintenanceActivities.AddAsync(new MaintenanceActivity
        {
            Code = code.Trim(),
            Name = name.Trim(),
            NameFa = nameFa,
            MaintenanceGroupId = maintenanceGroupId,
            MaintenanceTypeId = maintenanceTypeId,
            Description = description,
            IsSystemDefault = false,
            IsActive = true,
            CreatedDate = DateTime.Now
        });

        await _context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ToggleMaintenanceActivityActiveAsync(int id)
    {

        var item = await _context.MaintenanceActivities.FirstOrDefaultAsync(x => x.MaintenanceActivityId == id);
        if (item == null) return (false, "NotFound");

        item.IsActive = !item.IsActive;
        await _context.SaveChangesAsync();

        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ToggleFailureModeActiveAsync(int id)
    {

        var item = await _context.FailureModes.FirstOrDefaultAsync(x => x.FailureModeId == id);
        if (item == null) return (false, "NotFound");

        item.IsActive = !item.IsActive;
        await _context.SaveChangesAsync();

        return (true, null);
    }

    public async Task<(bool Success, string? Error)> TogglePMTemplateActiveAsync(int id)
    {

        var item = await _context.PMTemplates.FirstOrDefaultAsync(x => x.PMTemplateId == id);
        if (item == null) return (false, "NotFound");

        item.IsActive = !item.IsActive;
        await _context.SaveChangesAsync();

        return (true, null);
    }

    public async Task<(bool Success, string? Error)> CreateEvaluationProfileAsync(string code, string name, string? nameFa, string variableType, int? standardReferenceId, int? equipmentClassId, string? operatingCondition, DateTime? effectiveFrom)
    {

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(variableType))
        {
            return (false, "کد، نام و VariableType الزامی هستند.");
        }

        if (await _context.EvaluationProfiles.AnyAsync(x => x.Code == code.Trim()))
        {
            return (false, "Evaluation Profile ای با همین کد از قبل وجود دارد.");
        }

        await _context.EvaluationProfiles.AddAsync(new EvaluationProfile
        {
            Code = code.Trim(),
            Name = name.Trim(),
            NameFa = nameFa,
            VariableType = variableType.Trim(),
            StandardReferenceId = standardReferenceId,
            EquipmentClassId = equipmentClassId,
            OperatingCondition = operatingCondition,
            Version = 1,
            EffectiveFrom = effectiveFrom ?? DateTime.Now,
            IsActive = true,
            CreatedDate = DateTime.Now
        });

        await _context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ToggleEvaluationProfileActiveAsync(int id)
    {

        var item = await _context.EvaluationProfiles.FirstOrDefaultAsync(x => x.EvaluationProfileId == id);
        if (item == null) return (false, "NotFound");

        item.IsActive = !item.IsActive;
        await _context.SaveChangesAsync();

        return (true, null);
    }

    public async Task<(bool Success, string? Error)> AddEvaluationLimitAsync(int evaluationProfileId, string status, decimal? lowValue, decimal? highValue, bool lowInclusive, bool highInclusive)
    {

        if (string.IsNullOrWhiteSpace(status))
        {
            return (false, "Status الزامی است.");
        }

        if (lowValue == null && highValue == null)
        {
            return (false, "حداقل یکی از Low/High Value باید وارد شود - آستانه‌ی خالی/فرضی ثبت نمی‌شود.");
        }

        var profileExists = await _context.EvaluationProfiles.AnyAsync(x => x.EvaluationProfileId == evaluationProfileId);
        if (!profileExists)
        {
            return (false, "Evaluation Profile معتبر نیست.");
        }

        await _context.EvaluationLimits.AddAsync(new EvaluationLimit
        {
            EvaluationProfileId = evaluationProfileId,
            Status = status.Trim(),
            LowValue = lowValue,
            HighValue = highValue,
            LowInclusive = lowInclusive,
            HighInclusive = highInclusive,
            IsActive = true,
            CreatedDate = DateTime.Now
        });

        await _context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ToggleEvaluationLimitActiveAsync(int id)
    {

        var item = await _context.EvaluationLimits.FirstOrDefaultAsync(x => x.EvaluationLimitId == id);
        if (item == null) return (false, "NotFound");

        item.IsActive = !item.IsActive;
        await _context.SaveChangesAsync();

        return (true, null);
    }

    public async Task<(bool Success, string? Error)> CreateConditionPriorityMappingAsync(string condition, string priority)
    {

        if (string.IsNullOrWhiteSpace(condition) || string.IsNullOrWhiteSpace(priority))
        {
            return (false, "Condition و Priority الزامی هستند.");
        }

        if (await _context.ConditionPriorityMappings.AnyAsync(x => x.Condition == condition.Trim()))
        {
            return (false, "نگاشتی برای این Condition از قبل وجود دارد.");
        }

        await _context.ConditionPriorityMappings.AddAsync(new ConditionPriorityMapping
        {
            Condition = condition.Trim(),
            Priority = priority,
            IsSystemDefault = false,
            IsActive = true,
            CreatedDate = DateTime.Now
        });

        await _context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> UpdateConditionPriorityMappingAsync(int id, string priority)
    {

        var item = await _context.ConditionPriorityMappings.FirstOrDefaultAsync(x => x.ConditionPriorityMappingId == id);
        if (item == null) return (false, "NotFound");

        item.Priority = priority;
        await _context.SaveChangesAsync();

        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ToggleConditionPriorityMappingActiveAsync(int id)
    {

        var item = await _context.ConditionPriorityMappings.FirstOrDefaultAsync(x => x.ConditionPriorityMappingId == id);
        if (item == null) return (false, "NotFound");

        item.IsActive = !item.IsActive;
        await _context.SaveChangesAsync();

        return (true, null);
    }
}
