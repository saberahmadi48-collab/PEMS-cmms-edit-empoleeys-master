using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Shared.Search;

namespace PEMS.Persistence.Services.SearchProviders;

/// <summary>
/// ==============================================================================
/// نمونه‌ی اثبات صحت Global Search Framework (فاز A) - نه شروع فاز B. طبق مثال اصلی
/// درخواست: جستجوی "PI"، "101"، "PUMP"، "MCC" باید تجهیز درست را پیدا کند.
///
/// روش کار: چون SQL Server معمولی (بدون Full-Text Search که به فاز C موکول شده) توانایی
/// Fuzzy واقعی در سطح Query ندارد، این Provider یک نامزد نسبتاً وسیع با LIKE از دیتابیس
/// می‌گیرد (StartsWith/Contains روی چند فیلد)، سپس امتیازدهی دقیق (شامل Fuzzy سبک) و
/// مرتب‌سازی نهایی را در حافظه با SearchScoring انجام می‌دهد.
/// ==============================================================================
/// </summary>
public class EquipmentSearchProvider : ISearchableEntityProvider
{
    private readonly PEMSDbContext _context;

    public string EntityType => "Equipment";
    public string DisplayName => "تجهیزات";

    public EquipmentSearchProvider(PEMSDbContext context)
    {
        _context = context;
    }

    public async Task<(List<GlobalSearchResultItemDto> Items, int TotalCount)> SearchAsync(
        string query, int page, int pageSize, int? projectId)
    {
        var matches = await TryFullTextSearchAsync(query, projectId);

        // ==============================================================================
        // فاز C: اگر Full-Text Search فعال باشه (اسکریپت EnableFullTextSearch.sql اجرا شده)
        // از همون‌جا نتیجه می‌گیریم - دقیق‌تر و برای Fuzzy واقعی بهتره. اگر null برگشت یعنی
        // یا نصب نیست یا هنوز Index ساخته نشده - بدون هیچ خطایی به روش LIKE قبلی برمی‌گردیم.
        // ==============================================================================
        if (matches == null)
        {
            // جستجوی بی‌اعتنا به جداکننده‌ها: «FT 6001» و «FT6001» هم تگ «80-FT-6001» را پیدا می‌کنند (خط‌تیره/فاصله/نقطه/اسلش)
            var squashed = Squash(query);

            var candidatesQuery = _context.Equipments
                .Include(x => x.Discipline)
                .Where(x => x.IsActive)
                .Where(x =>
                    x.TagNumber.Contains(query) ||
                    (squashed.Length >= 3 && x.TagNumber.Replace("-", "").Replace(" ", "").Replace(".", "").Replace("/", "").Replace("_", "").Contains(squashed)) ||
                    x.Name.Contains(query) ||
                    (x.Description != null && x.Description.Contains(query)) ||
                    (x.Manufacturer != null && x.Manufacturer.Contains(query)));

            if (projectId.HasValue)
                candidatesQuery = candidatesQuery.Where(x => x.ProjectId == projectId.Value);

            matches = await candidatesQuery.Take(500).ToListAsync();
        }

        var scored = matches
            .Select(eq => new
            {
                Equipment = eq,
                Score = Math.Max(
                    SearchScoring.ComputeWeightedScore(query,
                        (eq.TagNumber, 1.0),
                        (eq.Name, 0.8),
                        (eq.Description, 0.6),
                        (eq.Manufacturer, 0.5)),
                    // تطبیق بدون جداکننده روی تگ (کمی کم‌وزن‌تر از تطبیق دقیق)
                    (int)Math.Round(SearchScoring.ComputeFieldScore(Squash(eq.TagNumber), Squash(query)) * 0.9))
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Equipment.TagNumber)
            .ToList();

        var totalCount = scored.Count;

        var pageItems = scored
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new GlobalSearchResultItemDto
            {
                EntityType = EntityType,
                Id = x.Equipment.EquipmentId,
                Label = x.Equipment.TagNumber,
                HighlightedLabel = SearchScoring.Highlight(x.Equipment.TagNumber, query),
                SubLabel = string.Join(" — ", new[]
                {
                    x.Equipment.Name,
                    x.Equipment.Discipline?.Name
                }.Where(s => !string.IsNullOrWhiteSpace(s))),
                Url = $"/Equipment/Details/{x.Equipment.EquipmentId}",
                Score = x.Score
            })
            .ToList();

        return (pageItems, totalCount);
    }


    // ==============================================================================
    // فاز C: تلاش با CONTAINS واقعی SQL Server Full-Text Search (اگه اسکریپت
    // EnableFullTextSearch.sql اجرا شده باشه). اگه Index هنوز نساخته شده یا FTS نصب نیست،
    // SQL Server خطا می‌ده (مثلاً "Cannot use a CONTAINS or FREETEXT predicate on column
    // ... because it is not full-text indexed") - این‌جا امن گرفته می‌شه و null برمی‌گرده
    // تا مسیر LIKE قبلی (که همیشه کار می‌کنه) جایگزینش بشه.
    // ==============================================================================
    private async Task<List<Equipment>?> TryFullTextSearchAsync(string query, int? projectId)
    {
        try
        {
            // Full-Text فقط روی SQL Server رابطه‌ای معنا دارد (InMemory/تست: مستقیم LIKE)
            if (!_context.Database.IsRelational()) return null;

            var ftsTerm = BuildPrefixTerm(query);
            if (ftsTerm == null) return null; // عبارت خیلی کوتاه/نامعتبر برای FTS

            var ftsQuery = _context.Equipments
                .Include(x => x.Discipline)
                .Where(x => x.IsActive)
                .Where(x =>
                    EF.Functions.Contains(x.TagNumber, ftsTerm) ||
                    EF.Functions.Contains(x.Name, ftsTerm) ||
                    (x.Description != null && EF.Functions.Contains(x.Description, ftsTerm)) ||
                    (x.Manufacturer != null && EF.Functions.Contains(x.Manufacturer, ftsTerm)));

            if (projectId.HasValue)
                ftsQuery = ftsQuery.Where(x => x.ProjectId == projectId.Value);

            return await ftsQuery.Take(500).ToListAsync();
        }
        catch
        {
            // طبق طراحی: هر خطایی (FTS نصب نیست، Index نیست، ...) یعنی برگرد به LIKE - بدون
            // اینکه کاربر متوجه بشه یا جستجو خراب بشه
            return null;
        }
    }

    internal static string Squash(string? text)
        => new string((text ?? "").Where(c => c is not ('-' or ' ' or '.' or '/' or '_')).ToArray());

    // یک عبارت کوتاه یا شامل کاراکترهای خاص، تبدیل به Prefix Term امن برای CONTAINS می‌شود:
    // "query*" - برای پشتیبانی از Search-As-You-Type (نه فقط کلمه‌ی کامل)
    internal static string? BuildPrefixTerm(string query)
    {
        var cleaned = query.Trim().Replace("\"", "").Replace("'", "");
        if (cleaned.Length == 0) return null;

        // اعداد/حروف تنها - کاراکترهای خاص FTS (& | ! ( ) و ...) می‌تونن Syntax رو خراب کنن
        if (cleaned.Any(c => "&|!()~\"".Contains(c))) return null;

        return $"\"{cleaned}*\"";
    }
}
