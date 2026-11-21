using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Shared.Search;

namespace PEMS.Persistence.Services.SearchProviders;

/// <summary>
/// طبق «PEMS Phase 2A — Global Search Security» (Category A: Permission only):
/// WorkOrderController.Index خودش قبل از هرچیز HasPermissionAsync(..., "WorkOrder", "View")
/// را بررسی می‌کند - این‌جا هم عیناً همون مجوز اعمال می‌شود تا کاربری که حتی حق دیدن فهرست
/// عادی WorkOrder را ندارد، از طریق جستجوی سراسری هم داده‌ای نبیند.
/// </summary>
public class WorkOrderSearchProvider : ISearchableEntityProvider
{
    private readonly PEMSDbContext _context;
    private readonly IPermissionService _permissionService;
    private readonly ICurrentUserContext _currentUser;

    public string EntityType => "WorkOrder";
    public string DisplayName => "دستورکارهای تعمیر و نگهداری (CMMS)";
    // SCRUM-64 security contract: the rule of the page the results open.
    public SearchAccessRule Access => SearchAccessRule.Permission("WorkOrder");

    public WorkOrderSearchProvider(PEMSDbContext context, IPermissionService permissionService, ICurrentUserContext currentUser)
    {
        _context = context;
        _permissionService = permissionService;
        _currentUser = currentUser;
    }

    public async Task<(List<GlobalSearchResultItemDto> Items, int TotalCount)> SearchAsync(
        string query, int page, int pageSize, int? projectId)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "WorkOrder", "View"))
            return (new List<GlobalSearchResultItemDto>(), 0);

        var pool = await _context.WorkOrders
            .Include(x => x.Equipment)
            .Where(x => x.IsActive)
            .Where(x => x.WorkOrderNumber.Contains(query) || (x.Description != null && x.Description.Contains(query))
                || (x.Equipment != null && x.Equipment.TagNumber.Contains(query)))
            .Take(500)
            .ToListAsync();

        if (pool.Count < 5 && query.Length is >= 2 and <= 25)
        {
            var fallback = await _context.WorkOrders.Include(x => x.Equipment).Where(x => x.IsActive)
                .OrderByDescending(x => x.CreatedDate).Take(500).ToListAsync();
            foreach (var w in fallback)
                if (pool.All(x => x.WorkOrderId != w.WorkOrderId)) pool.Add(w);
        }

        var scored = pool
            .Select(w => new
            {
                WorkOrder = w,
                Score = SearchScoring.ComputeWeightedScore(query,
                    (w.WorkOrderNumber, 1.0), (w.Description, 0.6), (w.Equipment?.TagNumber, 0.8))
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score).ThenBy(x => x.WorkOrder.WorkOrderNumber)
            .ToList();

        var totalCount = scored.Count;
        var pageItems = scored.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new GlobalSearchResultItemDto
            {
                EntityType = EntityType,
                Id = x.WorkOrder.WorkOrderId,
                Label = x.WorkOrder.WorkOrderNumber,
                HighlightedLabel = SearchScoring.Highlight(x.WorkOrder.WorkOrderNumber, query),
                SubLabel = string.Join(" — ", new[] { x.WorkOrder.Equipment?.TagNumber, x.WorkOrder.Status, x.WorkOrder.Priority }.Where(s => !string.IsNullOrWhiteSpace(s))),
                Url = $"/WorkOrder/Details/{x.WorkOrder.WorkOrderId}",
                Score = x.Score
            }).ToList();

        return (pageItems, totalCount);
    }
}
