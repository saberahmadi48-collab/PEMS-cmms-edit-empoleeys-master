using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Web.Models;
using PEMS.Web.Resources;
using PEMS.Web.Security;

namespace PEMS.Web.Controllers;

// ==============================================================================
// بخش DCC (Document Control Center) - کاملاً جدا از ماژول عمومی Correspondence، طبق
// درخواست صریح: "این بخش باید کاملاً جدا ساخته بشه". این کنترلر جای عملیاتی‌شدن طراحی
// سازمانی DCC (که قبلاً بدون کد ارائه شد) است:
//   - داشبورد: کارهای در انتظار DCC (مدارک آماده‌ی صدور، مکاتبات وارده‌ی وصل‌نشده)
//   - ثبت نامه‌ی وارده: صفحه‌ی دو-پنلی (فرم + پیش‌نمایش/رشته‌ی مکاتبه‌ی طرف)؛ تمام منطق در IIncomingCorrespondenceService است
//     و این کنترلر فقط ورودی را به DTO تبدیل و کدهای خطا را ترجمه می‌کند.
// ==============================================================================
public class DCCController : Controller
{
    private readonly PEMSDbContext _context;
    private readonly IIncomingCorrespondenceService _incoming;
    private readonly ICurrentUserService _currentUser;
    private readonly IPermissionService _permissions;
    private readonly IStringLocalizer<SharedResource> _localizer;

    public DCCController(
        PEMSDbContext context,
        IIncomingCorrespondenceService incoming,
        ICurrentUserService currentUser,
        IPermissionService permissions,
        IStringLocalizer<SharedResource> localizer)
    {
        _context = context;
        _incoming = incoming;
        _currentUser = currentUser;
        _permissions = permissions;
        _localizer = localizer;
    }


    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "Document Control Center (DCC)";
        ViewData["Section"] = "DCC";

        // مدارک آماده‌ی صدور (Approved، منتظر اقدام DCC)
        ViewBag.PendingIssueCount = await _context.EngineeringDocuments
            .CountAsync(x => x.IsActive && x.Status == "Approved");

        var pendingIssueDocs = await _context.EngineeringDocuments
            .Where(x => x.IsActive && x.Status == "Approved")
            .OrderByDescending(x => x.ModifiedDate)
            .Take(10)
            .Select(x => new { x.DocumentId, x.DocumentNo, x.Title, x.ModifiedDate })
            .ToListAsync();
        ViewBag.PendingIssueDocs = pendingIssueDocs;

        // مکاتبات وارده‌ای که هنوز به هیچ مدرک/MR/ترنسمیتال وصل نشده‌اند - نیاز به بررسی DCC دارند
        var unlinked = _context.Letters
            .Where(l => l.IsActive && l.LetterType == "Incoming"
                        && l.RelatedDocumentId == null && l.RelatedMRId == null && !l.Links.Any());

        // شمارش واقعی (قبلاً شمارنده همان ۱۵ ردیف نمایش‌داده‌شده بود)
        ViewBag.UnlinkedIncomingCount = await unlinked.CountAsync();
        ViewBag.UnlinkedIncoming = await unlinked
            .OrderByDescending(l => l.LetterDate)
            .Take(15)
            .Select(l => new { l.LetterId, l.LetterNumber, l.Sender, l.Subject, l.LetterDate })
            .ToListAsync();

        // آخرین مکاتبات وارده (لینک‌شده یا نشده) برای دید کلی
        ViewBag.RecentIncoming = await _context.Letters
            .Where(l => l.IsActive && l.LetterType == "Incoming")
            .OrderByDescending(l => l.LetterDate)
            .Take(10)
            .Select(l => new { l.LetterId, l.LetterNumber, l.Sender, l.Subject, l.LetterDate, l.RelatedDocumentId, l.RelatedMRId })
            .ToListAsync();

        return View();
    }


    // ------------------------------------------------------------------ صفحه‌ی ثبت

    [HttpGet]
    [RequirePermission("Correspondence", "Create")]
    public async Task<IActionResult> RegisterIncoming(int? partyId = null, int? replyTo = null)
    {
        ViewData["Title"] = _localizer["Dcc_PageTitle"].Value;
        ViewData["Section"] = "DCC";

        ViewBag.Projects = await _context.Projects.AsNoTracking().Where(x => x.IsActive)
            .OrderBy(x => x.ProjectName).Select(x => new { x.ProjectId, x.ProjectName, x.ProjectCode }).ToListAsync();
        ViewBag.Disciplines = await _context.Disciplines.AsNoTracking().Where(x => x.IsActive)
            .OrderBy(x => x.Name).Select(x => new { x.DisciplineId, x.Name }).ToListAsync();

        // «در پاسخ به» از صفحه‌ی نامه‌ی صادره: طرف و نامه از قبل انتخاب می‌شوند (فقط اگر نامه‌ی صادره‌ی قابل‌مشاهده‌ی همان طرف باشد)
        if (replyTo.HasValue)
        {
            var parent = await _context.Letters.AsNoTracking()
                .Where(l => l.LetterId == replyTo.Value && l.IsActive && l.LetterType == "Outgoing")
                .Select(l => new { l.LetterId, l.LetterNumber, l.Subject, l.CorrespondentPartyId, l.ProjectId })
                .FirstOrDefaultAsync();
            if (parent != null)
            {
                ViewBag.ReplyTo = parent;
                partyId ??= parent.CorrespondentPartyId;
            }
        }

        if (partyId.HasValue)
        {
            var party = (await _incoming.SearchPartiesAsync(null, null, 200)).FirstOrDefault(p => p.Id == partyId.Value);
            ViewBag.PresetParty = party;
        }

        ViewBag.CurrentUserName = _currentUser.UserName;
        return View();
    }


    // ------------------------------------------------------------------ ثبت (JSON)

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePermission("Correspondence", "Create")]
    [RequestSizeLimit(110_000_000)]
    public async Task<IActionResult> RegisterIncoming(IncomingRegisterRequest request)
    {
        var errors = new List<LetterComposeIssue>();
        var input = Map(request, errors);

        var streams = new List<Stream>();
        try
        {
            var files = request.Files ?? new List<IFormFile>();
            var roles = request.FileRoles ?? new List<string>();
            for (var i = 0; i < files.Count; i++)
            {
                var stream = files[i].OpenReadStream();
                streams.Add(stream);
                input.Files.Add(new IncomingFileInput
                {
                    FileName = Path.GetFileName(files[i].FileName),
                    ContentType = files[i].ContentType,
                    Length = files[i].Length,
                    Role = i < roles.Count ? roles[i] : "Attachment",
                    Content = stream
                });
            }

            var actorEmployeeId = await GetCurrentEmployeeIdAsync();
            var result = errors.Count > 0
                ? new IncomingRegistrationResult { Issues = errors }
                : await _incoming.RegisterAsync(input, _currentUser.UserId, actorEmployeeId);

            if (result.Success)
            {
                var url = Url.Action("Details", "Letter", new { id = result.LetterId });
                return Json(new { success = true, letterId = result.LetterId, letterNumber = result.LetterNumber, detailsUrl = url });
            }

            object? duplicate = result.DuplicateLetterId.HasValue
                ? new { letterId = result.DuplicateLetterId, letterNumber = result.DuplicateLetterNumber, url = Url.Action("Details", "Letter", new { id = result.DuplicateLetterId }) }
                : null;

            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return Json(new
            {
                success = false,
                errors = result.Issues.Select(i => new { field = i.Field, code = i.Code, message = Translate(i) }),
                duplicate
            });
        }
        finally
        {
            foreach (var s in streams) await s.DisposeAsync();
        }
    }


    // ------------------------------------------------------------------ جستجوها (JSON)

    [HttpGet]
    [RequirePermission("Correspondence", "Create")]
    public async Task<IActionResult> Parties(string? q, int? projectId)
        => Json(await _incoming.SearchPartiesAsync(q, projectId));

    [HttpGet]
    [RequirePermission("Correspondence", "Create")]
    public async Task<IActionResult> PartyThread(int partyId)
    {
        var thread = await _incoming.GetPartyThreadAsync(partyId, await CanViewConfidentialAsync(), _currentUser.UserId);
        return thread == null ? NotFound() : Json(thread);
    }

    [HttpGet]
    [RequirePermission("Correspondence", "Create")]
    public async Task<IActionResult> OutgoingForReply(int partyId)
        => Json(await _incoming.GetOutgoingForReplyAsync(partyId, await CanViewConfidentialAsync(), _currentUser.UserId));

    [HttpGet]
    [RequirePermission("Correspondence", "Create")]
    public async Task<IActionResult> Links(string type, string? q, int? projectId)
        => Json(await _incoming.SearchLinksAsync(type, q, projectId));

    [HttpGet]
    [RequirePermission("Correspondence", "Create")]
    public async Task<IActionResult> People(string? q)
        => Json(await _incoming.SearchPeopleAsync(q));

    [HttpGet]
    [RequirePermission("Correspondence", "Create")]
    public async Task<IActionResult> NextNumber(int? projectId, string? receivedDate)
    {
        var received = TryParseIsoDate(receivedDate) ?? DateTime.Today;
        return Json(new { number = await _incoming.PeekNextNumberAsync(projectId, received) });
    }

    [HttpGet]
    [RequirePermission("Correspondence", "Create")]
    public async Task<IActionResult> CheckDuplicate(int partyId, string? reference)
    {
        var hit = await _incoming.FindDuplicateAsync(partyId, reference ?? "");
        return Json(hit == null
            ? null
            : new { letterId = hit.Value.LetterId, letterNumber = hit.Value.LetterNumber, url = Url.Action("Details", "Letter", new { id = hit.Value.LetterId }) });
    }


    // ------------------------------------------------------------------ کمکی

    private IncomingRegistrationInput Map(IncomingRegisterRequest r, List<LetterComposeIssue> errors)
    {
        DateTime? Date(string? text, string field)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var parsed = TryParseIsoDate(text);
            if (parsed == null) errors.Add(new LetterComposeIssue("DateInvalid", field));
            return parsed;
        }

        var links = new List<IncomingLinkInput>();
        if (!string.IsNullOrWhiteSpace(r.Links))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<LinkJson>>(r.Links, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                links = (parsed ?? new()).Select(l => new IncomingLinkInput(l.LinkType ?? "", l.TargetId)).ToList();
            }
            catch (JsonException)
            {
                errors.Add(new LetterComposeIssue("LinkInvalid", "links"));
            }
        }

        return new IncomingRegistrationInput
        {
            ProjectId = r.ProjectId,
            ContractId = r.ContractId,
            CorrespondentPartyId = r.CorrespondentPartyId,
            NewPartyName = r.NewPartyName,
            NewPartyType = r.NewPartyType,
            SenderReference = r.SenderReference,
            DocumentDate = Date(r.DocumentDate, "documentDate"),
            ReceivedDate = Date(r.ReceivedDate, "receivedDate"),
            ReceiptChannel = string.IsNullOrWhiteSpace(r.ReceiptChannel) ? "OfficialLetter" : r.ReceiptChannel,
            Subject = r.Subject ?? "",
            Summary = r.Summary,
            Priority = string.IsNullOrWhiteSpace(r.Priority) ? "Normal" : r.Priority,
            Category = string.IsNullOrWhiteSpace(r.Category) ? null : r.Category,
            ConfidentialityLevel = string.IsNullOrWhiteSpace(r.ConfidentialityLevel) ? "Internal" : r.ConfidentialityLevel,
            DisciplineId = r.DisciplineId,
            ParentLetterId = r.ParentLetterId,
            ResponseRequired = r.ResponseRequired,
            ResponseDueDate = Date(r.ResponseDueDate, "responseDueDate"),
            OwnerEmployeeId = r.OwnerEmployeeId,
            CcEmployeeIds = r.CcEmployeeIds ?? new List<int>(),
            Instruction = r.Instruction,
            Links = links,
            ConfirmDuplicate = r.ConfirmDuplicate
        };
    }

    private sealed class LinkJson
    {
        public string? LinkType { get; set; }
        public int TargetId { get; set; }
    }

    private static DateTime? TryParseIsoDate(string? text)
        => DateTime.TryParseExact(text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private string Translate(LetterComposeIssue issue)
    {
        var localized = _localizer["Dcc_Err_" + issue.Code];
        var message = localized.ResourceNotFound ? issue.Code : localized.Value;
        return message.Replace("{0}", issue.Detail ?? "").Trim();
    }

    private async Task<bool> CanViewConfidentialAsync()
        => _currentUser.Roles.Contains("SystemAdmin")
           || await _permissions.HasPermissionAsync(_currentUser.Roles, "Correspondence", "Approve");

    private async Task<int?> GetCurrentEmployeeIdAsync()
    {
        if (!_currentUser.UserId.HasValue) return null;

        return await _context.Users
            .Where(u => u.Id == _currentUser.UserId.Value)
            .Select(u => u.EmployeeId)
            .FirstOrDefaultAsync();
    }
}
