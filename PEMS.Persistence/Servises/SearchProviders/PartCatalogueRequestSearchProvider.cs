using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Shared.Search;

namespace PEMS.Persistence.Services.SearchProviders;

/// <summary>
/// جستجوی سراسری درخواست‌های کاتالوگ قطعه (Warehouse). همان قاعده‌ی دیدِ سرویس را اعمال می‌کند: کاربری که مجوز
/// PartCatalogue.View/Review/Approve/Admin ندارد هیچ نتیجه‌ای نمی‌بیند؛ کاربر عادی فقط درخواست‌های خودش یا گام‌هایی که
/// گیرنده‌ی آن‌هاست؛ Review/Approve/Admin همه را می‌بینند.
/// </summary>
public class PartCatalogueRequestSearchProvider : ISearchableEntityProvider
{
    private readonly PEMSDbContext _context;
    private readonly ICurrentUserContext _user;
    private readonly IRoleModuleActionService _actions;

    public string EntityType => "PartCatalogueRequest";
    public string DisplayName => "Part Catalogue Requests";
    // SCRUM-64 security contract: the rule of the page the results open.
    public SearchAccessRule Access => SearchAccessRule.AnyUser(rowScope: "PartCatalogue actions: own, assigned or all for reviewers");

    public PartCatalogueRequestSearchProvider(PEMSDbContext context, ICurrentUserContext user, IRoleModuleActionService actions)
    {
        _context = context;
        _user = user;
        _actions = actions;
    }

    public async Task<(List<GlobalSearchResultItemDto> Items, int TotalCount)> SearchAsync(
        string query, int page, int pageSize, int? projectId)
    {
        var empty = (new List<GlobalSearchResultItemDto>(), 0);
        if (_user.EmployeeId is not int me) return empty;

        var roles = _user.Roles;
        var canView = await _actions.HasActionAsync(roles, "PartCatalogue", "View");
        var seeAll = await _actions.HasActionAsync(roles, "PartCatalogue", "Admin")
                     || await _actions.HasActionAsync(roles, "PartCatalogue", "Review")
                     || await _actions.HasActionAsync(roles, "PartCatalogue", "Approve");
        if (!canView && !seeAll) return empty;

        var q = _context.PartCatalogueRequests.AsNoTracking().Where(r => r.IsActive);
        if (!seeAll)
        {
            var pool = await _context.PartCatalogueApproverAssignments
                .Where(a => a.EmployeeId == me && a.IsActive).Select(a => a.StepKey).ToListAsync();
            q = q.Where(r => r.RequesterEmployeeId == me
                             || (r.WorkflowInstanceId == null && r.Steps.Any(s => s.AssignedEmployeeId == me || pool.Contains(s.StepKey)))
                             || (r.WorkflowInstanceId != null && _context.WorkflowInstanceStageCandidates.Any(c => c.EmployeeId == me && c.InstanceStage!.WorkflowInstanceId == r.WorkflowInstanceId)));
        }

        q = q.Where(r => (r.RequestNumber != null && r.RequestNumber.Contains(query))
                         || (r.AssignedPartNumber != null && r.AssignedPartNumber.Contains(query))
                         || (r.ExistingPartNumber != null && r.ExistingPartNumber.Contains(query))
                         || (r.TechnicalClass != null && (r.TechnicalClass.Code.Contains(query) || r.TechnicalClass.Description.Contains(query))));

        var pool300 = await q.Include(r => r.TechnicalClass).OrderByDescending(r => r.CreatedDate).Take(300).ToListAsync();

        var scored = pool300
            .Select(r => new
            {
                Item = r,
                Score = SearchScoring.ComputeWeightedScore(query,
                    (r.RequestNumber ?? string.Empty, 1.0), (r.AssignedPartNumber ?? string.Empty, 1.0),
                    (r.TechnicalClass?.Code ?? string.Empty, 0.7), (r.TechnicalClass?.Description ?? string.Empty, 0.6))
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score).ThenByDescending(x => x.Item.CreatedDate)
            .ToList();

        var items = scored.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x =>
            {
                var label = x.Item.RequestNumber ?? $"#{x.Item.PartCatalogueRequestId}";
                return new GlobalSearchResultItemDto
                {
                    EntityType = EntityType,
                    Id = x.Item.PartCatalogueRequestId,
                    Label = label,
                    HighlightedLabel = SearchScoring.Highlight(label, query),
                    SubLabel = string.Join(" — ", new[]
                    {
                        x.Item.RequestType, x.Item.TechnicalClass?.Description, x.Item.AssignedPartNumber, x.Item.Status
                    }.Where(s => !string.IsNullOrWhiteSpace(s))),
                    Url = $"/Warehouse/PartCatalogueRequest/Details/{x.Item.PartCatalogueRequestId}",
                    Score = x.Score
                };
            }).ToList();

        return (items, scored.Count);
    }
}
