using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Web.Resources;
using PEMS.Web.Security;

namespace PEMS.Web.Controllers;

/// <summary>
/// فرم رسمی نوشتن نامه‌ی داخلی/صادره (Compose): پیش‌نویس و ذخیره‌ی خودکار (JSON)، دایرکتوری کارکنان، ورود دسته‌ای گیرندگان،
/// ارسال به تایید/ارسال، آرشیو، جایگزینی تاییدکننده، نسخه‌ها و گزارش انطباق. منطق کسب‌وکار در ILetterComposeService / ILetterService / ILetterComplianceService است.
/// </summary>
public class LetterComposeController : Controller
{
    private readonly ILetterComposeService _compose;
    private readonly ILetterService _letters;
    private readonly ILetterComplianceService _compliance;
    private readonly PEMSDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IPermissionService _permissions;
    private readonly IRichTextSanitizerService _sanitizer;
    private readonly IFileStorageService _fileStorage;
    private readonly IStringLocalizer<SharedResource> _localizer;
    private readonly IConfiguration _configuration;
    private readonly ILetterTemplateService? _templates;

    public LetterComposeController(ILetterComposeService compose, ILetterService letters, ILetterComplianceService compliance,
        PEMSDbContext context, ICurrentUserService currentUser, IPermissionService permissions, IRichTextSanitizerService sanitizer,
        IFileStorageService fileStorage, IStringLocalizer<SharedResource> localizer, IConfiguration configuration,
        ILetterTemplateService? templates = null)
    {
        _compose = compose;
        _letters = letters;
        _compliance = compliance;
        _context = context;
        _currentUser = currentUser;
        _permissions = permissions;
        _sanitizer = sanitizer;
        _fileStorage = fileStorage;
        _localizer = localizer;
        _configuration = configuration;
        _templates = templates;
    }

    // ------------------------------------------------------------------ helpers

    private bool IsAdmin => _currentUser.Roles.Contains("SystemAdmin");

    private async Task<bool> CanSeeAllAsync()
        => IsAdmin || await _permissions.HasPermissionAsync(_currentUser.Roles, "Correspondence", "Approve");

    private async Task<LetterComposeActor?> BuildActorAsync()
    {
        if (!_currentUser.UserId.HasValue) return null;
        var userId = _currentUser.UserId.Value;
        var employeeId = await _context.Users.Where(u => u.Id == userId).Select(u => u.EmployeeId).FirstOrDefaultAsync();
        var manual = await _permissions.HasPermissionAsync(_currentUser.Roles, "Correspondence", "ManualNumber");
        return new LetterComposeActor(userId, _currentUser.UserName, employeeId, manual, IsAdmin);
    }

    private async Task<LetterViewer> BuildViewerAsync()
    {
        var userId = _currentUser.UserId;
        int? employeeId = userId.HasValue
            ? await _context.Users.Where(u => u.Id == userId.Value).Select(u => u.EmployeeId).FirstOrDefaultAsync()
            : null;
        return new LetterViewer(userId, employeeId, await CanSeeAllAsync(), _currentUser.UserName);
    }

    // مشاهده‌ی نسخه‌ها/گزارش انطباق فقط برای ثبت‌کننده یا کسی که همه‌ی نامه‌ها را می‌بیند (حساس‌تر از خواندن خود نامه)
    private async Task<bool> CanAuditAsync(int letterId)
    {
        if (!_currentUser.UserId.HasValue) return false;
        if (await CanSeeAllAsync()) return true;
        var registeredBy = await _context.Letters.Where(l => l.LetterId == letterId).Select(l => (int?)l.RegisteredById).FirstOrDefaultAsync();
        return registeredBy == _currentUser.UserId.Value;
    }

    private string Translate(LetterComposeIssue issue, bool warning)
    {
        var key = (warning ? "Lc_Warn_" : "Lc_Err_") + issue.Code;
        var text = _localizer[key];
        var message = text.ResourceNotFound ? issue.Code : text.Value;
        var detail = issue.Detail ?? "";
        if (issue.Code == "ConfidentialityBelowParent")
        {
            var level = _localizer["Lt_C_" + detail];
            if (!level.ResourceNotFound) detail = level.Value;
        }
        return message.Replace("{0}", detail).Trim();
    }

    private object ToJson(LetterComposeResult r) => new
    {
        success = r.Success,
        letterId = r.LetterId,
        letterNumber = r.LetterNumber,
        rowVersion = r.RowVersion == null ? null : Convert.ToBase64String(r.RowVersion),
        versionNo = r.VersionNo,
        savedAt = r.SavedAt.ToString("HH:mm:ss"),
        errors = r.Errors.Select(e => new { code = e.Code, field = e.Field, message = Translate(e, false) }),
        warnings = r.Warnings.Select(w => new { code = w.Code, field = w.Field, message = Translate(w, true) })
    };

    // ------------------------------------------------------------------ compose page

    [HttpGet]
    public async Task<IActionResult> Compose(int? id, string letterType = "Internal", int? templateId = null, int? replyTo = null, int? referTo = null)
    {
        ViewData["Title"] = "Compose Letter";
        ViewData["Section"] = "Correspondence Management";

        var actor = await BuildActorAsync();
        if (actor == null) return Challenge();
        if (!LetterComposeRules.ComposableTypes.Contains(letterType)) letterType = "Internal";

        LetterComposeInput input;
        if (id.HasValue)
        {
            var loaded = await _compose.LoadForEditAsync(id.Value, actor);
            if (loaded == null) return NotFound();
            input = loaded;
        }
        else if (referTo.HasValue)
        {
            // ارجاع به شخص دیگر: همان قاعده‌ی دیدن نامه‌ی اصلی؛ خروجی یک نامه‌ی داخلی با موضوع ثابت است
            if (!await _letters.CanViewAsync(referTo.Value, await BuildViewerAsync())) return Forbid();
            var referral = await _compose.BuildReferralInputAsync(referTo.Value, actor);
            if (referral == null) return NotFound();
            input = referral;
            letterType = input.LetterType;
        }
        else if (replyTo.HasValue)
        {
            // پاسخ: همان قاعده‌ی دیدن نامه‌ی اصلی؛ نامه‌ای که کاربر نمی‌بیند، پاسخ هم نمی‌گیرد
            if (!await _letters.CanViewAsync(replyTo.Value, await BuildViewerAsync())) return Forbid();
            var reply = await _compose.BuildReplyInputAsync(replyTo.Value, actor);
            if (reply == null) return NotFound();
            input = reply;
            letterType = input.LetterType;
        }
        else
        {
            input = new LetterComposeInput { LetterType = letterType, ApprovalRequired = letterType == "Outgoing" };
        }

        var parentInfo = input.ParentLetterId.HasValue
            ? await _context.Letters.AsNoTracking().Where(l => l.LetterId == input.ParentLetterId.Value)
                .Select(l => new { l.LetterId, l.LetterNumber, l.Subject, l.ConfidentialityLevel, l.LetterDate, l.ProjectId, l.Sender, l.LetterType }).FirstOrDefaultAsync()
            : null;

        var sender = await _compose.GetSenderBlockAsync(actor.UserId);
        if (!id.HasValue) input.SenderDepartmentId ??= sender.DepartmentId;

        var recipientIds = LetterComposeRules.AllRecipientIds(input);
        var directory = await _compose.GetDirectoryItemsAsync(recipientIds);

        var templates = _templates == null ? new List<LetterTemplateDto>() : await _templates.ListAsync(input.LetterType, includeInactive: false);
        if (templateId.HasValue && !id.HasValue && _templates != null)
        {
            var template = await _templates.GetAsync(templateId.Value);
            if (template is { IsActive: true } && (template.LetterType == null || template.LetterType == input.LetterType))
            {
                var values = TemplateValues(input.LetterType, sender);
                if (parentInfo != null)
                {
                    values["ParentNumber"] = parentInfo.LetterNumber;
                    values["ParentSubject"] = parentInfo.Subject;
                    values["ParentDate"] = parentInfo.LetterDate.ToString("yyyy-MM-dd");
                    values["Counterparty"] = parentInfo.Sender ?? "";
                    values["ParentProject"] = await _context.Projects.Where(x => x.ProjectId == parentInfo.ProjectId).Select(x => x.ProjectName).FirstOrDefaultAsync() ?? "";
                }
                input.Subject = LetterTemplatePlaceholders.Apply(template.Subject, values, htmlEncode: false);
                input.Body = LetterTemplatePlaceholders.Apply(template.Body, values, htmlEncode: true);
                input.Priority = template.Priority;
                input.ConfidentialityLevel = template.ConfidentialityLevel;
                ViewBag.SelectedTemplateId = template.LetterTemplateId;
            }
        }

        var model = new LetterComposeViewModel
        {
            Input = input,
            Sender = sender,
            Recipients = directory,
            Approvers = await _compose.GetApproverOptionsAsync(actor.UserId),
            Templates = templates,
            CanManualNumber = actor.CanManualNumber,
            LetterNumber = id.HasValue ? await _context.Letters.Where(l => l.LetterId == id.Value).Select(l => l.LetterNumber).FirstOrDefaultAsync() : null,
            Status = id.HasValue ? await _context.Letters.Where(l => l.LetterId == id.Value).Select(l => l.Status).FirstOrDefaultAsync() : "Draft",
            Attachments = id.HasValue ? await ListAttachmentsAsync(id.Value) : new List<ComposeAttachmentItem>(),
            Projects = await _context.Projects.Where(x => x.IsActive).OrderBy(x => x.ProjectName).Select(x => new ComposeLookup(x.ProjectId, x.ProjectName)).ToListAsync(),
            Disciplines = await _context.Disciplines.Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new ComposeLookup(x.DisciplineId, x.Name)).ToListAsync(),
            Departments = await _context.Departments.Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new ComposeLookup(x.DepartmentId, x.Name)).ToListAsync(),
            Parties = input.LetterType == "Outgoing"
                ? await _context.CorrespondentParties.Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new ComposeLookup(x.CorrespondentPartyId, x.Name)).ToListAsync()
                : new List<ComposeLookup>(),
            ExistingErrors = TempData["ComposeError"] as string,
            ParentLetterId = parentInfo?.LetterId,
            ParentNumber = parentInfo?.LetterNumber,
            ParentSubject = parentInfo?.Subject,
            IsReferral = input.IsReferral,
            MinConfidentialityRank = parentInfo == null ? 0 : LetterComposeRules.ConfidentialityRank(parentInfo.ConfidentialityLevel),
            MinConfidentiality = parentInfo?.ConfidentialityLevel
        };

        return View(model);
    }

    private static Dictionary<string, string> TemplateValues(string letterType, LetterSenderBlockDto sender) => new(StringComparer.OrdinalIgnoreCase)
    {
        ["Date"] = DateTime.Today.ToString("yyyy-MM-dd"),
        ["UserName"] = sender.FullName,
        ["LetterType"] = letterType,
        ["SenderName"] = sender.FullName,
        ["SenderTitle"] = sender.Title ?? "",
        ["SenderDepartment"] = sender.DepartmentName ?? ""
    };

    private async Task<List<ComposeAttachmentItem>> ListAttachmentsAsync(int letterId)
        => await _context.DocumentAttachments.AsNoTracking().Where(a => a.LetterId == letterId && a.IsActive)
            .OrderBy(a => a.UploadDate).Select(a => new ComposeAttachmentItem(a.AttachmentId, a.FileName, a.FileSize)).ToListAsync();

    // ------------------------------------------------------------------ draft save / auto-save / submit / discard (JSON)

    public sealed class ComposeSaveRequest
    {
        public LetterComposeInput Input { get; set; } = new();
        public bool AutoSave { get; set; }
        // Base64 از RowVersion (JSON نمی‌تواند byte[] خام را از JS بگیرد)
        public string? RowVersionBase64 { get; set; }
    }

    private static void ApplyRowVersion(ComposeSaveRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.RowVersionBase64))
        {
            try { request.Input.RowVersion = Convert.FromBase64String(request.RowVersionBase64); }
            catch (FormatException) { request.Input.RowVersion = null; }
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromBody] ComposeSaveRequest request)
    {
        var actor = await BuildActorAsync();
        if (actor == null) return Unauthorized();
        ApplyRowVersion(request);

        var result = await _compose.SaveDraftAsync(request.Input, actor, request.AutoSave);
        return Json(ToJson(result));
    }

    // ارسال به تایید (با زنجیره) یا ارسال مستقیم؛ ابتدا اعتبارسنجی کامل روی آخرین نسخه‌ی ذخیره‌شده
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit([FromBody] ComposeSaveRequest request)
    {
        var actor = await BuildActorAsync();
        if (actor == null) return Unauthorized();
        ApplyRowVersion(request);

        var saved = await _compose.SaveDraftAsync(request.Input, actor, autoSave: false);
        if (!saved.Success) return Json(ToJson(saved));

        var check = await _compose.ValidateForSubmitAsync(saved.LetterId, actor);
        check.RowVersion = saved.RowVersion;
        check.LetterNumber = saved.LetterNumber;
        if (!check.Success) return Json(ToJson(check));

        var (ok, error) = request.Input.ApprovalRequired
            ? await _letters.SubmitForReviewAsync(saved.LetterId, actor.UserId)
            : await _letters.SendAsync(saved.LetterId, actor.UserId);

        if (!ok)
        {
            check.Success = false;
            check.Errors.Add(new LetterComposeIssue("SubmitFailed", null, error));
            return Json(ToJson(check));
        }

        TempData["Message"] = request.Input.ApprovalRequired ? "Submitted for review" : "Letter sent";
        return Json(new { success = true, letterId = saved.LetterId, redirect = Url.Action("Details", "Letter", new { id = saved.LetterId }) });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Discard(int letterId)
    {
        var actor = await BuildActorAsync();
        if (actor == null) return Challenge();
        var (ok, error) = await _compose.DiscardDraftAsync(letterId, actor);
        TempData[ok ? "Message" : "Error"] = ok ? "Draft discarded" : error;
        return ok ? RedirectToAction("Index", "Letter") : RedirectToAction("Details", "Letter", new { id = letterId });
    }

    // ------------------------------------------------------------------ lifecycle actions from Details

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Send(int letterId)
    {
        var (ok, error) = await _letters.SendAsync(letterId, _currentUser.UserId);
        TempData[ok ? "Message" : "Error"] = ok ? "Letter sent" : error;
        return RedirectToAction("Details", "Letter", new { id = letterId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Archive(int letterId)
    {
        var canArchiveAny = await CanSeeAllAsync();
        var (ok, error) = await _letters.ArchiveAsync(letterId, _currentUser.UserId, canArchiveAny);
        TempData[ok ? "Message" : "Error"] = ok ? "Letter archived" : error;
        return RedirectToAction("Details", "Letter", new { id = letterId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReassignApprover(int letterId, int stepOrder, int newApproverUserId)
    {
        var (ok, error) = await _letters.ReassignApprovalStepAsync(letterId, stepOrder, newApproverUserId, _currentUser.UserId, IsAdmin);
        TempData[ok ? "Message" : "Error"] = ok ? "Approver reassigned" : error;
        return RedirectToAction("Details", "Letter", new { id = letterId });
    }

    // ------------------------------------------------------------------ directory / bulk recipients

    [HttpGet]
    public async Task<IActionResult> Directory(string? q, int take = 20)
    {
        if (!_currentUser.UserId.HasValue) return Unauthorized();
        var items = await _compose.SearchDirectoryAsync(q, take);
        return Json(items.Select(i => new { id = i.EmployeeId, name = i.FullName, position = i.Position, department = i.DepartmentName, email = i.Email, isExternal = i.IsExternal }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResolveRecipients(IFormFile? file, string? text)
    {
        if (!_currentUser.UserId.HasValue) return Unauthorized();

        var content = text ?? "";
        if (file is { Length: > 0 })
        {
            if (file.Length > 1024 * 1024) return BadRequest(_localizer["Lc_Err_CsvTooLarge"].Value);
            using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            content = await reader.ReadToEndAsync();
        }

        var result = await _compose.ResolveRecipientsAsync(content, LetterComposeRules.MaxRecipients);
        return Json(new
        {
            matched = result.Matched.Select(i => new { id = i.EmployeeId, name = i.FullName, position = i.Position, department = i.DepartmentName, isExternal = i.IsExternal }),
            unmatched = result.Unmatched,
            truncated = result.Truncated,
            max = LetterComposeRules.MaxRecipients
        });
    }

    // ------------------------------------------------------------------ attachments (draft only)

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadAttachment(int letterId, IFormFile file)
    {
        var actor = await BuildActorAsync();
        if (actor == null) return Unauthorized();
        if (file == null) return BadRequest(new { message = _localizer["Lc_Err_AttachmentEmpty"].Value });

        var editable = await _compose.LoadForEditAsync(letterId, actor);
        if (editable == null) return Forbid();

        var problem = LetterComposeRules.ValidateAttachment(file.FileName, file.Length);
        if (problem != null) return BadRequest(new { message = _localizer["Lc_Err_" + problem].Value });

        string storageKey;
        await using (var stream = file.OpenReadStream())
            storageKey = await _fileStorage.SaveAsync("letters", file.FileName, stream);

        var attachment = new DocumentAttachment
        {
            LetterId = letterId, FileName = Path.GetFileName(file.FileName), FileExtension = Path.GetExtension(file.FileName),
            FilePath = storageKey, FileSize = file.Length, MimeType = file.ContentType, UploadDate = DateTime.Now, IsActive = true, AIStatus = "NotApplicable"
        };
        await _context.DocumentAttachments.AddAsync(attachment);
        await _context.SaveChangesAsync();

        return Json(new { id = attachment.AttachmentId, name = attachment.FileName, size = attachment.FileSize });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveAttachment(int letterId, int attachmentId)
    {
        var actor = await BuildActorAsync();
        if (actor == null) return Unauthorized();
        if (await _compose.LoadForEditAsync(letterId, actor) == null) return Forbid();

        var attachment = await _context.DocumentAttachments.FirstOrDefaultAsync(a => a.AttachmentId == attachmentId && a.LetterId == letterId && a.IsActive);
        if (attachment == null) return NotFound();
        attachment.IsActive = false;   // نرم؛ فایل ذخیره‌شده دست‌نخورده می‌ماند
        await _context.SaveChangesAsync();
        return Json(new { removed = true });
    }

    // ------------------------------------------------------------------ preview (letterhead) – renders unsaved form data

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Preview([FromBody] LetterComposeInput input)
    {
        var actor = await BuildActorAsync();
        if (actor == null) return Unauthorized();

        var sender = await _compose.GetSenderBlockAsync(actor.UserId);
        var model = new LetterPrintModel
        {
            Letterhead = LetterheadSettings.From(_configuration),
            LetterNumber = string.IsNullOrWhiteSpace(input.ManualNumber) ? null : input.ManualNumber,
            LetterDate = input.LetterDate,
            Category = input.Category,
            Priority = input.Priority,
            Confidentiality = input.ConfidentialityLevel,
            ReferenceNumber = input.ReferenceNumber,
            Subject = input.Subject,
            BodyHtml = _sanitizer.Sanitize(input.Body),
            Sender = sender,
            To = (await _compose.GetDirectoryItemsAsync(input.ToEmployeeIds)).Select(i => i.FullName + (string.IsNullOrWhiteSpace(i.Position) ? "" : " — " + i.Position)).ToList(),
            Cc = (await _compose.GetDirectoryItemsAsync(input.CcEmployeeIds)).Select(i => i.FullName).ToList(),
            ExternalReceiver = input.ExternalReceiver,
            ResponseDueDate = input.ResponseRequired ? input.ResponseDueDate : null,
            IsPreview = true
        };
        return PartialView("_LetterPrint", model);
    }

    // ------------------------------------------------------------------ versions / who viewed / compliance

    [HttpGet]
    public async Task<IActionResult> History(int id)
    {
        ViewData["Title"] = "Letter History";
        ViewData["Section"] = "Correspondence Management";

        if (!await _letters.CanViewAsync(id, await BuildViewerAsync())) return Forbid();
        if (!await CanAuditAsync(id)) return Forbid();

        var report = await _compliance.BuildComplianceReportAsync(id);
        if (report == null) return NotFound();

        return View(new LetterHistoryViewModel
        {
            Report = report,
            Versions = await _compose.GetVersionsAsync(id),
            Views = await _compliance.GetViewLogAsync(id)
        });
    }

    [HttpGet]
    public async Task<IActionResult> ComplianceExport(int id)
    {
        if (!await _letters.CanViewAsync(id, await BuildViewerAsync())) return Forbid();
        if (!await CanAuditAsync(id)) return Forbid();

        var report = await _compliance.BuildComplianceReportAsync(id);
        if (report == null) return NotFound();

        var sb = new StringBuilder();
        sb.AppendLine("Timestamp,Actor,Category,Event,Detail");
        foreach (var row in report.Rows)
            sb.AppendLine(string.Join(",", Csv(row.Timestamp.ToString("yyyy-MM-dd HH:mm:ss")), Csv(row.Actor), Csv(row.Category), Csv(row.Event), Csv(row.Detail)));

        // BOM تا Excel فارسی را درست بخواند
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv", $"{report.LetterNumber}-compliance.csv");
    }

    // جلوگیری از CSV-injection: سلول‌هایی که با = + - @ شروع می‌شوند در Excel فرمول اجرا می‌کنند
    private static string Csv(string? value)
    {
        var text = value ?? "";
        if (text.Length > 0 && "=+-@\t\r".Contains(text[0])) text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}

public record ComposeLookup(int Id, string Name);
public record ComposeAttachmentItem(int Id, string Name, long Size);

public class LetterComposeViewModel
{
    public LetterComposeInput Input { get; set; } = new();
    public LetterSenderBlockDto Sender { get; set; } = new();
    public List<EmployeeDirectoryItem> Recipients { get; set; } = new();
    public List<ApproverOptionDto> Approvers { get; set; } = new();
    public List<LetterTemplateDto> Templates { get; set; } = new();
    public List<ComposeAttachmentItem> Attachments { get; set; } = new();
    public List<ComposeLookup> Projects { get; set; } = new();
    public List<ComposeLookup> Disciplines { get; set; } = new();
    public List<ComposeLookup> Departments { get; set; } = new();
    public List<ComposeLookup> Parties { get; set; } = new();
    public bool CanManualNumber { get; set; }
    public string? LetterNumber { get; set; }
    public string Status { get; set; } = "Draft";
    public string? ExistingErrors { get; set; }

    // پاسخ: نامه‌ی اصلی و کمترین سطح محرمانگی مجاز (پایین‌تر از آن انتخاب‌شدنی نیست)
    public int? ParentLetterId { get; set; }
    public string? ParentNumber { get; set; }
    public string? ParentSubject { get; set; }
    // ارجاع: موضوع ثابت، بدون تایید/دسته‌بندی، فقط برچسب «ارجاع»
    public bool IsReferral { get; set; }
    public int MinConfidentialityRank { get; set; }
    public string? MinConfidentiality { get; set; }
}

public class LetterHistoryViewModel
{
    public LetterComplianceReportDto Report { get; set; } = new();
    public List<LetterVersionDto> Versions { get; set; } = new();
    public List<LetterViewLogEntryDto> Views { get; set; } = new();
}

/// <summary>سربرگ شرکت برای پیش‌نمایش/PDF؛ از بخش "Letterhead" در appsettings خوانده می‌شود (پیش‌فرض: PEMS).</summary>
public class LetterheadSettings
{
    public string CompanyName { get; set; } = "PEMS";
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Website { get; set; }

    public static LetterheadSettings From(IConfiguration configuration)
    {
        var s = new LetterheadSettings();
        var section = configuration.GetSection("Letterhead");
        if (!string.IsNullOrWhiteSpace(section["CompanyName"])) s.CompanyName = section["CompanyName"]!;
        s.Address = section["Address"];
        s.Phone = section["Phone"];
        s.Website = section["Website"];
        return s;
    }
}

public class LetterPrintModel
{
    public LetterheadSettings Letterhead { get; set; } = new();
    public string? LetterNumber { get; set; }
    public DateTime LetterDate { get; set; } = DateTime.Today;
    public string? Category { get; set; }
    public string? Priority { get; set; }
    public string? Confidentiality { get; set; }
    public string? ReferenceNumber { get; set; }
    public string Subject { get; set; } = "";
    public string? BodyHtml { get; set; }
    public LetterSenderBlockDto Sender { get; set; } = new();
    public List<string> To { get; set; } = new();
    public List<string> Cc { get; set; } = new();
    public string? ExternalReceiver { get; set; }
    public DateTime? ResponseDueDate { get; set; }
    public bool IsPreview { get; set; }
}
