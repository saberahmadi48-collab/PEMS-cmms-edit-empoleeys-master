using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Shared.Search;

namespace PEMS.Persistence.Services.SearchProviders;

/// <summary>
/// ==============================================================================
/// جستجوی نامه‌نگاری (Correspondence) - طبق مثال دقیق درخواست اصلی: شماره نامه، موضوع،
/// فرستنده، گیرنده، پروژه، یا کلمات داخل متن. برای «کلمات داخل متن» (که ممکن است شبیه به
/// عبارت جستجو نباشند، فقط هم‌معنی/مرتبط باشند)، این Provider علاوه بر LIKE/Full-Text، از
/// Semantic Search واقعی (Ollama Embedding از طریق IKnowledgeBaseService) هم استفاده
/// می‌کند - طبق فاز D.
///
/// طبق «PEMS Phase 2A — Global Search Security» (Category D: Permission + Resource):
/// این‌جا دقیقاً همون قانون محرمانگی LetterService.SearchAsync (منبع حقیقت واقعی، نه یک
/// قانون جدید اختراع‌شده) تکرار می‌شود - بدون آن، یک نامه‌ی Confidential/Restricted که کاربر
/// حق دیدنش را در فهرست عادی Letters ندارد، همچنان با شماره/موضوع/طرف از طریق جستجوی
/// سراسری قابل‌کشف بود.
/// ==============================================================================
/// </summary>
public class LetterSearchProvider : ISearchableEntityProvider
{
    private readonly PEMSDbContext _context;
    private readonly IKnowledgeBaseService _knowledgeBaseService;
    private readonly IPermissionService _permissionService;
    private readonly ICurrentUserContext _currentUser;

    public string EntityType => "Letter";
    public string DisplayName => "نامه‌نگاری (Correspondence)";
    // SCRUM-64 security contract: the rule of the page the results open.
    public SearchAccessRule Access => SearchAccessRule.AnyUser(rowScope: "confidential letters: Correspondence/Approve, registrar or referral recipient");

    public LetterSearchProvider(PEMSDbContext context, IKnowledgeBaseService knowledgeBaseService,
        IPermissionService permissionService, ICurrentUserContext currentUser)
    {
        _context = context;
        _knowledgeBaseService = knowledgeBaseService;
        _permissionService = permissionService;
        _currentUser = currentUser;
    }

    public async Task<(List<GlobalSearchResultItemDto> Items, int TotalCount)> SearchAsync(
        string query, int page, int pageSize, int? projectId)
    {
        var canViewConfidential = await LetterConfidentialityScope.CanViewConfidentialAsync(_permissionService, _currentUser);

        // مرحله ۱ - تطبیق مستقیم (LIKE/Full-Text) روی فیلدهای ساختاریافته
        var likeQuery = _context.Letters
            .Include(x => x.Project)
            .Where(x => x.IsActive)
            .Where(x =>
                x.LetterNumber.Contains(query) ||
                x.Subject.Contains(query) ||
                (x.Sender != null && x.Sender.Contains(query)) ||
                (x.Receiver != null && x.Receiver.Contains(query)) ||
                (x.Project != null && x.Project.ProjectName.Contains(query)));

        if (projectId.HasValue)
            likeQuery = likeQuery.Where(x => x.ProjectId == projectId.Value);

        likeQuery = await LetterConfidentialityScope.ApplyAsync(likeQuery, canViewConfidential, _context, _currentUser);

        var matches = await likeQuery.Take(300).ToListAsync();
        var matchedIds = new HashSet<int>(matches.Select(x => x.LetterId));

        // مرحله ۲ - طبق فاز D: جستجوی معنایی روی «کلمات داخل متن» - نامه‌هایی که در LIKE بالا پیدا
        // نشدند ولی از نظر معنایی به عبارت جستجو نزدیکند (مثلاً کاربر "تاخیر تحویل" می‌زند
        // و نامه‌ای که دقیقاً همین کلمات را ندارد ولی درباره‌اش هست، پیدا می‌شود)
        var semanticIds = new HashSet<int>();
        try
        {
            var semanticResults = await _knowledgeBaseService.SearchAsync(query, topK: 15);
            foreach (var r in semanticResults.Where(r => r.SourceType == "Letter" && r.SimilarityScore >= 0.55))
                semanticIds.Add(r.SourceId);
        }
        catch
        {
            // Ollama در دسترس نیست یا مدل Embedding نصب نیست - فقط از تطبیق مستقیم استفاده
            // می‌شود، بدون اینکه جستجو خراب بشه
        }

        var idsToFetch = semanticIds.Where(id => !matchedIds.Contains(id)).ToList();
        if (idsToFetch.Count > 0)
        {
            var semanticQuery = _context.Letters
                .Include(x => x.Project)
                .Where(x => x.IsActive && idsToFetch.Contains(x.LetterId));

            if (projectId.HasValue)
                semanticQuery = semanticQuery.Where(x => x.ProjectId == projectId.Value);

            // طبق بند بالا: مسیر جستجوی معنایی یک راه دوم برای دور زدن همون محدودیت محرمانگی
            // نیست - دقیقاً همون فیلتر روی این Query هم اعمال می‌شود
            semanticQuery = await LetterConfidentialityScope.ApplyAsync(semanticQuery, canViewConfidential, _context, _currentUser);

            matches.AddRange(await semanticQuery.ToListAsync());
        }

        var scored = matches
            .Select(l => new
            {
                Letter = l,
                Score = Math.Max(
                    SearchScoring.ComputeWeightedScore(query,
                        (l.LetterNumber, 1.0),
                        (l.Subject, 0.8),
                        (l.Sender, 0.6),
                        (l.Receiver, 0.6),
                        (l.Project?.ProjectName, 0.4)),
                    // نتایج فقط-معنایی (که در متن هیچ تطبیق مستقیمی ندارند) امتیاز ثابت
                    // پایین‌تر می‌گیرند تا همیشه بعد از تطبیق‌های مستقیم دیده بشن
                    semanticIds.Contains(l.LetterId) ? SearchScoring.ScoreFuzzy : 0)
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Letter.LetterDate)
            .ToList();

        var totalCount = scored.Count;

        var pageItems = scored
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new GlobalSearchResultItemDto
            {
                EntityType = EntityType,
                Id = x.Letter.LetterId,
                Label = x.Letter.LetterNumber,
                HighlightedLabel = SearchScoring.Highlight(x.Letter.LetterNumber, query),
                SubLabel = string.Join(" — ", new[]
                {
                    x.Letter.Subject,
                    x.Letter.Sender ?? x.Letter.Receiver
                }.Where(s => !string.IsNullOrWhiteSpace(s))),
                SubLabelSuffixKey = x.Letter.LetterType,
                Url = $"/Letter/Details/{x.Letter.LetterId}",
                Score = x.Score
            })
            .ToList();

        return (pageItems, totalCount);
    }
}
