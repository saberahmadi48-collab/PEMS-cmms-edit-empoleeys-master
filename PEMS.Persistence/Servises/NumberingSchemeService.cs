using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Application.Numbering;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>
/// SCRUM-40: مدیریت الگوهای شماره‌گذاری اسناد تدارکات. تخصیص خود شماره در NumberingService است؛ این‌جا فقط الگو ذخیره، اعتبارسنجی
/// و پیش‌نمایش می‌شود (پیش‌نمایش شمارنده را مصرف نمی‌کند).
/// </summary>
public class NumberingSchemeService : INumberingSchemeService
{
    private readonly PEMSDbContext _context;

    public NumberingSchemeService(PEMSDbContext context) => _context = context;

    public static readonly IReadOnlyList<string> ProcurementDocumentTypes = new[]
    {
        "MaterialRequisition", "ProcurementPackage", "Tender", "Bid", "PurchaseOrder", "Contract",
    };

    /// <summary>Default patterns, seeded active (owner decision 2026-10-27: procurement numbers are automatic).</summary>
    public static readonly IReadOnlyDictionary<string, string> DefaultPatterns = new Dictionary<string, string>
    {
        ["MaterialRequisition"] = "MR-{YYYY}-{SEQ}",
        ["ProcurementPackage"] = "PP-{YYYY}-{SEQ}",
        ["Tender"] = "TND-{YYYY}-{SEQ}",
        ["Bid"] = "BID-{YYYY}-{SEQ}",
        ["PurchaseOrder"] = "PO-{YYYY}-{SEQ}",
        ["Contract"] = "CT-{YYYY}-{SEQ}",
    };

    public IReadOnlyList<string> DocumentTypes => ProcurementDocumentTypes;

    public async Task<IReadOnlyList<NumberingSchemeView>> GetAllAsync()
    {
        var schemes = await _context.NumberingSchemes.AsNoTracking().ToListAsync();
        var result = new List<NumberingSchemeView>();
        foreach (var type in ProcurementDocumentTypes)
        {
            var s = schemes.FirstOrDefault(x => x.DocumentType == type) ?? Default(type, isActive: false);
            result.Add(new NumberingSchemeView(type, s.Pattern, s.PadWidth, s.Calendar, s.ResetPeriod, s.IsActive, s.AllowManualEntry,
                s.NumberingSchemeId == 0 ? 0 : s.Version, s.RowVersion is { } rv ? Convert.ToBase64String(rv) : null,
                await PeekAsync(s, NumberingContext.Empty), s.ModifiedBy, s.ModifiedDate));
        }
        return result;
    }

    public async Task<string?> SaveAsync(NumberingSchemeInput input)
    {
        if (!ProcurementDocumentTypes.Contains(input.DocumentType)) return "Num_Err_DocumentType";
        var pattern = input.Pattern?.Trim() ?? "";
        if (NumberingPattern.Validate(pattern, input.PadWidth, input.Calendar, input.ResetPeriod) is { } error) return error;

        var scheme = await _context.NumberingSchemes.FirstOrDefaultAsync(x => x.DocumentType == input.DocumentType);
        if (scheme == null)
        {
            scheme = new NumberingScheme { DocumentType = input.DocumentType, Version = 0 };
            _context.NumberingSchemes.Add(scheme);
        }
        else if (!string.IsNullOrEmpty(input.RowVersion))
        {
            _context.Entry(scheme).Property(x => x.RowVersion).OriginalValue = Convert.FromBase64String(input.RowVersion);
        }

        scheme.Pattern = pattern;
        scheme.PadWidth = input.PadWidth;
        scheme.Calendar = input.Calendar;
        scheme.ResetPeriod = input.ResetPeriod;
        scheme.IsActive = input.IsActive;
        scheme.AllowManualEntry = input.AllowManualEntry;
        scheme.Version += 1;
        scheme.ModifiedDate = DateTime.Now;

        try { await _context.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { return "Num_Err_Concurrency"; }
        return null;
    }

    public (string? Number, string? Error) Preview(NumberingSchemeInput input, NumberingContext context)
    {
        var pattern = input.Pattern?.Trim() ?? "";
        if (NumberingPattern.Validate(pattern, input.PadWidth, input.Calendar, input.ResetPeriod) is { } error) return (null, error);
        var scheme = new NumberingScheme { DocumentType = input.DocumentType, Pattern = pattern, PadWidth = input.PadWidth, Calendar = input.Calendar, ResetPeriod = input.ResetPeriod };
        return (NumberingPattern.Format(scheme, DateTime.Now, context, 1), null);
    }

    public async Task<DocumentNumberField> GetFieldAsync(string documentType)
    {
        var scheme = await _context.NumberingSchemes.AsNoTracking().FirstOrDefaultAsync(x => x.DocumentType == documentType && x.IsActive);
        if (scheme == null) return new DocumentNumberField(false, true, null);
        // the form does not know the project/discipline yet: show the shape (PO-{PROJECT}-1405-####) instead of a wrong exact number
        var hint = scheme.Pattern.Contains(NumberingPattern.Project) || scheme.Pattern.Contains(NumberingPattern.Discipline)
            ? NumberingPattern.Shape(scheme, DateTime.Now)
            : await PeekAsync(scheme, NumberingContext.Empty);
        return new DocumentNumberField(true, scheme.AllowManualEntry, hint);
    }

    /// <summary>The number the next document would get, without consuming it (codes shown as X when the pattern uses them).</summary>
    private async Task<string> PeekAsync(NumberingScheme scheme, NumberingContext context)
    {
        var now = DateTime.Now;
        var scope = NumberingPattern.ScopeKey(scheme, now, context);
        var current = await _context.NumberingSequences.AsNoTracking()
            .Where(x => x.DocumentType == scheme.DocumentType && x.ScopeKey == scope).Select(x => (int?)x.CurrentValue).FirstOrDefaultAsync() ?? 0;
        return NumberingPattern.Format(scheme, now, context, current + 1);
    }

    public static NumberingScheme Default(string documentType, bool isActive) => new()
    {
        DocumentType = documentType,
        Pattern = DefaultPatterns.TryGetValue(documentType, out var p) ? p : documentType.ToUpperInvariant()[..Math.Min(4, documentType.Length)] + "-{YYYY}-{SEQ}",
        PadWidth = 4,
        Calendar = NumberingCalendars.Jalali,
        ResetPeriod = NumberingResetPeriods.Yearly,
        IsActive = isActive,
        AllowManualEntry = false,
    };
}
