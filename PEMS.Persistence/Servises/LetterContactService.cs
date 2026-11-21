using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

public class LetterContactService : ILetterContactService
{
    public const int MaxMembers = 200;
    public const int MaxPersonalGroups = 50;
    private readonly PEMSDbContext _ctx;

    public LetterContactService(PEMSDbContext ctx) => _ctx = ctx;

    public async Task<List<LetterContactDto>> SearchAsync(string? query, int take = 30)
    {
        take = Math.Clamp(take, 1, 100);
        var q = (query ?? "").Trim();

        var employees = _ctx.Employees.AsNoTracking().Where(e => e.IsActive);
        if (q.Length > 0) employees = employees.Where(e => (e.FirstName + " " + e.LastName).Contains(q) || (e.Position != null && e.Position.Contains(q)));
        var emp = await (from e in employees
                         join d in _ctx.Departments.AsNoTracking() on e.DepartmentId equals d.DepartmentId into gd
                         from d in gd.DefaultIfEmpty()
                         orderby e.FirstName, e.LastName
                         select new { e.EmployeeId, e.FirstName, e.LastName, e.Position, Department = d != null ? d.Name : null }).Take(take).ToListAsync();

        var parties = _ctx.CorrespondentParties.AsNoTracking().Where(p => p.IsActive);
        if (q.Length > 0) parties = parties.Where(p => p.Name.Contains(q) || (p.Code != null && p.Code.Contains(q)));
        var par = await parties.OrderBy(p => p.Name).Take(take).Select(p => new { p.CorrespondentPartyId, p.Name, p.PartyType }).ToListAsync();

        return emp.Select(e => new LetterContactDto
            {
                Kind = "Employee", Id = e.EmployeeId, Name = (e.FirstName + " " + e.LastName).Trim(),
                Subtitle = string.Join(" · ", new[] { e.Position, e.Department }.Where(s => !string.IsNullOrWhiteSpace(s)))
            })
            .Concat(par.Select(p => new LetterContactDto { Kind = "Party", Id = p.CorrespondentPartyId, Name = p.Name, Subtitle = p.PartyType }))
            .Take(take).ToList();
    }

    public async Task<List<LetterContactGroupDto>> GetGroupsAsync(int userId, bool canManageOrganizational)
    {
        var groups = await _ctx.LetterContactGroups.AsNoTracking().Include(g => g.Members)
            .Where(g => g.IsActive && (g.OwnerUserId == null || g.OwnerUserId == userId))
            .OrderBy(g => g.OwnerUserId != null).ThenBy(g => g.Name).ToListAsync();

        var empIds = groups.SelectMany(g => g.Members).Where(m => m.EmployeeId.HasValue).Select(m => m.EmployeeId!.Value).Distinct().ToList();
        var partyIds = groups.SelectMany(g => g.Members).Where(m => m.CorrespondentPartyId.HasValue).Select(m => m.CorrespondentPartyId!.Value).Distinct().ToList();
        var emp = await _ctx.Employees.AsNoTracking().Where(e => empIds.Contains(e.EmployeeId)).ToDictionaryAsync(e => e.EmployeeId, e => new { Name = (e.FirstName + " " + e.LastName).Trim(), e.Position });
        var par = await _ctx.CorrespondentParties.AsNoTracking().Where(p => partyIds.Contains(p.CorrespondentPartyId)).ToDictionaryAsync(p => p.CorrespondentPartyId, p => new { p.Name, p.PartyType });

        return groups.Select(g => new LetterContactGroupDto
        {
            LetterContactGroupId = g.LetterContactGroupId, Name = g.Name, IsPersonal = g.OwnerUserId.HasValue,
            CanEdit = g.OwnerUserId == userId || (!g.OwnerUserId.HasValue && canManageOrganizational),
            Members = g.Members.Select(m => new LetterContactMemberDto
            {
                LetterContactGroupMemberId = m.LetterContactGroupMemberId, EmployeeId = m.EmployeeId, CorrespondentPartyId = m.CorrespondentPartyId,
                Name = m.EmployeeId.HasValue ? emp.GetValueOrDefault(m.EmployeeId.Value)?.Name ?? "#" + m.EmployeeId
                    : par.GetValueOrDefault(m.CorrespondentPartyId ?? 0)?.Name ?? "#" + m.CorrespondentPartyId,
                Subtitle = m.EmployeeId.HasValue ? emp.GetValueOrDefault(m.EmployeeId.Value)?.Position : par.GetValueOrDefault(m.CorrespondentPartyId ?? 0)?.PartyType
            }).OrderBy(m => m.Name).ToList()
        }).ToList();
    }

    public async Task<CartableOpResult> SaveGroupAsync(int? groupId, string name, bool personal, IReadOnlyCollection<int> employeeIds,
        IReadOnlyCollection<int> partyIds, int userId, bool canManageOrganizational)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0 || name.Length > 120) return CartableOpResult.Fail("Grp_Err_Name");

        var emps = (employeeIds ?? Array.Empty<int>()).Distinct().ToList();
        var pars = (partyIds ?? Array.Empty<int>()).Distinct().ToList();
        if (emps.Count + pars.Count == 0) return CartableOpResult.Fail("Grp_Err_Empty");
        if (emps.Count + pars.Count > MaxMembers) return CartableOpResult.Fail("Grp_Err_TooMany");

        // فقط کارمند فعال و طرف‌حساب فعال؛ شناسه‌ی جعلی بی‌صدا نادیده گرفته نمی‌شود
        var validEmps = await _ctx.Employees.AsNoTracking().Where(e => e.IsActive && emps.Contains(e.EmployeeId)).Select(e => e.EmployeeId).ToListAsync();
        var validPars = await _ctx.CorrespondentParties.AsNoTracking().Where(p => p.IsActive && pars.Contains(p.CorrespondentPartyId)).Select(p => p.CorrespondentPartyId).ToListAsync();
        if (validEmps.Count != emps.Count || validPars.Count != pars.Count) return CartableOpResult.Fail("Grp_Err_InvalidMember");

        LetterContactGroup? group;
        if (groupId.HasValue)
        {
            group = await _ctx.LetterContactGroups.Include(g => g.Members).FirstOrDefaultAsync(g => g.LetterContactGroupId == groupId && g.IsActive);
            if (group == null) return CartableOpResult.Fail("Grp_Err_NotFound");
            if (!(group.OwnerUserId == userId || (!group.OwnerUserId.HasValue && canManageOrganizational))) return CartableOpResult.Fail("Grp_Err_NoPermission");
        }
        else
        {
            if (!personal && !canManageOrganizational) return CartableOpResult.Fail("Grp_Err_NoPermission");
            if (personal && await _ctx.LetterContactGroups.CountAsync(g => g.OwnerUserId == userId && g.IsActive) >= MaxPersonalGroups) return CartableOpResult.Fail("Grp_Err_TooManyGroups");
            group = new LetterContactGroup { OwnerUserId = personal ? userId : null };   // به Context بعد از اعتبارسنجی اضافه می‌شود
        }

        var owner = group.OwnerUserId;
        if (await _ctx.LetterContactGroups.AnyAsync(g => g.IsActive && g.Name == name && g.OwnerUserId == owner && g.LetterContactGroupId != group.LetterContactGroupId))
            return CartableOpResult.Fail("Grp_Err_Duplicate");

        group.Name = name;
        if (group.LetterContactGroupId == 0) _ctx.LetterContactGroups.Add(group);
        _ctx.LetterContactGroupMembers.RemoveRange(group.Members.ToList());
        group.Members.Clear();
        foreach (var e in emps) group.Members.Add(new LetterContactGroupMember { EmployeeId = e });
        foreach (var p in pars) group.Members.Add(new LetterContactGroupMember { CorrespondentPartyId = p });
        await _ctx.SaveChangesAsync();
        return CartableOpResult.Ok(emps.Count + pars.Count, group.LetterContactGroupId);
    }

    public async Task<CartableOpResult> DeleteGroupAsync(int groupId, int userId, bool canManageOrganizational)
    {
        var group = await _ctx.LetterContactGroups.FirstOrDefaultAsync(g => g.LetterContactGroupId == groupId && g.IsActive);
        if (group == null) return CartableOpResult.Fail("Grp_Err_NotFound");
        if (!(group.OwnerUserId == userId || (!group.OwnerUserId.HasValue && canManageOrganizational))) return CartableOpResult.Fail("Grp_Err_NoPermission");
        group.IsActive = false;
        await _ctx.SaveChangesAsync();
        return CartableOpResult.Ok();
    }
}
