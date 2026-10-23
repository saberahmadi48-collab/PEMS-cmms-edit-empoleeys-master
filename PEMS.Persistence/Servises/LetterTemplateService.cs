using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>
/// قالب‌های آماده‌ی نامه (Phase 2). قالب فقط مقدار اولیه‌ی فرم ایجاد نامه است و بعد از ثبت هیچ پیوندی با نامه ندارد؛
/// بنابراین ویرایش/غیرفعال‌کردن قالب نامه‌های ثبت‌شده را تغییر نمی‌دهد. حذف فیزیکی وجود ندارد.
/// </summary>
public class LetterTemplateService : ILetterTemplateService
{
    public static readonly string[] LetterTypes = { "Incoming", "Outgoing", "Internal" };
    public static readonly string[] Priorities = { "Low", "Normal", "High", "Urgent" };
    public static readonly string[] Confidentialities = { "Public", "Internal", "Confidential", "Restricted", "HighlyConfidential" };

    private readonly PEMSDbContext _context;
    private readonly IRichTextSanitizerService _sanitizer;

    public LetterTemplateService(PEMSDbContext context, IRichTextSanitizerService sanitizer)
    {
        _context = context;
        _sanitizer = sanitizer;
    }

    private static LetterTemplateDto ToDto(LetterTemplate x) => new()
    {
        LetterTemplateId = x.LetterTemplateId,
        Name = x.Name,
        LetterType = x.LetterType,
        Subject = x.Subject,
        Body = x.Body,
        Priority = x.Priority,
        ConfidentialityLevel = x.ConfidentialityLevel,
        IsActive = x.IsActive
    };

    public async Task<List<LetterTemplateDto>> ListAsync(string? letterType, bool includeInactive)
    {
        var query = _context.LetterTemplates.AsNoTracking().AsQueryable();
        if (!includeInactive) query = query.Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(letterType))
            query = query.Where(x => x.LetterType == letterType || x.LetterType == null);

        var items = await query.OrderBy(x => x.Name).ThenBy(x => x.LetterTemplateId).ToListAsync();
        return items.Select(ToDto).ToList();
    }

    public async Task<LetterTemplateDto?> GetAsync(int templateId)
    {
        var x = await _context.LetterTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.LetterTemplateId == templateId);
        return x == null ? null : ToDto(x);
    }

    public async Task<(bool Success, string? Error, int TemplateId)> SaveAsync(LetterTemplateDto template)
    {
        var name = (template.Name ?? "").Trim();
        var subject = (template.Subject ?? "").Trim();
        var type = string.IsNullOrWhiteSpace(template.LetterType) ? null : template.LetterType.Trim();

        if (name.Length == 0) return (false, "نام قالب الزامی است.", 0);
        if (name.Length > 150) return (false, "نام قالب نباید بیشتر از ۱۵۰ نویسه باشد.", 0);
        if (subject.Length == 0) return (false, "موضوع قالب الزامی است.", 0);
        if (subject.Length > 300) return (false, "موضوع قالب نباید بیشتر از ۳۰۰ نویسه باشد.", 0);
        var unknown = LetterTemplatePlaceholders.Unknown(subject).Concat(LetterTemplatePlaceholders.Unknown(template.Body))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (unknown.Count > 0)
            return (false, "نشانه‌ی جایگزینی ناشناخته: " + string.Join(", ", unknown.Select(u => "{{" + u + "}}"))
                + " - نشانه‌های مجاز: " + string.Join(", ", LetterTemplatePlaceholders.All.Select(a => "{{" + a + "}}")), 0);
        if (type != null && !LetterTypes.Contains(type)) return (false, "نوع نامه‌ی قالب نامعتبر است.", 0);
        if (!Priorities.Contains(template.Priority)) return (false, "اولویت قالب نامعتبر است.", 0);
        if (!Confidentialities.Contains(template.ConfidentialityLevel)) return (false, "سطح محرمانگی قالب نامعتبر است.", 0);

        var duplicate = await _context.LetterTemplates.AnyAsync(x =>
            x.LetterTemplateId != template.LetterTemplateId && x.Name == name && x.LetterType == type);
        if (duplicate) return (false, "قالبی با همین نام برای همین نوع نامه از قبل وجود دارد.", 0);

        LetterTemplate entity;
        if (template.LetterTemplateId == 0)
        {
            entity = new LetterTemplate { CreatedDate = DateTime.Now, IsActive = true };
            await _context.LetterTemplates.AddAsync(entity);
        }
        else
        {
            var existing = await _context.LetterTemplates.FirstOrDefaultAsync(x => x.LetterTemplateId == template.LetterTemplateId);
            if (existing == null) return (false, "قالب یافت نشد.", 0);
            entity = existing;
        }

        entity.Name = name;
        entity.LetterType = type;
        entity.Subject = subject;
        entity.Body = _sanitizer.Sanitize(template.Body);
        entity.Priority = template.Priority;
        entity.ConfidentialityLevel = template.ConfidentialityLevel;

        await _context.SaveChangesAsync();
        return (true, null, entity.LetterTemplateId);
    }

    public async Task<bool> SetActiveAsync(int templateId, bool active)
    {
        var entity = await _context.LetterTemplates.FirstOrDefaultAsync(x => x.LetterTemplateId == templateId);
        if (entity == null) return false;

        entity.IsActive = active;
        await _context.SaveChangesAsync();
        return true;
    }
}
