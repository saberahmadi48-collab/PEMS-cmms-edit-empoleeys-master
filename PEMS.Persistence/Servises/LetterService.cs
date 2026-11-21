using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>
/// پیاده‌سازی ماژول مکاتبات رسمی (Incoming/Outgoing/Internal یکپارچه با LetterType).
/// </summary>
public class LetterService : ILetterService
{
    private readonly PEMSDbContext _context;
    private readonly IRichTextSanitizerService _sanitizer;
    private readonly IKnowledgeBaseService _knowledgeBaseService;
    private readonly INotificationService? _notifications;
    private readonly ILetterRoutingRuleService? _routingRules;

    private static readonly string[] OutgoingStageOrder = { "Draft", "PendingApproval", "Approved", "Issued" };

    public LetterService(PEMSDbContext context, IRichTextSanitizerService sanitizer, IKnowledgeBaseService knowledgeBaseService,
        INotificationService? notifications = null, ILetterRoutingRuleService? routingRules = null)
    {
        _context = context;
        _sanitizer = sanitizer;
        _knowledgeBaseService = knowledgeBaseService;
        _notifications = notifications;
        _routingRules = routingRules;
    }

    public async Task<PagedResult<LetterDto>> SearchAsync(string? letterType, string? letterNumber, string? subject,
        string? sender, string? receiver, int? projectId, int? disciplineId, string? status,
        DateTime? fromDate, DateTime? toDate, int page, int pageSize,
        bool canViewConfidential, int? requestingApplicationUserId, bool starredOnly = false,
        LetterAdvancedFilter? advanced = null)
    {
        var query = await BuildFilteredQueryAsync(letterType, letterNumber, subject, sender, receiver, projectId,
            disciplineId, status, fromDate, toDate, canViewConfidential, requestingApplicationUserId, starredOnly, advanced);

        var totalCount = await query.CountAsync();

        page = page < 1 ? 1 : page;
        // Defense-in-depth طبق سند Global Data Grid Standard - حتی اگر Controller قبلاً
        // Normalize کرده باشد، Service هم دوباره فقط ۲۰/۵۰/۱۰۰ را می‌پذیرد.
        pageSize = DataGridPageSize.Normalize(pageSize);

        var letters = await OrderLetters(query, advanced)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<LetterDto>
        {
            Items = await BuildDtosAsync(letters, requestingApplicationUserId, includeSnippet: true, canViewConfidential: canViewConfidential),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    // خروجی دقیقاً با همان فیلتر و همان قانون محرمانگی Index ساخته می‌شود (یک مسیر Query مشترک)،
    // پس چیزی که کاربر در جدول نمی‌بیند هرگز وارد فایل خروجی نمی‌شود. سقف ردیف برای جلوگیری از بار بی‌رویه.
    public async Task<List<LetterDto>> ExportAsync(string? letterType, string? letterNumber, string? subject,
        string? sender, string? receiver, int? projectId, int? disciplineId, string? status,
        DateTime? fromDate, DateTime? toDate, bool canViewConfidential, int? requestingApplicationUserId,
        bool starredOnly, LetterAdvancedFilter? advanced, int maxRows = 5000)
    {
        var query = await BuildFilteredQueryAsync(letterType, letterNumber, subject, sender, receiver, projectId,
            disciplineId, status, fromDate, toDate, canViewConfidential, requestingApplicationUserId, starredOnly, advanced);

        var letters = await OrderLetters(query, advanced)
            .Take(Math.Clamp(maxRows, 1, 20000))
            .ToListAsync();

        return await BuildDtosAsync(letters, requestingApplicationUserId, canViewConfidential: canViewConfidential);
    }

    private async Task<IQueryable<Letter>> BuildFilteredQueryAsync(string? letterType, string? letterNumber, string? subject,
        string? sender, string? receiver, int? projectId, int? disciplineId, string? status,
        DateTime? fromDate, DateTime? toDate, bool canViewConfidential, int? requestingApplicationUserId,
        bool starredOnly, LetterAdvancedFilter? advanced)
    {
        var query = _context.Letters
            .AsNoTracking()
            .Include(x => x.Project)
            .Include(x => x.Discipline)
            .Include(x => x.RelatedDocument)
            .Include(x => x.RelatedMR)
            .Where(x => x.IsActive)
            .AsQueryable();

        if (starredOnly)
        {
            query = requestingApplicationUserId.HasValue
                ? query.Where(x => _context.LetterStars.Any(s => s.LetterId == x.LetterId && s.ApplicationUserId == requestingApplicationUserId.Value))
                : query.Where(x => false);
        }

        if (!string.IsNullOrWhiteSpace(letterType)) query = query.Where(x => x.LetterType == letterType);
        if (!string.IsNullOrWhiteSpace(letterNumber)) query = query.Where(x => x.LetterNumber.Contains(letterNumber));
        if (!string.IsNullOrWhiteSpace(subject)) query = query.Where(x => x.Subject.Contains(subject));
        if (!string.IsNullOrWhiteSpace(sender)) query = query.Where(x => x.Sender != null && x.Sender.Contains(sender));
        if (!string.IsNullOrWhiteSpace(receiver)) query = query.Where(x => x.Receiver != null && x.Receiver.Contains(receiver));
        if (projectId.HasValue) query = query.Where(x => x.ProjectId == projectId);
        if (disciplineId.HasValue) query = query.Where(x => x.DisciplineId == disciplineId);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);
        if (fromDate.HasValue) query = query.Where(x => x.LetterDate >= fromDate.Value);
        if (toDate.HasValue) query = query.Where(x => x.LetterDate <= toDate.Value);

        if (advanced != null)
        {
            if (!string.IsNullOrWhiteSpace(advanced.Query))
            {
                var q = advanced.Query.Trim();
                query = query.Where(x => x.LetterNumber.Contains(q) || x.Subject.Contains(q)
                    || (x.Sender != null && x.Sender.Contains(q)) || (x.Receiver != null && x.Receiver.Contains(q)));
            }

            // نخوانده = بیننده گیرنده‌ی نامه است و ردیف گیرنده‌اش هنوز IsRead نشده؛ بدون کارمند مرتبط، چیزی نخوانده حساب نمی‌شود
            if (advanced.UnreadOnly)
            {
                var myEmployeeId = requestingApplicationUserId.HasValue
                    ? await _context.Users.Where(u => u.Id == requestingApplicationUserId.Value).Select(u => u.EmployeeId).FirstOrDefaultAsync()
                    : null;
                query = myEmployeeId.HasValue
                    ? query.Where(x => _context.LetterRecipients.Any(r => r.LetterId == x.LetterId && r.EmployeeId == myEmployeeId.Value && !r.IsRead))
                    : query.Where(x => false);
            }

            if (!string.IsNullOrWhiteSpace(advanced.Priority))
            {
                var priority = advanced.Priority.Trim();
                query = query.Where(x => x.Priority == priority);
            }

            if (advanced.HasAttachments == true)
                query = query.Where(x => _context.DocumentAttachments.Any(a => a.LetterId == x.LetterId));
            else if (advanced.HasAttachments == false)
                query = query.Where(x => !_context.DocumentAttachments.Any(a => a.LetterId == x.LetterId));

            if (advanced.HasReferrals == true)
                query = query.Where(x => _context.LetterReferrals.Any(r => r.LetterId == x.LetterId));
            else if (advanced.HasReferrals == false)
                query = query.Where(x => !_context.LetterReferrals.Any(r => r.LetterId == x.LetterId));

            // پاسخ‌خواهی سطح نامه: لازم + مهلت گذشته + نه Closed/Rejected + هنوز نامه‌ی فرزند صادرشده ندارد (محاسبه‌شده)
            if (advanced.OverdueResponseOnly)
            {
                var today = DateTime.Today;
                query = query.Where(x => x.ResponseRequired && x.ResponseDueDate != null && x.ResponseDueDate < today
                    && !LetterResponseRules.FinishedStatuses.Contains(x.Status)
                    && !_context.Letters.Any(c => c.IsActive && c.ParentLetterId == x.LetterId
                        && LetterResponseRules.AnsweredChildStatuses.Contains(c.Status)));
            }

            // «معوق» محاسبه می‌شود (ارجاع باز با مهلت گذشته) - عمداً فیلد ذخیره‌شده‌ی جدا ندارد
            if (advanced.OverdueReferralOnly)
            {
                var now = DateTime.Now;
                query = query.Where(x => _context.LetterReferrals.Any(r => r.LetterId == x.LetterId
                    && r.Status != "Completed" && r.DueDate != null && r.DueDate < now));
            }
        }

        var visible = await ApplyConfidentialityAsync(query, canViewConfidential, requestingApplicationUserId);
        return await ApplyCartableAsync(visible, advanced, requestingApplicationUserId, canViewConfidential);
    }

    // کارتابل: پوشه‌ی شخصی (سطل/آرشیو/ارجاع/تفویض...)، برچسب با زیرمجموعه‌ها، محرمانگی و نمای رشته‌ای.
    // همیشه بعد از قانون محرمانگی اعمال می‌شود؛ هیچ پوشه‌ای چیزی را قابل‌دیدن‌تر نمی‌کند.
    // بدون کاربر (فراخوانی‌های سیستمی) هیچ وضعیت شخصی اعمال نمی‌شود.
    private async Task<IQueryable<Letter>> ApplyCartableAsync(IQueryable<Letter> visible, LetterAdvancedFilter? advanced,
        int? userId, bool canViewConfidential)
    {
        var query = visible;
        if (userId.HasValue)
        {
            var actor = await LetterFolderQuery.ResolveActorAsync(_context, userId.Value, DateTime.Now);
            query = LetterFolderQuery.Apply(_context, query, LetterFolders.Normalize(advanced?.Folder), actor);
        }

        if (advanced == null) return query;

        if (advanced.TagId.HasValue)
        {
            var nodes = await _context.Tags.AsNoTracking().Where(t => t.IsActive)
                .Select(t => new TagTreeRules.Node(t.TagId, t.ParentTagId, t.OwnerUserId, t.IsActive)).ToListAsync();
            var tag = nodes.FirstOrDefault(n => n.TagId == advanced.TagId.Value);
            if (tag == null || !TagTreeRules.IsVisibleTo(tag.OwnerUserId, userId)) return query.Where(_ => false);
            var tagIds = TagTreeRules.DescendantsAndSelf(tag.TagId, nodes.Where(n => TagTreeRules.IsVisibleTo(n.OwnerUserId, userId))).ToList();
            query = query.Where(x => _context.LetterTags.Any(t => t.LetterId == x.LetterId && tagIds.Contains(t.TagId)));
        }

        if (!string.IsNullOrWhiteSpace(advanced.Confidentiality))
        {
            var level = advanced.Confidentiality.Trim();
            query = query.Where(x => x.ConfidentialityLevel == level);
        }

        if (advanced.ThreadRootsOnly)
        {
            var scope = query;
            query = scope.Where(x => x.ParentLetterId == null || !scope.Any(p => p.LetterId == x.ParentLetterId));
        }
        return query;
    }

    // ترتیب فهرست: ستون‌های مجاز دلخواه؛ همیشه با شناسه‌ی نامه ثابت می‌شود تا صفحه‌بندی بین صفحه‌ها نپرد
    private static IOrderedQueryable<Letter> OrderLetters(IQueryable<Letter> query, LetterAdvancedFilter? advanced)
    {
        var desc = advanced?.SortDescending ?? true;
        IOrderedQueryable<Letter> Ord<TKey>(System.Linq.Expressions.Expression<Func<Letter, TKey>> key)
            => desc ? query.OrderByDescending(key) : query.OrderBy(key);

        var ordered = (advanced?.SortBy ?? "date").Trim().ToLowerInvariant() switch
        {
            "subject" => Ord(x => x.Subject),
            "number" => Ord(x => x.LetterNumber),
            "status" => Ord(x => x.Status),
            "priority" => Ord(x => x.Priority == "Urgent" ? 4 : x.Priority == "High" ? 3 : x.Priority == "Normal" ? 2 : 1),
            "sender" => Ord(x => x.Sender),
            "received" => Ord(x => x.ReceivedDate ?? x.LetterDate),
            _ => Ord(x => x.LetterDate)
        };
        return desc ? ordered.ThenByDescending(x => x.LetterId) : ordered.ThenBy(x => x.LetterId);
    }

    // طبق گزارش ممیزی (بند ۶.۵): محدودیت محرمانگی باید در خودِ Query اعمال شود، نه فقط
    // در نمایش. کاربر بدون مجوز بالا فقط نامه‌های Public/Internal، یا نامه‌هایی که خودش
    // ثبت کرده، یا نامه‌هایی که به او ارجاع شده را می‌بیند.
    private Task<IQueryable<Letter>> ApplyConfidentialityAsync(IQueryable<Letter> query,
        bool canViewConfidential, int? requestingApplicationUserId)
        => LetterVisibility.ApplyAsync(_context, query, canViewConfidential, requestingApplicationUserId);

    private string? MakeSnippet(string? body)
    {
        // بدون Sanitizer (سرویس ساخته‌شده‌ی ساده در تست‌ها) از تبدیل متن ساده‌ی خالص قواعد استفاده می‌شود؛ در برنامه همیشه Sanitizer هست
        var plain = _sanitizer != null ? _sanitizer.ToPlainText(body) : LetterComposeRules.PlainText(body);
        var text = System.Text.RegularExpressions.Regex.Replace(plain ?? "", @"\s+", " ").Trim();
        return text.Length == 0 ? null : text.Length > 120 ? text[..120].TrimEnd() + "…" : text;
    }

    // شمارنده‌ی تب‌ها: هر شمارش از همان پرس‌وجوی محرمانگی‌دارِ فهرست می‌آید، پس عددی که کاربر می‌بیند همان چیزی است که با کلیک می‌بیند
    public async Task<LetterFolderCountsDto> GetFolderCountsAsync(bool canViewConfidential, int? requestingApplicationUserId)
    {
        var visible = await BuildFilteredQueryAsync(null, null, null, null, null, null, null, null, null, null,
            canViewConfidential, requestingApplicationUserId, false, null);
        var starred = await BuildFilteredQueryAsync(null, null, null, null, null, null, null, null, null, null,
            canViewConfidential, requestingApplicationUserId, true, null);

        return new LetterFolderCountsDto
        {
            Draft = await visible.CountAsync(x => x.Status == "Draft"),
            PendingApproval = await visible.CountAsync(x => x.Status == "PendingApproval"),
            ReturnedForRevision = await visible.CountAsync(x => x.Status == "ReturnedForRevision"),
            Approved = await visible.CountAsync(x => x.Status == "Approved"),
            Starred = await starred.CountAsync()
        };
    }

    private async Task<List<LetterDto>> BuildDtosAsync(List<Letter> letters, int? requestingApplicationUserId, bool includeSnippet = false,
        bool canViewConfidential = true)
    {
        var letterIds = letters.Select(x => x.LetterId).ToList();

        // کارتابل: وضعیت شخصی بیننده (سطل/آرشیو)، یادآور بعدی، و اندازه‌ی رشته‌ی گفتگو (فقط نامه‌های قابل‌دیدن شمرده می‌شود)
        var myStates = requestingApplicationUserId.HasValue
            ? await _context.LetterUserStates.AsNoTracking()
                .Where(s => s.ApplicationUserId == requestingApplicationUserId.Value && letterIds.Contains(s.LetterId))
                .ToDictionaryAsync(s => s.LetterId)
            : new Dictionary<int, LetterUserState>();
        var myReminders = requestingApplicationUserId.HasValue
            ? (await _context.LetterReminders.AsNoTracking()
                .Where(r => r.OwnerUserId == requestingApplicationUserId.Value && !r.IsDone && letterIds.Contains(r.LetterId))
                .Select(r => new { r.LetterId, r.RemindAt }).ToListAsync())
                .GroupBy(r => r.LetterId).ToDictionary(g => g.Key, g => g.Min(r => r.RemindAt))
            : new Dictionary<int, DateTime>();
        var threads = await LetterThreadQuery.GetThreadInfoAsync(_context, letterIds, canViewConfidential, requestingApplicationUserId);

        var referralStats = await _context.LetterReferrals
            .Where(x => letterIds.Contains(x.LetterId))
            .ToListAsync();

        var reviewActivity = await _context.LetterReviews
            .Where(x => letterIds.Contains(x.LetterId))
            .GroupBy(x => x.LetterId)
            .Select(g => new { LetterId = g.Key, Last = g.Max(r => r.CreatedDate) })
            .ToDictionaryAsync(x => x.LetterId, x => x.Last);

        var starredIds = requestingApplicationUserId.HasValue
            ? await _context.LetterStars
                .Where(x => letterIds.Contains(x.LetterId) && x.ApplicationUserId == requestingApplicationUserId.Value)
                .Select(x => x.LetterId)
                .ToListAsync()
            : new List<int>();

        Dictionary<int, string> myRecipientTypes = new();
        var myUnreadIds = new HashSet<int>();

        if (requestingApplicationUserId.HasValue)
        {
            var myEmployeeId = await _context.Users
                .Where(x => x.Id == requestingApplicationUserId.Value)
                .Select(x => x.EmployeeId)
                .FirstOrDefaultAsync();

            if (myEmployeeId.HasValue)
            {
                var myRows = await _context.LetterRecipients
                    .Where(x => letterIds.Contains(x.LetterId) && x.EmployeeId == myEmployeeId.Value)
                    .Select(x => new { x.LetterId, x.RecipientType, x.IsRead })
                    .ToListAsync();

                myUnreadIds = myRows.Where(r => !r.IsRead).Select(r => r.LetterId).ToHashSet();

                // گروه‌بندی سمت کلاینت (مستقل از Provider: GroupBy+ToDictionaryAsync روی InMemory ترجمه نمی‌شد).
                // اگر هم To و هم Cc باشد، To اولویت دارد؛ BCC برای خودِ گیرنده «Bcc» نمایش داده می‌شود.
                myRecipientTypes = myRows
                    .GroupBy(x => x.LetterId)
                    .ToDictionary(g => g.Key, g => g.Any(r => r.RecipientType == "To") ? "To" : g.Any(r => r.RecipientType == "Cc") ? "Cc" : "Bcc");
            }
        }

        var letterTags = await _context.LetterTags
            .Include(x => x.Tag)
            .Where(x => letterIds.Contains(x.LetterId) && x.Tag != null && x.Tag.IsActive && (x.Tag.OwnerUserId == null || x.Tag.OwnerUserId == requestingApplicationUserId))
            .ToListAsync();

        // قبلاً یک Query جدا به‌ازای هر نامه اجرا می‌شد (N+1) - حالا یک Query گروهی واحد
        var attachmentCounts = await _context.DocumentAttachments
            .Where(x => x.LetterId.HasValue && letterIds.Contains(x.LetterId.Value))
            .GroupBy(x => x.LetterId!.Value)
            .Select(g => new { LetterId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.LetterId, x => x.Count);

        var senderNames = await ResolveSenderNamesAsync(letters.Select(l => l.SenderEmployeeId), letters.Select(l => l.SenderDepartmentId));

        var now = DateTime.Now;
        var today = DateTime.Today;

        var answeredParentIds = (await _context.Letters
            .Where(c => c.IsActive && c.ParentLetterId != null && letterIds.Contains(c.ParentLetterId.Value)
                && LetterResponseRules.AnsweredChildStatuses.Contains(c.Status))
            .Select(c => c.ParentLetterId!.Value)
            .Distinct()
            .ToListAsync()).ToHashSet();

        var dtos = letters.Select(l =>
        {
            var referrals = referralStats.Where(r => r.LetterId == l.LetterId).ToList();
            var open = referrals.Where(r => r.Status != "Completed").ToList();

            // آخرین فعالیت: بیشینه‌ی همه‌ی رویدادهای ثبت‌شده‌ی نامه (بدون ستون ذخیره‌شده‌ی جدید)
            var activity = new List<DateTime?>
            {
                l.CreatedDate, l.ApprovedDate, l.IssuedDate, l.ClosedDate, l.SignedDate, l.ReturnedDate, l.RejectedDate,
                reviewActivity.TryGetValue(l.LetterId, out var rv) ? rv : null
            };
            foreach (var r in referrals) { activity.Add(r.ReferredDate); activity.Add(r.ReadDate); activity.Add(r.CompletedDate); }

            var answered = answeredParentIds.Contains(l.LetterId);
            var responseOpen = l.ResponseRequired && l.ResponseDueDate.HasValue && !answered
                && !LetterResponseRules.FinishedStatuses.Contains(l.Status);

            var dueDates = open.Where(r => r.DueDate.HasValue).Select(r => r.DueDate!.Value).ToList();
            if (responseOpen) dueDates.Add(l.ResponseDueDate!.Value);
            DateTime? nextDue = dueDates.Count > 0 ? dueDates.Min() : null;

            return new LetterDto
            {
                LetterId = l.LetterId,
                LetterType = l.LetterType,
                LetterNumber = l.LetterNumber,
                LetterDate = l.LetterDate,
                Sender = l.Sender,
                Receiver = l.Receiver,
                Subject = l.Subject,
                Priority = l.Priority,
                ConfidentialityLevel = l.ConfidentialityLevel,
                ProjectName = l.Project?.ProjectName,
                DisciplineName = l.Discipline?.Name,
                RelatedDocumentId = l.RelatedDocumentId,
                RelatedDocumentNo = l.RelatedDocument?.DocumentNo,
                RelatedMRId = l.RelatedMRId,
                RelatedMRNumber = l.RelatedMR?.MRNumber,
                Status = l.Status,
                CreatedDate = l.CreatedDate,
                AttachmentsCount = attachmentCounts.TryGetValue(l.LetterId, out var ac) ? ac : 0,
                OpenReferralsCount = open.Count,
                HasOverdueReferral = open.Any(r => r.DueDate.HasValue && r.DueDate.Value < now),
                NextResponseDue = nextDue,
                ResponseRequired = l.ResponseRequired,
                ResponseDueDate = l.ResponseDueDate,
                ResponseAnswered = answered,
                ResponseOverdue = LetterResponseRules.IsOverdue(l.ResponseRequired, l.ResponseDueDate, answered, l.Status, today),
                Category = l.Category,
                ReferenceNumber = l.ReferenceNumber,
                SenderEmployeeId = l.SenderEmployeeId,
                SenderName = l.SenderEmployeeId.HasValue && senderNames.Employees.TryGetValue(l.SenderEmployeeId.Value, out var lsn) ? lsn : l.Sender,
                SenderTitle = l.SenderTitle,
                SenderDepartmentId = l.SenderDepartmentId,
                SenderDepartmentName = l.SenderDepartmentId.HasValue && senderNames.Departments.TryGetValue(l.SenderDepartmentId.Value, out var lsd) ? lsd : null,
                ApprovalRequired = l.ApprovalRequired,
                ArchivedDate = l.ArchivedDate,
                LastActivityDate = activity.Where(d => d.HasValue).Select(d => d!.Value).DefaultIfEmpty(l.CreatedDate).Max(),
                IsStarred = starredIds.Contains(l.LetterId),
                ParentLetterId = l.ParentLetterId,
                ThreadRootId = threads.TryGetValue(l.LetterId, out var th) ? th.RootLetterId : l.LetterId,
                ThreadCount = th?.Count ?? 1,
                IsTrashed = myStates.TryGetValue(l.LetterId, out var st) && st.IsTrashed,
                IsArchivedByMe = st != null && st.IsArchived,
                MyReminderAt = myReminders.TryGetValue(l.LetterId, out var rem) ? rem : null,
                IsUnread = myUnreadIds.Contains(l.LetterId) && !LetterAccessRules.PreSendStatuses.Contains(l.Status),
                Snippet = includeSnippet ? MakeSnippet(l.Body) : null,
                MyRecipientType = myRecipientTypes.TryGetValue(l.LetterId, out var rt) ? rt : null,
                Tags = letterTags.Where(lt => lt.LetterId == l.LetterId).Select(lt => new TagDto
                {
                    TagId = lt.Tag!.TagId,
                    Name = lt.Tag.Name,
                    Color = lt.Tag.Color
                }).ToList()
            };
        }).ToList();

        // «احیاشده از آرشیو»: بعد از آرشیو شخصی فعالیت تازه‌ای روی نامه ثبت شده است (محاسبه‌شده، ذخیره نمی‌شود)
        foreach (var dto in dtos)
            if (dto.IsArchivedByMe && myStates.TryGetValue(dto.LetterId, out var archivedState) && archivedState.ArchivedDate.HasValue
                && dto.LastActivityDate > archivedState.ArchivedDate.Value)
                dto.IsRevived = true;
        return dtos;
    }

    // ==============================================================================
    // عملیات گروهی امن. سیاست: فقط اقداماتی که با گردش‌کار کنترل‌شده و تفکیک وظایف در تعارض نیستند.
    // تایید/صدور/بستن گروهی نیست؛ حذف فیزیکی هم نیست (نامه‌ی رسمی تغییرناپذیر است) - فقط آرشیو نرم.
    // ==============================================================================
    private const int BulkMaxItems = 200;

    private static List<int> NormalizeIds(IReadOnlyCollection<int>? ids) =>
        (ids ?? Array.Empty<int>()).Where(x => x > 0).Distinct().Take(BulkMaxItems).ToList();

    public async Task<LetterBulkResult> BulkSetStarAsync(IReadOnlyCollection<int> letterIds, int applicationUserId,
        bool starred, bool canViewConfidential)
    {
        var ids = NormalizeIds(letterIds);
        var result = new LetterBulkResult();
        if (ids.Count == 0) return result;

        // ستاره فقط برای نامه‌هایی که کاربر اجازه‌ی دیدنشان را دارد (همان فیلتر محرمانگی Index)
        var visibleQuery = await ApplyConfidentialityAsync(
            _context.Letters.AsNoTracking().Where(x => x.IsActive && ids.Contains(x.LetterId)),
            canViewConfidential, applicationUserId);
        var visible = await visibleQuery.Select(x => new { x.LetterId, x.LetterNumber }).ToListAsync();
        var visibleIds = visible.Select(x => x.LetterId).ToHashSet();

        var existing = await _context.LetterStars
            .Where(x => x.ApplicationUserId == applicationUserId && ids.Contains(x.LetterId))
            .ToListAsync();
        var existingIds = existing.Select(x => x.LetterId).ToHashSet();

        foreach (var id in ids)
        {
            if (!visibleIds.Contains(id))
            {
                result.Failed.Add(new LetterBulkFailure { LetterId = id, Reason = "نامه یافت نشد یا دسترسی مشاهده‌ی آن را ندارید." });
                continue;
            }

            if (starred && !existingIds.Contains(id))
                await _context.LetterStars.AddAsync(new LetterStar { LetterId = id, ApplicationUserId = applicationUserId, CreatedDate = DateTime.Now });
            else if (!starred && existingIds.Contains(id))
                _context.LetterStars.RemoveRange(existing.Where(x => x.LetterId == id));

            result.Succeeded++;
        }

        await _context.SaveChangesAsync();
        return result;
    }

    public async Task<LetterBulkResult> BulkSubmitForReviewAsync(IReadOnlyCollection<int> letterIds, int? actorApplicationUserId)
    {
        var ids = NormalizeIds(letterIds);
        var result = new LetterBulkResult();
        if (ids.Count == 0) return result;

        var numbers = await _context.Letters.AsNoTracking()
            .Where(x => ids.Contains(x.LetterId))
            .ToDictionaryAsync(x => x.LetterId, x => x.LetterNumber);

        // هر نامه از همان اقدام تکی عبور می‌کند، پس قانون «فقط Draft» و «فقط نویسنده‌ی اصلی» دور زده نمی‌شود
        foreach (var id in ids)
        {
            var (ok, error) = await SubmitForReviewAsync(id, actorApplicationUserId);
            if (ok) result.Succeeded++;
            else result.Failed.Add(new LetterBulkFailure
            {
                LetterId = id,
                LetterNumber = numbers.TryGetValue(id, out var no) ? no : null,
                Reason = error ?? "ارسال انجام نشد."
            });
        }

        return result;
    }

    private static readonly string[] ArchivableStatuses = { "Draft", "Rejected" };

    public async Task<LetterBulkResult> BulkArchiveAsync(IReadOnlyCollection<int> letterIds, int? actorApplicationUserId,
        string? actorUserName, bool isSystemAdmin, string reason)
    {
        var result = new LetterBulkResult();
        var ids = NormalizeIds(letterIds);
        if (ids.Count == 0) return result;

        reason = (reason ?? "").Trim();
        if (reason.Length == 0)
        {
            foreach (var id in ids)
                result.Failed.Add(new LetterBulkFailure { LetterId = id, Reason = "دلیل آرشیو الزامی است." });
            return result;
        }
        if (reason.Length > 500) reason = reason[..500];

        foreach (var id in ids)
        {
            var letter = await _context.Letters.FirstOrDefaultAsync(x => x.LetterId == id && x.IsActive);
            if (letter == null)
            {
                result.Failed.Add(new LetterBulkFailure { LetterId = id, Reason = "نامه یافت نشد یا قبلاً آرشیو شده است." });
                continue;
            }

            if (!ArchivableStatuses.Contains(letter.Status))
            {
                result.Failed.Add(new LetterBulkFailure { LetterId = id, LetterNumber = letter.LetterNumber,
                    Reason = $"فقط نامه‌های «Draft» یا «Rejected» قابل آرشیو هستند - وضعیت فعلی «{letter.Status}» است." });
                continue;
            }

            if (!isSystemAdmin && letter.RegisteredById != actorApplicationUserId)
            {
                result.Failed.Add(new LetterBulkFailure { LetterId = id, LetterNumber = letter.LetterNumber,
                    Reason = "فقط نویسنده‌ی اصلی یا مدیر سیستم می‌تواند این نامه را آرشیو کند." });
                continue;
            }

            var status = letter.Status;
            letter.IsActive = false;
            // علاوه بر ردیف خودکار Update، یک ردیف صریح با دلیل و عملگر ثبت می‌شود
            await _context.AuditLogs.AddAsync(new AuditLog
            {
                EntityName = nameof(Letter),
                EntityKeyValue = letter.LetterId.ToString(),
                Action = "Archive",
                ChangedFieldsSummary = $"IsActive: True -> False\nStatus: {status}\nReason: {reason}",
                UserId = actorApplicationUserId,
                UserName = actorUserName,
                Timestamp = DateTime.Now
            });

            try
            {
                await _context.SaveChangesAsync();
                result.Succeeded++;
            }
            catch (DbUpdateConcurrencyException)
            {
                // نامه هم‌زمان توسط کاربر دیگری تغییر کرد - فقط همین مورد رد می‌شود
                _context.ChangeTracker.Clear();
                result.Failed.Add(new LetterBulkFailure { LetterId = id, LetterNumber = letter.LetterNumber,
                    Reason = "نامه هم‌زمان توسط کاربر دیگری تغییر کرد؛ صفحه را تازه کنید و دوباره تلاش کنید." });
            }
        }

        return result;
    }

    // ==============================================================================
    // کنترل دسترسی و ردگیری (بازطراحی مکاتبات داخلی) - قاعده: LetterAccessRules
    // ==============================================================================
    private async Task<LetterAccessFacts> BuildFactsAsync(Letter letter, LetterViewer viewer)
    {
        var isAuthor = viewer.UserId.HasValue && letter.RegisteredById == viewer.UserId;
        var isRecipient = viewer.EmployeeId.HasValue
            && await _context.LetterRecipients.AnyAsync(r => r.LetterId == letter.LetterId && r.EmployeeId == viewer.EmployeeId);
        var isReferral = viewer.EmployeeId.HasValue
            && await _context.LetterReferrals.AnyAsync(r => r.LetterId == letter.LetterId && r.ReferredToEmployeeId == viewer.EmployeeId);
        var isChain = viewer.UserId.HasValue
            && await _context.LetterApprovalSteps.AnyAsync(s => s.LetterId == letter.LetterId && s.ApproverUserId == viewer.UserId);

        return new LetterAccessFacts(letter.Status, letter.ConfidentialityLevel, isAuthor, isRecipient, isReferral, isChain, viewer.CanSeeAll);
    }

    public async Task<bool> CanViewAsync(int letterId, LetterViewer viewer)
    {
        var letter = await _context.Letters.AsNoTracking().FirstOrDefaultAsync(x => x.LetterId == letterId && x.IsActive);
        return letter != null && LetterAccessRules.CanView(await BuildFactsAsync(letter, viewer));
    }

    public async Task<LetterDetailAccessResult> GetDetailForViewerAsync(int letterId, LetterViewer viewer)
    {
        var letter = await _context.Letters.AsNoTracking().FirstOrDefaultAsync(x => x.LetterId == letterId && x.IsActive);
        if (letter == null) return new LetterDetailAccessResult { Outcome = LetterAccessOutcome.NotFound };

        var facts = await BuildFactsAsync(letter, viewer);
        if (!LetterAccessRules.CanView(facts)) return new LetterDetailAccessResult { Outcome = LetterAccessOutcome.Forbidden };

        var detail = await GetDetailAsync(letterId);
        if (detail == null) return new LetterDetailAccessResult { Outcome = LetterAccessOutcome.NotFound };

        // BCC: فقط نویسنده، کسی با دسترسی کامل، و خودِ همان گیرنده
        var mayListBcc = viewer.CanSeeAll || facts.IsAuthor;
        detail.Recipients = detail.Recipients
            .Where(r => r.RecipientType != "Bcc" || mayListBcc || (viewer.EmployeeId.HasValue && r.EmployeeId == viewer.EmployeeId))
            .ToList();

        // برچسب شخصیِ دیگران روی نامه‌ی مشترک دیده نمی‌شود
        detail.Letter.Tags = await _context.LetterTags.AsNoTracking()
            .Where(x => x.LetterId == letterId && x.Tag != null && x.Tag.IsActive && (x.Tag.OwnerUserId == null || x.Tag.OwnerUserId == viewer.UserId))
            .Select(x => new TagDto { TagId = x.Tag!.TagId, Name = x.Tag.Name, Color = x.Tag.Color })
            .ToListAsync();

        await RecordViewAsync(letter, viewer);
        return new LetterDetailAccessResult { Outcome = LetterAccessOutcome.Ok, Detail = detail };
    }

    // ثبت «چه کسی دیده» + خوانده‌شدن گیرنده؛ فقط برای نامه‌ی ارسال‌شده. شکست ثبت هرگز نمایش نامه را خراب نمی‌کند.
    private async Task RecordViewAsync(Letter letter, LetterViewer viewer)
    {
        if (!viewer.UserId.HasValue || LetterAccessRules.IsPreSend(letter.Status)) return;

        try
        {
            var now = DateTime.Now;
            var log = await _context.LetterViewLogs.FirstOrDefaultAsync(x => x.LetterId == letter.LetterId && x.UserId == viewer.UserId.Value);
            if (log == null)
                _context.LetterViewLogs.Add(new LetterViewLog { LetterId = letter.LetterId, UserId = viewer.UserId.Value, UserName = viewer.UserName, FirstViewedDate = now, LastViewedDate = now, ViewCount = 1 });
            else
            {
                log.LastViewedDate = now;
                log.ViewCount++;
            }

            if (viewer.EmployeeId.HasValue)
                foreach (var r in await _context.LetterRecipients.Where(x => x.LetterId == letter.LetterId && x.EmployeeId == viewer.EmployeeId && !x.IsRead).ToListAsync())
                {
                    r.IsRead = true;
                    r.ReadDate = now;
                }

            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // دو بازکردن هم‌زمان ممکن است هر دو ردیف اول را بسازند (Unique)؛ ثبت فقط بهترین‌تلاش است
        }
    }

    private async Task<(Dictionary<int, string> Employees, Dictionary<int, string> Departments)> ResolveSenderNamesAsync(
        IEnumerable<int?> employeeIds, IEnumerable<int?> departmentIds)
    {
        var emp = employeeIds.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        var dep = departmentIds.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();

        var employees = emp.Count == 0 ? new Dictionary<int, string>()
            : (await _context.Employees.Where(e => emp.Contains(e.EmployeeId)).Select(e => new { e.EmployeeId, e.FirstName, e.LastName }).ToListAsync())
                .ToDictionary(e => e.EmployeeId, e => (e.FirstName + " " + e.LastName).Trim());
        var departments = dep.Count == 0 ? new Dictionary<int, string>()
            : await _context.Departments.Where(d => dep.Contains(d.DepartmentId)).ToDictionaryAsync(d => d.DepartmentId, d => d.Name);

        return (employees, departments);
    }

    private async Task<List<LetterApprovalStepDto>> LoadApprovalStepsAsync(int letterId, string letterStatus)
    {
        var steps = await _context.LetterApprovalSteps.AsNoTracking().Where(s => s.LetterId == letterId).OrderBy(s => s.StepOrder).ToListAsync();
        if (steps.Count == 0) return new List<LetterApprovalStepDto>();

        var ids = steps.Select(s => s.ApproverUserId).Distinct().ToList();
        var names = await _context.Users.Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.UserName ?? "-");
        var currentOrder = letterStatus == "PendingApproval" ? steps.FirstOrDefault(s => s.Status == "Pending")?.StepOrder : null;

        return steps.Select(s => new LetterApprovalStepDto
        {
            StepOrder = s.StepOrder,
            ApproverUserId = s.ApproverUserId,
            ApproverName = names.TryGetValue(s.ApproverUserId, out var n) ? n : "-",
            Status = s.Status,
            DecidedDate = s.DecidedDate,
            Comment = s.Comment,
            IsCurrent = currentOrder == s.StepOrder
        }).ToList();
    }

    public async Task<LetterDetailDto?> GetDetailAsync(int letterId)
    {
        var letter = await _context.Letters
            .Include(x => x.Project)
            .Include(x => x.Discipline)
            .Include(x => x.RelatedDocument)
            .Include(x => x.RelatedMR)
            .FirstOrDefaultAsync(x => x.LetterId == letterId);

        if (letter == null) return null;

        var referrals = await _context.LetterReferrals
            .Include(x => x.ReferredToEmployee)
            .Include(x => x.ReferredToDepartment)
            .Where(x => x.LetterId == letterId)
            .OrderByDescending(x => x.ReferredDate)
            .ToListAsync();

        var attachments = await _context.DocumentAttachments
            .Where(x => x.LetterId == letterId)
            .ToListAsync();

        var recipients = await _context.LetterRecipients
            .Include(x => x.Employee)
            .Include(x => x.Department)
            .Where(x => x.LetterId == letterId)
            .ToListAsync();

        var tags = await _context.LetterTags
            .Include(x => x.Tag)
            .Where(x => x.LetterId == letterId && x.Tag != null && x.Tag.IsActive)
            .Select(x => new TagDto { TagId = x.Tag!.TagId, Name = x.Tag.Name, Color = x.Tag.Color })
            .ToListAsync();

        var reviews = await _context.LetterReviews
            .Where(x => x.LetterId == letterId && x.IsActive)
            .OrderByDescending(x => x.CreatedDate)
            .ToListAsync();

        // طبق قرارداد موجود این Entity: RegisteredById/ApprovedById/ReturnedByUserId/
        // RejectedByUserId/ReviewerUserId همه ApplicationUserId هستند، بدون Navigation - نام
        // نمایشی این‌جا با یک Query گروهی واحد از روی ApplicationUsers.FullName حل می‌شود
        var actorUserIds = new[] { letter.ApprovedById, letter.ReturnedByUserId, letter.RejectedByUserId }
            .Concat(reviews.Select(r => r.ReviewerUserId))
            .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();

        var actorNames = actorUserIds.Count == 0
            ? new Dictionary<int, string>()
            : await _context.Users.Where(u => actorUserIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.UserName ?? "-");

        string? NameOf(int? userId) => userId.HasValue && actorNames.TryGetValue(userId.Value, out var n) ? n : null;

        var detailAnswered = await _context.Letters.AnyAsync(c => c.IsActive && c.ParentLetterId == letterId
            && LetterResponseRules.AnsweredChildStatuses.Contains(c.Status));
        var detailSenderNames = await ResolveSenderNamesAsync(new[] { letter.SenderEmployeeId }, new[] { letter.SenderDepartmentId });
        var approvalSteps = await LoadApprovalStepsAsync(letterId, letter.Status);

        return new LetterDetailDto
        {
            ApprovalSteps = approvalSteps,
            Letter = new LetterDto
            {
                ResponseRequired = letter.ResponseRequired,
                ResponseDueDate = letter.ResponseDueDate,
                ResponseAnswered = detailAnswered,
                ResponseOverdue = LetterResponseRules.IsOverdue(letter.ResponseRequired, letter.ResponseDueDate, detailAnswered, letter.Status, DateTime.Today),
                Category = letter.Category,
                ReferenceNumber = letter.ReferenceNumber,
                SenderEmployeeId = letter.SenderEmployeeId,
                SenderName = letter.SenderEmployeeId.HasValue && detailSenderNames.Employees.TryGetValue(letter.SenderEmployeeId.Value, out var sn) ? sn : letter.Sender,
                SenderTitle = letter.SenderTitle,
                SenderDepartmentId = letter.SenderDepartmentId,
                SenderDepartmentName = letter.SenderDepartmentId.HasValue && detailSenderNames.Departments.TryGetValue(letter.SenderDepartmentId.Value, out var sd) ? sd : null,
                ApprovalRequired = letter.ApprovalRequired,
                ArchivedDate = letter.ArchivedDate,
                LetterId = letter.LetterId,
                LetterType = letter.LetterType,
                LetterNumber = letter.LetterNumber,
                LetterDate = letter.LetterDate,
                Sender = letter.Sender,
                Receiver = letter.Receiver,
                Subject = letter.Subject,
                Priority = letter.Priority,
                ConfidentialityLevel = letter.ConfidentialityLevel,
                ProjectName = letter.Project?.ProjectName,
                DisciplineName = letter.Discipline?.Name,
                RelatedDocumentId = letter.RelatedDocumentId,
                RelatedDocumentNo = letter.RelatedDocument?.DocumentNo,
                RelatedMRId = letter.RelatedMRId,
                RelatedMRNumber = letter.RelatedMR?.MRNumber,
                Status = letter.Status,
                CreatedDate = letter.CreatedDate,
                AttachmentsCount = attachments.Count,
                OpenReferralsCount = referrals.Count(r => r.Status != "Completed"),
                Tags = tags,
                SignedByName = letter.SignedByName,
                SignedDate = letter.SignedDate,
                RowVersion = letter.RowVersion,
                RegisteredById = letter.RegisteredById,
                ReturnedByName = NameOf(letter.ReturnedByUserId),
                ReturnedDate = letter.ReturnedDate,
                ReturnReason = letter.ReturnReason,
                RejectedByName = NameOf(letter.RejectedByUserId),
                RejectedDate = letter.RejectedDate,
                RejectReason = letter.RejectReason,
                ApprovedByName = NameOf(letter.ApprovedById),
                ApprovedDate = letter.ApprovedDate,
                IssuedDate = letter.IssuedDate,
                ClosedDate = letter.ClosedDate,
                RevisionCount = letter.RevisionCount
            },
            Body = letter.Body,
            Reviews = reviews.Select(r => new LetterReviewDto
            {
                LetterReviewId = r.LetterReviewId,
                ReviewerName = NameOf(r.ReviewerUserId),
                Decision = r.Decision,
                Comment = r.Comment,
                CreatedDate = r.CreatedDate,
                PreviousSubjectSnapshot = r.PreviousSubjectSnapshot,
                PreviousBodySnapshot = r.PreviousBodySnapshot
            }).ToList(),
            Referrals = referrals.Select(r => new LetterReferralDto
            {
                LetterReferralId = r.LetterReferralId,
                LetterId = r.LetterId,
                ReferredToEmployeeName = r.ReferredToEmployee != null
                    ? r.ReferredToEmployee.FirstName + " " + r.ReferredToEmployee.LastName
                    : null,
                ReferredToDepartmentName = r.ReferredToDepartment?.Name,
                Instructions = r.Instructions,
                DueDate = r.DueDate,
                Status = r.Status,
                IsRead = r.IsRead,
                ReadDate = r.ReadDate,
                ReferredByName = r.ReferredByName,
                ReferredDate = r.ReferredDate,
                ResponseNote = r.ResponseNote,
                IsOverdue = r.Status != "Completed" && r.DueDate.HasValue && r.DueDate.Value < DateTime.Now
            }).ToList(),
            Attachments = attachments.Select(a => new LetterAttachmentDto
            {
                AttachmentId = a.AttachmentId,
                FileName = a.FileName,
                FilePath = a.FilePath
            }).ToList(),
            Recipients = recipients.Select(r => new LetterRecipientDto
            {
                LetterRecipientId = r.LetterRecipientId,
                EmployeeId = r.EmployeeId,
                RecipientType = r.RecipientType,
                DisplayName = r.Employee != null
                    ? r.Employee.FirstName + " " + r.Employee.LastName
                    : r.Department?.Name ?? r.ExternalName ?? "-",
                IsRead = r.IsRead,
                ReadDate = r.ReadDate
            }).ToList()
        };
    }

    public async Task<int> CreateAsync(string letterType, string letterNumber, DateTime letterDate, string? sender,
        string? receiver, string subject, string? body, string priority, string confidentialityLevel,
        int? projectId, int? disciplineId, int? relatedDocumentId, int? relatedMRId, int? registeredById,
        bool responseRequired = false, DateTime? responseDueDate = null)
    {
        // Controller قبلاً اعتبارسنجی می‌کند؛ این‌جا دفاع در عمق است تا هیچ مسیر دیگری (DCC/VDR/...) نامه‌ی ناسازگار نسازد
        var responseError = LetterResponseRules.Validate(responseRequired, responseDueDate, letterDate);
        if (responseError != null) throw new ArgumentException(responseError, nameof(responseDueDate));

        var letter = new Letter
        {
            ResponseRequired = responseRequired,
            ResponseDueDate = responseRequired ? responseDueDate!.Value.Date : null,
            LetterType = letterType,
            LetterNumber = letterNumber,
            LetterDate = letterDate,
            Sender = sender,
            Receiver = receiver,
            Subject = subject,
            Body = _sanitizer.Sanitize(body),
            Priority = string.IsNullOrWhiteSpace(priority) ? "Normal" : priority,
            ConfidentialityLevel = string.IsNullOrWhiteSpace(confidentialityLevel) ? "Internal" : confidentialityLevel,
            ProjectId = projectId,
            DisciplineId = disciplineId,
            RelatedDocumentId = relatedDocumentId,
            RelatedMRId = relatedMRId,
            RegisteredById = registeredById,
            Status = letterType == "Outgoing" ? "Draft" : letterType == "Incoming" ? "Registered" : "Sent",
            IsActive = true,
            CreatedDate = DateTime.Now
        };

        await _context.Letters.AddAsync(letter);
        await _context.SaveChangesAsync();

        // طبق فاز D: قبلاً هیچ نامه‌ای برای Semantic Search Index نمی‌شد
        await IndexLetterForSemanticSearchAsync(letter);

        return letter.LetterId;
    }

    private async Task IndexLetterForSemanticSearchAsync(Letter letter)
    {
        try
        {
            var content = string.Join(" ", new[] { letter.LetterNumber, letter.Subject, StripHtml(letter.Body) }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

            if (!string.IsNullOrWhiteSpace(content))
                await _knowledgeBaseService.IndexAsync("Letter", letter.LetterId, content);
        }
        catch
        {
            // خطای Indexing نباید ثبت نامه را متوقف کند
        }
    }

    private static string StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        return System.Text.RegularExpressions.Regex.Replace(html, "<.*?>", " ");
    }

    public async Task<List<RelatedLetterDto>> GetRelatedToDocumentAsync(int documentId)
    {
        return await _context.Letters
            .Where(x => x.IsActive && x.RelatedDocumentId == documentId)
            .OrderByDescending(x => x.LetterDate)
            .Select(x => new RelatedLetterDto
            {
                LetterId = x.LetterId,
                LetterType = x.LetterType,
                LetterNumber = x.LetterNumber,
                Subject = x.Subject,
                LetterDate = x.LetterDate,
                Status = x.Status
            })
            .ToListAsync();
    }

    public async Task<List<RelatedLetterDto>> GetRelatedToMRAsync(int mrId)
    {
        return await _context.Letters
            .Where(x => x.IsActive && x.RelatedMRId == mrId)
            .OrderByDescending(x => x.LetterDate)
            .Select(x => new RelatedLetterDto
            {
                LetterId = x.LetterId,
                LetterType = x.LetterType,
                LetterNumber = x.LetterNumber,
                Subject = x.Subject,
                LetterDate = x.LetterDate,
                Status = x.Status
            })
            .ToListAsync();
    }

    // ==============================================================================
    // طبق «PEMS Letter Details — Controlled Lifecycle» بند ۳۴: نگاشت گذارهای قانونی - کاربر
    // هرگز وضعیت دلخواه انتخاب نمی‌کند، فقط یک اقدام کسب‌وکاری صریح که خودش گذار درست را
    // اعمال می‌کند. این نگاشت فقط برای نامه‌های Outgoing/Internal است که واقعاً از این
    // گردش‌کار عبور می‌کنند - Incoming خط‌سیر ساده‌ی خودش (Registered/UnderReview/Referred/
    // Closed) را دارد و دست‌نخورده می‌ماند (بند ۳: هرگز به یک گردش‌کار یکسان اجباری نمی‌شود).
    // ==============================================================================
    private static readonly Dictionary<string, string> RequiredFromStatus = new()
    {
        [nameof(SubmitForReviewAsync)] = "Draft",
        [nameof(ApproveAsync)] = "PendingApproval",
        [nameof(ReturnForRevisionAsync)] = "PendingApproval",
        [nameof(RejectAsync)] = "PendingApproval",
        [nameof(IssueAsync)] = "Approved",
        [nameof(CloseAsync)] = "Issued",
    };

    private async Task<(Letter? Letter, string? Error)> LoadForTransitionAsync(int letterId, string requiredFromStatus, string actionLabel)
    {
        var letter = await _context.Letters.FirstOrDefaultAsync(x => x.LetterId == letterId && x.IsActive);
        if (letter == null) return (null, "نامه یافت نشد.");

        if (letter.Status != requiredFromStatus)
            return (null, $"«{actionLabel}» فقط از وضعیت «{requiredFromStatus}» ممکن است - وضعیت فعلی این نامه «{letter.Status}» است.");

        return (letter, null);
    }

    // طبق بند ۱۳: «مالکیت» یکی از چک‌های الزامی برای هر عملیات مهم است - فقط نویسنده‌ی اصلی
    // (RegisteredById) می‌تواند پیش‌نویس خودش را برای بازبینی ارسال کند؛ در غیر این‌صورت هر
    // کاربر احرازهویت‌شده‌ای می‌توانست پیش‌نویس نامه‌ی شخص دیگری را زودتر از موعد ارسال کند.
    // ==============================================================================
    // زنجیره‌ی تایید ترتیبی (بازطراحی مکاتبات داخلی). نامه‌ی بدون زنجیره (همه‌ی نامه‌های قدیمی) دقیقاً مثل قبل رفتار می‌کند: هر کسی با مجوز
    // Approve یک‌مرحله تایید می‌کند. نامه‌ی دارای زنجیره: فقط تاییدکننده‌ی «مرحله‌ی جاری» (اولین Pending)، و نویسنده هرگز تاییدکننده‌ی نامه‌ی
    // خودش نیست. «تایید دوری» (A برای B و B برای A روی هم‌دیگر) با دو قید جلوگیری می‌شود: نویسنده در زنجیره‌ی نامه‌ی خودش نیست، و هر نفر
    // فقط یک بار در یک زنجیره می‌آید (هر دو در LetterComposeService اعتبارسنجی می‌شوند؛ این‌جا هم دوباره اعمال می‌شود).
    // ==============================================================================
    private sealed record ApprovalGate(List<LetterApprovalStep> Steps, LetterApprovalStep? Current, string? Error);

    private async Task<ApprovalGate> GateApproverAsync(Letter letter, int? reviewerUserId)
    {
        var steps = await _context.LetterApprovalSteps.Where(s => s.LetterId == letter.LetterId).OrderBy(s => s.StepOrder).ToListAsync();
        if (steps.Count == 0) return new ApprovalGate(steps, null, null);

        var current = steps.FirstOrDefault(s => s.Status == "Pending");
        if (current == null) return new ApprovalGate(steps, null, "برای این نامه مرحله‌ی در انتظار تاییدی باقی نمانده است.");
        if (letter.RegisteredById.HasValue && letter.RegisteredById == reviewerUserId)
            return new ApprovalGate(steps, current, "نویسنده‌ی نامه نمی‌تواند نامه‌ی خودش را تایید یا رد کند (تفکیک وظایف).");
        if (!reviewerUserId.HasValue || current.ApproverUserId != reviewerUserId.Value)
            return new ApprovalGate(steps, current, "نوبت تصمیم‌گیری روی این نامه با شما نیست؛ فقط تاییدکننده‌ی مرحله‌ی جاری مجاز است.");

        return new ApprovalGate(steps, current, null);
    }

    // آماده‌بودن نامه‌ی «فرمال» (ساخته‌شده با فرم نوشتن؛ Category دارد) برای ارسال به تایید/ارسال. نامه‌ی قدیمی (بدون Category) سخت‌گیری جدید ندارد.
    private async Task<string?> CheckReadyAsync(Letter letter)
    {
        if (letter.Category == null) return null;

        if (string.IsNullOrWhiteSpace(letter.Subject)) return "موضوع نامه الزامی است.";
        // موضوع ارجاع را کاربر نمی‌نویسد (همان موضوع نامه‌ی اصلی است؛ ممکن است از نامه‌ی واردِ طولانی آمده باشد)
        if (!letter.IsReferral && letter.Subject.Trim().Length > LetterComposeRules.MaxSubjectLength) return $"موضوع نامه حداکثر {LetterComposeRules.MaxSubjectLength} نویسه است.";
        if (LetterComposeRules.PlainText(letter.Body).Length == 0) return "متن نامه خالی است.";

        if (letter.LetterType == "Internal"
            && !await _context.LetterRecipients.AnyAsync(r => r.LetterId == letter.LetterId && r.RecipientType == "To" && r.EmployeeId != null))
            return "حداقل یک گیرنده‌ی اصلی (To) لازم است.";
        if (letter.LetterType == "Outgoing" && string.IsNullOrWhiteSpace(letter.Receiver)) return "گیرنده‌ی نامه‌ی خارجی الزامی است.";

        if (letter.ApprovalRequired)
        {
            var steps = await _context.LetterApprovalSteps.Where(s => s.LetterId == letter.LetterId).ToListAsync();
            if (steps.Count == 0) return "زنجیره‌ی تایید تعریف نشده است.";
            if (steps.Any(s => s.ApproverUserId == letter.RegisteredById)) return "نویسنده نمی‌تواند در زنجیره‌ی تایید نامه‌ی خودش باشد.";
        }

        return null;
    }

    public async Task<(bool Success, string? Error)> SubmitForReviewAsync(int letterId, int? actorApplicationUserId)
    {
        var (letter, error) = await LoadForTransitionAsync(letterId, RequiredFromStatus[nameof(SubmitForReviewAsync)], "Submit for Review");
        if (letter == null) return (false, error);

        if (letter.RegisteredById != actorApplicationUserId)
            return (false, "فقط نویسنده‌ی اصلی این نامه مجاز به ارسال آن برای بازبینی است.");

        if (letter.Category != null)
        {
            if (!letter.ApprovalRequired) return (false, "این نامه نیاز به تایید ندارد؛ مستقیماً «ارسال» کنید.");
            var notReady = await CheckReadyAsync(letter);
            if (notReady != null) return (false, notReady);

            foreach (var step in await _context.LetterApprovalSteps.Where(s => s.LetterId == letterId).ToListAsync())
            {
                step.Status = "Pending";
                step.DecidedDate = null;
                step.Comment = null;
            }
            await LetterVersioning.SnapshotAsync(_context, letter, actorApplicationUserId, "Submitted");
        }

        letter.Status = "PendingApproval";
        await _context.SaveChangesAsync();
        await NotifyTransitionAsync(letter, "Submitted", actorApplicationUserId, null);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ApproveAsync(int letterId, int? reviewerApplicationUserId, string? comment)
    {
        var (letter, error) = await LoadForTransitionAsync(letterId, RequiredFromStatus[nameof(ApproveAsync)], "Approve");
        if (letter == null) return (false, error);

        var gate = await GateApproverAsync(letter, reviewerApplicationUserId);
        if (gate.Error != null) return (false, gate.Error);

        await _context.LetterReviews.AddAsync(new LetterReview
        {
            LetterId = letterId,
            ReviewerUserId = reviewerApplicationUserId,
            Decision = LetterReviewDecisions.Approved,
            Comment = comment,
            CreatedDate = DateTime.Now,
            IsActive = true
        });

        if (gate.Current != null)
        {
            gate.Current.Status = "Approved";
            gate.Current.DecidedDate = DateTime.Now;
            gate.Current.Comment = comment;

            // مرحله‌ی میانی: نامه PendingApproval می‌ماند و نوبت به تاییدکننده‌ی بعدی می‌رسد
            if (gate.Steps.Any(s => s.StepOrder > gate.Current.StepOrder && s.Status == "Pending"))
            {
                var interim = await SaveWithConcurrencyCheckAsync();
                if (interim.Success) await NotifyTransitionAsync(letter, "StepApproved", reviewerApplicationUserId, comment);
                return interim;
            }
        }

        letter.Status = "Approved";
        letter.ApprovedDate = DateTime.Now;
        letter.ApprovedById = reviewerApplicationUserId;

        return await FinishTransitionAsync(letter, "Approved", reviewerApplicationUserId, comment);
    }

    public async Task<(bool Success, string? Error)> ReturnForRevisionAsync(int letterId, int? reviewerApplicationUserId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return (false, "دلیل برگشت برای اصلاح (Return Reason) الزامی است.");

        var (letter, error) = await LoadForTransitionAsync(letterId, RequiredFromStatus[nameof(ReturnForRevisionAsync)], "Return for Revision");
        if (letter == null) return (false, error);

        var gate = await GateApproverAsync(letter, reviewerApplicationUserId);
        if (gate.Error != null) return (false, gate.Error);

        // طبق بند ۱۱: Snapshot محتوایی که بازبین همین الان دیده - پیش از این‌که نویسنده آن را ویرایش کند
        await _context.LetterReviews.AddAsync(new LetterReview
        {
            LetterId = letterId,
            ReviewerUserId = reviewerApplicationUserId,
            Decision = LetterReviewDecisions.ReturnedForRevision,
            Comment = reason,
            PreviousSubjectSnapshot = letter.Subject,
            PreviousBodySnapshot = letter.Body,
            CreatedDate = DateTime.Now,
            IsActive = true
        });

        if (gate.Current != null)
        {
            gate.Current.Status = "Returned";
            gate.Current.DecidedDate = DateTime.Now;
            gate.Current.Comment = reason;
        }

        letter.Status = "ReturnedForRevision";
        letter.ReturnedByUserId = reviewerApplicationUserId;
        letter.ReturnedDate = DateTime.Now;
        letter.ReturnReason = reason;

        return await FinishTransitionAsync(letter, "ReturnedForRevision", reviewerApplicationUserId, reason);
    }

    public async Task<(bool Success, string? Error)> RejectAsync(int letterId, int? reviewerApplicationUserId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return (false, "دلیل رد (Reject Reason) الزامی است.");

        var (letter, error) = await LoadForTransitionAsync(letterId, RequiredFromStatus[nameof(RejectAsync)], "Reject");
        if (letter == null) return (false, error);

        var gate = await GateApproverAsync(letter, reviewerApplicationUserId);
        if (gate.Error != null) return (false, gate.Error);

        if (gate.Current != null)
        {
            gate.Current.Status = "Rejected";
            gate.Current.DecidedDate = DateTime.Now;
            gate.Current.Comment = reason;
        }

        letter.Status = "Rejected";
        letter.RejectedByUserId = reviewerApplicationUserId;
        letter.RejectedDate = DateTime.Now;
        letter.RejectReason = reason;

        await _context.LetterReviews.AddAsync(new LetterReview
        {
            LetterId = letterId,
            ReviewerUserId = reviewerApplicationUserId,
            Decision = LetterReviewDecisions.Rejected,
            Comment = reason,
            CreatedDate = DateTime.Now,
            IsActive = true
        });

        return await FinishTransitionAsync(letter, "Rejected", reviewerApplicationUserId, reason);
    }

    public async Task<(bool Success, string? Error)> ReassignApprovalStepAsync(int letterId, int stepOrder, int newApproverUserId,
        int? actorApplicationUserId, bool isAdmin)
    {
        if (!isAdmin) return (false, "فقط مدیر سیستم می‌تواند تاییدکننده‌ی یک مرحله را جایگزین کند.");

        var letter = await _context.Letters.FirstOrDefaultAsync(x => x.LetterId == letterId && x.IsActive);
        if (letter == null) return (false, "نامه یافت نشد.");
        if (letter.Status != "PendingApproval") return (false, $"فقط نامه‌ی در انتظار تایید قابل تغییر است - وضعیت فعلی «{letter.Status}» است.");

        var steps = await _context.LetterApprovalSteps.Where(s => s.LetterId == letterId).ToListAsync();
        var step = steps.FirstOrDefault(s => s.StepOrder == stepOrder);
        if (step == null) return (false, "مرحله یافت نشد.");
        if (step.Status != "Pending") return (false, "فقط مرحله‌ی هنوز بلاتکلیف (Pending) قابل جایگزینی است.");
        if (newApproverUserId == letter.RegisteredById) return (false, "نویسنده نمی‌تواند تاییدکننده‌ی نامه‌ی خودش باشد.");
        if (steps.Any(s => s.StepOrder != stepOrder && s.ApproverUserId == newApproverUserId)) return (false, "این کاربر قبلاً در زنجیره‌ی این نامه هست.");
        if (!(await LetterDirectoryQueries.ApproverUserIdsAsync(_context)).Contains(newApproverUserId)) return (false, "این کاربر مجوز Approve مکاتبات را ندارد.");

        var previous = step.ApproverUserId;
        step.ApproverUserId = newApproverUserId;
        await _context.AuditLogs.AddAsync(new AuditLog
        {
            EntityName = nameof(Letter), EntityKeyValue = letterId.ToString(), Action = "ReassignApprover",
            ChangedFieldsSummary = $"Step {stepOrder}: approver {previous} -> {newApproverUserId}", UserId = actorApplicationUserId, Timestamp = DateTime.Now
        });

        var saved = await SaveWithConcurrencyCheckAsync();
        if (saved.Success && steps.Where(s => s.Status == "Pending").OrderBy(s => s.StepOrder).FirstOrDefault()?.StepOrder == stepOrder)
            await NotifyTransitionAsync(letter, "StepApproved", actorApplicationUserId, null);   // نوبت فعلی به تاییدکننده‌ی جدید اعلان می‌شود
        return saved;
    }

    public async Task<(bool Success, string? Error)> IssueAsync(int letterId, int? actorApplicationUserId)
    {
        var (letter, error) = await LoadForTransitionAsync(letterId, RequiredFromStatus[nameof(IssueAsync)], "Issue");
        if (letter == null) return (false, error);

        letter.Status = "Issued";
        letter.IssuedDate = DateTime.Now;
        await LetterVersioning.SnapshotAsync(_context, letter, actorApplicationUserId, "Sent");
        await CloseParentIfAnsweredAsync(letter);
        await StageReferralEffectsAsync(letter);

        return await FinishTransitionAsync(letter, "Issued", actorApplicationUserId, null);
    }

    // ارسال نامه‌ی داخلی - Approved -> Sent (یا Draft -> Sent وقتی تایید لازم نیست). فقط نویسنده.
    public async Task<(bool Success, string? Error)> SendAsync(int letterId, int? actorApplicationUserId)
    {
        var letter = await _context.Letters.FirstOrDefaultAsync(x => x.LetterId == letterId && x.IsActive);
        if (letter == null) return (false, "نامه یافت نشد.");
        if (letter.LetterType != "Internal") return (false, "فقط نامه‌ی داخلی «ارسال» می‌شود؛ نامه‌ی خارجی با «صدور» (Issue) منتشر می‌شود.");
        if (letter.RegisteredById != actorApplicationUserId) return (false, "فقط نویسنده‌ی اصلی این نامه مجاز به ارسال آن است.");

        var allowed = letter.Status == "Approved" || (letter.Status == "Draft" && !letter.ApprovalRequired);
        if (!allowed)
            return (false, letter.Status == "Draft"
                ? "این نامه نیاز به تایید دارد؛ ابتدا آن را «برای تایید ارسال» کنید."
                : $"«ارسال» از وضعیت فعلی «{letter.Status}» ممکن نیست.");

        var notReady = await CheckReadyAsync(letter);
        if (notReady != null) return (false, notReady);

        letter.Status = "Sent";
        letter.IssuedDate = DateTime.Now;
        await LetterVersioning.SnapshotAsync(_context, letter, actorApplicationUserId, "Sent");
        await CloseParentIfAnsweredAsync(letter);
        await StageReferralEffectsAsync(letter);

        var saved = await SaveWithConcurrencyCheckAsync();
        if (saved.Success)
        {
            await NotifyRecipientsOfSentLetterAsync(letter, actorApplicationUserId);
            // قوانین مسیریابی خودکارِ گیرندگان (برچسب/ستاره/ارجاع به زیردست)؛ هرگز ارسال را نمی‌شکند
            if (_routingRules != null) await _routingRules.ApplyToArrivedLetterAsync(letter.LetterId);
        }
        return saved;
    }

    // ارجاع و پاسخ یکپارچه‌اند و هر دو «نامه» هستند؛ اثر آن‌ها روی ردیف‌های ارجاع (که پیگیری مهلت/معوق به آن‌ها وابسته است) هم‌زمان با ارسال و در همان ذخیره ثبت می‌شود:
    //  - نامه‌ی ارجاعی (IsReferral): برای هر گیرنده‌ی «To» یک LetterReferral روی نامه‌ی اصلی (وصل‌شده با ReferralLetterId)
    //  - پاسخ (غیر ارجاعی): ارجاع‌های بازِ خودِ پاسخ‌دهنده روی همان نامه (یا نامه‌ی ارجاعی‌ای که به آن پاسخ می‌دهد) بسته می‌شود
    private async Task StageReferralEffectsAsync(Letter letter)
    {
        if (!letter.ParentLetterId.HasValue) return;
        var parent = await _context.Letters.FirstOrDefaultAsync(x => x.LetterId == letter.ParentLetterId.Value);
        if (parent == null) return;

        if (letter.IsReferral)
        {
            var targets = await _context.LetterRecipients.Where(r => r.LetterId == letter.LetterId && r.RecipientType == "To" && r.EmployeeId != null)
                .Select(r => r.EmployeeId!.Value).Distinct().ToListAsync();
            var existing = await _context.LetterReferrals.Where(r => r.ReferralLetterId == letter.LetterId).Select(r => r.ReferredToEmployeeId).ToListAsync();
            var instructions = LetterReferralRules.InstructionText(letter.Body);
            foreach (var employeeId in targets.Where(id => !existing.Contains(id)))
                _context.LetterReferrals.Add(new LetterReferral
                {
                    LetterId = parent.LetterId, ReferredToEmployeeId = employeeId, Instructions = instructions,
                    DueDate = letter.ResponseRequired ? letter.ResponseDueDate : null, Status = "Pending", IsRead = false,
                    ReferredByName = letter.Sender, ReferredDate = DateTime.Now, ReferralLetterId = letter.LetterId
                });
            if (parent.LetterType == "Incoming" && parent.Status == "Registered") parent.Status = "Referred";
            return;
        }

        if (!letter.SenderEmployeeId.HasValue) return;
        var open = await _context.LetterReferrals
            .Where(r => r.ReferredToEmployeeId == letter.SenderEmployeeId && r.Status != "Completed" && (r.LetterId == parent.LetterId || r.ReferralLetterId == parent.LetterId))
            .ToListAsync();
        foreach (var referral in open.Where(r => LetterReferralRules.ClosesOnReply(r.ReferredToEmployeeId, r.Status, r.LetterId, r.ReferralLetterId, letter.SenderEmployeeId, parent.LetterId)))
        {
            referral.Status = "Completed";
            referral.CompletedDate = DateTime.Now;
            referral.IsRead = true;
            referral.ReadDate ??= DateTime.Now;
            referral.ResponseNote = "پاسخ داده شد: " + letter.LetterNumber;
        }
    }

    private static readonly string[] ClosableParentStatuses = { "Registered", "UnderReview", "Referred", "Sent", "Acknowledged", "Issued" };

    // پاسخ رسیده/ارسال شده -> نامه‌ی مادرِ پاسخ‌خواه بسته می‌شود («Waiting for Response -> Received Response -> Closed»)
    private async Task CloseParentIfAnsweredAsync(Letter child)
    {
        if (!child.ParentLetterId.HasValue) return;

        var parent = await _context.Letters.FirstOrDefaultAsync(x => x.LetterId == child.ParentLetterId.Value && x.IsActive);
        if (parent == null || !parent.ResponseRequired || !ClosableParentStatuses.Contains(parent.Status)) return;

        parent.Status = "Closed";
        parent.ClosedDate = DateTime.Now;
    }

    private async Task NotifyRecipientsOfSentLetterAsync(Letter letter, int? actorUserId)
    {
        if (_notifications == null) return;

        try
        {
            var employeeIds = await _context.LetterRecipients.Where(r => r.LetterId == letter.LetterId && r.EmployeeId != null)
                .Select(r => r.EmployeeId!.Value).Distinct().ToListAsync();
            var userIds = await _context.Users.Where(u => u.IsActive && u.EmployeeId != null && employeeIds.Contains(u.EmployeeId.Value)).Select(u => u.Id).ToListAsync();

            var message = $"{letter.LetterNumber} - {letter.Subject}"
                + (letter.ResponseRequired && letter.ResponseDueDate.HasValue ? $" (response required by {letter.ResponseDueDate:yyyy-MM-dd})" : "");

            foreach (var userId in userIds.Where(u => u != actorUserId))
                await _notifications.CreateAsync(userId, letter.IsReferral ? "نامه‌ای به شما ارجاع شد" : "New letter received", message, $"/Letter/Details/{letter.LetterId}", "Letter", letter.LetterId);
        }
        catch (Exception)
        {
            // اعلان بهترین‌تلاش است
        }
    }

    public async Task<(bool Success, string? Error)> ArchiveAsync(int letterId, int? actorApplicationUserId, bool canArchiveAny)
    {
        var letter = await _context.Letters.FirstOrDefaultAsync(x => x.LetterId == letterId && x.IsActive);
        if (letter == null) return (false, "نامه یافت نشد.");
        if (!canArchiveAny && letter.RegisteredById != actorApplicationUserId)
            return (false, "فقط نویسنده یا کاربر دارای اختیار مدیریت می‌تواند نامه را آرشیو کند.");

        var archivable = new[] { "Registered", "UnderReview", "Referred", "Sent", "Acknowledged", "Issued", "Closed" };
        if (!archivable.Contains(letter.Status))
            return (false, $"فقط نامه‌ی ارسال‌شده/بسته‌شده قابل آرشیو است - وضعیت فعلی «{letter.Status}» است.");

        letter.Status = "Archived";
        letter.ArchivedDate = DateTime.Now;
        return await SaveWithConcurrencyCheckAsync();
    }

    public async Task<(bool Success, string? Error)> CloseAsync(int letterId, int? actorApplicationUserId)
    {
        var letter = await _context.Letters.FirstOrDefaultAsync(x => x.LetterId == letterId && x.IsActive);
        if (letter == null) return (false, "نامه یافت نشد.");

        // طبق بند ۳: Incoming/Internal خط‌سیر پایانی خودشان را دارند (Referred/Sent/Acknowledged
        // هم می‌توانند به Closed برسند) - فقط Outgoing باید حتماً از Issued عبور کرده باشد
        var validFromStatuses = letter.LetterType == "Outgoing"
            ? new[] { "Issued" }
            : new[] { "Registered", "UnderReview", "Referred", "Sent", "Acknowledged" };

        if (!validFromStatuses.Contains(letter.Status))
            return (false, $"«Close» از وضعیت فعلی «{letter.Status}» برای این نوع نامه ممکن نیست.");

        letter.Status = "Closed";
        letter.ClosedDate = DateTime.Now;

        return await SaveWithConcurrencyCheckAsync();
    }

    // ==============================================================================
    // طبق بند ۷: قلب کل قانون خودتاییدی این نشست - فقط CurrentUserId == RegisteredById AND
    // Status == ReturnedForRevision. نه بر اساس Role، نه بر اساس نام Employee، نه فقط مخفی‌کردن
    // دکمه در UI - این‌جا (لایه‌ی سرویس) تنها مرز واقعی اجراست.
    // ==============================================================================
    private static (bool Allowed, string? Error) CheckOriginalAuthorCanEdit(Letter letter, int currentApplicationUserId)
    {
        if (letter.RegisteredById != currentApplicationUserId)
            return (false, "فقط نویسنده‌ی اصلی این نامه مجاز به ویرایش محتوای آن است.");

        if (letter.Status != "Draft" && letter.Status != "ReturnedForRevision")
            return (false, $"محتوای رسمی این نامه در وضعیت «{letter.Status}» غیرقابل‌ویرایش است.");

        return (true, null);
    }

    public async Task<(bool Success, string? Error)> SaveDraftEditAsync(int letterId, string subject, string body,
        int currentApplicationUserId, byte[]? rowVersion)
    {
        var letter = await _context.Letters.FirstOrDefaultAsync(x => x.LetterId == letterId && x.IsActive);
        if (letter == null) return (false, "نامه یافت نشد.");

        var (allowed, error) = CheckOriginalAuthorCanEdit(letter, currentApplicationUserId);
        if (!allowed) return (false, error);

        ApplyConcurrencyToken(letter, rowVersion);

        letter.Subject = subject;
        letter.Body = _sanitizer.Sanitize(body);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return (false, "این نامه هم‌زمان توسط کاربر دیگری تغییر کرده است. لطفاً صفحه را رفرش کنید و دوباره تلاش کنید.");
        }

        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ResubmitAsync(int letterId, string subject, string body,
        int currentApplicationUserId, byte[]? rowVersion)
    {
        var letter = await _context.Letters.FirstOrDefaultAsync(x => x.LetterId == letterId && x.IsActive);
        if (letter == null) return (false, "نامه یافت نشد.");

        // طبق بند ۷/۳۵: بازارسال فقط از ReturnedForRevision و فقط توسط نویسنده‌ی اصلی
        if (letter.Status != "ReturnedForRevision")
            return (false, $"«Resubmit» فقط از وضعیت «ReturnedForRevision» ممکن است - وضعیت فعلی «{letter.Status}» است.");

        if (letter.RegisteredById != currentApplicationUserId)
            return (false, "فقط نویسنده‌ی اصلی این نامه مجاز به ارسال مجدد آن است.");

        ApplyConcurrencyToken(letter, rowVersion);

        letter.Subject = subject;
        letter.Body = _sanitizer.Sanitize(body);
        letter.RevisionCount += 1;

        // طبق بند ۳۵: بلافاصله دوباره غیرقابل‌ویرایش می‌شود - Cache برگشت پاک می‌شود (تاریخچه‌ی
        // واقعی در LetterReview دست‌نخورده می‌ماند، دقیقاً همون الگوی ClientReviewStatus فاز ۲ EDMS)
        letter.Status = "PendingApproval";
        letter.ReturnedByUserId = null;
        letter.ReturnedDate = null;
        letter.ReturnReason = null;

        // زنجیره‌ی تایید از مرحله‌ی اول دوباره شروع می‌شود + نسخه‌ی «Resubmitted» در تاریخچه
        foreach (var step in await _context.LetterApprovalSteps.Where(s => s.LetterId == letterId).ToListAsync())
        {
            step.Status = "Pending";
            step.DecidedDate = null;
            step.Comment = null;
        }
        await LetterVersioning.SnapshotAsync(_context, letter, currentApplicationUserId, "Resubmitted");

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return (false, "این نامه هم‌زمان توسط کاربر دیگری تغییر کرده است. لطفاً صفحه را رفرش کنید و دوباره تلاش کنید.");
        }

        await NotifyTransitionAsync(letter, "Resubmitted", currentApplicationUserId, null);
        return (true, null);
    }

    // طبق بند ۲۹ (Concurrency): مقدار RowVersion ارسالی از فرم (که کاربر لحظه‌ی بازکردن صفحه
    // دیده) به‌عنوان OriginalValue تنظیم می‌شود - نه CurrentValue. این‌طوری دستور UPDATE واقعی
    // EF دقیقاً همان چیزی را در WHERE می‌گذارد که کاربر دیده؛ اگر بین‌این‌مدت کس دیگری ذخیره
    // کرده باشد، صفر ردیف تطبیق می‌یابد و DbUpdateConcurrencyException پرتاب می‌شود - برخلاف
    // اشتباه رایج «ست‌کردن مستقیم CurrentValue» که اصلاً هیچ تعارضی را تشخیص نمی‌دهد چون همین
    // متد لحظه‌ای قبلش خودش رکورد را تازه خوانده است.
    private void ApplyConcurrencyToken(Letter letter, byte[]? rowVersion)
    {
        if (rowVersion != null)
            _context.Entry(letter).Property(x => x.RowVersion).OriginalValue = rowVersion;
    }

    // طبق «PEMS Correspondence Workspace» بند ۳۰: تست Concurrency واقعی (روی SQL Server ایزوله،
    // نه InMemory) ثابت کرد این متد قبلاً یک Exception خام پرتاب می‌کرد به‌جای برگرداندن همون
    // Tuple (Success, Error) که بقیه‌ی متدهای این کلاس استفاده می‌کنند - یعنی وقتی دو درخواست
    // هم‌زمان (مثلاً Approve و ReturnForRevision) روی یک نامه هم‌پوشانی می‌کردند، بازنده به‌جای
    // یک پیام کنترل‌شده در TempData["Error"]، باعث یک خطای ۵۰۰ کنترل‌نشده در Controller می‌شد
    // (چون LetterController فقط منتظر Tuple است، نه try/catch اطراف هر Action). مکانیزم واقعی
    // Concurrency (RowVersion) درست کار می‌کرد - فقط شکل بازگشت خطا اشتباه بود.
    private async Task<(bool Success, string? Error)> SaveWithConcurrencyCheckAsync()
    {
        try
        {
            await _context.SaveChangesAsync();
            return (true, null);
        }
        catch (DbUpdateConcurrencyException)
        {
            return (false, "این نامه هم‌زمان توسط کاربر دیگری تغییر کرده است. لطفاً صفحه را رفرش کنید و دوباره تلاش کنید.");
        }
    }

    // ==============================================================================
    // اعلان‌های چرخه‌ی حیات (Phase 2). بهترین‌تلاش و فقط بعد از Commit موفق؛ شکست اعلان هرگز گذار نامه را برنمی‌گرداند.
    //   Submitted/Resubmitted -> بازبین‌ها (نقش‌هایی که Correspondence/Approve دارند)
    //   Approved/ReturnedForRevision/Rejected/Issued -> نویسنده‌ی اصلی (RegisteredById)
    // خود عامل هرگز برای کار خودش اعلان نمی‌گیرد.
    // ==============================================================================
    private async Task<(bool Success, string? Error)> FinishTransitionAsync(Letter letter, string kind, int? actorUserId, string? detail)
    {
        var result = await SaveWithConcurrencyCheckAsync();
        if (result.Success) await NotifyTransitionAsync(letter, kind, actorUserId, detail);
        return result;
    }

    private async Task NotifyTransitionAsync(Letter letter, string kind, int? actorUserId, string? detail)
    {
        if (_notifications == null) return;

        try
        {
            var link = $"/Letter/Details/{letter.LetterId}";
            var label = $"{letter.LetterNumber} - {letter.Subject}";
            var reasonSuffix = string.IsNullOrWhiteSpace(detail) ? "" : $" Reason/comment: {detail.Trim()}";

            if (kind is "Submitted" or "Resubmitted" or "StepApproved")
            {
                var title = kind == "Resubmitted" ? "Revised letter awaiting your approval" : "Letter awaiting your approval";

                // با زنجیره‌ی تایید فقط تاییدکننده‌ی مرحله‌ی جاری اعلان می‌گیرد؛ بدون زنجیره (نامه‌ی قدیمی) همه‌ی دارندگان مجوز Approve
                var current = await _context.LetterApprovalSteps.Where(s => s.LetterId == letter.LetterId && s.Status == "Pending")
                    .OrderBy(s => s.StepOrder).Select(s => (int?)s.ApproverUserId).FirstOrDefaultAsync();
                var targets = current.HasValue ? new List<int> { current.Value } : await GetApproverUserIdsAsync();

                foreach (var userId in targets)
                {
                    if (userId == actorUserId) continue;
                    await _notifications.CreateAsync(userId, title, label, link, "Letter", letter.LetterId);
                }

                // نویسنده از پیشرفت زنجیره باخبر می‌شود (یک مرحله تایید شد و نوبت نفر بعد است)
                if (kind == "StepApproved" && letter.RegisteredById.HasValue && letter.RegisteredById != actorUserId)
                    await _notifications.CreateAsync(letter.RegisteredById.Value, "Your letter passed an approval step", label, link, "Letter", letter.LetterId);
                return;
            }

            if (!letter.RegisteredById.HasValue || letter.RegisteredById == actorUserId) return;

            var (authorTitle, authorMessage) = kind switch
            {
                "Approved" => ("Your letter was approved", label),
                "ReturnedForRevision" => ("Your letter was returned for revision", label + reasonSuffix),
                "Rejected" => ("Your letter was rejected", label + reasonSuffix),
                "Issued" => ("Your letter was issued", label),
                _ => (null, null)
            };

            if (authorTitle != null)
                await _notifications.CreateAsync(letter.RegisteredById.Value, authorTitle, authorMessage!, link, "Letter", letter.LetterId);
        }
        catch (Exception)
        {
            // اعلان بهترین‌تلاش است
        }
    }

    private Task<List<int>> GetApproverUserIdsAsync() => LetterDirectoryQueries.ApproverUserIdsAsync(_context);

    // ==============================================================================
    // پاسخ‌خواهی (Phase 2)
    // ==============================================================================
    public async Task<(bool Success, string? Error)> SetResponseDueAsync(int letterId, bool responseRequired, DateTime? dueDate,
        int? actorApplicationUserId, bool canManageAny)
    {
        var letter = await _context.Letters.FirstOrDefaultAsync(x => x.LetterId == letterId && x.IsActive);
        if (letter == null) return (false, "نامه یافت نشد.");

        if (LetterResponseRules.FinishedStatuses.Contains(letter.Status))
            return (false, $"پاسخ‌خواهی نامه‌ی «{letter.Status}» قابل تغییر نیست.");

        if (!canManageAny && (!actorApplicationUserId.HasValue || letter.RegisteredById != actorApplicationUserId))
            return (false, "فقط ثبت‌کننده‌ی نامه یا کاربر دارای اختیار مدیریت می‌تواند پاسخ‌خواهی را تغییر دهد.");

        var error = LetterResponseRules.Validate(responseRequired, dueDate, letter.LetterDate);
        if (error != null) return (false, error);

        letter.ResponseRequired = responseRequired;
        letter.ResponseDueDate = responseRequired ? dueDate!.Value.Date : null;

        return await SaveWithConcurrencyCheckAsync();
    }

    // ==============================================================================
    // داشبوردها. هر دو از همان فیلتر محرمانگی فهرست عبور می‌کنند (قبلاً شمارنده‌ها همه‌ی نامه‌ها را بدون محدودیت می‌شمردند).
    // ==============================================================================
    public async Task<LetterDashboardDto> GetDashboardAsync(int? currentApplicationUserId = null, bool canViewConfidential = false)
    {
        var visible = await ApplyConfidentialityAsync(
            _context.Letters.AsNoTracking().Where(x => x.IsActive), canViewConfidential, currentApplicationUserId);
        var letters = await visible.ToListAsync();
        var visibleIds = letters.Select(x => x.LetterId).ToList();
        var referrals = await _context.LetterReferrals.Where(r => visibleIds.Contains(r.LetterId)).ToListAsync();

        var dashboard = new LetterDashboardDto
        {
            PendingLetters = letters.Count(x => x.Status is "Registered" or "UnderReview" or "PendingApproval" or "Draft"),
            OverdueReferrals = referrals.Count(r => r.Status != "Completed" && r.DueDate.HasValue && r.DueDate.Value < DateTime.Now),
            RecentlyReceived = letters.Count(x => x.LetterType == "Incoming" && x.LetterDate >= DateTime.Now.AddDays(-7)),
            RecentlySent = letters.Count(x => x.LetterType == "Outgoing" && x.Status == "Issued" && x.IssuedDate >= DateTime.Now.AddDays(-7)),
            ByStatus = letters.GroupBy(x => x.Status).ToDictionary(g => g.Key, g => g.Count()),
            ByType = letters.GroupBy(x => x.LetterType).ToDictionary(g => g.Key, g => g.Count())
        };

        if (currentApplicationUserId.HasValue)
        {
            var employeeId = await _context.Users
                .Where(x => x.Id == currentApplicationUserId.Value)
                .Select(x => x.EmployeeId)
                .FirstOrDefaultAsync();

            if (employeeId.HasValue)
            {
                dashboard.MyOpenReferrals = referrals.Count(r =>
                    r.ReferredToEmployeeId == employeeId.Value && r.Status != "Completed");
            }
        }

        return dashboard;
    }

    private static readonly string[] ClosedLikeStatuses = { "Closed", "Rejected", "Issued" };

    public async Task<LetterWorkspaceDashboardDto> GetWorkspaceDashboardAsync(bool canViewConfidential, int? applicationUserId, bool canReview)
    {
        var visible = await ApplyConfidentialityAsync(
            _context.Letters.AsNoTracking().Where(x => x.IsActive), canViewConfidential, applicationUserId);
        var letters = await visible.Select(x => new
        {
            x.LetterId, x.LetterNumber, x.Subject, x.LetterType, x.Status, x.Priority, x.LetterDate, x.IssuedDate,
            x.RegisteredById, x.ResponseRequired, x.ResponseDueDate
        }).ToListAsync();

        var ids = letters.Select(x => x.LetterId).ToList();
        var openReferrals = await _context.LetterReferrals
            .Where(r => ids.Contains(r.LetterId) && r.Status != "Completed")
            .Select(r => new { r.LetterId, r.DueDate, r.ReferredToEmployeeId })
            .ToListAsync();

        var answered = (await _context.Letters
            .Where(c => c.IsActive && c.ParentLetterId != null && ids.Contains(c.ParentLetterId.Value)
                && LetterResponseRules.AnsweredChildStatuses.Contains(c.Status))
            .Select(c => c.ParentLetterId!.Value).Distinct().ToListAsync()).ToHashSet();

        var now = DateTime.Now;
        var today = DateTime.Today;
        var d = new LetterWorkspaceDashboardDto
        {
            TotalActive = letters.Count,
            ReceivedLast30Days = letters.Count(x => x.LetterType == "Incoming" && x.LetterDate >= today.AddDays(-30)),
            IssuedLast30Days = letters.Count(x => x.Status == "Issued" && x.IssuedDate >= today.AddDays(-30)),
            OpenReferrals = openReferrals.Count,
            OverdueReferrals = openReferrals.Count(r => r.DueDate.HasValue && r.DueDate.Value < now),
            ByStatus = letters.GroupBy(x => x.Status).ToDictionary(g => g.Key, g => g.Count()),
            ByType = letters.GroupBy(x => x.LetterType).ToDictionary(g => g.Key, g => g.Count()),
            ByPriority = letters.GroupBy(x => x.Priority).ToDictionary(g => g.Key, g => g.Count()),
            AwaitingApproval = canReview ? letters.Count(x => x.Status == "PendingApproval") : 0
        };

        if (applicationUserId.HasValue)
        {
            d.MyDrafts = letters.Count(x => x.Status == "Draft" && x.RegisteredById == applicationUserId);
            d.MyReturnedForRevision = letters.Count(x => x.Status == "ReturnedForRevision" && x.RegisteredById == applicationUserId);

            var employeeId = await _context.Users.Where(x => x.Id == applicationUserId.Value)
                .Select(x => x.EmployeeId).FirstOrDefaultAsync();
            if (employeeId.HasValue)
                d.MyOpenReferrals = openReferrals.Count(r => r.ReferredToEmployeeId == employeeId.Value);
        }

        var open = letters.Where(x => !ClosedLikeStatuses.Contains(x.Status)).ToList();
        d.OpenLetters = open.Count;

        int Age(DateTime date) => Math.Max(0, (int)(today - date.Date).TotalDays);
        d.Aging = new List<LetterAgingBucketDto>
        {
            new() { Key = "0-7", Count = open.Count(x => Age(x.LetterDate) <= 7) },
            new() { Key = "8-30", Count = open.Count(x => Age(x.LetterDate) is > 7 and <= 30) },
            new() { Key = "31-90", Count = open.Count(x => Age(x.LetterDate) is > 30 and <= 90) },
            new() { Key = "90+", Count = open.Count(x => Age(x.LetterDate) > 90) }
        };

        var overdueResponses = letters
            .Where(x => LetterResponseRules.IsOverdue(x.ResponseRequired, x.ResponseDueDate, answered.Contains(x.LetterId), x.Status, today))
            .ToList();
        d.OverdueResponses = overdueResponses.Count;

        var byId = letters.ToDictionary(x => x.LetterId);
        var attention = new List<LetterAttentionItemDto>();
        foreach (var g in openReferrals.Where(r => r.DueDate.HasValue && r.DueDate.Value < now).GroupBy(r => r.LetterId))
        {
            var due = g.Min(r => r.DueDate!.Value);
            var l = byId[g.Key];
            attention.Add(new LetterAttentionItemDto
            {
                LetterId = l.LetterId, LetterNumber = l.LetterNumber, Subject = l.Subject, Reason = "OverdueReferral",
                DueDate = due, DaysOverdue = Math.Max(1, (int)(today - due.Date).TotalDays)
            });
        }
        foreach (var l in overdueResponses)
        {
            attention.Add(new LetterAttentionItemDto
            {
                LetterId = l.LetterId, LetterNumber = l.LetterNumber, Subject = l.Subject, Reason = "OverdueResponse",
                DueDate = l.ResponseDueDate!.Value, DaysOverdue = Math.Max(1, (int)(today - l.ResponseDueDate!.Value.Date).TotalDays)
            });
        }
        d.NeedsAttention = attention.OrderByDescending(a => a.DaysOverdue).ThenBy(a => a.LetterId).Take(10).ToList();

        return d;
    }

    public async Task ToggleStarAsync(int letterId, int applicationUserId)
    {
        var existing = await _context.LetterStars
            .FirstOrDefaultAsync(x => x.LetterId == letterId && x.ApplicationUserId == applicationUserId);

        if (existing != null)
        {
            _context.LetterStars.Remove(existing);
        }
        else
        {
            await _context.LetterStars.AddAsync(new LetterStar
            {
                LetterId = letterId,
                ApplicationUserId = applicationUserId,
                CreatedDate = DateTime.Now
            });
        }

        await _context.SaveChangesAsync();
    }

    public async Task AddRecipientAsync(int letterId, string recipientType, int? employeeId, int? departmentId, string? externalName)
    {
        await _context.LetterRecipients.AddAsync(new LetterRecipient
        {
            LetterId = letterId,
            RecipientType = string.IsNullOrWhiteSpace(recipientType) ? "To" : recipientType,
            EmployeeId = employeeId,
            DepartmentId = departmentId,
            ExternalName = externalName,
            IsRead = false,
            CreatedDate = DateTime.Now
        });

        await _context.SaveChangesAsync();
    }

    public async Task MarkRecipientReadAsync(int letterRecipientId, int applicationUserId)
    {
        var myEmployeeId = await _context.Users
            .Where(x => x.Id == applicationUserId)
            .Select(x => x.EmployeeId)
            .FirstOrDefaultAsync();

        var recipient = await _context.LetterRecipients
            .FirstOrDefaultAsync(x => x.LetterRecipientId == letterRecipientId
                && (!myEmployeeId.HasValue || x.EmployeeId == myEmployeeId.Value));

        if (recipient == null) return;

        recipient.IsRead = true;
        recipient.ReadDate = DateTime.Now;

        await _context.SaveChangesAsync();
    }

    public async Task SignAsync(int letterId, string signedByName)
    {
        var letter = await _context.Letters.FirstOrDefaultAsync(x => x.LetterId == letterId);
        if (letter == null) return;

        letter.SignedByName = signedByName;
        letter.SignedDate = DateTime.Now;

        await _context.SaveChangesAsync();
    }

    public async Task<List<LetterTimelineEventDto>> GetTimelineAsync(int letterId, LetterViewer? viewer = null)
    {
        var letter = await _context.Letters.FirstOrDefaultAsync(x => x.LetterId == letterId);
        if (letter == null) return new List<LetterTimelineEventDto>();

        // کارمند اقدام‌کننده‌ی ثبت/تایید (RegisteredById/ApprovedById شناسه‌ی کاربرند) برای فیلتر نقش در تاریخچه گردش
        var actorUserIds = new[] { letter.RegisteredById, letter.ApprovedById }.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        var actorEmployees = actorUserIds.Count == 0
            ? new Dictionary<int, int?>()
            : await _context.Users.Where(u => actorUserIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.EmployeeId);
        int? EmployeeOfUser(int? userId) => userId.HasValue && actorEmployees.TryGetValue(userId.Value, out var e) ? e : null;

        var events = new List<LetterTimelineEventDto>
        {
            new()
            {
                EventDate = letter.CreatedDate,
                EventType = "Created",
                Description = $"{letter.LetterType} letter registered: {letter.Subject}",
                ActorEmployeeId = EmployeeOfUser(letter.RegisteredById),
                Icon = "bi-file-earmark-plus",
                Color = "primary"
            }
        };

        var recipients = await _context.LetterRecipients
            .Include(x => x.Employee)
            .Include(x => x.Department)
            .Where(x => x.LetterId == letterId)
            .ToListAsync();

        // BCC در تایم‌لاین هم فقط برای نویسنده/دسترسی کامل/خودِ گیرنده دیده می‌شود (بدون viewer: پنهان)
        var mayListBcc = viewer != null && (viewer.CanSeeAll || (viewer.UserId.HasValue && letter.RegisteredById == viewer.UserId));
        recipients = recipients
            .Where(r => r.RecipientType != "Bcc" || mayListBcc || (viewer?.EmployeeId.HasValue == true && r.EmployeeId == viewer.EmployeeId))
            .ToList();

        foreach (var r in recipients)
        {
            var name = r.Employee != null ? r.Employee.FirstName + " " + r.Employee.LastName : (r.Department?.Name ?? r.ExternalName ?? "-");

            events.Add(new LetterTimelineEventDto
            {
                EventDate = r.CreatedDate,
                EventType = "Recipient",
                Description = $"Added as {(r.RecipientType == "To" ? "recipient (To)" : r.RecipientType == "Bcc" ? "Bcc" : "Cc")}: {name}",
                Icon = "bi-person-plus",
                Color = "secondary"
            });

            if (r.IsRead && r.ReadDate.HasValue)
            {
                events.Add(new LetterTimelineEventDto
                {
                    EventDate = r.ReadDate.Value,
                    EventType = "Read",
                    Description = $"Read by {name}",
                    ActorEmployeeId = r.EmployeeId,
                    Icon = "bi-check2-all",
                    Color = "success"
                });
            }
        }

        var referrals = await _context.LetterReferrals
            .Include(x => x.ReferredToEmployee)
            .Include(x => x.ReferredToDepartment)
            .Where(x => x.LetterId == letterId)
            .ToListAsync();

        foreach (var ref_ in referrals)
        {
            var target = ref_.ReferredToEmployee != null
                ? ref_.ReferredToEmployee.FirstName + " " + ref_.ReferredToEmployee.LastName
                : (ref_.ReferredToDepartment?.Name ?? "-");

            events.Add(new LetterTimelineEventDto
            {
                EventDate = ref_.ReferredDate,
                EventType = "Referred",
                Description = $"Referred to {target}" + (string.IsNullOrEmpty(ref_.Instructions) ? "" : $" — \"{ref_.Instructions}\""),
                ActorName = ref_.ReferredByName,
                Icon = "bi-signpost-split",
                Color = "warning"
            });

            if (ref_.IsRead && ref_.ReadDate.HasValue)
            {
                events.Add(new LetterTimelineEventDto
                {
                    EventDate = ref_.ReadDate.Value,
                    EventType = "Read",
                    Description = $"Referral read by {target}",
                    ActorEmployeeId = ref_.ReferredToEmployeeId,
                    Icon = "bi-eye",
                    Color = "info"
                });
            }

            if (ref_.Status == "Completed" && ref_.CompletedDate.HasValue)
            {
                events.Add(new LetterTimelineEventDto
                {
                    EventDate = ref_.CompletedDate.Value,
                    EventType = "Completed",
                    Description = $"Referral completed by {target}" + (string.IsNullOrEmpty(ref_.ResponseNote) ? "" : $" — \"{ref_.ResponseNote}\""),
                    ActorEmployeeId = ref_.ReferredToEmployeeId,
                    Icon = "bi-check-circle",
                    Color = "success"
                });
            }
        }

        if (letter.ApprovedDate.HasValue)
        {
            events.Add(new LetterTimelineEventDto
            {
                EventDate = letter.ApprovedDate.Value,
                EventType = "Status",
                Description = "Approved",
                ActorEmployeeId = EmployeeOfUser(letter.ApprovedById),
                Icon = "bi-check2-square",
                Color = "success"
            });
        }

        if (letter.IssuedDate.HasValue)
        {
            events.Add(new LetterTimelineEventDto
            {
                EventDate = letter.IssuedDate.Value,
                EventType = "Status",
                Description = "Issued",
                Icon = "bi-send-check",
                Color = "primary"
            });
        }

        if (letter.SignedDate.HasValue)
        {
            events.Add(new LetterTimelineEventDto
            {
                EventDate = letter.SignedDate.Value,
                EventType = "Signed",
                Description = $"Electronically acknowledged by {letter.SignedByName}",
                Icon = "bi-patch-check-fill",
                Color = "success"
            });
        }

        // تاریخچه‌ی پاسخ‌خواهی (Phase 3): چه کسی، چه زمانی، الزام پاسخ/مهلت را تنظیم، تغییر یا حذف کرد - از ممیزی خودکار خوانده می‌شود
        var key = letterId.ToString();
        var audits = await _context.AuditLogs.AsNoTracking()
            .Where(a => a.EntityName == nameof(Letter) && a.EntityKeyValue == key && a.Action == "Update"
                && a.ChangedFieldsSummary != null
                && (a.ChangedFieldsSummary.Contains("ResponseRequired:") || a.ChangedFieldsSummary.Contains("ResponseDueDate:")))
            .ToListAsync();

        foreach (var audit in audits)
        {
            var description = LetterResponseHistory.Describe(audit.ChangedFieldsSummary);
            if (description == null) continue;

            events.Add(new LetterTimelineEventDto
            {
                EventDate = audit.Timestamp,
                EventType = "Response",
                Description = description,
                ActorName = audit.UserName,
                Icon = "bi-reply",
                Color = "info"
            });
        }

        return events.OrderBy(x => x.EventDate).ToList();
    }

    public async Task<List<ThreadMessageDto>> GetThreadAsync(int letterId)
    {
        var chain = new List<ThreadMessageDto>();
        var currentId = (int?)letterId;
        var visited = new HashSet<int>();

        while (currentId.HasValue && visited.Add(currentId.Value))
        {
            var letter = await _context.Letters.FirstOrDefaultAsync(x => x.LetterId == currentId.Value);
            if (letter == null) break;

            var recipients = await _context.LetterRecipients
                .Include(x => x.Employee)
                .Include(x => x.Department)
                .Where(x => x.LetterId == letter.LetterId)
                .ToListAsync();

            var recipientsSummary = string.Join("، ", recipients.Select(r =>
                r.Employee != null ? r.Employee.FirstName + " " + r.Employee.LastName : (r.Department?.Name ?? r.ExternalName ?? "-")));

            chain.Add(new ThreadMessageDto
            {
                LetterId = letter.LetterId,
                Subject = letter.Subject,
                Body = letter.Body,
                LetterNumber = letter.LetterNumber,
                LetterDate = letter.LetterDate,
                SenderOrRegisteredByName = letter.LetterType == "Incoming" ? letter.Sender : letter.Receiver,
                RecipientsSummary = recipientsSummary
            });

            currentId = letter.ParentLetterId;
        }

        chain.Reverse();

        return chain;
    }

    public async Task<int> ReplyAsync(int parentLetterId, string letterNumber, string subject, string? body,
        string? receiver, int? registeredById)
    {
        var parent = await _context.Letters.FirstOrDefaultAsync(x => x.LetterId == parentLetterId);
        if (parent == null) throw new Exception("نامه‌ی اصلی یافت نشد");

        var reply = new Letter
        {
            LetterType = "Outgoing",
            LetterNumber = letterNumber,
            LetterDate = DateTime.Now,
            Receiver = receiver ?? parent.Sender,
            Subject = subject,
            Body = _sanitizer.Sanitize(body),
            Priority = parent.Priority,
            ConfidentialityLevel = parent.ConfidentialityLevel,
            ProjectId = parent.ProjectId,
            DisciplineId = parent.DisciplineId,
            ParentLetterId = parent.LetterId,
            RegisteredById = registeredById,
            Status = "Draft",
            IsActive = true,
            CreatedDate = DateTime.Now
        };

        await _context.Letters.AddAsync(reply);
        await _context.SaveChangesAsync();

        if (parent.Status == "Registered")
            parent.Status = "Referred";

        await _context.SaveChangesAsync();

        await IndexLetterForSemanticSearchAsync(reply);

        return reply.LetterId;
    }


    public async Task AddAttachmentAsync(int letterId, string fileName, string fileExtension, string filePath,
        long fileSize, string? mimeType, int? uploadedById)
    {
        await _context.DocumentAttachments.AddAsync(new DocumentAttachment
        {
            LetterId = letterId,
            FileName = fileName,
            FileExtension = fileExtension,
            FilePath = filePath,
            FileSize = fileSize,
            MimeType = mimeType,
            UploadedById = uploadedById,
            UploadDate = DateTime.Now,
            IsActive = true,
            AIStatus = "NotApplicable"
        });

        await _context.SaveChangesAsync();
    }
}
