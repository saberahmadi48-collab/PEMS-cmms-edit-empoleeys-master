using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>
/// تنظیمات سراسری کاتالوگ قطعه (روش تولید شماره‌ی قطعه + قالب). فاز ۱ فقط ذخیره/خواندن پرچم است؛ هیچ بخشی از مسیر ثبت
/// درخواست هنوز آن را مصرف نمی‌کند (فاز ۲). بدون ردیف ذخیره‌شده، رفتار فعلی (MANUAL) پیش‌فرض است.
/// </summary>
public class PartCatalogueSettingsService : IPartCatalogueSettingsService
{
    public const int MaxFormatLength = 50;

    // حروف/ارقام و جداکننده‌های رایج؛ بدون فاصله و کاراکتر کنترلی
    private static readonly Regex AllowedFormat = new(@"^[A-Za-z0-9._/#-]+$", RegexOptions.Compiled);

    // بخش ترتیبی قالب: حداقل سه N پشت‌سرهم (NNN..NNNNNNNNNN)
    private static readonly Regex SequenceToken = new(@"N{3,10}", RegexOptions.Compiled);

    private readonly PEMSDbContext _ctx;
    private readonly IRoleModuleActionService? _actions;

    /// <param name="actions">null فقط برای تست‌های بدون مجوز؛ در DI همیشه مقدار دارد.</param>
    public PartCatalogueSettingsService(PEMSDbContext ctx, IRoleModuleActionService? actions = null)
    {
        _ctx = ctx;
        _actions = actions;
    }

    public async Task<PartCatalogueSettingsDto> GetAsync()
    {
        var row = await _ctx.PartCatalogueSettings.AsNoTracking().OrderBy(x => x.PartCatalogueSettingsId).FirstOrDefaultAsync();
        if (row == null) return new PartCatalogueSettingsDto();

        return new PartCatalogueSettingsDto
        {
            PartNoGenerationMethod = row.PartNoGenerationMethod,
            PartNoFormat = row.PartNoFormat,
            RowVersion = row.RowVersion,
            ModifiedDate = row.ModifiedDate,
            ModifiedBy = row.ModifiedBy,
            IsPersisted = true
        };
    }

    public async Task<PcResult> SaveAsync(string? method, string? format, byte[]? rowVersion, PcActor actor)
    {
        if (_actions == null || !await _actions.HasActionAsync(actor.Roles, PartCatalogueService.Module, "Admin"))
            return PcResult.Fail("Id", "Pc_Err_NotAuthorized");

        method = method?.Trim().ToUpperInvariant();
        if (method == null || !PartNoGenerationMethods.All.Contains(method))
            return PcResult.Fail("PartNoGenerationMethod", "Pc_Err_PartNoMethodInvalid");

        format = string.IsNullOrWhiteSpace(format) ? null : format.Trim();
        if (format != null)
        {
            if (format.Length > MaxFormatLength) return PcResult.Fail("PartNoFormat", "Pc_Err_TooLong");
            if (!AllowedFormat.IsMatch(format)) return PcResult.Fail("PartNoFormat", "Pc_Err_PartNoFormatInvalid");
        }
        if (method == PartNoGenerationMethods.Api)
        {
            if (format == null) return PcResult.Fail("PartNoFormat", "Pc_Err_PartNoFormatRequired");
            if (!SequenceToken.IsMatch(format)) return PcResult.Fail("PartNoFormat", "Pc_Err_PartNoFormatNeedsSequence");
        }

        var row = await _ctx.PartCatalogueSettings.OrderBy(x => x.PartCatalogueSettingsId).FirstOrDefaultAsync();
        if (row == null)
        {
            row = new PartCatalogueSettings();
            _ctx.PartCatalogueSettings.Add(row);
        }
        else if (rowVersion is { Length: > 0 })
        {
            _ctx.Entry(row).Property(x => x.RowVersion).OriginalValue = rowVersion;
        }

        row.PartNoGenerationMethod = method;
        row.PartNoFormat = format;
        row.ModifiedDate = DateTime.Now;

        try
        {
            await _ctx.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return PcResult.Fail("Id", "Pc_Err_Concurrency");
        }
        return PcResult.Ok();
    }
}
