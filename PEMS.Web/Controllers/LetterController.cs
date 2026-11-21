using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Web.Helpers;
using PEMS.Web.Security;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PEMS.Web.Controllers;

/// <summary>
/// ماژول مکاتبات رسمی: Incoming/Outgoing/Internal یکپارچه، گردش‌کار ارجاع، جستجو، Dashboard.
/// </summary>
public class LetterController : Controller
{
    private readonly ILetterService _letterService;
    private readonly ILetterReferralService _referralService;
    private readonly PEMSDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IPermissionService _permissionService;
    private readonly ITagService _tagService;
    private readonly IRichTextSanitizerService _richTextSanitizer;
    private readonly ICacheService _cache;
    private readonly ILetterNumberingService _numberingService;
    private readonly IFileStorageService _fileStorage;
    private readonly INotificationService _notifications;
    private readonly ILetterTemplateService? _templates;
    private readonly ILetterReminderService? _reminders;
    private readonly ILetterComposeService? _composeService;
    private readonly IConfiguration _configuration;
    private readonly ILetterCartableService? _cartable;
    private readonly ILetterTagTreeService? _tagTree;
    private readonly ILetterUserSettingsService? _userSettings;

    public LetterController(ILetterService letterService, ILetterReferralService referralService,
        PEMSDbContext context, ICurrentUserService currentUser, IPermissionService permissionService,
        ITagService tagService, IRichTextSanitizerService richTextSanitizer, ICacheService cache,
        ILetterNumberingService numberingService, IFileStorageService fileStorage,
        INotificationService notifications, ILetterTemplateService? templateService = null,
        ILetterReminderService? reminderService = null, ILetterComposeService? composeService = null,
        IConfiguration? configuration = null, ILetterCartableService? cartable = null,
        ILetterTagTreeService? tagTree = null, ILetterUserSettingsService? userSettings = null)
    {
        _cartable = cartable;
        _tagTree = tagTree;
        _userSettings = userSettings;
        _composeService = composeService;
        _configuration = configuration ?? new ConfigurationBuilder().Build();
        _templates = templateService;
        _reminders = reminderService;
        _notifications = notifications;
        _letterService = letterService;
        _referralService = referralService;
        _context = context;
        _currentUser = currentUser;
        _permissionService = permissionService;
        _tagService = tagService;
        _richTextSanitizer = richTextSanitizer;
        _cache = cache;
        _numberingService = numberingService;
        _fileStorage = fileStorage;
    }

    // طبق سند «PEMS — Global Data Grid Standard» (فاز ۶B - P1): قبلاً pageSize=25 مستقیم و
    // بدون Normalize پاس داده می‌شد و از QueryString هم پذیرفته نمی‌شد.
    public async Task<IActionResult> Index(string? letterType, string? letterNumber, string? subject,
        string? sender, string? receiver, int? projectId, int? disciplineId, string? status,
        DateTime? fromDate, DateTime? toDate, bool starred = false, int page = 1, int? pageSize = null,
        string? priority = null, bool? hasAttachments = null, bool? hasReferrals = null, bool overdueReferral = false,
        bool overdueResponse = false, bool unread = false, string? q = null,
        string? folder = null, int? tagId = null, string? confidentiality = null, string? view = null, string? sort = null, string? dir = null)
    {
        ViewData["Title"] = "Correspondence";
        ViewData["Section"] = "Correspondence Management";
        ViewData["HelpKey"] = "Letter.Index";

        var isSystemAdmin = _currentUser.Roles.Contains("SystemAdmin");
        var canViewConfidential = isSystemAdmin ||
            await _permissionService.HasPermissionAsync(_currentUser.Roles, "Correspondence", "Approve");

        // تنظیمات شخصی کارتابل (نمای پیش‌فرض/اندازه‌ی صفحه)؛ پارامتر صریح URL همیشه بر تنظیمات غالب است
        var settings = _userSettings != null && _currentUser.UserId.HasValue
            ? await _userSettings.GetAsync(_currentUser.UserId.Value)
            : LetterUserSettingsRules.Sanitize(null);
        var viewMode = LetterUserSettingsRules.Views.Contains(view ?? "") ? view! : settings.View;
        var normalizedFolder = LetterFolders.Normalize(folder);

        var normalizedPageSize = DataGridPageSize.Normalize(pageSize ?? settings.PageSize);
        var advanced = BuildAdvancedFilter(priority, hasAttachments, hasReferrals, overdueReferral, overdueResponse, unread, q);
        advanced.Folder = normalizedFolder;
        advanced.TagId = tagId;
        advanced.Confidentiality = string.IsNullOrWhiteSpace(confidentiality) ? null : confidentiality.Trim();
        advanced.ThreadRootsOnly = viewMode == "thread";
        advanced.SortBy = string.IsNullOrWhiteSpace(sort) ? null : sort.Trim().ToLowerInvariant();
        advanced.SortDescending = !string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase);

        var result = await _letterService.SearchAsync(letterType, letterNumber, subject, sender,
            receiver, projectId, disciplineId, status, fromDate, toDate, page, normalizedPageSize,
            canViewConfidential, _currentUser.UserId, starredOnly: starred, advanced: advanced);

        var dashboard = await _letterService.GetDashboardAsync(_currentUser.UserId, canViewConfidential);

        ViewBag.Dashboard = dashboard;
        ViewBag.FolderCounts = await _letterService.GetFolderCountsAsync(canViewConfidential, _currentUser.UserId);
        ViewBag.Unread = unread;
        ViewBag.SearchQuery = string.IsNullOrWhiteSpace(q) ? null : q.Trim();

        // کارتابل: شمارنده‌ی پوشه‌های شخصی، درخت برچسب (سازمانی + شخصی)، تنظیمات و حالت نمایش
        var canManageOrg = isSystemAdmin || await _permissionService.HasPermissionAsync(_currentUser.Roles, "Correspondence", "Approve");
        ViewBag.Cartable = _cartable != null && _currentUser.UserId.HasValue
            ? await _cartable.GetCountsAsync(_currentUser.UserId.Value, canViewConfidential) : new CartableCountsDto();
        ViewBag.TagTree = _tagTree != null && _currentUser.UserId.HasValue
            ? await _tagTree.GetTreeAsync(_currentUser.UserId.Value, canManageOrg, withCounts: false, canViewConfidential)
            : new List<LetterTagNodeDto>();
        ViewBag.UserSettings = settings;
        ViewBag.Folder = normalizedFolder;
        ViewBag.TagId = tagId;
        ViewBag.ViewMode = viewMode;
        ViewBag.Sort = advanced.SortBy;
        ViewBag.SortDesc = advanced.SortDescending;
        ViewBag.Confidentiality = advanced.Confidentiality;
        ViewBag.CanManageOrg = canManageOrg;

        // طبق بند ۲۴/۲۸: Index هرگز از Employee/Document/MR/Tag استفاده نمی‌کند (فقط Project و
        // Discipline برای فیلتر/میانبر) - قبلاً همون LoadDropdownsAsync سنگین Create را صدا
        // می‌زد و همه‌ی این‌ها را بی‌فایده بارگذاری می‌کرد
        await LoadIndexDropdownsAsync();

        ViewBag.Filters = new
        {
            letterType, letterNumber, subject, sender, receiver, projectId, disciplineId, status, fromDate, toDate, starred,
            priority = NormalizePriority(priority), hasAttachments, hasReferrals, overdueReferral, overdueResponse
        };

        // دکمه‌ی آرشیو گروهی فقط برای کسی که مجوز Delete ماژول مکاتبات را دارد دیده می‌شود (سرور دوباره بررسی می‌کند)
        ViewBag.CanManageTemplates = canViewConfidential;   // = SystemAdmin أو Correspondence/Approve
        ViewBag.CanBulkArchive = isSystemAdmin ||
            await _permissionService.HasPermissionAsync(_currentUser.Roles, "Correspondence", "Delete");

        return View(result);
    }

    private static readonly string[] KnownPriorities = { "Low", "Normal", "High", "Urgent" };

    private static string? NormalizePriority(string? priority)
        => KnownPriorities.FirstOrDefault(p => string.Equals(p, priority?.Trim(), StringComparison.OrdinalIgnoreCase));

    private static LetterAdvancedFilter BuildAdvancedFilter(string? priority, bool? hasAttachments, bool? hasReferrals,
        bool overdueReferral, bool overdueResponse = false, bool unread = false, string? query = null)
        => new()
        {
            Priority = NormalizePriority(priority),
            HasAttachments = hasAttachments,
            HasReferrals = hasReferrals,
            OverdueReferralOnly = overdueReferral,
            OverdueResponseOnly = overdueResponse,
            UnreadOnly = unread,
            Query = string.IsNullOrWhiteSpace(query) ? null : query.Trim()
        };

    // خروجی همان فیلترهای Index (CSV یا Excel) - سرویس همان قانون محرمانگی Index را اعمال می‌کند
    [HttpGet]
    public async Task<IActionResult> Export(string format, string? letterType, string? letterNumber, string? subject,
        string? sender, string? receiver, int? projectId, int? disciplineId, string? status,
        DateTime? fromDate, DateTime? toDate, bool starred = false,
        string? priority = null, bool? hasAttachments = null, bool? hasReferrals = null, bool overdueReferral = false,
        bool overdueResponse = false, bool unread = false, string? q = null,
        string? folder = null, int? tagId = null, string? confidentiality = null, string? sort = null, string? dir = null)
    {
        var isSystemAdmin = _currentUser.Roles.Contains("SystemAdmin");
        var canViewConfidential = isSystemAdmin ||
            await _permissionService.HasPermissionAsync(_currentUser.Roles, "Correspondence", "Approve");

        var advanced = BuildAdvancedFilter(priority, hasAttachments, hasReferrals, overdueReferral, overdueResponse, unread, q);
        advanced.Folder = LetterFolders.Normalize(folder);
        advanced.TagId = tagId;
        advanced.Confidentiality = string.IsNullOrWhiteSpace(confidentiality) ? null : confidentiality.Trim();
        advanced.SortBy = string.IsNullOrWhiteSpace(sort) ? null : sort.Trim().ToLowerInvariant();
        advanced.SortDescending = !string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase);

        var items = await _letterService.ExportAsync(letterType, letterNumber, subject, sender, receiver, projectId,
            disciplineId, status, fromDate, toDate, canViewConfidential, _currentUser.UserId, starred, advanced);

        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
        return string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase)
            ? File(LetterListExport.BuildCsv(items), "text/csv; charset=utf-8", $"Correspondence_{stamp}.csv")
            : File(LetterListExport.BuildExcel(items),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Correspondence_{stamp}.xlsx");
    }

    // عملیات گروهی امن روی ردیف‌های انتخاب‌شده. تایید/صدور/بستن عمداً اینجا نیست (تفکیک وظایف).
    //   star / unstar  - فقط ستاره‌ی خود کاربر
    //   submit         - فقط پیش‌نویس‌های خود کاربر (همان قانون اقدام تکی)
    //   archive        - آرشیو نرم Draft/Rejected با دلیل الزامی، نیازمند مجوز Delete، ثبت در AuditLog
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Bulk(string op, int[]? ids, string? reason, string? returnUrl, int? tagId = null)
    {
        string Back() => string.IsNullOrEmpty(returnUrl) || !Url.IsLocalUrl(returnUrl) ? Url.Action(nameof(Index))! : returnUrl;

        if (ids == null || ids.Length == 0 || !_currentUser.UserId.HasValue)
        {
            TempData["Error"] = "No letters were selected.";
            return LocalRedirect(Back());
        }

        var isSystemAdmin = _currentUser.Roles.Contains("SystemAdmin");
        LetterBulkResult result;
        string done;

        switch ((op ?? "").ToLowerInvariant())
        {
            case "star":
            case "unstar":
                var canViewConfidential = isSystemAdmin ||
                    await _permissionService.HasPermissionAsync(_currentUser.Roles, "Correspondence", "Approve");
                var starring = string.Equals(op, "star", StringComparison.OrdinalIgnoreCase);
                result = await _letterService.BulkSetStarAsync(ids, _currentUser.UserId.Value, starring, canViewConfidential);
                done = starring ? "starred" : "unstarred";
                break;

            case "submit":
                result = await _letterService.BulkSubmitForReviewAsync(ids, _currentUser.UserId);
                done = "submitted for review";
                break;

            case "archive":
                if (!isSystemAdmin && !await _permissionService.HasPermissionAsync(_currentUser.Roles, "Correspondence", "Delete"))
                    return Forbid();
                result = await _letterService.BulkArchiveAsync(ids, _currentUser.UserId, _currentUser.UserName, isSystemAdmin, reason ?? "");
                done = "archived";
                break;

            // کارتابل شخصی: سطل/آرشیو/خوانده/برچسب - هیچ‌کدام خود نامه را تغییر نمی‌دهند
            case "trash": case "restore": case "purge": case "archiveme": case "unarchiveme":
            case "read": case "unread": case "tag": case "untag":
                return await CartableBulkAsync(op.ToLowerInvariant(), ids, tagId, Back());

            default:
                TempData["Error"] = "Unknown bulk action.";
                return LocalRedirect(Back());
        }

        if (result.Succeeded > 0) TempData["Message"] = $"{result.Succeeded} letter(s) {done}.";
        if (result.Failed.Count > 0)
        {
            var detail = string.Join(" | ", result.Failed.Take(5).Select(f =>
                $"{(string.IsNullOrEmpty(f.LetterNumber) ? "#" + f.LetterId : f.LetterNumber)}: {f.Reason}"));
            TempData["Error"] = $"{result.Failed.Count} letter(s) skipped. {detail}" + (result.Failed.Count > 5 ? " ..." : "");
        }

        return LocalRedirect(Back());
    }

    private async Task<bool> CanViewConfidentialAsync()
        => _currentUser.Roles.Contains("SystemAdmin") || await _permissionService.HasPermissionAsync(_currentUser.Roles, "Correspondence", "Approve");

    private async Task<IActionResult> CartableBulkAsync(string op, int[] ids, int? tagId, string back)
    {
        if (_cartable == null || _tagTree == null)
        {
            TempData["Error"] = "The cartable is not available.";
            return LocalRedirect(back);
        }

        var uid = _currentUser.UserId!.Value;
        var canView = await CanViewConfidentialAsync();
        CartableOpResult result;
        string done;

        switch (op)
        {
            case "trash": result = await _cartable.TrashAsync(ids, uid, canView); done = "moved to trash"; break;
            case "restore": result = await _cartable.RestoreAsync(ids, uid); done = "restored"; break;
            case "purge": result = await _cartable.PurgeAsync(ids, uid); done = "removed from your trash for good"; break;
            case "archiveme": result = await _cartable.ArchiveForMeAsync(ids, uid, canView); done = "archived for you"; break;
            case "unarchiveme": result = await _cartable.UnarchiveForMeAsync(ids, uid); done = "returned to your inbox"; break;
            case "read": result = await _cartable.SetReadAsync(ids, uid, true, canView); done = "marked as read"; break;
            case "unread": result = await _cartable.SetReadAsync(ids, uid, false, canView); done = "marked as unread"; break;
            default:
                if (!tagId.HasValue) { TempData["Error"] = "No tag was chosen."; return LocalRedirect(back); }
                var attach = op == "tag";
                result = await _tagTree.ApplyAsync(tagId.Value, ids, attach, uid, canView);
                done = attach ? "tagged" : "untagged";
                break;
        }

        if (result.Success) TempData["Message"] = $"{result.Affected} letter(s) {done}.";
        else TempData["Error"] = result.ErrorKey == "Cart_Err_NothingToDo" ? "Nothing to change for the selected letters." : result.ErrorKey;
        return LocalRedirect(back);
    }

    // خالی‌کردن سطل زباله‌ی شخصی = پنهان‌سازی دائم برای همین کاربر (خود نامه‌ها دست‌نخورده می‌مانند)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EmptyTrash()
    {
        if (_cartable == null || !_currentUser.UserId.HasValue) return Forbid();
        var r = await _cartable.EmptyTrashAsync(_currentUser.UserId.Value);
        if (r.Success) TempData["Message"] = $"{r.Affected} letter(s) removed from your trash for good.";
        return RedirectToAction(nameof(Index), new { folder = LetterFolders.Trash });
    }

    // اعضای رشته‌ی گفتگو برای باز شدن ردیف در «نمای رشته‌ای»؛ حق دیدن خودِ نامه اول سنجیده می‌شود
    [HttpGet]
    public async Task<IActionResult> ThreadMembers(int id)
    {
        if (_cartable == null) return NotFound();
        if (!await _letterService.CanViewAsync(id, await BuildViewerAsync())) return Forbid();
        var members = await _cartable.GetThreadAsync(id, _currentUser.UserId, await CanViewConfidentialAsync());
        return Json(members.Select(m => new
        {
            m.LetterId, m.ParentLetterId, m.Depth, m.LetterNumber, m.Subject, m.LetterType, m.Status, m.SenderName, m.IsCurrent,
            Date = m.LetterDate.ToString("yyyy-MM-dd"),
            Url = Url.Action(nameof(Details), new { id = m.LetterId })
        }));
    }

    // داشبورد اختصاصی مکاتبات (Phase 2): فقط داده‌ی واقعی و با همان قانون محرمانگی فهرست
    public async Task<IActionResult> Dashboard()
    {
        ViewData["Title"] = "Correspondence Dashboard";
        ViewData["Section"] = "Correspondence Management";

        var isSystemAdmin = _currentUser.Roles.Contains("SystemAdmin");
        var canReview = isSystemAdmin ||
            await _permissionService.HasPermissionAsync(_currentUser.Roles, "Correspondence", "Approve");

        var model = await _letterService.GetWorkspaceDashboardAsync(canReview, _currentUser.UserId, canReview);
        ViewBag.CanReview = canReview;
        ViewBag.CanRunReminders = isSystemAdmin && _reminders != null;
        return View(model);
    }

    // اجرای دستی یادآوری معوقات (فقط SystemAdmin): برای راستی‌آزمایی بدون انتظار تا ساعت زمان‌بندی؛ ضد تکرار روزانه سرویس همچنان برقرار است
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendReminders()
    {
        if (!_currentUser.Roles.Contains("SystemAdmin")) return Forbid();
        if (_reminders == null) return NotFound();

        var result = await _reminders.SendDueRemindersAsync();
        TempData["Message"] = $"Reminders sent: {result.ReferralReminders} referral, {result.ResponseReminders} response ({result.SkippedDuplicates} already sent today)";
        if (result.Failed > 0) TempData["Error"] = $"{result.Failed} reminder(s) could not be delivered.";
        return RedirectToAction(nameof(Dashboard));
    }

    // بیننده‌ی فعلی برای قاعده‌ی دسترسی نامه (LetterAccessRules): کاربر، کارمند مرتبط، و «دسترسی کامل» (SystemAdmin یا Approve)
    private async Task<LetterViewer> BuildViewerAsync()
    {
        var userId = _currentUser.UserId;
        int? employeeId = userId.HasValue
            ? await _context.Users.Where(u => u.Id == userId.Value).Select(u => u.EmployeeId).FirstOrDefaultAsync()
            : null;
        var canSeeAll = _currentUser.Roles.Contains("SystemAdmin")
            || await _permissionService.HasPermissionAsync(_currentUser.Roles, "Correspondence", "Approve");

        return new LetterViewer(userId, employeeId, canSeeAll, _currentUser.UserName);
    }

    // هر اقدام روی یک نامه‌ی مشخص (ستاره، گیرنده، برچسب، پیوست، ارجاع، امضا، پاسخ...) پیش از اجرا حق دیدن همان نامه را می‌سنجد؛
    // قبلاً فقط باز کردن صفحه کنترل نمی‌شد و ID حدس‌زده‌شده برای نامه‌ی محرمانه‌ی دیگران هم کار می‌کرد.
    // بخش کاربر جاری برای پیشوند شماره‌ی نامه ([DEPT]-[YEAR]-[SEQ])؛ بدون کارمند/بخش → null و شمارنده GEN را می‌گذارد
    private async Task<int?> GetCurrentDepartmentIdAsync()
    {
        var userId = _currentUser.UserId;
        if (!userId.HasValue) return null;
        var employeeId = await _context.Users.Where(u => u.Id == userId.Value).Select(u => u.EmployeeId).FirstOrDefaultAsync();
        if (!employeeId.HasValue) return null;
        return await _context.Employees.Where(e => e.EmployeeId == employeeId.Value).Select(e => (int?)e.DepartmentId).FirstOrDefaultAsync();
    }

    private async Task<IActionResult?> DenyIfNoAccessAsync(int letterId)
        => await _letterService.CanViewAsync(letterId, await BuildViewerAsync()) ? null : Forbid();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetResponseDue(int letterId, bool responseRequired, DateTime? responseDueDate)
    {
        var denied = await DenyIfNoAccessAsync(letterId);
        if (denied != null) return denied;

        var canManageAny = _currentUser.Roles.Contains("SystemAdmin") ||
            await _permissionService.HasPermissionAsync(_currentUser.Roles, "Correspondence", "Approve");

        var (success, error) = await _letterService.SetResponseDueAsync(letterId, responseRequired, responseDueDate, _currentUser.UserId, canManageAny);
        TempData[success ? "Message" : "Error"] = success ? "Response requirement updated" : error;
        return RedirectToAction(nameof(Details), new { id = letterId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleStar(int letterId, string? returnUrl)
    {
        var denied = await DenyIfNoAccessAsync(letterId);
        if (denied != null) return denied;

        if (_currentUser.UserId.HasValue)
            await _letterService.ToggleStarAsync(letterId, _currentUser.UserId.Value);

        return string.IsNullOrEmpty(returnUrl) ? RedirectToAction(nameof(Index)) : LocalRedirect(returnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddRecipient(int letterId, string recipientType, int? employeeId, int? departmentId, string? externalName)
    {
        var denied = await DenyIfNoAccessAsync(letterId);
        if (denied != null) return denied;

        await _letterService.AddRecipientAsync(letterId, recipientType, employeeId, departmentId, externalName);
        TempData["Message"] = "Recipient added";
        return RedirectToAction(nameof(Details), new { id = letterId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRecipientRead(int letterId, int letterRecipientId)
    {
        var denied = await DenyIfNoAccessAsync(letterId);
        if (denied != null) return denied;

        if (_currentUser.UserId.HasValue)
            await _letterService.MarkRecipientReadAsync(letterRecipientId, _currentUser.UserId.Value);

        return RedirectToAction(nameof(Details), new { id = letterId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AttachTag(int letterId, int? tagId, string? newTagName, string? newTagColor)
    {
        var denied = await DenyIfNoAccessAsync(letterId);
        if (denied != null) return denied;

        var idToAttach = tagId;

        if (!idToAttach.HasValue && !string.IsNullOrWhiteSpace(newTagName))
            idToAttach = await _tagService.CreateAsync(newTagName, newTagColor ?? "secondary", _currentUser.UserId);

        if (idToAttach.HasValue)
            await _tagService.AttachToLetterAsync(letterId, idToAttach.Value, _currentUser.UserId);

        return RedirectToAction(nameof(Details), new { id = letterId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DetachTag(int letterId, int tagId)
    {
        var denied = await DenyIfNoAccessAsync(letterId);
        if (denied != null) return denied;

        await _tagService.DetachFromLetterAsync(letterId, tagId);
        return RedirectToAction(nameof(Details), new { id = letterId });
    }

    public async Task<IActionResult> MyReferrals()
    {
        ViewData["Title"] = "My Referrals";
        ViewData["Section"] = "Correspondence Management";

        if (_currentUser.UserId == null) return Forbid();

        var letters = await _referralService.GetMyReferralsAsync(_currentUser.UserId.Value);

        return View(letters);
    }

    public async Task<IActionResult> Details(int id, bool embed = false, string? historyRole = null, string? historyOrder = null)
    {
        ViewData["Title"] = "Letter Details";
        ViewData["Section"] = "Correspondence Management";

        var viewer = await BuildViewerAsync();
        var access = await _letterService.GetDetailForViewerAsync(id, viewer);
        if (access.Outcome == LetterAccessOutcome.Forbidden) return Forbid();
        if (access.Outcome != LetterAccessOutcome.Ok) return NotFound();
        var detail = access.Detail!;

        // باز کردن مستقیم نامه (نه فقط از روی اعلان) هم اعلان‌های مرتبطِ همین کاربر را Read می‌کند
        if (_currentUser.UserId.HasValue)
            await _notifications.MarkRelatedReadAsync(_currentUser.UserId.Value, "Letter", id);

        // تاریخچه‌ی گردش: ترتیب (قدیم→جدید / جدید→قدیم) و فیلتر نقش (همه/من/مدیر/زیردستان) بر پایه‌ی چارت سازمانی
        var timeline = await _letterService.GetTimelineAsync(id, viewer);
        var newestFirst = string.Equals(historyOrder, "desc", StringComparison.OrdinalIgnoreCase);
        var role = LetterHistoryFilter.NormalizeRole(historyRole);
        ViewBag.Timeline = _cartable != null && _currentUser.UserId.HasValue
            ? await _cartable.FilterHistoryAsync(timeline, role, newestFirst, _currentUser.UserId.Value)
            : (newestFirst ? timeline.OrderByDescending(e => e.EventDate).ToList() : timeline);
        ViewBag.HistoryRole = role;
        ViewBag.HistoryOrder = newestFirst ? "desc" : "asc";
        ViewBag.Embed = embed;
        // طبق بند ۱۹: ParentLetterId/Thread از قبل در سرویس وجود داشت اما هرگز از Details
        // فراخوانی نمی‌شد - نمای مکالمه‌ای (Gmail-Inspired) دقیقاً به همین داده نیاز دارد
        ViewBag.Thread = await _letterService.GetThreadAsync(id);
        // گردش پیام‌ها با متن کامل (نامه‌ی اصلی، پاسخ‌ها و ارجاع‌ها)؛ فقط پیام‌هایی که این کاربر اجازه‌ی دیدنشان را دارد
        ViewBag.Conversation = _cartable != null
            ? await _cartable.GetConversationAsync(id, _currentUser.UserId, await CanViewConfidentialAsync())
            : null;

        // طبق بند ۷: فقط پرچم UI - محافظت واقعی در سرویس (CheckOriginalAuthorCanEdit) است، نه این‌جا
        ViewBag.CanEditReturned = _currentUser.UserId.HasValue
            && _currentUser.UserId.Value == detail.Letter.RegisteredById
            && detail.Letter.Status == "ReturnedForRevision";

        ViewBag.CanEditDraft = _currentUser.UserId.HasValue
            && _currentUser.UserId.Value == detail.Letter.RegisteredById
            && detail.Letter.Status == "Draft";

        ViewBag.CanReview = await _permissionService.HasPermissionAsync(_currentUser.Roles, "Correspondence", "Approve");
        // تاییدکننده‌ی مرحله‌ی جاریِ همین نامه (حتی بدون مجوز Approve ماژول) دکمه‌های تایید/برگشت/رد را می‌بیند
        ViewBag.IsCurrentApprover = detail.Letter.Status == "PendingApproval" && detail.ApprovalSteps.OrderBy(s => s.StepOrder).FirstOrDefault(s => s.Status == "Pending")?.ApproverUserId == _currentUser.UserId;

        // flags for the formal-workflow buttons (send / archive / reassign approver / history); services re-check every rule
        var isAuthor = _currentUser.UserId.HasValue && _currentUser.UserId.Value == detail.Letter.RegisteredById;
        var isAdmin = _currentUser.Roles.Contains("SystemAdmin");
        ViewBag.IsAuthor = isAuthor;
        ViewBag.IsAdmin = isAdmin;
        ViewBag.CanArchive = isAuthor || isAdmin || (bool)ViewBag.CanReview;
        ViewBag.CanAudit = isAuthor || isAdmin || (bool)ViewBag.CanReview;
        if (isAdmin && _composeService != null && detail.ApprovalSteps.Any(x => x.Status == "Pending"))
            ViewBag.ApproverOptions = await _composeService.GetApproverOptionsAsync();

        // پاسخ‌خواهی: ثبت‌کننده یا کسی با اختیار Approve/SystemAdmin (سرویس دوباره بررسی می‌کند)
        ViewBag.CanManageResponse = _currentUser.UserId.HasValue
            && (_currentUser.UserId.Value == detail.Letter.RegisteredById
                || _currentUser.Roles.Contains("SystemAdmin") || (bool)ViewBag.CanReview)
            && !LetterResponseRules.FinishedStatuses.Contains(detail.Letter.Status);

        // طبق بند ۲۶: صفحه‌ی جزئیات (عمدتاً فقط-خواندنی) نباید Dropdown های سنگین (همه‌ی
        // Employee/Document/MR) را بارگذاری کند - فقط زمانی که کاربر واقعاً دیالوگ افزودن
        // گیرنده/ارجاع/تگ را باز می‌کند، لازم است. این‌جا فقط موارد سبک (Department/Tag) از قبل
        // آماده می‌مانند و Employee با یک Endpoint سبک جداگانه (به‌محض باز شدن دیالوگ) پر می‌شود.
        await LoadLightDropdownsForDetailsAsync();

        return View(detail);
    }

    // پاسخ در همان فرم رسمی نوشتن نامه باز می‌شود (پیوست، تایید، محرمانگی، ذخیره‌ی خودکار)؛ دسترسی به نامه‌ی اصلی آنجا بررسی می‌شود
    [HttpGet]
    public IActionResult Reply(int id, int? templateId = null)
        => RedirectToAction("Compose", "LetterCompose", new { replyTo = id, templateId });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reply(int parentLetterId, string subject, string? body, string? receiver)
    {
        var denied = await DenyIfNoAccessAsync(parentLetterId);
        if (denied != null) return denied;

        var letterNumber = await _numberingService.GenerateNextNumberAsync("Outgoing", null, await GetCurrentDepartmentIdAsync());

        var id = await _letterService.ReplyAsync(parentLetterId, letterNumber, subject, body, receiver, _currentUser.UserId);

        TempData["Message"] = "Reply drafted";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Create(string letterType = "Incoming", int? templateId = null)
    {
        // Internal / Outgoing letters are written in the formal composer (sender block, To/Cc/Bcc, category, approval chain, auto-save)
        if (letterType is "Internal" or "Outgoing")
            return RedirectToAction("Compose", "LetterCompose", new { letterType, templateId });

        ViewData["Title"] = "New Letter";
        ViewData["Section"] = "Correspondence Management";

        ViewBag.LetterType = letterType;
        ViewBag.CanManualNumber = await _permissionService.HasPermissionAsync(_currentUser.Roles, "Correspondence", "ManualNumber");
        await LoadCreateDropdownsAsync();
        await LoadTemplatesAsync(letterType);

        // قالب فقط مقدار اولیه‌ی فرم را پر می‌کند؛ قالب غیرفعال یا نامناسب برای این نوع نادیده گرفته می‌شود
        if (templateId.HasValue && _templates != null)
        {
            var template = await _templates.GetAsync(templateId.Value);
            if (template != null && template.IsActive && (template.LetterType == null || template.LetterType == letterType))
            {
                ViewBag.Prefill = ApplyPlaceholders(template, PlaceholderValues(letterType, null));
                ViewBag.SelectedTemplateId = template.LetterTemplateId;
            }
        }

        return View();
    }

    // مقادیر نشانه‌های قالب. نشانه‌های پاسخ (Parent*) فقط وقتی نامه‌ی مادر هست مقدار می‌گیرند؛ در ایجاد نامه دست‌نخورده می‌مانند.
    private Dictionary<string, string> PlaceholderValues(string letterType, LetterDetailDto? parent)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Date"] = DateTime.Today.ToString("yyyy-MM-dd"),
            ["UserName"] = _currentUser.FullName ?? _currentUser.UserName ?? "",
            ["LetterType"] = letterType
        };

        if (parent != null)
        {
            var p = parent.Letter;
            values["ParentNumber"] = p.LetterNumber;
            values["ParentSubject"] = p.Subject;
            values["ParentDate"] = p.LetterDate.ToString("yyyy-MM-dd");
            values["Counterparty"] = (p.LetterType == "Incoming" ? p.Sender : p.Receiver) ?? "";
            values["ParentProject"] = p.ProjectName ?? "";
        }

        return values;
    }

    private static LetterTemplateDto ApplyPlaceholders(LetterTemplateDto template, IReadOnlyDictionary<string, string> values) => new()
    {
        LetterTemplateId = template.LetterTemplateId,
        Name = template.Name,
        LetterType = template.LetterType,
        Subject = LetterTemplatePlaceholders.Apply(template.Subject, values, htmlEncode: false),
        Body = LetterTemplatePlaceholders.Apply(template.Body, values, htmlEncode: true),
        Priority = template.Priority,
        ConfidentialityLevel = template.ConfidentialityLevel,
        IsActive = template.IsActive
    };

    private async Task LoadTemplatesAsync(string letterType)
    {
        ViewBag.Templates = _templates == null
            ? new List<LetterTemplateDto>()
            : await _templates.ListAsync(letterType, includeInactive: false);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string letterType, string? letterNumber, DateTime letterDate,
        string? sender, string? receiver, string subject, string? body, string priority,
        string confidentialityLevel, int? projectId, int? disciplineId, int? relatedDocumentId, int? relatedMRId,
        bool useManualNumber = false, bool responseRequired = false, DateTime? responseDueDate = null)
    {
        // اعتبارسنجی پاسخ‌خواهی پیش از هر تولید شماره؛ در خطا، فرم با همان مقادیر تایپ‌شده دوباره نمایش داده می‌شود (متن نامه از دست نمی‌رود)
        var responseError = LetterResponseRules.Validate(responseRequired, responseDueDate, letterDate);
        if (responseError != null)
        {
            ViewData["Title"] = "New Letter";
            ViewData["Section"] = "Correspondence Management";
            ModelState.AddModelError("responseDueDate", responseError);
            ViewBag.LetterType = letterType;
            ViewBag.CanManualNumber = await _permissionService.HasPermissionAsync(_currentUser.Roles, "Correspondence", "ManualNumber");
            await LoadCreateDropdownsAsync();
            await LoadTemplatesAsync(letterType);
            ViewBag.Prefill = new LetterTemplateDto { Subject = subject, Body = body, Priority = priority, ConfidentialityLevel = confidentialityLevel };
            ViewBag.PrefillSender = sender;
            ViewBag.PrefillReceiver = receiver;
            ViewBag.PrefillDate = letterDate;
            ViewBag.PrefillResponseRequired = responseRequired;
            ViewBag.PrefillResponseDue = responseDueDate;
            ViewBag.ResponseError = responseError;
            return View();
        }

        var canManualNumber = await _permissionService.HasPermissionAsync(_currentUser.Roles, "Correspondence", "ManualNumber");

        if (!useManualNumber || !canManualNumber || string.IsNullOrWhiteSpace(letterNumber))
            letterNumber = await _numberingService.GenerateNextNumberAsync(letterType, projectId, await GetCurrentDepartmentIdAsync());

        var id = await _letterService.CreateAsync(letterType, letterNumber, letterDate, sender, receiver,
            subject, body, priority, confidentialityLevel, projectId, disciplineId, relatedDocumentId,
            relatedMRId, _currentUser.UserId, responseRequired, responseDueDate);

        TempData["Message"] = "Letter registered successfully";
        return RedirectToAction(nameof(Details), new { id });
    }

    // آپلود تصویر داخل متن (طبق بررسی نوار ابزار CKEditor که فرستادید - تنها شکاف واقعی)
    // SCRUM-202: type, size and content are checked by the upload guard; the image is stored outside wwwroot and served by Files/Image.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [PEMS.Web.Security.Uploads.UploadPolicy(PEMS.Application.Uploads.UploadProfile.Image)]
    public async Task<IActionResult> UploadEditorImage(IFormFile file, [FromServices] PEMS.Application.Uploads.IProtectedImageStore images)
    {
        if (file == null || file.Length == 0) return BadRequest();

        await using var stream = file.OpenReadStream();
        var url = await images.SaveAsync(PEMS.Application.Uploads.ProtectedImageStore.LetterImages, file.FileName, stream);
        return Json(new { url });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [PEMS.Web.Security.Uploads.UploadPolicy(PEMS.Application.Uploads.UploadProfile.Attachment)]
    public async Task<IActionResult> UploadAttachment(int letterId, IFormFile file)
    {
        var denied = await DenyIfNoAccessAsync(letterId);
        if (denied != null) return denied;

        if (file != null && file.Length > 0)
        {
            string storageKey;
            await using (var uploadStream = file.OpenReadStream())
            {
                storageKey = await _fileStorage.SaveAsync("letters", file.FileName, uploadStream);
            }

            await _context.DocumentAttachments.AddAsync(new DocumentAttachment
            {
                LetterId = letterId,
                FileName = file.FileName,
                FileExtension = Path.GetExtension(file.FileName),
                FilePath = storageKey,
                FileSize = file.Length,
                MimeType = file.ContentType,
                UploadDate = DateTime.Now,
                IsActive = true,
                AIStatus = "NotApplicable"
            });

            await _context.SaveChangesAsync();
        }

        TempData["Message"] = "Attachment uploaded";
        return RedirectToAction(nameof(Details), new { id = letterId });
    }

    // ==============================================================================
    // طبق «PEMS Letter Details — Controlled Lifecycle» بند ۳۳: جایگزین AdvanceStatus قدیمی
    // (وضعیت آزاد بدون اعتبارسنجی) - هر اکشن زیر یک اقدام کسب‌وکاری صریح است؛ کاربر هرگز
    // وضعیت دلخواه انتخاب نمی‌کند و سرویس خودش گذار قانونی را اعمال/رد می‌کند.
    // ==============================================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitForReview(int letterId)
    {
        var (success, error) = await _letterService.SubmitForReviewAsync(letterId, _currentUser.UserId);
        TempData[success ? "Message" : "Error"] = success ? "Submitted for review" : error;
        return RedirectToAction(nameof(Details), new { id = letterId });
    }

    // تایید/برگشت/رد: دارنده‌ی مجوز Approve ماژول، مدیر سیستم، یا تاییدکننده‌ی تعیین‌شده برای «مرحله‌ی جاری» زنجیره‌ی تایید همین نامه.
    // تعیین‌شدن در زنجیره (توسط نویسنده، از فهرست کارکنان/مافوق‌ها) همان اختیار است؛ سرویس هم دوباره فقط مرحله‌ی جاری و نه نویسنده را می‌پذیرد.
    private async Task<bool> CanReviewLetterAsync(int letterId)
    {
        if (await CanViewConfidentialAsync()) return true;
        var userId = _currentUser.UserId;
        if (!userId.HasValue) return false;
        var current = await _context.LetterApprovalSteps.AsNoTracking()
            .Where(s => s.LetterId == letterId && s.Status == "Pending").OrderBy(s => s.StepOrder).Select(s => (int?)s.ApproverUserId).FirstOrDefaultAsync();
        return current == userId.Value;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int letterId, string? comment)
    {
        if (!await CanReviewLetterAsync(letterId)) return Forbid();
        var (success, error) = await _letterService.ApproveAsync(letterId, _currentUser.UserId, comment);
        TempData[success ? "Message" : "Error"] = success ? "Letter approved" : error;
        return RedirectToAction(nameof(Details), new { id = letterId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReturnForRevision(int letterId, string reason)
    {
        if (!await CanReviewLetterAsync(letterId)) return Forbid();
        var (success, error) = await _letterService.ReturnForRevisionAsync(letterId, _currentUser.UserId, reason);
        TempData[success ? "Message" : "Error"] = success ? "Letter returned for revision" : error;
        return RedirectToAction(nameof(Details), new { id = letterId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int letterId, string reason)
    {
        if (!await CanReviewLetterAsync(letterId)) return Forbid();
        var (success, error) = await _letterService.RejectAsync(letterId, _currentUser.UserId, reason);
        TempData[success ? "Message" : "Error"] = success ? "Letter rejected" : error;
        return RedirectToAction(nameof(Details), new { id = letterId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePermission("Correspondence", "Approve")]
    public async Task<IActionResult> Issue(int letterId)
    {
        var (success, error) = await _letterService.IssueAsync(letterId, _currentUser.UserId);
        TempData[success ? "Message" : "Error"] = success ? "Letter issued" : error;
        return RedirectToAction(nameof(Details), new { id = letterId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePermission("Correspondence", "Approve")]
    public async Task<IActionResult> Close(int letterId)
    {
        var (success, error) = await _letterService.CloseAsync(letterId, _currentUser.UserId);
        TempData[success ? "Message" : "Error"] = success ? "Letter closed" : error;
        return RedirectToAction(nameof(Details), new { id = letterId });
    }

    // طبق بند ۴/۷/۱۰: ذخیره‌ی پیش‌نویس - بدون تغییر Status (نه ارسال مجدد). طبق بند ۷ صریحاً:
    // این مجوز بر اساس Role نیست - فقط مالکیت (RegisteredById) + وضعیت (Draft/ReturnedForRevision)
    // که سرویس (CheckOriginalAuthorCanEdit) واقعاً اجرا می‌کند، نه یک [RequirePermission] نقشی
    // که می‌تواند نویسنده‌ی واقعی را (اگر نقشش سطح Edit ندارد) از ویرایش کار خودش محروم کند.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveDraft(int letterId, string subject, string body, byte[]? rowVersion)
    {
        if (!_currentUser.UserId.HasValue) return Forbid();

        var (success, error) = await _letterService.SaveDraftEditAsync(letterId, subject, body, _currentUser.UserId.Value, rowVersion);
        TempData[success ? "Message" : "Error"] = success ? "Draft saved" : error;
        return RedirectToAction(nameof(Details), new { id = letterId });
    }

    // طبق بند ۷/۹/۳۵: فقط نویسنده‌ی اصلی، فقط از ReturnedForRevision - محافظت واقعی در سرویس است
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Resubmit(int letterId, string subject, string body, byte[]? rowVersion)
    {
        if (!_currentUser.UserId.HasValue) return Forbid();

        var (success, error) = await _letterService.ResubmitAsync(letterId, subject, body, _currentUser.UserId.Value, rowVersion);
        TempData[success ? "Message" : "Error"] = success ? "Letter revised and resubmitted for review" : error;
        return RedirectToAction(nameof(Details), new { id = letterId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Refer(int letterId, int? referredToEmployeeId,
        int? referredToDepartmentId, string? instructions, DateTime? dueDate)
    {
        var denied = await DenyIfNoAccessAsync(letterId);
        if (denied != null) return denied;

        await _referralService.CreateAsync(letterId, referredToEmployeeId, referredToDepartmentId,
            instructions, dueDate, _currentUser.FullName);

        TempData["Message"] = "Letter referred";
        return RedirectToAction(nameof(Details), new { id = letterId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkReferralRead(int referralId, int letterId)
    {
        var denied = await DenyIfNoAccessAsync(letterId);
        if (denied != null) return denied;

        if (_currentUser.UserId != null)
            await _referralService.MarkReadAsync(referralId, _currentUser.UserId.Value);

        return RedirectToAction(nameof(Details), new { id = letterId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompleteReferral(int referralId, int letterId, string? responseNote)
    {
        var denied = await DenyIfNoAccessAsync(letterId);
        if (denied != null) return denied;

        await _referralService.CompleteAsync(referralId, responseNote);
        TempData["Message"] = "Referral marked as completed";
        return RedirectToAction(nameof(Details), new { id = letterId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Sign(int letterId, string signedByName)
    {
        var denied = await DenyIfNoAccessAsync(letterId);
        if (denied != null) return denied;

        await _letterService.SignAsync(letterId, signedByName);
        TempData["Message"] = "Letter signed";
        return RedirectToAction(nameof(Details), new { id = letterId });
    }

    public async Task<IActionResult> ExportPdf(int id)
    {
        var pdfAccess = await _letterService.GetDetailForViewerAsync(id, await BuildViewerAsync());
        if (pdfAccess.Outcome == LetterAccessOutcome.Forbidden) return Forbid();
        if (pdfAccess.Outcome != LetterAccessOutcome.Ok) return NotFound();
        var detail = pdfAccess.Detail!;

        var letter = detail.Letter;
        var letterhead = LetterheadSettings.From(_configuration);

        // Bcc is never printed (not even for the author); To/Cc come from the stored recipients, falling back to the free-text receiver
        string Names(string type) => string.Join("\u060C ", detail.Recipients.Where(r => r.RecipientType == type).Select(r => r.DisplayName));
        var toLine = Names("To");
        if (string.IsNullOrWhiteSpace(toLine)) toLine = letter.Receiver ?? "";
        var ccLine = Names("Cc");
        var fromLine = !string.IsNullOrWhiteSpace(letter.SenderName)
            ? letter.SenderName + (string.IsNullOrWhiteSpace(letter.SenderTitle) ? "" : " - " + letter.SenderTitle)
                + (string.IsNullOrWhiteSpace(letter.SenderDepartmentName) ? "" : " (" + letter.SenderDepartmentName + ")")
            : letter.Sender ?? "";

        var document = QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(11));

                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text(letterhead.CompanyName).FontSize(16).Bold();
                            if (!string.IsNullOrWhiteSpace(letterhead.Address)) c.Item().Text(letterhead.Address).FontSize(9);
                            var contact = string.Join("  ", new[] { letterhead.Phone, letterhead.Website }.Where(v => !string.IsNullOrWhiteSpace(v)));
                            if (contact.Length > 0) c.Item().Text(contact).FontSize(9);
                        });
                        row.ConstantItem(190).AlignRight().Column(c =>
                        {
                            c.Item().Text($"No: {letter.LetterNumber}").Bold();
                            c.Item().Text($"Date: {letter.LetterDate:yyyy-MM-dd}");
                            if (!string.IsNullOrWhiteSpace(letter.ReferenceNumber)) c.Item().Text($"Ref: {letter.ReferenceNumber}");
                            if (!string.IsNullOrWhiteSpace(letter.Category)) c.Item().Text($"Category: {letter.Category}");
                        });
                    });
                    col.Item().PaddingTop(6).LineHorizontal(1.5f);
                });

                page.Content().PaddingVertical(12).Column(col =>
                {
                    if (fromLine.Length > 0) col.Item().Text(t => { t.Span("From: ").Bold(); t.Span(fromLine); });
                    if (toLine.Length > 0) col.Item().Text(t => { t.Span("To: ").Bold(); t.Span(toLine); });
                    if (ccLine.Length > 0) col.Item().Text(t => { t.Span("Cc: ").Bold(); t.Span(ccLine); });
                    col.Item().PaddingVertical(8).Text($"Subject: {letter.Subject}").Bold().FontSize(12);
                    col.Item().PaddingTop(4).Text(_richTextSanitizer.ToPlainText(detail.Body));

                    col.Item().PaddingTop(28).Column(sign =>
                    {
                        if (!string.IsNullOrWhiteSpace(letter.SenderName)) sign.Item().Text(letter.SenderName).Bold();
                        if (!string.IsNullOrWhiteSpace(letter.SenderTitle)) sign.Item().Text(letter.SenderTitle);
                        if (!string.IsNullOrWhiteSpace(letter.SenderDepartmentName)) sign.Item().Text(letter.SenderDepartmentName);
                        if (!string.IsNullOrWhiteSpace(letter.SignedByName))
                            sign.Item().PaddingTop(4).Text($"Signed: {letter.SignedByName} {letter.SignedDate:yyyy-MM-dd}").FontSize(9);
                    });
                });

                page.Footer().Row(row =>
                {
                    row.RelativeItem().Text($"Confidentiality: {letter.ConfidentialityLevel}  |  Priority: {letter.Priority}").FontSize(8);
                    row.RelativeItem().AlignRight().Text(x =>
                    {
                        x.DefaultTextStyle(t => t.FontSize(8));
                        x.Span($"{DateTime.Now:yyyy-MM-dd HH:mm}  -  Page ");
                        x.CurrentPageNumber();
                        x.Span(" / ");
                        x.TotalPages();
                    });
                });
            });
        });

        var pdfBytes = document.GeneratePdf();

        return File(pdfBytes, "application/pdf", $"{detail.Letter.LetterNumber}.pdf");
    }


    // طبق بند ۲۴/۲۸: Index (فهرست کارتابل) فقط از Project/Discipline برای فیلتر و میانبر
    // دیسیپلین استفاده می‌کند - نه Employee/Document/MR/Tag که فقط صفحه‌ی Create به آن‌ها نیاز دارد
    private async Task LoadIndexDropdownsAsync()
    {
        ViewBag.Projects = await _context.Projects.Where(x => x.IsActive)
            .Select(x => new { x.ProjectId, x.ProjectName }).ToListAsync();

        ViewBag.Disciplines = await _cache.GetOrCreateAsync("dropdown:disciplines", async () =>
            await _context.Disciplines.Where(x => x.IsActive)
                .Select(x => new { x.DisciplineId, x.Name }).ToListAsync());
    }

    // طبق بند ۲۴/۲۸: Create.cshtml هرگز از Employee/Department/Tag استفاده نمی‌کند (فقط
    // Project/Discipline/RelatedDocument/RelatedMR در بخش «More Details») - نسخه‌ی قدیمی این
    // متد (LoadDropdownsAsync) این‌ها را هم بی‌فایده بارگذاری می‌کرد
    private async Task LoadCreateDropdownsAsync()
    {
        ViewBag.Projects = await _context.Projects.Where(x => x.IsActive)
            .Select(x => new { x.ProjectId, x.ProjectName }).ToListAsync();

        ViewBag.Disciplines = await _cache.GetOrCreateAsync("dropdown:disciplines", async () =>
            await _context.Disciplines.Where(x => x.IsActive)
                .Select(x => new { x.DisciplineId, x.Name }).ToListAsync());

        ViewBag.RelatedDocuments = await _context.Documents.Where(x => x.IsActive)
            .OrderByDescending(x => x.CreatedDate).Take(200)
            .Select(x => new { x.DocumentId, x.DocumentNo, x.Title }).ToListAsync();

        ViewBag.RelatedMRs = await _context.MaterialRequisitions
            .OrderByDescending(x => x.CreatedDate).Take(200)
            .Select(x => new { x.MRId, x.MRNumber }).ToListAsync();

        ViewBag.AllTags = await _tagService.GetAllAsync(_currentUser.UserId);
    }

    // طبق بند ۲۶: Details عمدتاً فقط-خواندنی است - نباید همه‌ی Employee/Document/MR را بارگذاری
    // کند فقط برای این‌که دیالوگ‌های افزودن گیرنده/ارجاع/تگ اصلاً باز نشده‌اند. فقط Department
    // (سبک) و Tag (سبک، از قبل Cache شده در سرویس خودش) از پیش آماده می‌مانند؛ Employees با
    // GetEmployeesForPicker (زیر) فقط وقتی دیالوگ واقعاً باز می‌شود بارگذاری می‌شود.
    private async Task LoadLightDropdownsForDetailsAsync()
    {
        ViewBag.Departments = await _context.Departments.Where(x => x.IsActive)
            .Select(x => new { x.DepartmentId, x.Name }).ToListAsync();

        ViewBag.AllTags = await _tagService.GetAllAsync(_currentUser.UserId);
    }

    [HttpGet]
    public async Task<IActionResult> GetEmployeesForPicker()
    {
        var employees = await _context.Employees.Where(x => x.IsActive)
            .OrderBy(x => x.FirstName).ThenBy(x => x.LastName)
            .Select(x => new { x.EmployeeId, Name = x.FirstName + " " + x.LastName })
            .ToListAsync();

        return Json(employees);
    }
}
