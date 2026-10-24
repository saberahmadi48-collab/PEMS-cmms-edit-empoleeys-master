using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>
/// رشته‌ی گفتگوی نامه‌ها (نامه + پاسخ‌هایش با ParentLetterId). ریشه با بالا رفتن از والدها و اعضا با پایین آمدن از فرزندان
/// پیدا می‌شود؛ شمارش و فهرست اعضا فقط نامه‌هایی را می‌شمارد که بیننده اجازه‌ی دیدنشان را دارد (هیچ نشتی از نامه‌ی محرمانه).
/// عمق پیمایش محدود و دورها بی‌اثر است.
/// </summary>
internal static class LetterThreadQuery
{
    private const int MaxLevels = 12;

    private sealed record Row(int LetterId, int? ParentLetterId);

    public static async Task<Dictionary<int, LetterThreadInfo>> GetThreadInfoAsync(PEMSDbContext ctx, IReadOnlyCollection<int> letterIds,
        bool canViewConfidential, int? userId)
    {
        var result = new Dictionary<int, LetterThreadInfo>();
        if (letterIds.Count == 0) return result;

        // ۱) بالا رفتن تا ریشه
        var parentOf = new Dictionary<int, int?>();
        var frontier = letterIds.Distinct().ToList();
        for (var level = 0; level < MaxLevels && frontier.Count > 0; level++)
        {
            var rows = await ctx.Letters.AsNoTracking().Where(l => l.IsActive && frontier.Contains(l.LetterId))
                .Select(l => new Row(l.LetterId, l.ParentLetterId)).ToListAsync();
            foreach (var r in rows) parentOf[r.LetterId] = r.ParentLetterId;
            frontier = rows.Where(r => r.ParentLetterId.HasValue && !parentOf.ContainsKey(r.ParentLetterId.Value))
                .Select(r => r.ParentLetterId!.Value).Distinct().ToList();
        }

        int RootOf(int id)
        {
            var seen = new HashSet<int>();
            var cur = id;
            while (parentOf.TryGetValue(cur, out var p) && p.HasValue && seen.Add(cur)) cur = p.Value;
            return cur;
        }
        var rootByLetter = letterIds.Distinct().ToDictionary(id => id, RootOf);
        var roots = rootByLetter.Values.Distinct().ToList();

        // ۲) پایین آمدن از ریشه‌ها و جمع‌آوری همه‌ی اعضا
        var members = new Dictionary<int, HashSet<int>>();   // root -> اعضا (شامل ریشه)
        var owner = new Dictionary<int, int>();               // نامه -> ریشه
        foreach (var r in roots) { members[r] = new HashSet<int> { r }; owner[r] = r; }
        var level2 = roots;
        for (var level = 0; level < MaxLevels && level2.Count > 0; level++)
        {
            var kids = await ctx.Letters.AsNoTracking().Where(l => l.IsActive && l.ParentLetterId != null && level2.Contains(l.ParentLetterId.Value))
                .Select(l => new Row(l.LetterId, l.ParentLetterId)).ToListAsync();
            var next = new List<int>();
            foreach (var k in kids)
            {
                if (owner.ContainsKey(k.LetterId)) continue;
                var root = owner[k.ParentLetterId!.Value];
                owner[k.LetterId] = root;
                members[root].Add(k.LetterId);
                next.Add(k.LetterId);
            }
            level2 = next;
        }

        // ۳) فقط نامه‌های قابل‌دیدن برای بیننده شمرده می‌شوند
        var all = owner.Keys.ToList();
        var visibleQuery = await LetterVisibility.ApplyAsync(ctx, ctx.Letters.AsNoTracking().Where(l => l.IsActive && all.Contains(l.LetterId)), canViewConfidential, userId);
        var visibleIds = (await visibleQuery.Select(l => l.LetterId).ToListAsync()).ToHashSet();

        foreach (var (id, root) in rootByLetter)
            result[id] = new LetterThreadInfo(root, Math.Max(1, members[root].Count(visibleIds.Contains)));
        return result;
    }

    /// <summary>اعضای قابل‌دیدن رشته‌ی نامه (به‌ترتیب درخت)، با عمق هر عضو.</summary>
    public static async Task<List<LetterThreadMemberDto>> GetMembersAsync(PEMSDbContext ctx, int letterId, bool canViewConfidential, int? userId)
    {
        var info = (await GetThreadInfoAsync(ctx, new[] { letterId }, canViewConfidential, userId)).GetValueOrDefault(letterId);
        if (info == null) return new();

        // کل زیردرخت ریشه
        var collected = new Dictionary<int, (int? Parent, int Depth)> { [info.RootLetterId] = (null, 0) };
        var frontier = new List<int> { info.RootLetterId };
        for (var level = 1; level <= MaxLevels && frontier.Count > 0; level++)
        {
            var kids = await ctx.Letters.AsNoTracking().Where(l => l.IsActive && l.ParentLetterId != null && frontier.Contains(l.ParentLetterId.Value))
                .Select(l => new Row(l.LetterId, l.ParentLetterId)).ToListAsync();
            frontier = new List<int>();
            foreach (var k in kids.Where(k => !collected.ContainsKey(k.LetterId)))
            {
                collected[k.LetterId] = (k.ParentLetterId, level);
                frontier.Add(k.LetterId);
            }
        }

        var ids = collected.Keys.ToList();
        var visible = await LetterVisibility.ApplyAsync(ctx, ctx.Letters.AsNoTracking().Where(l => l.IsActive && ids.Contains(l.LetterId)), canViewConfidential, userId);
        var letters = await visible.Select(l => new
        {
            l.LetterId, l.LetterNumber, l.Subject, l.LetterType, l.Status, l.LetterDate, l.Sender, l.SenderEmployeeId
        }).ToListAsync();

        var employeeIds = letters.Where(l => l.SenderEmployeeId.HasValue).Select(l => l.SenderEmployeeId!.Value).Distinct().ToList();
        var names = employeeIds.Count == 0 ? new Dictionary<int, string>()
            : await ctx.Employees.AsNoTracking().Where(e => employeeIds.Contains(e.EmployeeId))
                .ToDictionaryAsync(e => e.EmployeeId, e => (e.FirstName + " " + e.LastName).Trim());

        // ترتیب درخت: پیمایش عمق‌اول با ترتیب تاریخ
        var byParent = letters.GroupBy(l => collected[l.LetterId].Parent ?? 0).ToDictionary(g => g.Key, g => g.OrderBy(x => x.LetterDate).ThenBy(x => x.LetterId).ToList());
        var ordered = new List<LetterThreadMemberDto>();
        var visibleSet = letters.Select(l => l.LetterId).ToHashSet();
        void Walk(int parentKey, int displayDepth)
        {
            foreach (var l in byParent.GetValueOrDefault(parentKey) ?? new())
            {
                ordered.Add(new LetterThreadMemberDto
                {
                    LetterId = l.LetterId, ParentLetterId = collected[l.LetterId].Parent, Depth = displayDepth,
                    LetterNumber = l.LetterNumber, Subject = l.Subject, LetterType = l.LetterType, Status = l.Status, LetterDate = l.LetterDate,
                    SenderName = l.SenderEmployeeId.HasValue && names.TryGetValue(l.SenderEmployeeId.Value, out var n) ? n : l.Sender,
                    IsCurrent = l.LetterId == letterId
                });
                Walk(l.LetterId, displayDepth + 1);
            }
        }
        Walk(0, 0);

        // نامه‌ای که والدش برای بیننده نامرئی است نباید گم شود: به‌عنوان ریشه‌ی نمایشی اضافه می‌شود
        foreach (var l in letters.Where(l => collected[l.LetterId].Parent.HasValue && !visibleSet.Contains(collected[l.LetterId].Parent!.Value)))
        {
            if (ordered.Any(o => o.LetterId == l.LetterId)) continue;
            ordered.Add(new LetterThreadMemberDto
            {
                LetterId = l.LetterId, ParentLetterId = null, Depth = 0, LetterNumber = l.LetterNumber, Subject = l.Subject, LetterType = l.LetterType,
                Status = l.Status, LetterDate = l.LetterDate, SenderName = l.Sender, IsCurrent = l.LetterId == letterId
            });
            Walk(l.LetterId, 1);
        }
        return ordered;
    }
}
