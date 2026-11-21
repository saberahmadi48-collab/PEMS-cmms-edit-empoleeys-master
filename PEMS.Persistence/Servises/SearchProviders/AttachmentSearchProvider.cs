using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Shared.Search;

namespace PEMS.Persistence.Services.SearchProviders;

/// <summary>
/// جستجوی پیوست‌ها (Attachments) - بر اساس نام فایل و متادیتای استخراج‌شده توسط AI
/// (AISummary/AIKeywords/AITags) هنگام Intake.
///
/// طبق «PEMS Phase 2A — Global Search Security»: یک پیوست یا به یک مدرک مهندسی یا به یک
/// نامه وصل است (یا به هیچ‌کدام، حین Intake پیش از پیوند) - پس همون دو قانون
/// (EngineeringDocumentDisciplineScope/LetterConfidentialityScope) که برای خودِ مدرک/نامه
/// اعمال می‌شود، این‌جا هم لازم است. وگرنه نام فایل/خلاصه‌ی AI یک پیوست محرمانه (روی نامه‌ی
/// Confidential یا مدرک دیسیپلین دیگر) از طریق جستجوی پیوست‌ها قابل‌کشف می‌ماند، حتی اگر
/// خودِ نامه/مدرک والدش درست محدود شده باشد.
/// </summary>
public class AttachmentSearchProvider : ISearchableEntityProvider
{
    private readonly PEMSDbContext _context;
    private readonly IAccessControlService _accessControlService;
    private readonly IPermissionService _permissionService;
    private readonly ICurrentUserContext _currentUser;

    public string EntityType => "Attachment";
    public string DisplayName => "پیوست‌ها";
    // SCRUM-64 security contract: the rule of the page the results open.
    public SearchAccessRule Access => SearchAccessRule.AnyUser(rowScope: "parent engineering document discipline and parent letter confidentiality");

    public AttachmentSearchProvider(PEMSDbContext context, IAccessControlService accessControlService,
        IPermissionService permissionService, ICurrentUserContext currentUser)
    {
        _context = context;
        _accessControlService = accessControlService;
        _permissionService = permissionService;
        _currentUser = currentUser;
    }

    public async Task<(List<GlobalSearchResultItemDto> Items, int TotalCount)> SearchAsync(
        string query, int page, int pageSize, int? projectId)
    {
        var (isDisciplineRestricted, disciplineId) = await EngineeringDocumentDisciplineScope.ResolveAsync(
            _context, _accessControlService, _currentUser);
        var canViewConfidential = await LetterConfidentialityScope.CanViewConfidentialAsync(_permissionService, _currentUser);

        var candidatesQuery = _context.DocumentAttachments
            .Include(x => x.Document)
            .Include(x => x.Letter)
            .Where(x => x.IsActive)
            .Where(x =>
                x.FileName.Contains(query) ||
                (x.AISummary != null && x.AISummary.Contains(query)) ||
                (x.AIKeywords != null && x.AIKeywords.Contains(query)) ||
                (x.AITags != null && x.AITags.Contains(query)));

        if (projectId.HasValue)
            candidatesQuery = candidatesQuery.Where(x => x.IntakeProjectId == projectId.Value);

        // یک پیوست بدون DocumentId/LetterId (هنوز در Intake، پیش از پیوند) بدون محدودیت
        // این دو قانون می‌ماند - محدودیت فقط وقتی اعمال می‌شود که واقعاً به یک مدرک/نامه‌ی
        // مشخص وصل باشد
        if (isDisciplineRestricted)
            candidatesQuery = candidatesQuery.Where(x =>
                x.DocumentId == null || (x.Document != null && (x.Document.DisciplineId == disciplineId || x.Document.DisciplineId == null)));

        if (!canViewConfidential)
        {
            var referredLetterIds = _currentUser.EmployeeId.HasValue
                ? await _context.LetterReferrals.Where(x => x.ReferredToEmployeeId == _currentUser.EmployeeId.Value)
                    .Select(x => x.LetterId).ToListAsync()
                : new List<int>();

            candidatesQuery = candidatesQuery.Where(x =>
                x.LetterId == null ||
                (x.Letter != null && (
                    x.Letter.ConfidentialityLevel == "Public" || x.Letter.ConfidentialityLevel == "Internal" ||
                    (_currentUser.UserId.HasValue && x.Letter.RegisteredById == _currentUser.UserId.Value) ||
                    referredLetterIds.Contains(x.LetterId.Value))));
        }

        var pool = await candidatesQuery.Take(500).ToListAsync();

        var scored = pool
            .Select(a => new
            {
                Attachment = a,
                Score = SearchScoring.ComputeWeightedScore(query,
                    (a.FileName, 1.0), (a.AIKeywords, 0.6), (a.AITags, 0.6), (a.AISummary, 0.4))
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score).ThenByDescending(x => x.Attachment.UploadDate)
            .ToList();

        var totalCount = scored.Count;
        var pageItems = scored.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new GlobalSearchResultItemDto
            {
                EntityType = EntityType,
                Id = x.Attachment.AttachmentId,
                Label = x.Attachment.FileName,
                HighlightedLabel = SearchScoring.Highlight(x.Attachment.FileName, query),
                SubLabel = x.Attachment.AISummary != null && x.Attachment.AISummary.Length > 100
                    ? x.Attachment.AISummary[..100] + "..."
                    : x.Attachment.AISummary,
                // مقصد بسته به این‌که پیوست به کدام مدرک/نامه وصل شده متفاوت است
                Url = x.Attachment.DocumentId.HasValue ? $"/EngineeringDocuments/Details/{x.Attachment.DocumentId}"
                    : x.Attachment.LetterId.HasValue ? $"/Letter/Details/{x.Attachment.LetterId}"
                    : null,
                Score = x.Score
            }).ToList();

        return (pageItems, totalCount);
    }
}
