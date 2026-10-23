using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Web.Security;

namespace PEMS.Web.Controllers;

/// <summary>
/// طبق ممیزی Package Procurement Contract Finalization (§C/§D، Phase 7.2): تا پیش از این هیچ UI
/// ای برای زنجیره‌ی Tender→Bid→Evaluation→Negotiation→Winner وجود نداشت - فقط لایه‌ی سرویس (Phase 3)
/// و تست‌ها. این کنترلر همان الگوی مجوز درون‌خطی ProcurementPackageController/ContractController
/// را دنبال می‌کند.
/// </summary>
public class TenderController : Controller
{
    private readonly ITenderService _service;
    private readonly IProcurementPackageService _packageService;
    private readonly IPermissionService _permissionService;
    private readonly ICurrentUserService _currentUser;
    private readonly PEMSDbContext _context;
    private readonly IAccessControlService _accessControl;

    public TenderController(ITenderService service, IProcurementPackageService packageService,
        IPermissionService permissionService, ICurrentUserService currentUser, PEMSDbContext context,
        IAccessControlService accessControl)
    {
        _service = service;
        _packageService = packageService;
        _permissionService = permissionService;
        _currentUser = currentUser;
        _context = context;
        _accessControl = accessControl;
    }

    public async Task<IActionResult> Index(int? procurementPackageId)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Tender", "View"))
            return Forbid();

        ViewData["Title"] = "Tenders";
        ViewData["Section"] = "Engineering Management";

        var query = _context.Tenders.Include(x => x.ProcurementPackage).AsQueryable();
        if (procurementPackageId.HasValue)
            query = query.Where(x => x.ProcurementPackageId == procurementPackageId.Value);

        var tenders = await query.OrderByDescending(x => x.TenderId).ToListAsync();
        ViewBag.ProcurementPackageId = procurementPackageId;
        return View(tenders);
    }

    public async Task<IActionResult> Details(int id)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Tender", "View"))
            return Forbid();

        var tender = await _context.Tenders
            .Include(x => x.ProcurementPackage)
            .Include(x => x.InvitedVendors).ThenInclude(x => x.Vendor)
            .Include(x => x.Bids).ThenInclude(x => x.Vendor)
            .Include(x => x.Bids).ThenInclude(x => x.Evaluations)
            .Include(x => x.Bids).ThenInclude(x => x.Negotiations)
            .Include(x => x.AwardedBid)
            .FirstOrDefaultAsync(x => x.TenderId == id);

        if (tender == null) return NotFound();

        ViewData["Title"] = "Tender Details";
        ViewData["Section"] = "Engineering Management";

        ViewBag.Vendors = await _context.Vendors.Where(x => x.IsActive)
            .Select(x => new { x.VendorId, x.Name }).ToListAsync();

        // طبق گزارش سخت‌افزاری نهایی (Gap ۲): شفاف‌سازی‌های Technical/Commercial/Contractual
        // مطرح‌شده در طول همین مناقصه - از همان ProcurementCommunication موجود، نه یک معماری تازه
        ViewBag.Clarifications = await _context.ProcurementCommunications
            .Include(x => x.Vendor)
            .Include(x => x.Responses)
            .Where(x => x.TenderId == id)
            .OrderByDescending(x => x.RaisedDate)
            .ToListAsync();

        return View(tender);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RaiseClarification(int tenderId, int procurementPackageId, string type, string subject, string body, int? vendorId)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Tender", "Edit"))
            return Forbid();

        await _packageService.AddCommunicationAsync(procurementPackageId, type, subject, body, vendorId, _currentUser.FullName, null, tenderId);
        TempData["Message"] = "Clarification raised";
        return RedirectToAction(nameof(Details), new { id = tenderId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RespondClarification(int tenderId, int procurementPackageId, int communicationId, string responseText)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Tender", "Edit"))
            return Forbid();

        await _packageService.RespondToCommunicationAsync(procurementPackageId, communicationId, responseText, _currentUser.FullName);
        TempData["Message"] = "Response recorded";
        return RedirectToAction(nameof(Details), new { id = tenderId });
    }

    [HttpGet]
    public async Task<IActionResult> Create(int procurementPackageId)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Tender", "Create"))
            return Forbid();

        ViewData["Title"] = "New Tender";
        ViewData["Section"] = "Engineering Management";
        ViewBag.ProcurementPackageId = procurementPackageId;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int procurementPackageId, string title, DateTime? issueDate,
        DateTime? responseDeadline, bool requiresVendorQualification, string? technicalRequirements,
        string? commercialRequirements, string? inspectionRequirements, string? contractualRequirements)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Tender", "Create"))
            return Forbid();

        var id = await _service.CreateAsync(procurementPackageId, title, issueDate, responseDeadline,
            requiresVendorQualification, technicalRequirements, commercialRequirements,
            inspectionRequirements, contractualRequirements);

        TempData["Message"] = "Tender created";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> InviteVendor(int tenderId, int vendorId)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Tender", "Edit"))
            return Forbid();

        var result = await _service.InviteVendorAsync(tenderId, vendorId);
        if (!result.Success) TempData["Error"] = result.Error;
        else TempData["Message"] = "Vendor invited";
        return RedirectToAction(nameof(Details), new { id = tenderId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OpenForBidding(int tenderId)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Tender", "Edit"))
            return Forbid();

        var result = await _service.OpenForBiddingAsync(tenderId);
        if (!result.Success) TempData["Error"] = result.Error;
        else TempData["Message"] = "Tender opened for bidding";
        return RedirectToAction(nameof(Details), new { id = tenderId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CloseBidding(int tenderId)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Tender", "Edit"))
            return Forbid();

        var result = await _service.CloseBiddingAsync(tenderId);
        if (!result.Success) TempData["Error"] = result.Error;
        else TempData["Message"] = "Bidding closed";
        return RedirectToAction(nameof(Details), new { id = tenderId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitBid(int tenderId, int vendorId, decimal originalValue, string currency,
        string? technicalSubmissionReference, string? commercialSubmissionReference)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Tender", "Edit"))
            return Forbid();

        var result = await _service.SubmitBidAsync(tenderId, vendorId, originalValue, currency,
            technicalSubmissionReference, commercialSubmissionReference);
        if (!result.Success) TempData["Error"] = result.Error;
        else TempData["Message"] = "Bid submitted";
        return RedirectToAction(nameof(Details), new { id = tenderId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RecordEvaluation(int tenderId, int bidId, string evaluationType, string decision, string? comment)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Tender", "Edit"))
            return Forbid();

        var employeeId = await GetCurrentEmployeeIdAsync();
        var result = await _service.RecordBidEvaluationAsync(bidId, evaluationType, employeeId, null, decision, comment);
        if (!result.Success) TempData["Error"] = result.Error;
        else TempData["Message"] = $"{evaluationType} evaluation recorded";
        return RedirectToAction(nameof(Details), new { id = tenderId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestNegotiation(int tenderId, int bidId, decimal negotiatedValue,
        string? participants, string? justification)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Tender", "Edit"))
            return Forbid();

        var employeeId = await GetCurrentEmployeeIdAsync();
        var result = await _service.RequestNegotiationAsync(bidId, negotiatedValue, participants, null, null, null, justification, null, employeeId);
        if (!result.Success) TempData["Error"] = result.Error;
        else TempData["Message"] = "Negotiation proposed";
        return RedirectToAction(nameof(Details), new { id = tenderId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveNegotiation(int tenderId, int bidNegotiationId)
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Tender", "Approve"))
            return Forbid();
        // P0-10: مناقصه از خودِ مذاکره تعیین می‌شود، نه از tenderId فرم
        var separation = await EnforceCreatorSeparationAsync(null, bidNegotiationId, tenderId);
        if (separation != null) return separation;

        var employeeId = await GetCurrentEmployeeIdAsync();
        var result = await _service.ApproveNegotiationAsync(bidNegotiationId, employeeId);
        if (!result.Success) TempData["Error"] = result.Error;
        else TempData["Message"] = "Negotiation approved";
        return RedirectToAction(nameof(Details), new { id = tenderId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SelectWinner(int tenderId, int bidId)
    {
        // P0 / F-02+F-03: انتخاب برنده یک تایید مستقل است؛ گارد مشترک Fail-Closed (نقش + هویت کاربر + Scope/SoD + ثبت در
        // AuthorizationAuditLog) به‌جای بررسی فقط-نقش. هر نتیجه‌ی غیر از Allowed انتخاب را متوقف می‌کند.
        var outcome = await ProtectedActionGuard.EvaluateAsync(
            HttpContext, _permissionService, _accessControl, _currentUser, "Tender", "Approve", tenderId.ToString());
        if (outcome == ProtectedActionOutcome.DeniedBySoD)
        {
            TempData["Error"] = "طبق سیاست تفکیک وظایف، این اقدام برای شما مجاز نیست.";
            return RedirectToAction(nameof(Details), new { id = tenderId });
        }
        if (outcome != ProtectedActionOutcome.Allowed)
            return Forbid();
        var separation = await EnforceCreatorSeparationAsync(tenderId, null, tenderId);
        if (separation != null) return separation;

        var employeeId = await GetCurrentEmployeeIdAsync();
        var result = await _service.SelectWinnerAsync(tenderId, bidId, employeeId);
        if (!result.Success) TempData["Error"] = result.Error;
        else TempData["Message"] = "Winner selected";
        return RedirectToAction(nameof(Details), new { id = tenderId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateContract(int tenderId, string contractNo, DateTime? startDate, DateTime? endDate)
    {
        // P0 / F-03: ساخت قرارداد از برنده، علاوه بر تایید مناقصه، خودش ایجاد یک قرارداد است و باید مجوز Contracts.Create
        // داشته باشد (همان شرط ContractController.Create) - وگرنه Tender.Approve به‌تنهایی قرارداد می‌ساخت.
        var outcome = await ProtectedActionGuard.EvaluateAsync(
            HttpContext, _permissionService, _accessControl, _currentUser, "Tender", "Approve", tenderId.ToString());
        if (outcome == ProtectedActionOutcome.DeniedBySoD)
        {
            TempData["Error"] = "طبق سیاست تفکیک وظایف، این اقدام برای شما مجاز نیست.";
            return RedirectToAction(nameof(Details), new { id = tenderId });
        }
        if (outcome != ProtectedActionOutcome.Allowed)
            return Forbid();
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, "Contracts", "Create"))
            return Forbid();
        var separation = await EnforceCreatorSeparationAsync(tenderId, null, tenderId);
        if (separation != null) return separation;

        // P0.1 / R-01: رویداد «Contracts.Create» (که SoD «ایجادکننده ≠ تاییدکننده» به آن تکیه دارد) دیگر اینجا و با AuthorizeAsync نوشته نمی‌شود.
        // P0.3: سرویس قرارداد، هر دو AuditLog عمومی و این رویداد را روی SQL Server در یک تراکنش می‌نویسد؛ شکست هرکدام کل عملیات را Rollback می‌کند.
        // رکوردهای گارد Tender.Approve (بالا) جدا و پیش از آن Commit می‌شوند و بعد از Rollback ساخت قرارداد باقی می‌مانند (تصمیم مجوز، نه رویداد ایجاد).
        var ip = HttpContext?.Connection.RemoteIpAddress?.ToString();
        var result = await _service.CreateContractFromWinningBidAsync(tenderId, contractNo, startDate, endDate, _currentUser.UserId, ip);

        if (result.OutcomeUnknown)
        {
            // Commit نامطمئن بود و بررسی بعدی هم وضعیت را روشن نکرد: هرگز «ساخته نشد» نمی‌گوییم
            TempData["Error"] = result.Error;
            return RedirectToAction(nameof(Details), new { id = tenderId });
        }

        if (result.Success)
        {
            var problems = new List<string>();
            if (result.GenericAuditFailed)
                problems.Add("ثبت AuditLog عمومی این عملیات با خطا مواجه شد (در لاگ سیستم ثبت شده است)");
            if (result.CreatorEvent == ContractCreatorEventStatus.NotRecorded)
                problems.Add("رویداد «ایجادکننده‌ی قرارداد» ثبت نشد؛ برای ترمیم، همین عملیات را دوباره اجرا کنید (قرارداد تکراری ساخته نمی‌شود)");

            if (problems.Count > 0)
                TempData["Error"] = "قرارداد ساخته و ذخیره شد، اما " + string.Join("؛ ", problems) + ".";
            else if (result.RecoveredAfterUncertainCommit)
                TempData["Message"] = "Contract draft created from winning bid (the commit result was uncertain and was confirmed by re-reading the database; no duplicate was created)";
            else
                TempData["Message"] = "Contract draft created from winning bid";

            return RedirectToAction("Details", "Contract", new { id = result.ContractId });
        }

        if (result.Committed && result.ContractId.HasValue)
        {
            // قرارداد از قبل ساخته شده است؛ تلاش مجدد هیچ قرارداد تکراری نمی‌سازد و فقط رویداد ناقصِ ایجادکننده‌ی اصلی را ترمیم می‌کند
            switch (result.CreatorEvent)
            {
                case ContractCreatorEventStatus.Repaired:
                    TempData["Message"] = "قرارداد از قبل ساخته شده بود؛ رویداد ناقص «ایجادکننده‌ی قرارداد» برای ایجادکننده‌ی اصلی ترمیم شد. قرارداد تکراری ساخته نشد.";
                    break;
                case ContractCreatorEventStatus.NotRecorded:
                    TempData["Error"] = "قرارداد از قبل ساخته شده است، اما ترمیم رویداد «ایجادکننده‌ی قرارداد» ناموفق بود؛ دوباره تلاش کنید.";
                    break;
                case ContractCreatorEventStatus.OutcomeUnknown:
                    TempData["Error"] = "قرارداد از قبل ساخته شده است، اما وضعیت رویداد «ایجادکننده‌ی قرارداد» قابل تأیید نبود؛ گزارش تشخیص را بررسی کنید.";
                    break;
                case ContractCreatorEventStatus.CreatorUnidentified:
                    TempData["Error"] = "قرارداد از قبل ساخته شده است، اما ایجادکننده‌ی اصلی آن قابل شناسایی نیست؛ رویداد نوشته نشد و نیاز به بررسی دستی دارد.";
                    break;
                default:
                    TempData["Error"] = result.Error;
                    break;
            }
            return RedirectToAction(nameof(Details), new { id = tenderId });
        }

        TempData["Error"] = result.Error;
        return RedirectToAction(nameof(Details), new { id = tenderId });
    }

    /// <summary>
    /// P0-10 / D-OPEN-04: ایجادکننده‌ی مناقصه نمی‌تواند آن را تأیید کند (SystemAdmin مستثنا، با ثبت ممیزی). null = ادامه مجاز است.
    /// </summary>
    private async Task<IActionResult?> EnforceCreatorSeparationAsync(int? tenderId, int? bidNegotiationId, int redirectTenderId)
    {
        var result = await _service.CheckCreatorApproverSeparationAsync(tenderId, bidNegotiationId, _currentUser.UserId, _currentUser.UserName,
            _currentUser.Roles.Contains(PEMS.Persistence.Identity.SystemRoles.SystemAdmin), HttpContext?.Connection.RemoteIpAddress?.ToString());

        switch (result)
        {
            case TenderApprovalSeparation.DeniedCreator:
                TempData["Error"] = "طبق سیاست تفکیک وظایف، ایجادکننده‌ی مناقصه نمی‌تواند همان مناقصه را تأیید کند.";
                return RedirectToAction(nameof(Details), new { id = redirectTenderId });
            case TenderApprovalSeparation.DeniedUnverifiable:
                return Forbid();
            default:
                return null;
        }
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
