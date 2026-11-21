using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Shared.Search;

namespace PEMS.Persistence.Services.SearchProviders;

/// <summary>
/// جستجوی مدارک مهندسی (MDR) - طبق مثال درخواست اصلی: کاربر با Document Number، بخشی از
/// عنوان، یا بخشی از Description بتواند مدرک را پیدا کند.
///
/// طبق «PEMS Phase 2A — Global Search Security» (Category C: Permission + Resource): این‌جا
/// دقیقاً همون قانون محدودیت دیسیپلین EngineeringDocumentsController.Index/
/// ScopeToOwnDisciplineIfRestrictedAsync (منبع حقیقت واقعی) تکرار می‌شود - بدون آن، یک
/// Discipline Engineer که در فهرست عادی فقط مدارک دیسیپلین خودش را می‌بیند، همچنان می‌توانست
/// شماره/عنوان مدارک هر دیسیپلین دیگری را از طریق جستجوی سراسری کشف کند.
/// </summary>
public class EngineeringDocumentSearchProvider : ISearchableEntityProvider
{
    private readonly PEMSDbContext _context;
    private readonly IKnowledgeBaseService _knowledgeBaseService;
    private readonly IAccessControlService _accessControlService;
    private readonly ICurrentUserContext _currentUser;

    public string EntityType => "EngineeringDocument";
    public string DisplayName => "مدارک مهندسی (MDR)";
    // SCRUM-64 security contract: the rule of the page the results open.
    public SearchAccessRule Access => SearchAccessRule.AnyUser(rowScope: "own discipline unless EngineeringDocuments/ViewAllDisciplines");

    public EngineeringDocumentSearchProvider(PEMSDbContext context, IKnowledgeBaseService knowledgeBaseService,
        IAccessControlService accessControlService, ICurrentUserContext currentUser)
    {
        _context = context;
        _knowledgeBaseService = knowledgeBaseService;
        _accessControlService = accessControlService;
        _currentUser = currentUser;
    }

    public async Task<(List<GlobalSearchResultItemDto> Items, int TotalCount)> SearchAsync(
        string query, int page, int pageSize, int? projectId)
    {
        var (isRestricted, disciplineId) = await EngineeringDocumentDisciplineScope.ResolveAsync(
            _context, _accessControlService, _currentUser);

        var matches = await TryFullTextSearchAsync(query, projectId, isRestricted, disciplineId);

        if (matches == null)
        {
            var candidatesQuery = _context.Set<PEMS.Domain.Entities.EngineeringDocument>()
                .Include(x => x.Discipline)
                .Where(x => x.IsActive)
                .Where(x =>
                    x.DocumentNo.Contains(query) ||
                    x.Title.Contains(query) ||
                    (x.Description != null && x.Description.Contains(query)));

            if (projectId.HasValue)
                candidatesQuery = candidatesQuery.Where(x => x.ProjectId == projectId.Value);

            candidatesQuery = EngineeringDocumentDisciplineScope.Apply(candidatesQuery, isRestricted, disciplineId);

            matches = await candidatesQuery.Take(500).ToListAsync();

            // طبق همون محدودیت شناخته‌شده‌ی فاز A: نمونه‌ی Fallback برای Fuzzy وقتی LIKE کم نتیجه داد
            if (matches.Count < 5 && query.Length is >= 2 and <= 25)
            {
                var fallbackQuery = _context.Set<PEMS.Domain.Entities.EngineeringDocument>()
                    .Include(x => x.Discipline).Where(x => x.IsActive);
                if (projectId.HasValue) fallbackQuery = fallbackQuery.Where(x => x.ProjectId == projectId.Value);
                fallbackQuery = EngineeringDocumentDisciplineScope.Apply(fallbackQuery, isRestricted, disciplineId);

                var fallbackPool = await fallbackQuery.OrderByDescending(x => x.CreatedDate).Take(500).ToListAsync();
                foreach (var doc in fallbackPool)
                {
                    if (matches.All(x => x.DocumentId != doc.DocumentId))
                        matches.Add(doc);
                }
            }
        }

        // ==============================================================================
        // طبق فاز D: علاوه بر تطبیق متنی/Fuzzy، جستجوی معنایی هم اضافه می‌شود - برای وقتی
        // که کاربر مفهوم مدرک را می‌نویسد نه لزوماً کلمات دقیق آن (مثلاً "نقشه‌ی لوله‌کشی
        // پمپ اصلی" حتی اگر عنوان دقیق مدرک این کلمات را نداشته باشد).
        // ==============================================================================
        var semanticIds = new HashSet<int>();
        try
        {
            var semanticResults = await _knowledgeBaseService.SearchAsync(query, topK: 15);
            var matchedIds = new HashSet<int>(matches.Select(x => x.DocumentId));

            foreach (var r in semanticResults.Where(r => r.SourceType == "EngineeringDocument" && r.SimilarityScore >= 0.55))
                semanticIds.Add(r.SourceId);

            var idsToFetch = semanticIds.Where(id => !matchedIds.Contains(id)).ToList();
            if (idsToFetch.Count > 0)
            {
                var semanticQuery = _context.Set<PEMS.Domain.Entities.EngineeringDocument>()
                    .Include(x => x.Discipline)
                    .Where(x => x.IsActive && idsToFetch.Contains(x.DocumentId));

                if (projectId.HasValue) semanticQuery = semanticQuery.Where(x => x.ProjectId == projectId.Value);
                semanticQuery = EngineeringDocumentDisciplineScope.Apply(semanticQuery, isRestricted, disciplineId);

                matches.AddRange(await semanticQuery.ToListAsync());
            }
        }
        catch
        {
            // Ollama در دسترس نیست یا مدل Embedding نصب نیست - فقط از تطبیق متنی/Fuzzy
            // بالا استفاده می‌شود، بدون اینکه جستجو خراب بشه
        }

        var scored = matches
            .Select(d => new
            {
                Document = d,
                Score = Math.Max(
                    SearchScoring.ComputeWeightedScore(query,
                        (d.DocumentNo, 1.0),
                        (d.Title, 0.8),
                        (d.Description, 0.6)),
                    semanticIds.Contains(d.DocumentId) ? SearchScoring.ScoreFuzzy : 0)
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Document.DocumentNo)
            .ToList();

        var totalCount = scored.Count;

        var pageItems = scored
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new GlobalSearchResultItemDto
            {
                EntityType = EntityType,
                Id = x.Document.DocumentId,
                Label = x.Document.DocumentNo,
                HighlightedLabel = SearchScoring.Highlight(x.Document.DocumentNo, query),
                SubLabel = string.Join(" — ", new[]
                {
                    x.Document.Title,
                    x.Document.Discipline?.Name,
                    x.Document.Status
                }.Where(s => !string.IsNullOrWhiteSpace(s))),
                Url = $"/EngineeringDocuments/Details/{x.Document.DocumentId}",
                Score = x.Score
            })
            .ToList();

        return (pageItems, totalCount);
    }


    private async Task<List<PEMS.Domain.Entities.EngineeringDocument>?> TryFullTextSearchAsync(
        string query, int? projectId, bool isRestricted, int? disciplineId)
    {
        try
        {
            var ftsTerm = EquipmentSearchProvider.BuildPrefixTerm(query);
            if (ftsTerm == null) return null;

            var ftsQuery = _context.Set<PEMS.Domain.Entities.EngineeringDocument>()
                .Include(x => x.Discipline)
                .Where(x => x.IsActive)
                .Where(x =>
                    EF.Functions.Contains(x.DocumentNo, ftsTerm) ||
                    EF.Functions.Contains(x.Title, ftsTerm) ||
                    (x.Description != null && EF.Functions.Contains(x.Description, ftsTerm)));

            if (projectId.HasValue)
                ftsQuery = ftsQuery.Where(x => x.ProjectId == projectId.Value);

            ftsQuery = EngineeringDocumentDisciplineScope.Apply(ftsQuery, isRestricted, disciplineId);

            return await ftsQuery.Take(500).ToListAsync();
        }
        catch
        {
            return null;
        }
    }
}
