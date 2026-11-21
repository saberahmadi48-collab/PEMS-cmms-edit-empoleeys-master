using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

public class FunctionalLocationService : IFunctionalLocationService
{
    private readonly PEMSDbContext _context;

    public FunctionalLocationService(PEMSDbContext context)
    {
        _context = context;
    }

    public async Task<TreeNodePage> GetTreeNodesAsync(int? parentId, string? query, int page, int pageSize)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var q = (query ?? "").Trim();
        var source = _context.FunctionalLocations.AsNoTracking().Where(x => x.IsActive);
        var lower = q.ToLowerInvariant();
        source = q.Length >= 2
            ? source.Where(x => x.Code.ToLower().Contains(lower) || x.Name.ToLower().Contains(lower))
            : source.Where(x => x.ParentId == parentId);
        var rows = await source.OrderBy(x => x.Code).ThenBy(x => x.FunctionalLocationId)
            .Skip((page - 1) * pageSize).Take(pageSize + 1)
            .Select(x => new { x.FunctionalLocationId, x.Code, x.Name, x.Plant, x.Area, x.Unit, HasChildren = x.Children.Any(c => c.IsActive) })
            .ToListAsync();
        var items = rows.Take(pageSize).Select(x => new TreeNodeDto(x.FunctionalLocationId, x.Code + " - " + x.Name,
            string.Join(" / ", new[] { x.Plant, x.Area, x.Unit }.Where(p => !string.IsNullOrWhiteSpace(p))) is { Length: > 0 } sub ? sub : null,
            q.Length < 2 && x.HasChildren)).ToList();
        return new TreeNodePage(items, rows.Count > pageSize);
    }

    public async Task<List<FunctionalLocationListDto>> GetListAsync()
    {
        var all = await _context.FunctionalLocations.Where(x => x.IsActive).ToListAsync();
        var byId = all.ToDictionary(x => x.FunctionalLocationId);

        var equipmentCounts = await _context.Equipments
            .Where(x => x.IsActive && x.FunctionalLocationId != null)
            .GroupBy(x => x.FunctionalLocationId!.Value)
            .Select(g => new { FunctionalLocationId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.FunctionalLocationId, x => x.Count);

        var assetCounts = await _context.Assets
            .Where(x => x.IsActive && x.FunctionalLocationId != null)
            .GroupBy(x => x.FunctionalLocationId!.Value)
            .Select(g => new { FunctionalLocationId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.FunctionalLocationId, x => x.Count);

        return all
            .OrderBy(x => BuildBreadcrumb(x, byId))
            .ThenBy(x => x.FunctionalLocationId)
            .Select(x => new FunctionalLocationListDto
            {
                FunctionalLocationId = x.FunctionalLocationId,
                Code = x.Code,
                Name = x.Name,
                Plant = x.Plant,
                Area = x.Area,
                Unit = x.Unit,
                ParentId = x.ParentId,
                ParentCode = x.ParentId.HasValue && byId.TryGetValue(x.ParentId.Value, out var parent) ? parent.Code : null,
                PathBreadcrumb = BuildBreadcrumb(x, byId),
                EquipmentCount = equipmentCounts.GetValueOrDefault(x.FunctionalLocationId),
                AssetCount = assetCounts.GetValueOrDefault(x.FunctionalLocationId),
                IsActive = x.IsActive
            })
            .ToList();
    }

    public async Task<PagedResult<FunctionalLocationListDto>> GetPagedListAsync(int page, int pageSize)
    {
        var all = await GetListAsync();

        page = page < 1 ? 1 : page;
        pageSize = DataGridPageSize.Normalize(pageSize);

        var items = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new PagedResult<FunctionalLocationListDto> { Items = items, Page = page, PageSize = pageSize, TotalCount = all.Count };
    }

    public async Task<List<FunctionalLocationPickerItemDto>> GetPickerListAsync()
    {
        var all = await _context.FunctionalLocations.Where(x => x.IsActive).ToListAsync();
        var byId = all.ToDictionary(x => x.FunctionalLocationId);

        return all
            .Select(x => new FunctionalLocationPickerItemDto
            {
                FunctionalLocationId = x.FunctionalLocationId,
                Code = x.Code,
                PathBreadcrumb = BuildBreadcrumb(x, byId)
            })
            .OrderBy(x => x.PathBreadcrumb)
            .ToList();
    }

    public async Task<string?> GetBreadcrumbAsync(int? functionalLocationId)
    {
        if (!functionalLocationId.HasValue) return null;

        var all = await _context.FunctionalLocations.ToListAsync();
        var byId = all.ToDictionary(x => x.FunctionalLocationId);

        return byId.TryGetValue(functionalLocationId.Value, out var node) ? BuildBreadcrumb(node, byId) : null;
    }

    public async Task<(bool Success, string? Error, int? Id)> CreateAsync(
        string code, string name, int? parentId, string? plant, string? area, string? unit,
        string? description, string? currentUserName)
    {
        code = code?.Trim() ?? "";
        name = name?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(code)) return (false, "کد Functional Location الزامی است.", null);
        if (string.IsNullOrWhiteSpace(name)) return (false, "نام Functional Location الزامی است.", null);

        if (await _context.FunctionalLocations.AnyAsync(x => x.Code == code && x.IsActive))
            return (false, $"کد «{code}» قبلاً استفاده شده است.", null);

        if (parentId.HasValue && !await _context.FunctionalLocations.AnyAsync(x => x.FunctionalLocationId == parentId.Value && x.IsActive))
            return (false, "Parent انتخاب‌شده نامعتبره.", null);

        var location = new FunctionalLocation
        {
            Code = code,
            Name = name,
            ParentId = parentId,
            Plant = plant,
            Area = area,
            Unit = unit,
            Description = description,
            IsActive = true,
            CreatedDate = DateTime.Now
        };

        await _context.FunctionalLocations.AddAsync(location);
        await _context.SaveChangesAsync();

        return (true, null, location.FunctionalLocationId);
    }

    public async Task<(bool Success, string? Error)> UpdateAsync(
        int id, string code, string name, int? parentId, string? plant, string? area, string? unit,
        string? description, string? currentUserName)
    {
        var location = await _context.FunctionalLocations.FirstOrDefaultAsync(x => x.FunctionalLocationId == id);
        if (location == null) return (false, "Functional Location پیدا نشد.");

        code = code?.Trim() ?? "";
        name = name?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(code)) return (false, "کد Functional Location الزامی است.");
        if (string.IsNullOrWhiteSpace(name)) return (false, "نام Functional Location الزامی است.");

        if (await _context.FunctionalLocations.AnyAsync(x => x.FunctionalLocationId != id && x.Code == code && x.IsActive))
            return (false, $"کد «{code}» قبلاً استفاده شده است.");

        if (parentId == id)
            return (false, "یک Functional Location نمی‌تواند Parent خودش باشد.");

        if (parentId.HasValue && await WouldCreateCycleAsync(id, parentId.Value))
            return (false, "این انتخاب باعث یک حلقه در ساختار درختی می‌شود.");

        location.Code = code;
        location.Name = name;
        location.ParentId = parentId;
        location.Plant = plant;
        location.Area = area;
        location.Unit = unit;
        location.Description = description;

        await _context.SaveChangesAsync();
        return (true, null);
    }

    // طبق «PEMS CMMS — Bulk Define Maintenance Strategy»: جدول FunctionalLocation کوچک است
    // (طبق ممیزی مستقیم دیتابیس: ۳۲ ردیف واقعی) - یک بارگذاری کامل + پیمایش در حافظه، دقیقاً
    // همون الگوی BuildBreadcrumb بالا که از قبل کل جدول را برای مسیر رو-به-بالا می‌خواند؛
    // این‌جا همون داده، فقط رو-به-پایین (Children) پیمایش می‌شود.
    public async Task<List<int>> GetDescendantIdsAsync(IEnumerable<int> functionalLocationIds, bool includeSelf)
    {
        var ids = functionalLocationIds?.Distinct().ToList() ?? new List<int>();
        if (ids.Count == 0) return new List<int>();

        var all = await _context.FunctionalLocations
            .Select(x => new { x.FunctionalLocationId, x.ParentId })
            .ToListAsync();

        var childrenByParent = all
            .Where(x => x.ParentId.HasValue)
            .GroupBy(x => x.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(x => x.FunctionalLocationId).ToList());

        var result = new HashSet<int>();
        var queue = new Queue<int>(ids);
        var visited = new HashSet<int>();

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current)) continue; // جلوگیری از حلقه‌ی بی‌نهایت در صورت داده‌ی نامعتبر

            if (includeSelf || !ids.Contains(current))
                result.Add(current);

            if (childrenByParent.TryGetValue(current, out var children))
                foreach (var child in children)
                    if (!visited.Contains(child))
                        queue.Enqueue(child);
        }

        return result.ToList();
    }

    private async Task<bool> WouldCreateCycleAsync(int id, int proposedParentId)
    {
        var all = await _context.FunctionalLocations.Select(x => new { x.FunctionalLocationId, x.ParentId }).ToListAsync();
        var byId = all.ToDictionary(x => x.FunctionalLocationId, x => x.ParentId);

        var cursor = (int?)proposedParentId;
        while (cursor.HasValue)
        {
            if (cursor.Value == id) return true;
            byId.TryGetValue(cursor.Value, out cursor);
        }

        return false;
    }

    private static string BuildBreadcrumb(FunctionalLocation node, Dictionary<int, FunctionalLocation> byId)
    {
        var parts = new List<string> { node.Code };
        var cursor = node.ParentId;
        var guard = 0;

        while (cursor.HasValue && byId.TryGetValue(cursor.Value, out var parent) && guard++ < 20)
        {
            parts.Insert(0, parent.Code);
            cursor = parent.ParentId;
        }

        return string.Join(" / ", parts);
    }
}
