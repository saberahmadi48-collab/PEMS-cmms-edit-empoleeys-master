using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Web.Security;

namespace PEMS.Web.Controllers;

/// <summary>
/// نقطه‌ی ورود آپلود، طبق AI Engineering Intelligence Redesign: Upload دیگر هرگز مستقیم
/// EngineeringDocument نمی‌سازد. هر فایل وارد AI Intake Queue می‌شود، AI آن را طبقه‌بندی و
/// مسیریابی می‌کند (یا برای تایید انسانی در Intake Queue می‌ماند).
/// </summary>
public class DocumentUploadController : Controller
{
    private readonly IIntakeService _intakeService;
    private readonly PEMSDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DocumentUploadController> _logger;

    public DocumentUploadController(
        IIntakeService intakeService,
        PEMSDbContext context,
        ICurrentUserService currentUser,
        IServiceScopeFactory scopeFactory,
        ILogger<DocumentUploadController> logger)
    {
        _intakeService = intakeService;
        _context = context;
        _currentUser = currentUser;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        // Import Center parity: this action has no module permission, so enforce the same role gate that shows the Import Center card
        if (!PEMS.Web.Security.ImportCatalog.CanUse("DocumentUpload", User))
            return Forbid();

        ViewData["Title"] = "Document Upload";
        ViewData["Section"] = "AI Engineering Intelligence";

        ViewBag.Projects = await _context.Projects.Where(x => x.IsActive)
            .Select(x => new { x.ProjectId, x.ProjectName }).ToListAsync();

        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [PEMS.Web.Security.Uploads.UploadPolicy(PEMS.Application.Uploads.UploadProfile.Attachment)]
    public async Task<IActionResult> Upload(int projectId, List<IFormFile> files)
    {
        // Import Center parity: this action has no module permission, so enforce the same role gate that shows the Import Center card
        if (!PEMS.Web.Security.ImportCatalog.CanUse("DocumentUpload", User))
            return Forbid();

        if (files == null || !files.Any())
        {
            TempData["Error"] = "Please select at least one file.";
            return RedirectToAction(nameof(Index));
        }

        var attachmentIds = new List<int>();

        // طبق همان الگوی رفع‌شده در Import ها: UploadedById باید EmployeeId باشد - یک‌بار
        // بیرون از حلقه resolve می‌شود چون برای همه‌ی فایل‌های این درخواست یکی است
        var uploadedByEmployeeId = await GetCurrentEmployeeIdAsync();

        foreach (var file in files)
        {
            if (file.Length == 0) continue;

            using var stream = file.OpenReadStream();

            var attachmentId = await _intakeService.UploadToIntakeAsync(
                stream, file.FileName, file.Length, file.ContentType, projectId, uploadedByEmployeeId);

            attachmentIds.Add(attachmentId);
        }

        foreach (var attachmentId in attachmentIds)
        {
            Hangfire.BackgroundJob.Enqueue<IIntakeService>(x => x.ProcessIntakeAsync(attachmentId));
        }

        TempData["Message"] = $"{attachmentIds.Count} file(s) uploaded to the AI Intake Queue and are being classified.";
        return Redirect("/p/intake");
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
