using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Application.Organization;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

public class LetterRoutingRuleService : ILetterRoutingRuleService
{
    public const int MaxRulesPerUser = 30;
    private readonly PEMSDbContext _ctx;
    private readonly IOrganizationSubordinateResolver _org;
    private readonly ILetterReferralService? _referrals;

    public LetterRoutingRuleService(PEMSDbContext ctx, IOrganizationSubordinateResolver org, ILetterReferralService? referrals = null)
    {
        _ctx = ctx;
        _org = org;
        _referrals = referrals;
    }

    public async Task<List<LetterRoutingRuleDto>> GetAsync(int userId)
    {
        var rules = await _ctx.LetterRoutingRules.AsNoTracking().Where(r => r.OwnerUserId == userId).OrderBy(r => r.SortOrder).ThenBy(r => r.LetterRoutingRuleId).ToListAsync();
        var tagIds = rules.Where(r => r.ApplyTagId.HasValue).Select(r => r.ApplyTagId!.Value).Distinct().ToList();
        var empIds = rules.Where(r => r.ReferToEmployeeId.HasValue).Select(r => r.ReferToEmployeeId!.Value).Distinct().ToList();
        var tagNames = await _ctx.Tags.AsNoTracking().Where(t => tagIds.Contains(t.TagId)).ToDictionaryAsync(t => t.TagId, t => t.Name);
        var empNames = await _ctx.Employees.AsNoTracking().Where(e => empIds.Contains(e.EmployeeId)).ToDictionaryAsync(e => e.EmployeeId, e => (e.FirstName + " " + e.LastName).Trim());

        return rules.Select(r => new LetterRoutingRuleDto
        {
            LetterRoutingRuleId = r.LetterRoutingRuleId, Name = r.Name, IsActive = r.IsActive, SortOrder = r.SortOrder,
            MatchLetterType = r.MatchLetterType, MatchPriority = r.MatchPriority, MatchCategory = r.MatchCategory,
            MatchProjectId = r.MatchProjectId, MatchDisciplineId = r.MatchDisciplineId,
            MatchSenderContains = r.MatchSenderContains, MatchSubjectContains = r.MatchSubjectContains,
            ApplyTagId = r.ApplyTagId, ApplyTagName = r.ApplyTagId.HasValue ? tagNames.GetValueOrDefault(r.ApplyTagId.Value) : null,
            StarLetter = r.StarLetter,
            ReferToEmployeeId = r.ReferToEmployeeId, ReferToName = r.ReferToEmployeeId.HasValue ? empNames.GetValueOrDefault(r.ReferToEmployeeId.Value) : null,
            ReferInstruction = r.ReferInstruction, MatchCount = r.MatchCount, LastMatchedDate = r.LastMatchedDate
        }).ToList();
    }

    private static string? Clean(string? s, int max) { s = s?.Trim(); return string.IsNullOrEmpty(s) ? null : s[..Math.Min(max, s.Length)]; }

    public async Task<CartableOpResult> SaveAsync(LetterRoutingRuleDto dto, int userId)
    {
        var name = Clean(dto.Name, 120);
        if (name == null) return CartableOpResult.Fail("Rule_Err_Name");

        var cond = new LetterRoutingCondition(Clean(dto.MatchLetterType, 20), Clean(dto.MatchPriority, 20), Clean(dto.MatchCategory, 60),
            dto.MatchProjectId, dto.MatchDisciplineId, Clean(dto.MatchSenderContains, 120), Clean(dto.MatchSubjectContains, 120));
        if (cond.IsEmpty) return CartableOpResult.Fail("Rule_Err_NoCondition");
        if (!dto.ApplyTagId.HasValue && !dto.StarLetter && !dto.ReferToEmployeeId.HasValue) return CartableOpResult.Fail("Rule_Err_NoAction");

        if (dto.ApplyTagId.HasValue)
        {
            var tagOk = await _ctx.Tags.AsNoTracking().AnyAsync(t => t.TagId == dto.ApplyTagId && t.IsActive && (t.OwnerUserId == null || t.OwnerUserId == userId));
            if (!tagOk) return CartableOpResult.Fail("Rule_Err_Tag");
        }

        // ارجاع خودکار فقط به زیردستان سازمانیِ صاحب قانون (چارت سازمانی)
        if (dto.ReferToEmployeeId.HasValue)
        {
            var myEmployeeId = await _ctx.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.EmployeeId).FirstOrDefaultAsync();
            var subs = myEmployeeId.HasValue ? (await _org.GetSubordinatesAsync(myEmployeeId.Value)).Select(s => s.EmployeeId).ToHashSet() : new HashSet<int>();
            if (!subs.Contains(dto.ReferToEmployeeId.Value)) return CartableOpResult.Fail("Rule_Err_NotSubordinate");
        }

        LetterRoutingRule? row;
        if (dto.LetterRoutingRuleId > 0)
        {
            row = await _ctx.LetterRoutingRules.FirstOrDefaultAsync(r => r.LetterRoutingRuleId == dto.LetterRoutingRuleId && r.OwnerUserId == userId);
            if (row == null) return CartableOpResult.Fail("Rule_Err_NotFound");
        }
        else
        {
            if (await _ctx.LetterRoutingRules.CountAsync(r => r.OwnerUserId == userId) >= MaxRulesPerUser) return CartableOpResult.Fail("Rule_Err_TooMany");
            row = new LetterRoutingRule { OwnerUserId = userId, SortOrder = (await _ctx.LetterRoutingRules.Where(r => r.OwnerUserId == userId).MaxAsync(r => (int?)r.SortOrder) ?? 0) + 1 };
            _ctx.LetterRoutingRules.Add(row);
        }

        row.Name = name; row.IsActive = dto.IsActive;
        row.MatchLetterType = cond.LetterType; row.MatchPriority = cond.Priority; row.MatchCategory = cond.Category;
        row.MatchProjectId = cond.ProjectId; row.MatchDisciplineId = cond.DisciplineId;
        row.MatchSenderContains = cond.SenderContains; row.MatchSubjectContains = cond.SubjectContains;
        row.ApplyTagId = dto.ApplyTagId; row.StarLetter = dto.StarLetter;
        row.ReferToEmployeeId = dto.ReferToEmployeeId; row.ReferInstruction = Clean(dto.ReferInstruction, 300);
        await _ctx.SaveChangesAsync();
        return CartableOpResult.Ok(1, row.LetterRoutingRuleId);
    }

    public async Task<CartableOpResult> SetActiveAsync(int ruleId, bool active, int userId)
    {
        var row = await _ctx.LetterRoutingRules.FirstOrDefaultAsync(r => r.LetterRoutingRuleId == ruleId && r.OwnerUserId == userId);
        if (row == null) return CartableOpResult.Fail("Rule_Err_NotFound");
        row.IsActive = active;
        await _ctx.SaveChangesAsync();
        return CartableOpResult.Ok();
    }

    public async Task<CartableOpResult> DeleteAsync(int ruleId, int userId)
    {
        var row = await _ctx.LetterRoutingRules.FirstOrDefaultAsync(r => r.LetterRoutingRuleId == ruleId && r.OwnerUserId == userId);
        if (row == null) return CartableOpResult.Fail("Rule_Err_NotFound");
        _ctx.LetterRoutingRules.Remove(row);   // قانون پیکربندی شخصی است (نه سند رسمی)
        await _ctx.SaveChangesAsync();
        return CartableOpResult.Ok();
    }

    // ---------------------------------------------------------------- اجرا
    public async Task<int> ApplyToArrivedLetterAsync(int letterId)
    {
        try { return await ApplyCoreAsync(letterId); }
        catch { return 0; }   // خطا در قانون هرگز ارسال/ثبت نامه را نمی‌شکند
    }

    private async Task<int> ApplyCoreAsync(int letterId)
    {
        var letter = await _ctx.Letters.AsNoTracking().FirstOrDefaultAsync(l => l.LetterId == letterId && l.IsActive);
        if (letter == null || LetterAccessRules.PreSendStatuses.Contains(letter.Status)) return 0;

        // صاحبان ممکن: کاربرِ گیرنده (To/Cc؛ Bcc هم گیرنده است) یا گیرنده‌ی ثبت‌شده‌ی نامه‌ی وارده
        var employeeIds = await _ctx.LetterRecipients.AsNoTracking().Where(r => r.LetterId == letterId && r.EmployeeId != null).Select(r => r.EmployeeId!.Value).ToListAsync();
        if (letter.ReceiverEmployeeId.HasValue) employeeIds.Add(letter.ReceiverEmployeeId.Value);
        employeeIds.AddRange(await _ctx.LetterReferrals.AsNoTracking().Where(r => r.LetterId == letterId && r.ReferredToEmployeeId != null).Select(r => r.ReferredToEmployeeId!.Value).ToListAsync());
        employeeIds = employeeIds.Distinct().ToList();
        if (employeeIds.Count == 0) return 0;

        var owners = await _ctx.Users.AsNoTracking().Where(u => u.IsActive && u.EmployeeId != null && employeeIds.Contains(u.EmployeeId.Value))
            .Select(u => new { u.Id, u.EmployeeId, u.FullName }).ToListAsync();
        if (owners.Count == 0) return 0;
        var ownerIds = owners.Select(o => o.Id).ToList();

        var rules = await _ctx.LetterRoutingRules.Where(r => r.IsActive && ownerIds.Contains(r.OwnerUserId)).OrderBy(r => r.SortOrder).ToListAsync();
        if (rules.Count == 0) return 0;

        var facts = new LetterRoutingFacts(letter.LetterType, letter.Priority, letter.Category, letter.ProjectId, letter.DisciplineId, letter.Sender, letter.Subject);
        var applied = 0;
        foreach (var ownerGroup in rules.GroupBy(r => r.OwnerUserId))
        {
            // صاحب قانون باید خودش اجازه‌ی دیدن نامه را داشته باشد (محرمانگی)؛ کلاینت سطح بالا فرض نمی‌شود
            var canSee = await (await LetterVisibility.ApplyAsync(_ctx, _ctx.Letters.AsNoTracking().Where(l => l.LetterId == letterId), false, ownerGroup.Key)).AnyAsync();
            if (!canSee) continue;
            var owner = owners.First(o => o.Id == ownerGroup.Key);
            var matched = new List<LetterRoutingRule>();

            foreach (var rule in ownerGroup)
            {
                var cond = new LetterRoutingCondition(rule.MatchLetterType, rule.MatchPriority, rule.MatchCategory, rule.MatchProjectId,
                    rule.MatchDisciplineId, rule.MatchSenderContains, rule.MatchSubjectContains);
                if (!LetterRoutingRuleEvaluator.Matches(cond, facts)) continue;

                if (rule.ApplyTagId.HasValue
                    && await _ctx.Tags.AnyAsync(t => t.TagId == rule.ApplyTagId && t.IsActive && (t.OwnerUserId == null || t.OwnerUserId == owner.Id))
                    && !await _ctx.LetterTags.AnyAsync(t => t.LetterId == letterId && t.TagId == rule.ApplyTagId))
                    _ctx.LetterTags.Add(new LetterTag { LetterId = letterId, TagId = rule.ApplyTagId.Value, CreatedDate = DateTime.Now });

                if (rule.StarLetter && !await _ctx.LetterStars.AnyAsync(s => s.LetterId == letterId && s.ApplicationUserId == owner.Id))
                    _ctx.LetterStars.Add(new LetterStar { LetterId = letterId, ApplicationUserId = owner.Id });

                rule.MatchCount++;
                rule.LastMatchedDate = DateTime.Now;
                matched.Add(rule);
                applied++;
            }
            await _ctx.SaveChangesAsync();

            // ارجاع به زیردست فقط اگر هنوز زیردست سازمانی باشد (چارت ممکن است بعد از ذخیره‌ی قانون عوض شده باشد)
            var referRules = matched.Where(r => r.ReferToEmployeeId.HasValue).ToList();
            if (referRules.Count > 0 && _referrals != null && owner.EmployeeId.HasValue)
            {
                var subs = (await _org.GetSubordinatesAsync(owner.EmployeeId.Value)).Select(s => s.EmployeeId).ToHashSet();
                foreach (var r in referRules)
                {
                    var target = r.ReferToEmployeeId!.Value;
                    if (!subs.Contains(target)) continue;
                    if (await _ctx.LetterReferrals.AnyAsync(x => x.LetterId == letterId && x.ReferredToEmployeeId == target)) continue;
                    await _referrals.CreateAsync(letterId, target, null, r.ReferInstruction, null, owner.FullName + " (قانون خودکار)");
                }
            }
        }
        return applied;
    }
}
