using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Exceptions;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Web.Resources;
using PEMS.Web.Security;

namespace PEMS.Web.Controllers;

public class TransmittalController : Controller
{
    private readonly ITransmittalService _service;
    private readonly PEMSDbContext _context;
    private readonly IPermissionService _permissionService;
    private readonly ICurrentUserService _currentUser;
    private readonly IStringLocalizer<SharedResource>? _localizer;

    public TransmittalController(ITransmittalService service, PEMSDbContext context,
        IPermissionService permissionService, ICurrentUserService currentUser, IStringLocalizer<SharedResource>? localizer = null)
    {
        _service = service;
        _context = context;
        _permissionService = permissionService;
        _currentUser = currentUser;
        _localizer = localizer;
    }

    // SCRUM-540: messages follow the page language
    private string T(string key) => _localizer?[key].Value ?? key;

    // طبق «PEMS — Global Grid Hardening» P1: قبلاً کل جدول Transmittals دوبار (یک‌بار برای
    // فهرست، یک‌بار برای دیکشنری SupersededBy) بدون Skip/Take بارگذاری می‌شد. این Grid فیلتری
    // نداشت؛ فقط صفحه‌بندی واقعی اضافه شد.
    public async Task<IActionResult> Index(int page = 1, int? pageSize = null)
    {
        ViewData["Title"] = "Transmittal";
        ViewData["Section"] = "Engineering Management";
        ViewData["HelpKey"] = "Transmittals.Index";

        var normalizedPageSize = DataGridPageSize.Normalize(pageSize);
        var result = await _service.GetPagedAsync(page, normalizedPageSize);
        return View(result);
    }

    public async Task<IActionResult> Details(int id)
    {
        ViewData["Title"] = "Transmittal Details";
        ViewData["Section"] = "Engineering Management";

        var transmittal = await _service.GetByIdAsync(id);
        if (transmittal == null) return NotFound();

        ViewBag.CanIssue = await _permissionService.HasPermissionAsync(_currentUser.Roles, "Transmittal", "Approve");
        ViewBag.CanCancel = await _permissionService.HasPermissionAsync(_currentUser.Roles, "Transmittal", "Delete");
        ViewBag.CanReissue = await _permissionService.HasPermissionAsync(_currentUser.Roles, "Transmittal", "Create");
        ViewBag.CanRecordResponse = await _permissionService.HasPermissionAsync(_currentUser.Roles, "Transmittal", "Edit");

        return View(transmittal);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Transmittal", "Create"))
            return Forbid();

        ViewData["Title"] = "New Transmittal";
        ViewData["Section"] = "Engineering Management";

        await LoadCreateViewBagsAsync();

        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Display(Name = "Transmittal code")] string transmittalNo,
        int projectId, [Display(Name = "From")] string from, [Display(Name = "To")] string to,
        [Display(Name = "Purpose")] string purpose, string? description, List<int> documentIds,
        bool responseRequired, List<string>? recipientName, List<string>? recipientType,
        List<string>? recipientOrganization, List<bool>? recipientResponseRequired)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Transmittal", "Create"))
            return Forbid();

        var recipients = new List<TransmittalRecipientInput>();
        if (recipientName != null)
        {
            for (var i = 0; i < recipientName.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(recipientName[i])) continue;

                recipients.Add(new TransmittalRecipientInput
                {
                    Name = recipientName[i],
                    RecipientType = recipientType != null && i < recipientType.Count ? recipientType[i] : "External",
                    Organization = recipientOrganization != null && i < recipientOrganization.Count ? recipientOrganization[i] : null,
                    ResponseRequired = recipientResponseRequired != null && i < recipientResponseRequired.Count && recipientResponseRequired[i]
                });
            }
        }

        if ((documentIds == null || !documentIds.Any()) ||
            (recipients.Count == 0 && string.IsNullOrWhiteSpace(to)))
        {
            if (documentIds == null || !documentIds.Any())
                ModelState.AddModelError(string.Empty, T("Please select at least one document."));
            if (recipients.Count == 0 && string.IsNullOrWhiteSpace(to))
                ModelState.AddModelError(string.Empty, T("Please add at least one recipient."));

            await LoadCreateViewBagsAsync();
            ViewData["Title"] = "New Transmittal";
            ViewData["Section"] = "Engineering Management";
            return View();
        }

        try
        {
            var id = await _service.CreateAsync(transmittalNo, projectId, from, to, purpose, description,
                documentIds!, responseRequired, recipients);

            TempData["Message"] = T("Transmittal created successfully");
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (PemsException ex) when (ex.Code is "transmittal-code-required" or "transmittal-code-duplicate" or "transmittal-documents-held")
        {
            var key = ex.Code switch
            {
                "transmittal-code-required" => "Transmittal_CreateCodeRequired",
                "transmittal-code-duplicate" => "Transmittal_CreateCodeDuplicate",
                _ => "Transmittal_CreateDocumentsHeld"
            };
            ModelState.AddModelError(string.Empty, T(key));
            await LoadCreateViewBagsAsync();
            ViewData["Title"] = "New Transmittal";
            ViewData["Section"] = "Engineering Management";
            return View();
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkSent(int id, int responseDueWorkingDays = 10)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Transmittal", "Approve"))
            return Forbid();

        try
        {
            await _service.MarkSentAsync(id, responseDueWorkingDays);
            TempData["Message"] = "Transmittal marked as Sent";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, string reason)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Transmittal", "Delete"))
            return Forbid();

        try
        {
            await _service.CancelAsync(id, reason, await GetCurrentEmployeeIdAsync());
            TempData["Message"] = "Transmittal cancelled";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reissue(int id)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Transmittal", "Create"))
            return Forbid();

        try
        {
            var newId = await _service.ReissueAsync(id, await GetCurrentEmployeeIdAsync());
            TempData["Message"] = "Draft created from cancelled transmittal - update transmittal number and re-send.";
            return RedirectToAction(nameof(Details), new { id = newId });
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Details), new { id });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RecordResponse(int id, string? responseNote)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Transmittal", "Edit"))
            return Forbid();

        try
        {
            await _service.RecordResponseAsync(id, responseNote);
            TempData["Message"] = "Response recorded";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Acknowledge(int id, int recipientId, string? note)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Transmittal", "Edit"))
            return Forbid();

        try
        {
            await _service.AcknowledgeAsync(recipientId, note, await GetCurrentEmployeeIdAsync());
            TempData["Message"] = "Acknowledgement recorded";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task LoadCreateViewBagsAsync()
    {
        ViewBag.Projects = await _context.Projects.Where(x => x.IsActive)
            .Select(x => new { x.ProjectId, x.ProjectName }).ToListAsync();

        ViewBag.Documents = await _context.EngineeringDocuments
            .Include(x => x.Discipline)
            .Where(x => x.IsActive)
            .Select(x => new { x.DocumentId, x.DocumentNo, x.Title, x.RevisionNo, x.ProjectId, DisciplineName = x.Discipline != null ? x.Discipline.Name : "Unassigned", x.Status, x.IsOnHold })
            .ToListAsync();
    }

    private async Task<int?> GetCurrentEmployeeIdAsync()
    {
        if (!_currentUser.UserId.HasValue) return null;

        return await _context.Users
            .Where(u => u.Id == _currentUser.UserId.Value)
            .Select(u => u.EmployeeId)
            .FirstOrDefaultAsync();
    }
}
