using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Web.Resources;
using PEMS.Web.Security;

namespace PEMS.Web.Controllers;

/// <summary>
/// کارتابل شخصی نامه‌نگاری: برچسب‌ها (درخت)، تنظیمات، تفویض اختیار، قانون‌های مسیریابی، مخاطبین/گروه‌ها، یادآورها و گزارش/SLA.
/// فقط لایه‌ی HTTP: همه‌ی قواعد در سرویس‌های Application/Persistence است و همه‌ی داده‌ها شخصیِ کاربر جاری‌اند
/// (به‌جز موارد سازمانی که مجوز Approve لازم دارند). پاسخ‌های JSON متن خطا را به زبان صفحه برمی‌گردانند.
/// </summary>
public class LetterCartableController : Controller
{
    private readonly ILetterTagTreeService _tags;
    private readonly ILetterUserSettingsService _settings;
    private readonly ILetterDelegationService _delegations;
    private readonly ILetterRoutingRuleService _rules;
    private readonly ILetterContactService _contacts;
    private readonly ILetterPersonalReminderService _reminders;
    private readonly ILetterReportService _reports;
    private readonly ICurrentUserService _currentUser;
    private readonly IPermissionService _permissions;
    private readonly PEMSDbContext _context;
    private readonly IStringLocalizer<SharedResource> _localizer;

    public LetterCartableController(ILetterTagTreeService tags, ILetterUserSettingsService settings, ILetterDelegationService delegations,
        ILetterRoutingRuleService rules, ILetterContactService contacts, ILetterPersonalReminderService reminders, ILetterReportService reports,
        ICurrentUserService currentUser, IPermissionService permissions, PEMSDbContext context, IStringLocalizer<SharedResource> localizer)
    {
        _tags = tags;
        _settings = settings;
        _delegations = delegations;
        _rules = rules;
        _contacts = contacts;
        _reminders = reminders;
        _reports = reports;
        _currentUser = currentUser;
        _permissions = permissions;
        _context = context;
        _localizer = localizer;
    }

    // ------------------------------------------------------------------ helpers
    private int UserId => _currentUser.UserId ?? 0;
    private bool IsAdmin => _currentUser.Roles.Contains("SystemAdmin");

    private async Task<bool> CanApproveAsync() => IsAdmin || await _permissions.HasPermissionAsync(_currentUser.Roles, "Correspondence", "Approve");

    private string T(string? key) => string.IsNullOrEmpty(key) ? "" : (_localizer[key].ResourceNotFound ? key : _localizer[key].Value);

    private IActionResult Res(CartableOpResult r) => r.Success ? Json(new { ok = true, affected = r.Affected, id = r.Id }) : Json(new { ok = false, error = T(r.ErrorKey) });
    private IActionResult Res(TagTreeOpResult r) => r.Success ? Json(new { ok = true, id = r.TagId }) : Json(new { ok = false, error = T(r.ErrorKey) });

    // ------------------------------------------------------------------ تنظیمات نمایشی
    [HttpGet]
    public async Task<IActionResult> Settings() => Json(await _settings.GetAsync(UserId));

    public sealed record SettingsRequest(string? View, string? Density, List<string>? HiddenColumns, int FrozenColumns, bool OpenPreviewPane, bool ConfirmTrash, bool MarkReadOnOpen, int PageSize);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SettingsSave([FromBody] SettingsRequest r)
    {
        await _settings.SaveAsync(UserId, new LetterUserSettingsRules.Settings
        {
            View = r.View ?? "flat", Density = r.Density ?? "normal", HiddenColumns = r.HiddenColumns ?? new(), FrozenColumns = r.FrozenColumns,
            OpenPreviewPane = r.OpenPreviewPane, ConfirmTrash = r.ConfirmTrash, MarkReadOnOpen = r.MarkReadOnOpen, PageSize = r.PageSize
        });
        return Json(new { ok = true });
    }

    // ------------------------------------------------------------------ برچسب‌ها
    [HttpGet]
    public async Task<IActionResult> Tags()
        => Json(await _tags.GetTreeAsync(UserId, await CanApproveAsync(), withCounts: true, await CanApproveAsync()));

    public sealed record TagRequest(int? TagId, string? Name, string? Color, int? ParentTagId, bool Personal, int SortOrder);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> TagSave([FromBody] TagRequest r)
    {
        var org = await CanApproveAsync();
        var result = r.TagId.HasValue
            ? await _tags.UpdateAsync(r.TagId.Value, r.Name ?? "", r.Color, r.ParentTagId, r.SortOrder, UserId, org)
            : await _tags.CreateAsync(r.Name ?? "", r.Color, r.ParentTagId, r.Personal, UserId, org);
        return Res(result);
    }

    public sealed record IdRequest(int Id);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> TagDelete([FromBody] IdRequest r) => Res(await _tags.DeactivateAsync(r.Id, UserId, await CanApproveAsync()));

    // ------------------------------------------------------------------ تفویض اختیار
    [HttpGet]
    public async Task<IActionResult> Delegations()
    {
        var (given, received) = await _delegations.GetAsync(UserId);
        return Json(new { given, received, candidates = await _delegations.GetCandidatesAsync(UserId) });
    }

    public sealed record DelegationRequest(int DelegateUserId, DateTime FromDate, DateTime ToDate, string? Note);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DelegationCreate([FromBody] DelegationRequest r)
        => Res(await _delegations.CreateAsync(UserId, r.DelegateUserId, r.FromDate, r.ToDate, r.Note));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DelegationRevoke([FromBody] IdRequest r) => Res(await _delegations.RevokeAsync(r.Id, UserId));

    // ------------------------------------------------------------------ قانون‌های مسیریابی
    [HttpGet]
    public async Task<IActionResult> Rules()
    {
        var lookups = new
        {
            projects = await _context.Projects.Where(p => p.IsActive).OrderBy(p => p.ProjectName).Select(p => new { id = p.ProjectId, name = p.ProjectName }).Take(300).ToListAsync(),
            disciplines = await _context.Disciplines.Where(d => d.IsActive).OrderBy(d => d.Name).Select(d => new { id = d.DisciplineId, name = d.Name }).ToListAsync()
        };
        return Json(new { rules = await _rules.GetAsync(UserId), lookups });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RuleSave([FromBody] LetterRoutingRuleDto r) => Res(await _rules.SaveAsync(r, UserId));

    public sealed record ToggleRequest(int Id, bool Active);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RuleToggle([FromBody] ToggleRequest r) => Res(await _rules.SetActiveAsync(r.Id, r.Active, UserId));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RuleDelete([FromBody] IdRequest r) => Res(await _rules.DeleteAsync(r.Id, UserId));

    // ------------------------------------------------------------------ مخاطبین و گروه‌ها
    [HttpGet]
    public async Task<IActionResult> Contacts(string? q) => Json(await _contacts.SearchAsync(q));

    [HttpGet]
    public async Task<IActionResult> Groups() => Json(await _contacts.GetGroupsAsync(UserId, await CanApproveAsync()));

    public sealed record GroupRequest(int? GroupId, string? Name, bool Personal, List<int>? EmployeeIds, List<int>? PartyIds);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> GroupSave([FromBody] GroupRequest r)
        => Res(await _contacts.SaveGroupAsync(r.GroupId, r.Name ?? "", r.Personal, r.EmployeeIds ?? new(), r.PartyIds ?? new(), UserId, await CanApproveAsync()));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> GroupDelete([FromBody] IdRequest r) => Res(await _contacts.DeleteGroupAsync(r.Id, UserId, await CanApproveAsync()));

    // ------------------------------------------------------------------ یادآورها
    [HttpGet]
    public async Task<IActionResult> Reminders(bool includeDone = false) => Json(await _reminders.GetAsync(UserId, includeDone));

    public sealed record ReminderRequest(int LetterId, DateTime RemindAt, string? Note);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReminderAdd([FromBody] ReminderRequest r)
        => Res(await _reminders.AddAsync(r.LetterId, r.RemindAt, r.Note, UserId, await CanApproveAsync()));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReminderComplete([FromBody] IdRequest r) => Res(await _reminders.CompleteAsync(r.Id, UserId));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReminderDelete([FromBody] IdRequest r) => Res(await _reminders.DeleteAsync(r.Id, UserId));

    // ------------------------------------------------------------------ گزارش و SLA
    public async Task<IActionResult> Report(DateTime? from, DateTime? to)
    {
        ViewData["Title"] = "Ct_ReportTitle";
        ViewData["Section"] = "Correspondence Management";

        var end = (to ?? DateTime.Today).Date;
        var start = (from ?? end.AddDays(-89)).Date;
        if (end < start) (start, end) = (end, start);
        if ((end - start).TotalDays > 730) start = end.AddDays(-730);

        var model = await _reports.GetAsync(UserId, start, end, await CanApproveAsync());
        return View(model);
    }
}
