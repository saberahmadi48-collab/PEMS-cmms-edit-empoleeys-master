using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Application.Organization;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

public class LetterCartableService : ILetterCartableService
{
    private const int MaxBatch = 200;
    private readonly PEMSDbContext _ctx;
    private readonly IOrganizationSubordinateResolver _org;

    public LetterCartableService(PEMSDbContext ctx, IOrganizationSubordinateResolver org)
    {
        _ctx = ctx;
        _org = org;
    }

    private static List<int> Normalize(IReadOnlyCollection<int>? ids) =>
        (ids ?? Array.Empty<int>()).Where(x => x > 0).Distinct().Take(MaxBatch).ToList();

    /// <summary>نامه‌های فعالِ قابل‌دیدن برای کاربر (قانون محرمانگی)، بدون هیچ پوشه‌ای.</summary>
    private async Task<IQueryable<Letter>> VisibleAsync(int userId, bool canViewConfidential)
        => await LetterVisibility.ApplyAsync(_ctx, _ctx.Letters.AsNoTracking().Where(l => l.IsActive), canViewConfidential, userId);

    private async Task<List<int>> VisibleIdsAsync(IReadOnlyCollection<int> ids, int userId, bool canViewConfidential)
    {
        var wanted = Normalize(ids);
        if (wanted.Count == 0) return wanted;
        var visible = await VisibleAsync(userId, canViewConfidential);
        return await visible.Where(l => wanted.Contains(l.LetterId)).Select(l => l.LetterId).ToListAsync();
    }

    private async Task<Dictionary<int, LetterUserState>> StatesAsync(IReadOnlyCollection<int> ids, int userId)
        => await _ctx.LetterUserStates.Where(s => s.ApplicationUserId == userId && ids.Contains(s.LetterId)).ToDictionaryAsync(s => s.LetterId);

    private LetterUserState GetOrAdd(Dictionary<int, LetterUserState> states, int letterId, int userId)
    {
        if (states.TryGetValue(letterId, out var s)) return s;
        s = new LetterUserState { LetterId = letterId, ApplicationUserId = userId };
        _ctx.LetterUserStates.Add(s);
        states[letterId] = s;
        return s;
    }

    // ---------------------------------------------------------------- شمارنده‌ها
    public async Task<CartableCountsDto> GetCountsAsync(int userId, bool canViewConfidential)
    {
        var now = DateTime.Now;
        var actor = await LetterFolderQuery.ResolveActorAsync(_ctx, userId, now);
        var visible = await VisibleAsync(userId, canViewConfidential);
        IQueryable<Letter> In(string? folder) => LetterFolderQuery.Apply(_ctx, visible, folder, actor);

        var inbox = In(LetterFolders.Inbox);
        var dto = new CartableCountsDto
        {
            Inbox = await inbox.CountAsync(),
            Shared = await In(LetterFolders.Shared).CountAsync(),
            Sent = await In(LetterFolders.Sent).CountAsync(),
            Drafts = await In(LetterFolders.Drafts).CountAsync(),
            Starred = await In(LetterFolders.Starred).CountAsync(),
            Archive = await In(LetterFolders.Archive).CountAsync(),
            Trash = await In(LetterFolders.Trash).CountAsync(),
            All = await In(LetterFolders.All).CountAsync(),
            Delegated = actor.DelegatorEmployeeIds.Count == 0 ? 0 : await In(LetterFolders.Delegated).CountAsync()
        };

        if (actor.EmployeeId.HasValue)
        {
            var me = actor.EmployeeId.Value;
            dto.InboxUnread = await inbox.CountAsync(x => _ctx.LetterRecipients.Any(r => r.LetterId == x.LetterId && r.EmployeeId == me && !r.IsRead));
        }

        // شمارش هر برچسب با زیرمجموعه‌ها روی نمای پیش‌فرض (بدون سطل/آرشیو شخصی)
        var tags = await _ctx.Tags.AsNoTracking().Where(t => t.IsActive && (t.OwnerUserId == null || t.OwnerUserId == userId))
            .Select(t => new TagTreeRules.Node(t.TagId, t.ParentTagId, t.OwnerUserId, t.IsActive)).ToListAsync();
        if (tags.Count > 0)
        {
            var pairs = await (from lt in _ctx.LetterTags.AsNoTracking()
                               join l in In(null) on lt.LetterId equals l.LetterId
                               select new { lt.TagId, lt.LetterId }).Distinct().ToListAsync();
            foreach (var t in tags)
            {
                var subtree = TagTreeRules.DescendantsAndSelf(t.TagId, tags);
                dto.TagCounts[t.TagId] = pairs.Where(p => subtree.Contains(p.TagId)).Select(p => p.LetterId).Distinct().Count();
            }
        }
        return dto;
    }

    // ---------------------------------------------------------------- سطل زباله
    public async Task<CartableOpResult> TrashAsync(IReadOnlyCollection<int> letterIds, int userId, bool canViewConfidential)
    {
        var ids = await VisibleIdsAsync(letterIds, userId, canViewConfidential);
        if (ids.Count == 0) return CartableOpResult.Fail("Cart_Err_NothingToDo");
        var states = await StatesAsync(ids, userId);
        var now = DateTime.Now;
        foreach (var id in ids)
        {
            var s = GetOrAdd(states, id, userId);
            if (s.IsPurged) continue;
            s.IsTrashed = true; s.TrashedDate = now; s.IsArchived = false; s.ArchivedDate = null; s.UpdatedDate = now;
        }
        await _ctx.SaveChangesAsync();
        return CartableOpResult.Ok(ids.Count);
    }

    public async Task<CartableOpResult> RestoreAsync(IReadOnlyCollection<int> letterIds, int userId)
    {
        var ids = Normalize(letterIds);
        var states = await _ctx.LetterUserStates.Where(s => s.ApplicationUserId == userId && ids.Contains(s.LetterId) && s.IsTrashed && !s.IsPurged).ToListAsync();
        if (states.Count == 0) return CartableOpResult.Fail("Cart_Err_NothingToDo");
        foreach (var s in states) { s.IsTrashed = false; s.TrashedDate = null; s.UpdatedDate = DateTime.Now; }
        await _ctx.SaveChangesAsync();
        return CartableOpResult.Ok(states.Count);
    }

    public async Task<CartableOpResult> PurgeAsync(IReadOnlyCollection<int> letterIds, int userId)
    {
        var ids = Normalize(letterIds);
        var states = await _ctx.LetterUserStates.Where(s => s.ApplicationUserId == userId && ids.Contains(s.LetterId) && s.IsTrashed && !s.IsPurged).ToListAsync();
        if (states.Count == 0) return CartableOpResult.Fail("Cart_Err_NothingToDo");
        var now = DateTime.Now;
        foreach (var s in states) { s.IsPurged = true; s.PurgedDate = now; s.UpdatedDate = now; }
        await _ctx.SaveChangesAsync();
        return CartableOpResult.Ok(states.Count);
    }

    public async Task<CartableOpResult> EmptyTrashAsync(int userId)
    {
        var states = await _ctx.LetterUserStates.Where(s => s.ApplicationUserId == userId && s.IsTrashed && !s.IsPurged).ToListAsync();
        if (states.Count == 0) return CartableOpResult.Fail("Cart_Err_NothingToDo");
        var now = DateTime.Now;
        foreach (var s in states) { s.IsPurged = true; s.PurgedDate = now; s.UpdatedDate = now; }
        await _ctx.SaveChangesAsync();
        return CartableOpResult.Ok(states.Count);
    }

    // ---------------------------------------------------------------- آرشیو شخصی
    public async Task<CartableOpResult> ArchiveForMeAsync(IReadOnlyCollection<int> letterIds, int userId, bool canViewConfidential)
    {
        var ids = await VisibleIdsAsync(letterIds, userId, canViewConfidential);
        if (ids.Count == 0) return CartableOpResult.Fail("Cart_Err_NothingToDo");
        var states = await StatesAsync(ids, userId);
        var now = DateTime.Now;
        foreach (var id in ids)
        {
            var s = GetOrAdd(states, id, userId);
            if (s.IsPurged || s.IsTrashed) continue;
            s.IsArchived = true; s.ArchivedDate = now; s.UpdatedDate = now;
        }
        await _ctx.SaveChangesAsync();
        return CartableOpResult.Ok(ids.Count);
    }

    public async Task<CartableOpResult> UnarchiveForMeAsync(IReadOnlyCollection<int> letterIds, int userId)
    {
        var ids = Normalize(letterIds);
        var states = await _ctx.LetterUserStates.Where(s => s.ApplicationUserId == userId && ids.Contains(s.LetterId) && s.IsArchived).ToListAsync();
        if (states.Count == 0) return CartableOpResult.Fail("Cart_Err_NothingToDo");
        foreach (var s in states) { s.IsArchived = false; s.ArchivedDate = null; s.UpdatedDate = DateTime.Now; }
        await _ctx.SaveChangesAsync();
        return CartableOpResult.Ok(states.Count);
    }

    // ---------------------------------------------------------------- خوانده / نخوانده
    public async Task<CartableOpResult> SetReadAsync(IReadOnlyCollection<int> letterIds, int userId, bool read, bool canViewConfidential)
    {
        var employeeId = await _ctx.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.EmployeeId).FirstOrDefaultAsync();
        if (!employeeId.HasValue) return CartableOpResult.Fail("Cart_Err_NoEmployee");

        var ids = await VisibleIdsAsync(letterIds, userId, canViewConfidential);
        var rows = await (from r in _ctx.LetterRecipients
                          join l in _ctx.Letters on r.LetterId equals l.LetterId
                          where ids.Contains(r.LetterId) && r.EmployeeId == employeeId && !LetterAccessRules.PreSendStatuses.Contains(l.Status)
                          select r).ToListAsync();
        var changed = 0;
        foreach (var r in rows.Where(r => r.IsRead != read))
        {
            r.IsRead = read;
            r.ReadDate = read ? DateTime.Now : null;
            changed++;
        }
        if (changed > 0) await _ctx.SaveChangesAsync();
        return changed == 0 ? CartableOpResult.Fail("Cart_Err_NothingToDo") : CartableOpResult.Ok(changed);
    }

    // ---------------------------------------------------------------- رشته‌ی گفتگو و تاریخچه
    public Task<List<LetterThreadMemberDto>> GetThreadAsync(int letterId, int? userId, bool canViewConfidential)
        => LetterThreadQuery.GetMembersAsync(_ctx, letterId, canViewConfidential, userId);

    public async Task<List<ConversationMessageDto>> GetConversationAsync(int letterId, int? userId, bool canViewConfidential)
    {
        var members = await LetterThreadQuery.GetMembersAsync(_ctx, letterId, canViewConfidential, userId);
        if (members.Count == 0) return new();
        var ids = members.Select(m => m.LetterId).ToList();

        var letters = await _ctx.Letters.AsNoTracking().Where(l => ids.Contains(l.LetterId)).ToListAsync();
        var recipients = await (from r in _ctx.LetterRecipients.AsNoTracking()
                                where ids.Contains(r.LetterId) && r.RecipientType != "Bcc"
                                join e in _ctx.Employees.AsNoTracking() on r.EmployeeId equals e.EmployeeId into ge
                                from e in ge.DefaultIfEmpty()
                                select new { r.LetterId, r.RecipientType, Name = e != null ? e.FirstName + " " + e.LastName : r.ExternalName }).ToListAsync();
        var senderIds = letters.Where(l => l.SenderEmployeeId.HasValue).Select(l => l.SenderEmployeeId!.Value).Distinct().ToList();
        var senders = await _ctx.Employees.AsNoTracking().Where(e => senderIds.Contains(e.EmployeeId)).ToDictionaryAsync(e => e.EmployeeId, e => (e.FirstName + " " + e.LastName).Trim());
        var attachments = await _ctx.DocumentAttachments.AsNoTracking().Where(a => a.LetterId != null && ids.Contains(a.LetterId.Value))
            .GroupBy(a => a.LetterId!.Value).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count);

        string Join(int id, bool cc) => string.Join("، ", recipients.Where(r => r.LetterId == id && (r.RecipientType == "Cc") == cc && !string.IsNullOrWhiteSpace(r.Name)).Select(r => r.Name!.Trim()));
        return letters.OrderBy(l => l.LetterDate).ThenBy(l => l.CreatedDate).ThenBy(l => l.LetterId).Select(l => new ConversationMessageDto
        {
            LetterId = l.LetterId, ParentLetterId = l.ParentLetterId, LetterNumber = l.LetterNumber, Subject = l.Subject, Body = l.Body,
            LetterDate = l.LetterDate, CreatedDate = l.CreatedDate, Status = l.Status, LetterType = l.LetterType, Priority = l.Priority,
            SenderName = l.SenderEmployeeId.HasValue && senders.TryGetValue(l.SenderEmployeeId.Value, out var n) ? n : l.Sender,
            SenderTitle = l.SenderTitle,
            RecipientsSummary = l.LetterType == "Outgoing" ? (l.Receiver ?? "") : Join(l.LetterId, false),
            CcSummary = Join(l.LetterId, true),
            Kind = l.IsReferral ? "Referral" : l.ParentLetterId.HasValue ? "Reply" : "Original",
            IsCurrent = l.LetterId == letterId,
            AttachmentsCount = attachments.GetValueOrDefault(l.LetterId)
        }).ToList();
    }

    public async Task<List<LetterTimelineEventDto>> FilterHistoryAsync(List<LetterTimelineEventDto> events, string? role, bool newestFirst, int userId)
    {
        var normalized = LetterHistoryFilter.NormalizeRole(role);
        var employeeId = await _ctx.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.EmployeeId).FirstOrDefaultAsync();

        IReadOnlySet<int> managers = new HashSet<int>(), subordinates = new HashSet<int>();
        if (employeeId.HasValue && (normalized == "manager" || normalized == "subordinates"))
        {
            var rel = await _org.GetRelationsAsync(employeeId.Value);
            managers = rel.ManagerEmployeeIds.ToHashSet();
            subordinates = rel.SubordinateIds;
        }
        return LetterHistoryFilter.Apply(events, normalized, newestFirst, employeeId, managers, subordinates);
    }
}
