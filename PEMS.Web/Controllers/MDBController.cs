using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Web.Resources;

namespace PEMS.Web.Controllers;

/// <summary>
/// طبق «VENDOR DOCUMENTATION MANAGEMENT PROCEDURE» بخش ۶-۷: تحویل مستندات نهایی (MDB).
/// </summary>
public class MDBController : Controller
{
    private readonly IManufacturerDataBookService _service;
    private readonly PEMSDbContext _context;
    private readonly IStringLocalizer<SharedResource>? _localizer;

    public MDBController(IManufacturerDataBookService service, PEMSDbContext context, IStringLocalizer<SharedResource>? localizer = null)
    {
        _service = service;
        _context = context;
        _localizer = localizer;
    }

    // SCRUM-540: messages follow the page language
    private string T(string key) => _localizer?[key].Value ?? key;

    // طبق «PEMS — Global Grid Hardening» P1: قبلاً کل جدول فعال MDB (با ۴ Include) بدون
    // Skip/Take بارگذاری می‌شد. فیلترهای Project/Vendor موجود دست‌نخورده ماندند؛ فقط صفحه‌بندی
    // واقعی اضافه شد.
    public async Task<IActionResult> Index(int? projectId, int? vendorId, int page = 1, int? pageSize = null)
    {
        ViewData["Title"] = "Manufacturer Data Books (MDB)";
        ViewData["Section"] = "Engineering Management";

        var normalizedPageSize = DataGridPageSize.Normalize(pageSize);
        var result = await _service.GetPagedAsync(page, normalizedPageSize, projectId, vendorId);

        ViewBag.Projects = await _context.Projects.Where(x => x.IsActive)
            .Select(x => new { x.ProjectId, x.ProjectName }).ToListAsync();
        ViewBag.Vendors = await _context.Vendors.Where(x => x.IsActive)
            .Select(x => new { x.VendorId, x.Name }).ToListAsync();
        ViewBag.SelectedProjectId = projectId;
        ViewBag.SelectedVendorId = vendorId;

        return View(result);
    }

    public async Task<IActionResult> Details(int id)
    {
        ViewData["Title"] = "MDB Details";
        ViewData["Section"] = "Engineering Management";

        var mdb = await _service.GetByIdAsync(id);
        if (mdb == null) return NotFound();

        ViewBag.Documents = await _context.Documents
            .Where(x => x.IsActive && x.ProjectId == mdb.ProjectId)
            .OrderByDescending(x => x.CreatedDate)
            .Select(x => new { x.DocumentId, x.DocumentNo, x.Title })
            .Take(500)
            .ToListAsync();

        return View(mdb);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        ViewData["Title"] = "New MDB";
        ViewData["Section"] = "Engineering Management";

        await LoadCreateViewBagsAsync();

        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int equipmentId, int vendorId, int? poId, int projectId, DateTime? plannedFinalIssueDate)
    {
        if (equipmentId == 0 || vendorId == 0 || projectId == 0)
        {
            ModelState.AddModelError(string.Empty, T("Equipment, Vendor and Project are required."));
            await LoadCreateViewBagsAsync();
            ViewData["Title"] = "New MDB";
            ViewData["Section"] = "Engineering Management";
            return View();
        }

        var id = await _service.CreateAsync(equipmentId, vendorId, poId, projectId, plannedFinalIssueDate);

        TempData["Message"] = T("MDB created successfully");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddDocument(int mdbId, string part, int? documentId, string? title, int? dvdVolumeNo, bool isNotApplicable, string? notes)
    {
        await _service.AddDocumentAsync(mdbId, part, documentId, title ?? string.Empty, dvdVolumeNo, isNotApplicable, notes);
        TempData["Message"] = "Document added to MDB";
        return RedirectToAction(nameof(Details), new { id = mdbId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveDocument(int mdbId, int mdbDocumentId)
    {
        await _service.RemoveDocumentAsync(mdbDocumentId);
        return RedirectToAction(nameof(Details), new { id = mdbId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkIndexSubmitted(int mdbId)
    {
        await _service.MarkIndexSubmittedAsync(mdbId);
        TempData["Message"] = "Index of Contents marked as submitted";
        return RedirectToAction(nameof(Details), new { id = mdbId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkFinalIssued(int mdbId)
    {
        await _service.MarkFinalIssuedAsync(mdbId);
        TempData["Message"] = "MDB marked as Final Issued";
        return RedirectToAction(nameof(Details), new { id = mdbId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkHardCopyShipped(int mdbId, int? hardCopyCount)
    {
        await _service.MarkHardCopyShippedAsync(mdbId, hardCopyCount);
        TempData["Message"] = "Hard copy shipment recorded";
        return RedirectToAction(nameof(Details), new { id = mdbId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkSoftCopyDelivered(int mdbId)
    {
        await _service.MarkSoftCopyDeliveredAsync(mdbId);
        TempData["Message"] = "Soft copy delivery recorded";
        return RedirectToAction(nameof(Details), new { id = mdbId });
    }

    private async Task LoadCreateViewBagsAsync()
    {
        ViewBag.Projects = await _context.Projects.Where(x => x.IsActive)
            .Select(x => new { x.ProjectId, x.ProjectName }).ToListAsync();
        ViewBag.Vendors = await _context.Vendors.Where(x => x.IsActive)
            .Select(x => new { x.VendorId, x.Name }).ToListAsync();
        ViewBag.Equipments = await _context.Equipments.Where(x => x.IsActive)
            .Select(x => new { x.EquipmentId, x.TagNumber, x.ProjectId }).ToListAsync();
        ViewBag.PurchaseOrders = await _context.PurchaseOrders
            .Select(x => new { x.PoId, x.PoNumber, x.VendorId }).ToListAsync();
    }
}
