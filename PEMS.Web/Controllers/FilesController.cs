using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Web.Security;

namespace PEMS.Web.Controllers;

/// <summary>
/// طبق ممیزی امنیتی (P0 - Engineering File Security Containment): تنها مسیر مجاز برای دریافت
/// بایت‌های واقعی هر پیوست کسب‌وکاری در کل برنامه. جایگزین ~۵ View که قبلاً مستقیم
/// attachment.FilePath را به‌عنوان یک URL عمومی رندر می‌کردند (که با UseStaticFiles بدون هیچ
/// احراز هویتی در دسترس بود) و جایگزین EngineeringDocumentsController.DownloadAttachment قدیمی
/// (که فقط IsActive چک می‌کرد، هیچ محدودیت Scope/دیسیپلینی نداشت).
///
/// زنجیره‌ی اجباری طبق دستورالعمل ممیزی:
/// Authentication (فیلتر سراسری RequireAuthenticatedUser در Program.cs)
///   → Authorization/Scope (این‌جا، بر اساس نوع والد پیوست)
///   → Parent Entity Access Check
///   → Attachment Lookup
///   → Storage Resolution (IFileStorageService - هرگز مسیر فیزیکی مستقیم)
///   → Secure Streaming (بدون افشای مسیر فیزیکی، بدون URL دائمی عمومی)
/// </summary>
public class FilesController : Controller
{
    private readonly PEMSDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IAccessControlService _accessControlService;
    private readonly IFileStorageService _fileStorage;
    private readonly PEMS.Application.Uploads.IProtectedImageStore? _images;

    public FilesController(
        PEMSDbContext context,
        ICurrentUserService currentUser,
        IAccessControlService accessControlService,
        IFileStorageService fileStorage,
        PEMS.Application.Uploads.IProtectedImageStore? images = null)
    {
        _context = context;
        _currentUser = currentUser;
        _accessControlService = accessControlService;
        _fileStorage = fileStorage;
        _images = images;
    }

    // GET /Files/Download/{id} - always a download (Content-Disposition: attachment)
    public Task<IActionResult> Download(int id) => ServeAttachmentAsync(id, preview: false);

    // GET /Files/Preview/{id} - SCRUM-202: same access checks; shown inline only for PDF, raster images and text,
    // inside a CSP sandbox with nosniff; any other type is still a download.
    public Task<IActionResult> Preview(int id) => ServeAttachmentAsync(id, preview: true);

    // SCRUM-202: profile photos, signatures and letter editor images; only for signed-in users (global auth filter).
    // The two /uploads routes keep the URLs of images uploaded before SCRUM-202 working, now behind the login.
    [HttpGet("/Files/Image/{folder}/{name}")]
    [HttpGet("/uploads/{folder:regex(^(profile-photos|signatures)$)}/{name}")]
    [HttpGet("/uploads/letters/inline/{name}", Name = "LegacyLetterImage")]
    public async Task<IActionResult> Image(string? folder, string name)
    {
        folder ??= PEMS.Application.Uploads.ProtectedImageStore.LetterImages;
        var image = _images == null ? null : await _images.OpenAsync(folder, name);
        if (image == null) return NotFound();

        PEMS.Web.Security.Uploads.SafeFileHeaders.Apply(Response, image.Value.ContentType, null, inline: true, cacheable: true);
        return File(image.Value.Content, image.Value.ContentType);
    }

    private async Task<IActionResult> ServeAttachmentAsync(int id, bool preview)
    {
        // ۱. Attachment Lookup - طبق بند صریح ممیزی: تنها ورودی، یک شناسه‌ی عددی داخلی است؛
        // هرگز نام فایل/مسیر از کاربر گرفته نمی‌شود - این ساختاری از Path Traversal جلوگیری می‌کند
        var attachment = await _context.DocumentAttachments
            .FirstOrDefaultAsync(x => x.AttachmentId == id && x.IsActive);

        if (attachment == null)
            return NotFound();

        // ۲/۳. Authorization + Scope/Parent Entity Access Check - بر اساس نوع والد واقعی
        var (allowed, denyReason) = await CheckAccessAsync(attachment);
        if (!allowed)
        {
            // طبق بند صریح: کاربر احراز-هویت-شده‌ی بدون مجوز باید ۴۰۳ بگیرد، نه فقط یک NotFound
            // گمراه‌کننده که وجود فایل را هم پنهان می‌کند
            return Forbid();
        }

        // ۴. Storage Resolution + ۵. Secure Streaming - هرگز مسیر فیزیکی افشا نمی‌شود، هرگز
        // مستقیم System.IO.File با ورودی خام کاربر خوانده نمی‌شود. طبق بند صریح ممیزی (Path
        // Traversal): حتی اگر یک مقدار FilePath ناسالم در دیتابیس بنشیند (مثلاً از یک مسیر Legacy
        // آلوده)، LocalFileStorageProvider آن را رد می‌کند - این‌جا آن رد را به پاسخ امن (نه ۵۰۰
        // خام که جزئیات داخلی را افشا می‌کند) ترجمه می‌کنیم
        bool exists;
        try
        {
            exists = await _fileStorage.ExistsAsync(attachment.FilePath);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }

        if (!exists)
            return NotFound();

        var stream = await _fileStorage.OpenReadAsync(attachment.FilePath);

        // SCRUM-202: the type sent is PEMS's own for the extension, never the type the uploading browser declared (it could
        // have said text/html); unknown types are plain bytes. Download name and inline/attachment are set by SafeFileHeaders.
        var mimeType = PEMS.Application.Uploads.UploadPolicy.ContentTypeFor(attachment.FileName);
        var inline = preview && PEMS.Application.Uploads.UploadPolicy.IsInlineSafe(attachment.FileName);
        PEMS.Web.Security.Uploads.SafeFileHeaders.Apply(Response, mimeType, attachment.FileName, inline);
        return File(stream, mimeType, enableRangeProcessing: true);
    }

    private async Task<(bool Allowed, string? Reason)> CheckAccessAsync(PEMS.Domain.Entities.DocumentAttachment attachment)
    {
        // پیوست مرتبط با یک Document (EngineeringDocument یا VDRItem - هر دو از پایه‌ی Document
        // مشترک TPT هستند)
        if (attachment.DocumentId.HasValue)
        {
            var isEngineeringDocument = await _context.EngineeringDocuments
                .AnyAsync(x => x.DocumentId == attachment.DocumentId.Value);

            if (isEngineeringDocument)
                return await CheckEngineeringDocumentAccessAsync(attachment.DocumentId.Value);

            // VDRItem یا هر زیرنوع دیگر Document: طبق طراحی فعلی ماژول VDR (بررسی‌شده در
            // VDRController) امروز هیچ محدودیت Scope/دیسیپلینی روی دیدن آیتم‌های VDR اعمال
            // نمی‌شود - فقط ورود معتبر لازم است. این‌جا همان سطح واقعی امروز حفظ می‌شود؛ اختراع
            // یک محدودیت جدید و سخت‌گیرانه‌تر که امروز جایی اعمال نمی‌شود، می‌تواند دسترسی
            // مشروع کاربران VDR را بشکند - خارج از حدود این وظیفه‌ی امنیتی مشخص.
            var documentExists = await _context.Documents.AnyAsync(x => x.DocumentId == attachment.DocumentId.Value && x.IsActive);
            return documentExists ? (true, null) : (false, "Parent document not found or inactive");
        }

        if (attachment.LetterId.HasValue)
        {
            var exists = await _context.Letters.AnyAsync(x => x.LetterId == attachment.LetterId.Value);
            return exists ? (true, null) : (false, "Parent letter not found");
        }

        if (attachment.TechnicalQueryId.HasValue)
        {
            var exists = await _context.Set<PEMS.Domain.Entities.TechnicalQuery>()
                .AnyAsync(x => x.TechnicalQueryId == attachment.TechnicalQueryId.Value);
            return exists ? (true, null) : (false, "Parent technical query not found");
        }

        if (attachment.MOCId.HasValue)
        {
            var exists = await _context.Set<PEMS.Domain.Entities.MOC>()
                .AnyAsync(x => x.MOCId == attachment.MOCId.Value);
            return exists ? (true, null) : (false, "Parent MOC not found");
        }

        if (attachment.IntakeProjectId.HasValue)
        {
            // Intake: پیوست هنوز مسیریابی نشده - فقط کاربر واردشده (مطابق سطح فعلی ماژول Intake)
            return (true, null);
        }

        // بدون هیچ FK والدی (مثلاً پیوست‌های Comment/Reply ماژول VDR که طبق طراحی موجود
        // DocumentId ندارند) - سطح دسترسی امروز هم همین است: فقط ورود معتبر
        return (true, null);
    }

    private async Task<(bool Allowed, string? Reason)> CheckEngineeringDocumentAccessAsync(int documentId)
    {
        var document = await _context.Documents
            .Where(x => x.DocumentId == documentId && x.IsActive)
            .Select(x => new { x.DocumentId, x.DisciplineId })
            .FirstOrDefaultAsync();

        if (document == null)
            return (false, "Document not found or inactive");

        // دقیقاً همون منطق EngineeringDocumentsController/DocumentRegisterController (فاز ۳
        // RBAC-ABAC) - نه یک قانون موازی جدید
        var canViewAllDisciplines = (await _accessControlService.AuthorizeForRolesAsync(
            _currentUser.Roles, "EngineeringDocuments", "ViewAllDisciplines")).Allowed;

        if (canViewAllDisciplines || !_currentUser.UserId.HasValue)
            return (true, null);

        var myEmployeeId = await _context.Users
            .Where(u => u.Id == _currentUser.UserId!.Value)
            .Select(u => u.EmployeeId)
            .FirstOrDefaultAsync();

        if (!myEmployeeId.HasValue)
            return (true, null); // طبق رفتار موجود: کارمند نامشخص = بدون محدودیت (نه بدون دسترسی)

        var myDisciplineId = await _context.Employees
            .Where(e => e.EmployeeId == myEmployeeId.Value)
            .Select(e => (int?)e.DisciplineId)
            .FirstOrDefaultAsync();

        if (document.DisciplineId == null || document.DisciplineId == myDisciplineId)
            return (true, null);

        return (false, "Document belongs to a different discipline");
    }
}
