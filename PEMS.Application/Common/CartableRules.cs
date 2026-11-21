namespace PEMS.Application.Common;

/// <summary>
/// پوشه‌های کارتابل نامه‌نگاری. «پوشه» یک نمای شخصی روی نامه‌هایی است که کاربر اجازه‌ی دیدنشان را دارد؛ هیچ پوشه‌ای
/// قانون محرمانگی را دور نمی‌زند (همه از همان پرس‌وجوی LetterVisibility می‌گذرند).
/// پوشه‌ی خالی (بدون folder) همان نمای قبلی «همه‌ی مکاتبات» است و فیلترهای قبلی status/starred به‌همان شکل کار می‌کنند.
/// </summary>
public static class LetterFolders
{
    public const string Inbox = "inbox";          // نامه‌هایی که به من رسیده (گیرنده/ارجاع‌شده/وارده‌ی ثبت‌شده برای من)
    public const string Sent = "sent";            // نامه‌هایی که خودم نوشته‌ام و از پیش‌نویس گذشته
    public const string Drafts = "drafts";        // پیش‌نویس‌ها و برگشت‌خورده‌های خودم
    public const string Shared = "shared";        // ارجاع‌شده به من
    public const string Delegated = "delegated";  // کارتابل تفویض‌شده به من
    public const string Starred = "starred";
    public const string Archive = "archive";      // آرشیو شخصی
    public const string Trash = "trash";          // سطل زباله‌ی شخصی
    public const string All = "all";              // همه‌ی پیام‌ها (به‌جز حذف دائم شخصی)

    public static readonly string[] Ordered = { Inbox, Shared, Sent, Drafts, Starred, Archive, Trash, All, Delegated };

    public static string? Normalize(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return null;
        var f = folder.Trim().ToLowerInvariant();
        return Ordered.Contains(f) ? f : null;
    }
}

/// <summary>درخت برچسب‌ها (مهندسی/سازمانی/شخصی): عمق و نبودن دور سمت سرور بررسی می‌شود.</summary>
public static class TagTreeRules
{
    public const int MaxDepth = 4;
    public const string ErrSelfParent = "Tag_Err_SelfParent";
    public const string ErrCycle = "Tag_Err_Cycle";
    public const string ErrTooDeep = "Tag_Err_TooDeep";
    public const string ErrParentMissing = "Tag_Err_ParentMissing";
    public const string ErrScopeMismatch = "Tag_Err_ScopeMismatch";

    public sealed record Node(int TagId, int? ParentTagId, int? OwnerUserId, bool IsActive);

    /// <summary>عمق یک گره (ریشه = ۱). دور یا والد گمشده = int.MaxValue.</summary>
    public static int DepthOf(int tagId, IReadOnlyDictionary<int, Node> nodes)
    {
        var depth = 0; int? current = tagId; var seen = new HashSet<int>();
        while (current.HasValue)
        {
            if (!seen.Add(current.Value) || !nodes.TryGetValue(current.Value, out var n)) return int.MaxValue;
            depth++;
            current = n.ParentTagId;
        }
        return depth;
    }

    /// <summary>شناسه‌ی همه‌ی نوادگان یک برچسب (برای فیلتر «این برچسب و زیرمجموعه‌هایش»).</summary>
    public static HashSet<int> DescendantsAndSelf(int tagId, IEnumerable<Node> nodes)
    {
        var list = nodes.ToList();
        var result = new HashSet<int> { tagId };
        var grew = true;
        while (grew)
        {
            grew = false;
            foreach (var n in list)
                if (n.ParentTagId.HasValue && result.Contains(n.ParentTagId.Value) && result.Add(n.TagId)) grew = true;
        }
        return result;
    }

    /// <summary>ارتفاع زیردرخت (خودش = ۱) تا انتقال یک شاخه‌ی عمیق، عمق مجاز را نشکند.</summary>
    public static int SubtreeHeight(int tagId, IEnumerable<Node> nodes)
    {
        var list = nodes.ToList();
        int H(int id, int guard) => guard > 16 ? 16 : 1 + list.Where(n => n.ParentTagId == id).Select(n => H(n.TagId, guard + 1)).DefaultIfEmpty(0).Max();
        return H(tagId, 0);
    }

    /// <summary>
    /// null = مجاز. والد باید وجود و فعال باشد، با خودش یکی نباشد، از نوادگانش نباشد، هم‌دامنه باشد (برچسب سازمانی فقط زیر
    /// سازمانی؛ شخصی فقط زیر شخصیِ همان کاربر یا زیر سازمانی) و عمق نهایی از MaxDepth نگذرد.
    /// </summary>
    public static string? ValidateParent(int? tagId, int? newParentId, int? ownerUserId, IReadOnlyDictionary<int, Node> nodes)
    {
        if (!newParentId.HasValue) return null;
        if (tagId.HasValue && newParentId.Value == tagId.Value) return ErrSelfParent;
        if (!nodes.TryGetValue(newParentId.Value, out var parent) || !parent.IsActive) return ErrParentMissing;
        if (tagId.HasValue && DescendantsAndSelf(tagId.Value, nodes.Values).Contains(newParentId.Value)) return ErrCycle;

        // سازمانی زیر شخصی ممنوع؛ شخصیِ کاربر دیگر ممنوع
        if (!ownerUserId.HasValue && parent.OwnerUserId.HasValue) return ErrScopeMismatch;
        if (ownerUserId.HasValue && parent.OwnerUserId.HasValue && parent.OwnerUserId != ownerUserId) return ErrScopeMismatch;

        var parentDepth = DepthOf(newParentId.Value, nodes);
        if (parentDepth == int.MaxValue) return ErrCycle;
        var below = tagId.HasValue ? SubtreeHeight(tagId.Value, nodes.Values) : 1;
        return parentDepth + below > MaxDepth ? ErrTooDeep : null;
    }

    /// <summary>بیننده برچسب را می‌بیند: سازمانی یا شخصیِ خودش.</summary>
    public static bool IsVisibleTo(int? tagOwnerUserId, int? viewerUserId) => !tagOwnerUserId.HasValue || tagOwnerUserId == viewerUserId;
}

/// <summary>قواعد تفویض کارتابل (فقط دیدن).</summary>
public static class LetterDelegationRules
{
    public const string ErrSelf = "Del_Err_Self";
    public const string ErrDates = "Del_Err_Dates";
    public const string ErrPast = "Del_Err_Past";
    public const string ErrTooLong = "Del_Err_TooLong";
    public const string ErrOverlap = "Del_Err_Overlap";
    public const string ErrCircular = "Del_Err_Circular";
    public const int MaxDays = 180;

    public sealed record Span(int DelegatorUserId, int DelegateUserId, DateTime FromDate, DateTime ToDate, bool IsActive);

    public static bool IsEffective(Span s, DateTime now) => s.IsActive && s.FromDate.Date <= now.Date && now.Date <= s.ToDate.Date;

    /// <summary>null = مجاز.</summary>
    public static string? Validate(int delegatorUserId, int delegateUserId, DateTime from, DateTime to, DateTime now, IEnumerable<Span> existing)
    {
        if (delegatorUserId == delegateUserId) return ErrSelf;
        if (to.Date < from.Date) return ErrDates;
        if (to.Date < now.Date) return ErrPast;
        if ((to.Date - from.Date).TotalDays > MaxDays) return ErrTooLong;

        var active = existing.Where(e => e.IsActive).ToList();
        // همان تفویض‌کننده، همان گیرنده، بازه‌ی هم‌پوشان
        if (active.Any(e => e.DelegatorUserId == delegatorUserId && e.DelegateUserId == delegateUserId && e.FromDate.Date <= to.Date && from.Date <= e.ToDate.Date)) return ErrOverlap;
        // تفویض دوطرفه هم‌زمان (الف به ب و ب به الف) بی‌معناست
        if (active.Any(e => e.DelegatorUserId == delegateUserId && e.DelegateUserId == delegatorUserId && e.FromDate.Date <= to.Date && from.Date <= e.ToDate.Date)) return ErrCircular;
        return null;
    }
}

/// <summary>حقایق یک نامه برای ارزیابی قانون مسیریابی (مستقل از EF تا قابل تست باشد).</summary>
public sealed record LetterRoutingFacts(string LetterType, string Priority, string? Category, int? ProjectId, int? DisciplineId, string? Sender, string Subject);

public sealed record LetterRoutingCondition(string? LetterType, string? Priority, string? Category, int? ProjectId, int? DisciplineId,
    string? SenderContains, string? SubjectContains)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(LetterType) && string.IsNullOrWhiteSpace(Priority) && string.IsNullOrWhiteSpace(Category)
        && !ProjectId.HasValue && !DisciplineId.HasValue && string.IsNullOrWhiteSpace(SenderContains) && string.IsNullOrWhiteSpace(SubjectContains);
}

public static class LetterRoutingRuleEvaluator
{
    /// <summary>همه‌ی شرط‌های پرشده باید برقرار باشند. قانون بدون هیچ شرطی هرگز مچ نمی‌شود (جلوگیری از اقدام روی همه‌ی نامه‌ها).</summary>
    public static bool Matches(LetterRoutingCondition c, LetterRoutingFacts f)
    {
        if (c.IsEmpty) return false;
        bool Eq(string? want, string? have) => string.IsNullOrWhiteSpace(want) || string.Equals(want.Trim(), have?.Trim(), StringComparison.OrdinalIgnoreCase);
        bool Has(string? needle, string? hay) => string.IsNullOrWhiteSpace(needle) || (hay ?? "").Contains(needle.Trim(), StringComparison.OrdinalIgnoreCase);
        return Eq(c.LetterType, f.LetterType) && Eq(c.Priority, f.Priority) && Eq(c.Category, f.Category)
            && (!c.ProjectId.HasValue || c.ProjectId == f.ProjectId)
            && (!c.DisciplineId.HasValue || c.DisciplineId == f.DisciplineId)
            && Has(c.SenderContains, f.Sender) && Has(c.SubjectContains, f.Subject);
    }
}

/// <summary>ستون‌ها و ترجیحات نمایشی کارتابل (فقط UI؛ مقدار نامعتبر = پیش‌فرض).</summary>
public static class LetterUserSettingsRules
{
    public static readonly string[] ToggleableColumns = { "priority", "discipline", "attachments", "responseDue", "lastActivity", "letterNo", "status", "date" };
    public static readonly string[] Views = { "flat", "thread" };
    public static readonly string[] Densities = { "normal", "dense" };

    public sealed class Settings
    {
        public string View { get; set; } = "flat";
        public string Density { get; set; } = "normal";
        // پیش‌فرض همان ستون‌های پنهانِ قبلی فهرست (دیسیپلین، پیوست، آخرین فعالیت)
        public List<string> HiddenColumns { get; set; } = new() { "discipline", "attachments", "lastActivity" };
        public int FrozenColumns { get; set; }
        public bool OpenPreviewPane { get; set; } = true;
        public bool ConfirmTrash { get; set; } = true;
        public bool MarkReadOnOpen { get; set; } = true;
        public int PageSize { get; set; } = 50;
    }

    /// <summary>هر مقدار ناشناخته/خراب با پیش‌فرض جایگزین می‌شود؛ هرگز استثنا نمی‌دهد.</summary>
    public static Settings Sanitize(Settings? s)
    {
        s ??= new Settings();
        s.View = Views.Contains(s.View) ? s.View : "flat";
        s.Density = Densities.Contains(s.Density) ? s.Density : "normal";
        s.HiddenColumns = (s.HiddenColumns ?? new()).Where(c => ToggleableColumns.Contains(c)).Distinct().ToList();
        s.FrozenColumns = Math.Clamp(s.FrozenColumns, 0, 3);
        s.PageSize = new[] { 20, 50, 100 }.Contains(s.PageSize) ? s.PageSize : 50;
        return s;
    }

    public static Settings Parse(string? json)
    {
        try { return Sanitize(string.IsNullOrWhiteSpace(json) ? null : System.Text.Json.JsonSerializer.Deserialize<Settings>(json)); }
        catch { return Sanitize(null); }
    }

    public static string Serialize(Settings s) => System.Text.Json.JsonSerializer.Serialize(Sanitize(s));
}

/// <summary>فیلتر و ترتیب تاریخچه‌ی گردش نامه بر اساس نقش اقدام‌کننده نسبت به بیننده (چارت سازمانی).</summary>
public static class LetterHistoryFilter
{
    public static readonly string[] Roles = { "all", "me", "manager", "subordinates" };

    public static string NormalizeRole(string? role)
    {
        var r = (role ?? "all").Trim().ToLowerInvariant();
        return Roles.Contains(r) ? r : "all";
    }

    /// <summary>
    /// «همه»: همه‌ی رویدادها. «من»/«مدیر»/«زیردستان»: فقط رویدادهایی که اقدام‌کننده‌شان شناخته‌شده و در آن گروه است؛
    /// رویداد بدون اقدام‌کننده‌ی شناخته‌شده در فیلترهای نقش نمایش داده نمی‌شود (چیزی حدس زده نمی‌شود).
    /// </summary>
    public static List<DTOs.LetterTimelineEventDto> Apply(IEnumerable<DTOs.LetterTimelineEventDto> events, string? role, bool newestFirst,
        int? meEmployeeId, IReadOnlySet<int> managerEmployeeIds, IReadOnlySet<int> subordinateEmployeeIds)
    {
        var r = NormalizeRole(role);
        var q = events.Where(e => r switch
        {
            "me" => meEmployeeId.HasValue && e.ActorEmployeeId == meEmployeeId,
            "manager" => e.ActorEmployeeId.HasValue && managerEmployeeIds.Contains(e.ActorEmployeeId.Value),
            "subordinates" => e.ActorEmployeeId.HasValue && subordinateEmployeeIds.Contains(e.ActorEmployeeId.Value),
            _ => true
        });
        return (newestFirst ? q.OrderByDescending(e => e.EventDate) : q.OrderBy(e => e.EventDate)).ToList();
    }
}
