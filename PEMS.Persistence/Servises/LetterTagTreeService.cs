using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

public class LetterTagTreeService : ILetterTagTreeService
{
    private static readonly string[] Colors = { "primary", "secondary", "success", "danger", "warning", "info", "dark" };
    private readonly PEMSDbContext _ctx;

    public LetterTagTreeService(PEMSDbContext ctx) => _ctx = ctx;

    private static string NormalizeColor(string? c) => Colors.Contains((c ?? "").Trim().ToLowerInvariant()) ? c!.Trim().ToLowerInvariant() : "secondary";

    public async Task<List<LetterTagNodeDto>> GetTreeAsync(int userId, bool canManageOrganizational, bool withCounts, bool canViewConfidential)
    {
        var tags = await _ctx.Tags.AsNoTracking().Where(t => t.IsActive && (t.OwnerUserId == null || t.OwnerUserId == userId)).ToListAsync();
        var counts = new Dictionary<int, int>();
        if (withCounts && tags.Count > 0)
        {
            var visible = await LetterVisibility.ApplyAsync(_ctx, _ctx.Letters.AsNoTracking().Where(l => l.IsActive), canViewConfidential, userId);
            var pairs = await (from lt in _ctx.LetterTags.AsNoTracking() join l in visible on lt.LetterId equals l.LetterId select new { lt.TagId, lt.LetterId }).Distinct().ToListAsync();
            var nodes = tags.Select(t => new TagTreeRules.Node(t.TagId, t.ParentTagId, t.OwnerUserId, t.IsActive)).ToList();
            foreach (var t in tags)
            {
                var subtree = TagTreeRules.DescendantsAndSelf(t.TagId, nodes);
                counts[t.TagId] = pairs.Where(p => subtree.Contains(p.TagId)).Select(p => p.LetterId).Distinct().Count();
            }
        }

        var ids = tags.Select(t => t.TagId).ToHashSet();
        var byParent = tags.GroupBy(t => t.ParentTagId.HasValue && ids.Contains(t.ParentTagId.Value) ? t.ParentTagId!.Value : 0)
            .ToDictionary(g => g.Key, g => g.OrderBy(t => t.OwnerUserId.HasValue).ThenBy(t => t.SortOrder).ThenBy(t => t.Name).ToList());

        var result = new List<LetterTagNodeDto>();
        var seen = new HashSet<int>();
        void Walk(int parentKey, int depth)
        {
            foreach (var t in byParent.GetValueOrDefault(parentKey) ?? new())
            {
                if (!seen.Add(t.TagId)) continue;
                result.Add(new LetterTagNodeDto
                {
                    TagId = t.TagId, ParentTagId = parentKey == 0 ? null : parentKey, Name = t.Name, Color = t.Color,
                    IsPersonal = t.OwnerUserId.HasValue, SortOrder = t.SortOrder, Depth = depth,
                    LetterCount = counts.GetValueOrDefault(t.TagId),
                    CanEdit = t.OwnerUserId == userId || (!t.OwnerUserId.HasValue && canManageOrganizational)
                });
                Walk(t.TagId, depth + 1);
            }
        }
        Walk(0, 1);
        return result;
    }

    private async Task<Dictionary<int, TagTreeRules.Node>> NodesAsync()
        => (await _ctx.Tags.AsNoTracking().Select(t => new TagTreeRules.Node(t.TagId, t.ParentTagId, t.OwnerUserId, t.IsActive)).ToListAsync())
            .ToDictionary(n => n.TagId);

    public async Task<TagTreeOpResult> CreateAsync(string name, string? color, int? parentTagId, bool personal, int userId, bool canManageOrganizational)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0 || name.Length > 100) return TagTreeOpResult.Fail("Tag_Err_Name");
        if (!personal && !canManageOrganizational) return TagTreeOpResult.Fail("Tag_Err_NoPermission");

        int? owner = personal ? userId : null;
        var err = TagTreeRules.ValidateParent(null, parentTagId, owner, await NodesAsync());
        if (err != null) return TagTreeOpResult.Fail(err);

        // نام تکراری فقط در یک والد و یک دامنه (سازمانی یا شخصیِ همین کاربر) ممنوع است
        if (await _ctx.Tags.AnyAsync(t => t.IsActive && t.Name == name && t.ParentTagId == parentTagId && t.OwnerUserId == owner))
            return TagTreeOpResult.Fail("Tag_Err_Duplicate");

        var tag = new Tag
        {
            Name = name, Color = NormalizeColor(color), ParentTagId = parentTagId, OwnerUserId = owner,
            CreatedById = userId, IsActive = true, CreatedDate = DateTime.Now
        };
        _ctx.Tags.Add(tag);
        await _ctx.SaveChangesAsync();
        return TagTreeOpResult.Ok(tag.TagId);
    }

    private static bool CanEdit(Tag t, int userId, bool canManageOrganizational) => t.OwnerUserId == userId || (!t.OwnerUserId.HasValue && canManageOrganizational);

    public async Task<TagTreeOpResult> UpdateAsync(int tagId, string name, string? color, int? parentTagId, int sortOrder, int userId, bool canManageOrganizational)
    {
        var tag = await _ctx.Tags.FirstOrDefaultAsync(t => t.TagId == tagId && t.IsActive);
        if (tag == null) return TagTreeOpResult.Fail("Tag_Err_NotFound");
        if (!CanEdit(tag, userId, canManageOrganizational)) return TagTreeOpResult.Fail("Tag_Err_NoPermission");

        name = (name ?? "").Trim();
        if (name.Length == 0 || name.Length > 100) return TagTreeOpResult.Fail("Tag_Err_Name");

        var err = TagTreeRules.ValidateParent(tagId, parentTagId, tag.OwnerUserId, await NodesAsync());
        if (err != null) return TagTreeOpResult.Fail(err);
        if (await _ctx.Tags.AnyAsync(t => t.IsActive && t.TagId != tagId && t.Name == name && t.ParentTagId == parentTagId && t.OwnerUserId == tag.OwnerUserId))
            return TagTreeOpResult.Fail("Tag_Err_Duplicate");

        tag.Name = name; tag.Color = NormalizeColor(color); tag.ParentTagId = parentTagId; tag.SortOrder = Math.Clamp(sortOrder, 0, 9999);
        await _ctx.SaveChangesAsync();
        return TagTreeOpResult.Ok(tag.TagId);
    }

    public async Task<TagTreeOpResult> DeactivateAsync(int tagId, int userId, bool canManageOrganizational)
    {
        var tag = await _ctx.Tags.FirstOrDefaultAsync(t => t.TagId == tagId && t.IsActive);
        if (tag == null) return TagTreeOpResult.Fail("Tag_Err_NotFound");
        if (!CanEdit(tag, userId, canManageOrganizational)) return TagTreeOpResult.Fail("Tag_Err_NoPermission");
        if (await _ctx.Tags.AnyAsync(t => t.ParentTagId == tagId && t.IsActive)) return TagTreeOpResult.Fail("Tag_Err_HasChildren");
        tag.IsActive = false;
        await _ctx.SaveChangesAsync();
        return TagTreeOpResult.Ok(tagId);
    }

    public async Task<CartableOpResult> ApplyAsync(int tagId, IReadOnlyCollection<int> letterIds, bool attach, int userId, bool canViewConfidential)
    {
        var tag = await _ctx.Tags.AsNoTracking().FirstOrDefaultAsync(t => t.TagId == tagId && t.IsActive);
        if (tag == null || !TagTreeRules.IsVisibleTo(tag.OwnerUserId, userId)) return CartableOpResult.Fail("Tag_Err_NotFound");

        var wanted = (letterIds ?? Array.Empty<int>()).Where(x => x > 0).Distinct().Take(200).ToList();
        var visible = await LetterVisibility.ApplyAsync(_ctx, _ctx.Letters.AsNoTracking().Where(l => l.IsActive), canViewConfidential, userId);
        var ids = await visible.Where(l => wanted.Contains(l.LetterId)).Select(l => l.LetterId).ToListAsync();
        if (ids.Count == 0) return CartableOpResult.Fail("Cart_Err_NothingToDo");

        var existing = await _ctx.LetterTags.Where(lt => lt.TagId == tagId && ids.Contains(lt.LetterId)).ToListAsync();
        var changed = 0;
        if (attach)
        {
            foreach (var id in ids.Where(id => existing.All(e => e.LetterId != id)))
            {
                _ctx.LetterTags.Add(new LetterTag { LetterId = id, TagId = tagId, CreatedDate = DateTime.Now });
                changed++;
            }
        }
        else
        {
            _ctx.LetterTags.RemoveRange(existing);
            changed = existing.Count;
        }
        if (changed > 0) await _ctx.SaveChangesAsync();
        return changed == 0 ? CartableOpResult.Fail("Cart_Err_NothingToDo") : CartableOpResult.Ok(changed);
    }
}
