using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

public class TagService : ITagService
{
    private readonly PEMSDbContext _context;

    public TagService(PEMSDbContext context)
    {
        _context = context;
    }

    // سازمانی (OwnerUserId == null) برای همه + برچسب‌های شخصیِ خودِ بیننده؛ برچسب شخصی دیگران هرگز دیده نمی‌شود
    public async Task<List<TagDto>> GetAllAsync(int? viewerUserId = null)
    {
        return await _context.Tags
            .Where(x => x.IsActive && (x.OwnerUserId == null || x.OwnerUserId == viewerUserId))
            .OrderBy(x => x.Name)
            .Select(x => new TagDto
            {
                TagId = x.TagId,
                Name = x.Name,
                Color = x.Color,
                LetterCount = _context.LetterTags.Count(lt => lt.TagId == x.TagId)
            })
            .ToListAsync();
    }

    public async Task<int> CreateAsync(string name, string color, int? createdById)
    {
        var existing = await _context.Tags.FirstOrDefaultAsync(x => x.Name == name && x.IsActive);
        if (existing != null) return existing.TagId;

        var tag = new Tag
        {
            Name = name,
            Color = string.IsNullOrWhiteSpace(color) ? "secondary" : color,
            CreatedById = createdById,
            IsActive = true,
            CreatedDate = DateTime.Now
        };

        await _context.Tags.AddAsync(tag);
        await _context.SaveChangesAsync();

        return tag.TagId;
    }

    public async Task DeleteAsync(int tagId, int? actorUserId = null)
    {
        var tag = await _context.Tags.FirstOrDefaultAsync(x => x.TagId == tagId);
        if (tag == null) return;
        // برچسب شخصی فقط توسط صاحبش غیرفعال می‌شود
        if (tag.OwnerUserId.HasValue && tag.OwnerUserId != actorUserId) return;
        // برچسب دارای زیرمجموعه‌ی فعال غیرفعال نمی‌شود تا درخت یتیم نشود
        if (await _context.Tags.AnyAsync(x => x.ParentTagId == tagId && x.IsActive)) return;

        tag.IsActive = false;
        await _context.SaveChangesAsync();
    }

    public async Task AttachToLetterAsync(int letterId, int tagId, int? actorUserId = null)
    {
        // برچسب شخصیِ دیگران قابل‌استفاده نیست
        var owner = await _context.Tags.Where(x => x.TagId == tagId && x.IsActive).Select(x => new { x.OwnerUserId }).FirstOrDefaultAsync();
        if (owner == null || (owner.OwnerUserId.HasValue && owner.OwnerUserId != actorUserId)) return;

        var existing = await _context.LetterTags
            .FirstOrDefaultAsync(x => x.LetterId == letterId && x.TagId == tagId);

        if (existing != null) return;

        await _context.LetterTags.AddAsync(new LetterTag
        {
            LetterId = letterId,
            TagId = tagId,
            CreatedDate = DateTime.Now
        });

        await _context.SaveChangesAsync();
    }

    public async Task DetachFromLetterAsync(int letterId, int tagId)
    {
        var existing = await _context.LetterTags
            .FirstOrDefaultAsync(x => x.LetterId == letterId && x.TagId == tagId);

        if (existing == null) return;

        _context.LetterTags.Remove(existing);
        await _context.SaveChangesAsync();
    }

    public async Task<List<TagDto>> GetForLetterAsync(int letterId, int? viewerUserId = null)
    {
        return await _context.LetterTags
            .Include(x => x.Tag)
            .Where(x => x.LetterId == letterId && x.Tag != null && x.Tag.IsActive
                && (x.Tag.OwnerUserId == null || x.Tag.OwnerUserId == viewerUserId))
            .Select(x => new TagDto
            {
                TagId = x.Tag!.TagId,
                Name = x.Tag.Name,
                Color = x.Tag.Color
            })
            .ToListAsync();
    }
}
