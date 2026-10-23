using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>
/// طبق ممیزی Package Procurement Contract Finalization (§C/§D/§I، Phase 3). سه قاعده‌ی حیاتی
/// این سرویس را هدایت می‌کنند: (۱) بی‌بازگشت‌بودن پیشنهاد اصلی - Bid.OriginalValue فقط یک‌بار در
/// SubmitBidAsync نوشته می‌شود و هیچ متد دیگری آن را تغییر نمی‌دهد؛ (۲) فنی ≠ تجاری - هیچ
/// Bid ای بدون هر دو ارزیابی مستقلِ «Passed» برنده نمی‌شود؛ (۳) صلاحیت فروشنده برای بسته‌های
/// بحرانی - اگر Tender.RequiresVendorQualification=true، دعوت و برد فقط برای فروشندگانی مجاز است
/// که در لحظه‌ی دعوت واجد صلاحیت بوده‌اند.
/// </summary>
public class TenderService : ITenderService
{
    private readonly PEMSDbContext _context;
    private readonly INumberingService _numbering;
    private readonly ILogger<TenderService>? _logger;
    private readonly ContractCreationScope _scope;

    // logger اختیاری است تا سازنده‌ی دو-پارامتری موجود (تست‌ها) سازگار بماند؛ DI آن را خودکار تأمین می‌کند.
    // P0.3: lockTimeoutMs مهلت انتظار قفل‌های کاربردی مسیر ساخت قرارداد است (پیش‌فرض ۱۵ ثانیه، هم‌اندازه با PostingScope)؛ فقط تست‌ها آن را کوتاه می‌کنند.
    public TenderService(PEMSDbContext context, INumberingService numbering, ILogger<TenderService>? logger = null, int lockTimeoutMs = 15000)
    {
        _context = context;
        _numbering = numbering;
        _logger = logger;
        _scope = new ContractCreationScope(context, logger, lockTimeoutMs);
    }

    public async Task<int> CreateAsync(int procurementPackageId, string title, DateTime? issueDate, DateTime? responseDeadline,
        bool requiresVendorQualification, string? technicalRequirements, string? commercialRequirements,
        string? inspectionRequirements, string? contractualRequirements)
    {
        var tenderNumber = await _numbering.GetNextNumberAsync("Tender");

        var tender = new Tender
        {
            TenderNumber = tenderNumber,
            ProcurementPackageId = procurementPackageId,
            Title = title,
            IssueDate = issueDate,
            ResponseDeadline = responseDeadline,
            RequiresVendorQualification = requiresVendorQualification,
            TechnicalRequirements = technicalRequirements,
            CommercialRequirements = commercialRequirements,
            InspectionRequirements = inspectionRequirements,
            ContractualRequirements = contractualRequirements,
            Status = "Draft"
        };

        _context.Tenders.Add(tender);
        await _context.SaveChangesAsync();
        return tender.TenderId;
    }

    public async Task<(bool Success, string? Error)> InviteVendorAsync(int tenderId, int vendorId)
    {
        var tender = await _context.Tenders.FirstOrDefaultAsync(x => x.TenderId == tenderId);
        if (tender == null) return (false, "مناقصه یافت نشد.");
        if (tender.Status != "Draft" && tender.Status != "Issued")
            return (false, $"دعوت فروشنده فقط پیش از باز شدن مناقصه مجاز است (وضعیت فعلی: {tender.Status}).");

        var vendor = await _context.Vendors.FirstOrDefaultAsync(x => x.VendorId == vendorId);
        if (vendor == null) return (false, "فروشنده یافت نشد.");

        var alreadyInvited = await _context.TenderInvitedVendors.AnyAsync(x => x.TenderId == tenderId && x.VendorId == vendorId);
        if (alreadyInvited) return (false, "این فروشنده قبلاً دعوت شده است.");

        // طبق §I قاعده‌ی ۱/۱۴: بسته‌های نیازمند صلاحیت نمی‌توانند فروشنده‌ی غیرواجد‌شرایط را دعوت کنند
        var isQualifiedNow = vendor.QualificationStatus == "Qualified"
            && (!vendor.QualificationExpiryDate.HasValue || vendor.QualificationExpiryDate.Value >= DateTime.Now);

        if (tender.RequiresVendorQualification && !isQualifiedNow)
            return (false, $"فروشنده «{vendor.Name}» واجد صلاحیت نیست (وضعیت: {vendor.QualificationStatus}) - این مناقصه نیازمند صلاحیت است.");

        _context.TenderInvitedVendors.Add(new TenderInvitedVendor
        {
            TenderId = tenderId,
            VendorId = vendorId,
            InvitedDate = DateTime.Now,
            QualificationConfirmed = isQualifiedNow
        });

        await _context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> OpenForBiddingAsync(int tenderId)
    {
        var tender = await _context.Tenders.FirstOrDefaultAsync(x => x.TenderId == tenderId);
        if (tender == null) return (false, "مناقصه یافت نشد.");
        if (tender.Status != "Draft")
            return (false, $"باز کردن مناقصه فقط از وضعیت Draft مجاز است (وضعیت فعلی: {tender.Status}).");

        var hasInvitedVendor = await _context.TenderInvitedVendors.AnyAsync(x => x.TenderId == tenderId);
        if (!hasInvitedVendor) return (false, "پیش از باز کردن مناقصه، حداقل یک فروشنده باید دعوت شده باشد.");

        tender.Status = "Open";
        await _context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> CloseBiddingAsync(int tenderId)
    {
        var tender = await _context.Tenders.FirstOrDefaultAsync(x => x.TenderId == tenderId);
        if (tender == null) return (false, "مناقصه یافت نشد.");
        if (tender.Status != "Open")
            return (false, $"بستن مناقصه فقط از وضعیت Open مجاز است (وضعیت فعلی: {tender.Status}).");

        var hasBid = await _context.Bids.AnyAsync(x => x.TenderId == tenderId);
        if (!hasBid) return (false, "پیش از بستن مناقصه، حداقل یک پیشنهاد باید ثبت شده باشد.");

        tender.Status = "Closed";
        await _context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error, int? BidId)> SubmitBidAsync(
        int tenderId, int vendorId, decimal originalValue, string currency,
        string? technicalSubmissionReference, string? commercialSubmissionReference)
    {
        var tender = await _context.Tenders.FirstOrDefaultAsync(x => x.TenderId == tenderId);
        if (tender == null) return (false, "مناقصه یافت نشد.", null);
        if (tender.Status != "Open")
            return (false, $"ثبت پیشنهاد فقط در مناقصه‌ی باز مجاز است (وضعیت فعلی: {tender.Status}).", null);

        var invitation = await _context.TenderInvitedVendors
            .FirstOrDefaultAsync(x => x.TenderId == tenderId && x.VendorId == vendorId);
        if (invitation == null) return (false, "این فروشنده به این مناقصه دعوت نشده است.", null);

        // طبق §I قاعده‌ی ۱/۱۴: عکس‌فوری صلاحیت در لحظه‌ی دعوت - نه یک ارجاع زنده که بشود بعداً دور زد
        if (tender.RequiresVendorQualification && !invitation.QualificationConfirmed)
            return (false, "فروشنده در لحظه‌ی دعوت واجد صلاحیت تایید نشده بود - نمی‌تواند پیشنهاد ثبت کند.", null);

        var alreadyBid = await _context.Bids.AnyAsync(x => x.TenderId == tenderId && x.VendorId == vendorId);
        if (alreadyBid) return (false, "این فروشنده قبلاً برای این مناقصه پیشنهاد ثبت کرده است.", null);

        var bidNumber = await _numbering.GetNextNumberAsync("Bid");

        var bid = new Bid
        {
            BidNumber = bidNumber,
            TenderId = tenderId,
            VendorId = vendorId,
            SubmittedDate = DateTime.Now,
            OriginalValue = originalValue,
            Currency = currency,
            TechnicalSubmissionReference = technicalSubmissionReference,
            CommercialSubmissionReference = commercialSubmissionReference,
            Status = "Submitted"
        };

        _context.Bids.Add(bid);
        await _context.SaveChangesAsync();
        return (true, null, bid.BidId);
    }

    public async Task<(bool Success, string? Error)> RecordBidEvaluationAsync(
        int bidId, string evaluationType, int? evaluatorEmployeeId, string? disciplineRole, string decision, string? comment)
    {
        if (evaluationType != "Technical" && evaluationType != "Commercial")
            return (false, "نوع ارزیابی باید Technical یا Commercial باشد.");
        if (decision != "Pending" && decision != "Passed" && decision != "Failed")
            return (false, "تصمیم ارزیابی باید Pending/Passed/Failed باشد.");

        var bid = await _context.Bids.FirstOrDefaultAsync(x => x.BidId == bidId);
        if (bid == null) return (false, "پیشنهاد یافت نشد.");
        if (bid.Status != "Submitted" && bid.Status != "UnderTechnicalEvaluation" && bid.Status != "UnderCommercialEvaluation")
            return (false, $"ارزیابی روی این پیشنهاد در وضعیت فعلی ({bid.Status}) مجاز نیست.");

        _context.BidEvaluations.Add(new BidEvaluation
        {
            BidId = bidId,
            EvaluationType = evaluationType,
            EvaluatorId = evaluatorEmployeeId,
            DisciplineRole = disciplineRole,
            Decision = decision,
            Comment = comment,
            EvaluationDate = DateTime.Now
        });

        bid.Status = evaluationType == "Technical" ? "UnderTechnicalEvaluation" : "UnderCommercialEvaluation";

        await _context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error, int? NegotiationId)> RequestNegotiationAsync(
        int bidId, decimal negotiatedValue, string? participants, string? commercialChanges, string? technicalChanges,
        string? contractualChanges, string? justification, string? supportingDocumentReference, int? actorEmployeeId)
    {
        var bid = await _context.Bids.Include(x => x.Negotiations).FirstOrDefaultAsync(x => x.BidId == bidId);
        if (bid == null) return (false, "پیشنهاد یافت نشد.", null);

        var previousValue = bid.NegotiatedValue ?? bid.OriginalValue;
        var nextRound = (bid.Negotiations.Count == 0 ? 0 : bid.Negotiations.Max(x => x.RoundNo)) + 1;

        var negotiation = new BidNegotiation
        {
            BidId = bidId,
            RoundNo = nextRound,
            NegotiationDate = DateTime.Now,
            Participants = participants,
            PreviousValue = previousValue,
            NegotiatedValue = negotiatedValue,
            CommercialChanges = commercialChanges,
            TechnicalChanges = technicalChanges,
            ContractualChanges = contractualChanges,
            Justification = justification,
            SupportingDocumentReference = supportingDocumentReference,
            Status = "Proposed"
        };

        _context.BidNegotiations.Add(negotiation);
        bid.Status = "UnderNegotiation";

        await _context.SaveChangesAsync();
        return (true, null, negotiation.BidNegotiationId);
    }

    public async Task<(bool Success, string? Error)> ApproveNegotiationAsync(int bidNegotiationId, int? actorEmployeeId)
    {
        var negotiation = await _context.BidNegotiations.Include(x => x.Bid).FirstOrDefaultAsync(x => x.BidNegotiationId == bidNegotiationId);
        if (negotiation == null) return (false, "این مذاکره یافت نشد.");
        if (negotiation.Status != "Proposed") return (false, "این مذاکره قبلاً تصمیم‌گیری شده است.");

        negotiation.Status = "Approved";
        negotiation.ApprovedById = actorEmployeeId;
        negotiation.ApprovedDate = DateTime.Now;

        // طبق §I قاعده‌ی ۴: فقط NegotiatedValue به‌روز می‌شود - OriginalValue هرگز بازنویسی نمی‌شود
        negotiation.Bid!.NegotiatedValue = negotiation.NegotiatedValue;

        await _context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<TenderApprovalSeparation> CheckCreatorApproverSeparationAsync(
        int? tenderId, int? bidNegotiationId, int? userId, string? userName, bool isSystemAdmin, string? ipAddress = null)
    {
        var resolvedTenderId = tenderId;
        if (bidNegotiationId.HasValue)
        {
            resolvedTenderId = await _context.BidNegotiations.AsNoTracking()
                .Where(n => n.BidNegotiationId == bidNegotiationId.Value)
                .Select(n => (int?)n.Bid!.TenderId)
                .FirstOrDefaultAsync();
        }
        if (!resolvedTenderId.HasValue) return TenderApprovalSeparation.NotFound;

        var tender = await _context.Tenders.AsNoTracking()
            .Where(t => t.TenderId == resolvedTenderId.Value)
            .Select(t => new { t.TenderId, t.CreatedBy })
            .FirstOrDefaultAsync();
        if (tender == null) return TenderApprovalSeparation.NotFound;

        // رکورد قدیمی بدون ایجادکننده‌ی ثبت‌شده: ایجادکننده قابل‌تشخیص نیست (محدودیت مستند، نه حدس)
        if (string.IsNullOrWhiteSpace(tender.CreatedBy)) return TenderApprovalSeparation.Allowed;

        if (string.IsNullOrWhiteSpace(userName) || !userId.HasValue) return TenderApprovalSeparation.DeniedUnverifiable;

        if (!string.Equals(tender.CreatedBy, userName, StringComparison.OrdinalIgnoreCase))
            return TenderApprovalSeparation.Allowed;

        if (!isSystemAdmin) return TenderApprovalSeparation.DeniedCreator;

        // استثنای SystemAdmin فقط با ردیف ممیزی؛ شکست ثبت = رد (Fail-Closed)
        var exemption = new AuthorizationAuditLog
        {
            UserId = userId.Value,
            ResourceName = "Tender",
            ActionName = "Approve",
            ResourceId = tender.TenderId.ToString(),
            Decision = AccessDecisionReason.Allow.ToString(),
            Reason = "SoD exemption: SystemAdmin approved a tender they created (owner decision D-OPEN-04, Tender Create<->Approve)",
            IpAddress = ipAddress,
            Timestamp = DateTime.Now
        };
        try
        {
            _context.AuthorizationAuditLogs.Add(exemption);
            await _context.SaveChangesAsync();
            return TenderApprovalSeparation.AllowedBySystemAdminExemption;
        }
        catch (Exception ex)
        {
            // ردیف ناموفق نباید با ذخیره‌ی بعدی همین درخواست بی‌صدا نوشته شود
            _context.Entry(exemption).State = EntityState.Detached;
            _logger?.LogError(ex, "Could not audit the SystemAdmin SoD exemption for tender {TenderId}; approval denied.", tender.TenderId);
            return TenderApprovalSeparation.DeniedUnverifiable;
        }
    }

    public async Task<(bool Success, string? Error)> SelectWinnerAsync(int tenderId, int bidId, int? actorEmployeeId)
    {
        var tender = await _context.Tenders.FirstOrDefaultAsync(x => x.TenderId == tenderId);
        if (tender == null) return (false, "مناقصه یافت نشد.");
        if (tender.Status != "Closed")
            return (false, $"انتخاب برنده فقط پس از بستن مناقصه مجاز است (وضعیت فعلی: {tender.Status}).");

        var bid = await _context.Bids.Include(x => x.Evaluations).FirstOrDefaultAsync(x => x.BidId == bidId && x.TenderId == tenderId);
        if (bid == null) return (false, "این پیشنهاد متعلق به این مناقصه نیست.");

        // طبق §D/§I قاعده‌ی ۲: تایید فنی و تجاری همیشه دو گیت مستقل‌اند - هیچ‌کدام جایگزین دیگری نیست
        // P0 / F-02: ارزیابی‌ها Append-only‌اند. قبلاً Any(Passed) یک ارزیابی مردودِ بعدی را نادیده می‌گرفت. قاعده‌ی محافظه‌کارانه:
        // برای هر دسته‌ی اجباری (فنی/تجاری) باید دست‌کم یک ارزیابی فعال Passed وجود داشته باشد و هیچ ارزیابی فعال Failed نباید
        // وجود داشته باشد (ردِ ثبت‌شده هرگز با پذیرشِ قبل یا بعد از آن خنثی نمی‌شود). ارزیابی Pending هرگز پذیرش حساب نمی‌شود.
        // این‌جا فرمول امتیازدهی/وزن‌دهی ساخته نشده؛ اینکه «آخرین ارزیابی» جایگزین ردِ قبلی شود یا نه تصمیم تجاری باز است.
        var activeEvaluations = bid.Evaluations.Where(x => x.IsActive).ToList();

        foreach (var (type, label) in new[] { ("Technical", "فنی"), ("Commercial", "تجاری") })
        {
            var ofType = activeEvaluations.Where(x => x.EvaluationType == type).ToList();
            if (ofType.Any(x => x.Decision == "Failed"))
                return (false, $"این پیشنهاد ارزیابی {label} مردود دارد - انتخاب به‌عنوان برنده مجاز نیست.");
            if (!ofType.Any(x => x.Decision == "Passed"))
                return (false, $"این پیشنهاد هنوز ارزیابی {label} موفق ندارد - انتخاب به‌عنوان برنده مجاز نیست.");
        }

        if (tender.RequiresVendorQualification)
        {
            var invitation = await _context.TenderInvitedVendors.FirstOrDefaultAsync(x => x.TenderId == tenderId && x.VendorId == bid.VendorId);
            if (invitation == null || !invitation.QualificationConfirmed)
                return (false, "فروشنده‌ی این پیشنهاد واجد صلاحیت تاییدشده نیست - این مناقصه نیازمند صلاحیت است.");
        }

        var allBidsForTender = await _context.Bids.Where(x => x.TenderId == tenderId).ToListAsync();
        foreach (var b in allBidsForTender)
            b.Status = b.BidId == bidId ? "Won" : "Lost";

        bid.FinalValue = bid.NegotiatedValue ?? bid.OriginalValue;

        tender.Status = "Awarded";
        tender.AwardedBidId = bidId;
        tender.AwardedDate = DateTime.Now;
        tender.AwardedById = actorEmployeeId;

        await _context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<TenderContractResult> CreateContractFromWinningBidAsync(
        int tenderId, string contractNo, DateTime? startDate, DateTime? endDate, int? creatorUserId = null, string? ipAddress = null)
    {
        contractNo = contractNo?.Trim() ?? string.Empty;

        // P0.3: ساخت قرارداد + اتصال به مناقصه + هر دو ذخیره‌ی AuditLog عمومی + رویداد «Contracts.Create» (و AuditLog عمومی آن) روی SQL Server
        // یک تراکنش صریح داخل Execution Strategy است. هر شکست پیش از Commit همه‌ی این‌ها را با هم Rollback می‌کند؛ بدنه‌ی هر تلاش، مناقصه را
        // دوباره از دیتابیس می‌خواند و پیش از بررسی یکتایی ContractNo وجود قرارداد را بررسی می‌کند، پس تلاش مجدد بعد از Commit نامطمئن
        // قرارداد تکراری نمی‌سازد. روی Providerهای بدون تراکنش (InMemory) همان بدنه بدون اتمیک‌بودن اجرا می‌شود.
        var state = new ContractCreationScope.State();
        try
        {
            return await _scope.RunAsync(state, () => CreateContractAttemptAsync(state, tenderId, contractNo, startDate, endDate, creatorUserId, ipAddress));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return await ClassifyCreateFailureAsync(state, ex, tenderId, contractNo, creatorUserId);
        }
    }

    private async Task<(TenderContractResult Value, bool Commit)> CreateContractAttemptAsync(
        ContractCreationScope.State state, int tenderId, string contractNo, DateTime? startDate, DateTime? endDate, int? creatorUserId, string? ipAddress)
    {
        // قفل مناقصه: درخواست هم‌زمان دوم تا پایان اولی منتظر می‌ماند و بعد وجود قرارداد را می‌بیند
        await _scope.AcquireLockAsync(ContractCreationLocks.TenderResource(tenderId));
        await _scope.ReloadPreTrackedAsync(state);

        var tender = await _context.Tenders
            .Include(x => x.ProcurementPackage)
            .Include(x => x.AwardedBid)
            .FirstOrDefaultAsync(x => x.TenderId == tenderId);

        if (tender == null) return (new TenderContractResult(false, "مناقصه یافت نشد.", null), false);
        if (tender.Status != "Awarded" || tender.AwardedBid == null)
            return (new TenderContractResult(false, "پیش از ساخت قرارداد، مناقصه باید به یک برنده رسیده باشد (Awarded).", null), false);

        if (tender.ContractId.HasValue)
        {
            // قرارداد از قبل ساخته شده؛ این بررسی عمداً قبل از بررسی یکتایی ContractNo است (وگرنه تلاش مجدد بعد از Commit نامطمئن، شماره‌ی خودش را «تکراری» می‌دید).
            // قرارداد تکراری ساخته نمی‌شود؛ رویداد ناقص برای ایجادکننده‌ی اصلی (Contract.CreatedBy) ترمیم می‌شود - نه برای کاربری که اکنون تلاش مجدد می‌کند.
            var existingId = tender.ContractId.Value;
            state.ExistingContractId = existingId;
            var repair = await ContractCreatorEvents.EnsureCoreAsync(_context, _scope, state, _logger, existingId, ipAddress);
            if (state.GeneratedContractIds.Contains(existingId))
            {
                // قرارداد را یکی از تلاش‌های قبلیِ همین فراخوانی ساخته و Commit آن نامطمئن بود؛ اکنون وجود قرارداد (و رویداد) تأیید شد
                var status = !creatorUserId.HasValue ? ContractCreatorEventStatus.NotApplicable
                    : repair.Status is ContractCreatorEventStatus.AlreadyRecorded or ContractCreatorEventStatus.Repaired ? ContractCreatorEventStatus.Recorded
                    : repair.Status;
                return (new TenderContractResult(true, null, existingId, Committed: true, CreatorEvent: status, RecoveredAfterUncertainCommit: true), repair.Wrote);
            }

            return (new TenderContractResult(false, "برای این مناقصه قبلاً قرارداد ساخته شده است.", existingId, Committed: true, CreatorEvent: repair.Status), repair.Wrote);
        }

        // P0 / F-03: شماره‌ی قرارداد الزامی و یکتا. P0.3: بررسی یکتایی زیر قفل کاربردیِ شماره‌ی نرمال‌شده انجام می‌شود تا دو مناقصه‌ی هم‌زمان با یک شماره
        // هر دو از بررسی رد نشوند؛ این قفل جایگزین Index یکتا نیست (مسیرهای دیگر ساخت قرارداد قفل نمی‌گیرند).
        if (contractNo.Length == 0)
            return (new TenderContractResult(false, "شماره‌ی قرارداد الزامی است.", null), false);

        await _scope.AcquireLockAsync(ContractCreationLocks.ContractNoResource(contractNo));
        if (await _context.Contracts.AsNoTracking().AnyAsync(x => x.ContractNo == contractNo))
            return (new TenderContractResult(false, $"قراردادی با شماره‌ی «{contractNo}» از قبل وجود دارد.", null), false);

        var contract = new Contract
        {
            ContractNo = contractNo,
            ProjectId = tender.ProcurementPackage!.ProjectId,
            VendorId = tender.AwardedBid.VendorId,
            ContractValue = tender.AwardedBid.FinalValue,
            StartDate = startDate,
            EndDate = endDate,
            Status = "Draft",
            // طبق گزارش سخت‌افزاری نهایی (Gap ۱): بسته‌ای که بازرسی الزامی داشت، قرارداد برخاسته
            // از آن هم باید همان الزام را به ارث ببرد - نه این‌که خاموش شروع شود
            RequiresInspection = tender.ProcurementPackage.RequiresInspection,
            CriticalityClass = tender.ProcurementPackage.CriticalityClass
        };

        // قرارداد و اتصال آن به مناقصه یک SaveChanges است (RowVersion مناقصه در همان ذخیره بررسی می‌شود). PEMSDbContext.SaveChangesAsync دو ذخیره‌ی
        // فیزیکی انجام می‌دهد: (A) داده‌ی کسب‌وکار (B) AuditLog عمومی؛ هر دو در تراکنش جاری هستند.
        tender.Contract = contract;
        try
        {
            await _context.SaveChangesAsync();
        }
        finally
        {
            // اگر ذخیره‌ی اول (A) موفق بود ولی دومی (B) شکست خورد، شناسه‌ی تولیدشده روی موجودیت ردیابی‌شده می‌ماند؛ آن را ثبت می‌کنیم تا طبقه‌بندی
            // شکست (و تلاش مجدد بعد از Commit نامطمئن) بتواند قرارداد این فراخوانی را از قرارداد درخواست دیگر تشخیص دهد
            if (contract.ContractId > 0) state.GeneratedContractIds.Add(contract.ContractId);
        }

        // (C) رویداد «Contracts.Create» که SoD «ایجادکننده ≠ تاییدکننده» به آن تکیه دارد و (D) AuditLog عمومی آن، در همان تراکنش. شکست هرکدام کل
        // عملیات (از جمله قرارداد) را Rollback می‌کند؛ هیچ استثنایی بلعیده نمی‌شود.
        var creatorEvent = ContractCreatorEventStatus.NotApplicable;
        if (creatorUserId.HasValue)
        {
            ContractCreatorEvents.Add(_context, contract.ContractId, creatorUserId.Value, ipAddress, "Creator attribution: contract created from the awarded tender");
            await _context.SaveChangesAsync();
            creatorEvent = ContractCreatorEventStatus.Recorded;
        }

        return (new TenderContractResult(true, null, contract.ContractId, Committed: true, CreatorEvent: creatorEvent), true);
    }

    /// <summary>
    /// طبقه‌بندی شکست بعد از پایان Execution Strategy (P0.5 / H-1، سازگار با READ_COMMITTED_SNAPSHOT=ON).
    /// ۱) اگر درخواست Commit صادر شده بود، سرنوشت تراکنش نامعلوم است و فقط خواندنِ تأییدی زیر قفل مناقصه (که تراکنش اصلی از ابتدا تا پایانش نگه می‌داشت؛
    ///    ContractCreationScope.TryReadUnderLockAsync) تصمیم می‌گیرد: مناقصه به قرارداد همین فراخوانی متصل است = موفقیتِ بازیابی‌شده؛ به قرارداد دیگری = «قبلاً ساخته
    ///    شده»؛ به هیچ قراردادی متصل نیست = «ساخته نشد» (قفل گرفته شد پس تراکنش اصلی تمام شده و Commit نکرده)؛ قفل/خواندن ناموفق = OutcomeUnknown.
    ///    «چیزی پیدا نشد» به‌تنهایی هرگز «ساخته نشد» نیست.
    /// ۲) Provider بدون تراکنش (InMemory/تراکنش بیرونی): از خواندن مستقیم طبقه‌بندی می‌شود (بدون ادعای اتمیک‌بودن).
    /// ۳) شکست پیش از هر درخواست Commit: تراکنش قطعاً Rollback شده؛ بدون خواندن، فقط نگاشت نوع خطا.
    /// </summary>
    private async Task<TenderContractResult> ClassifyCreateFailureAsync(ContractCreationScope.State state, Exception ex, int tenderId, string contractNo, int? creatorUserId)
    {
        _logger?.LogError(ex, "Tender {TenderId}: creating the contract {ContractNo} reported an error (attempts={Attempts}, commitAttempted={CommitAttempted}).",
            tenderId, contractNo, state.Attempts, state.CommitAttempted);

        async Task<(int? LinkedId, bool EventExists)> ReadLinkAsync()
        {
            var linked = await _context.Tenders.AsNoTracking()
                .Where(t => t.TenderId == tenderId).Select(t => t.ContractId).FirstOrDefaultAsync();
            var hasEvent = linked.HasValue && await ContractCreatorEvents.ExistsAsync(_context, linked.Value);
            return (linked, hasEvent);
        }

        if (state.CommitAttempted)
        {
            var check = await _scope.TryReadUnderLockAsync(ContractCreationLocks.TenderResource(tenderId), ReadLinkAsync);
            if (!check.Verified)
                return new TenderContractResult(false,
                    "نتیجه‌ی ساخت قرارداد نامطمئن است (درخواست Commit صادر شد و بررسی امن نتیجه ممکن نبود). قبل از تلاش مجدد، فهرست قراردادها و گزارش تشخیص را بررسی کنید؛ تلاش مجدد قرارداد تکراری نمی‌سازد.",
                    null, OutcomeUnknown: true, CreatorEvent: ContractCreatorEventStatus.OutcomeUnknown);

            var (linkedId, eventExists) = check.Value;
            if (linkedId.HasValue)
            {
                if (state.GeneratedContractIds.Contains(linkedId.Value))
                {
                    var status = !creatorUserId.HasValue ? ContractCreatorEventStatus.NotApplicable
                        : eventExists ? ContractCreatorEventStatus.Recorded : ContractCreatorEventStatus.NotRecorded;
                    return new TenderContractResult(true, null, linkedId, Committed: true, CreatorEvent: status, RecoveredAfterUncertainCommit: true);
                }

                // قرارداد را درخواست هم‌زمان دیگری ساخته و Commit کرده است (یا مناقصه از قبل متصل بود)
                return new TenderContractResult(false, "برای این مناقصه قبلاً قرارداد ساخته شده است.", linkedId, Committed: true,
                    CreatorEvent: eventExists ? ContractCreatorEventStatus.AlreadyRecorded : ContractCreatorEventStatus.NotRecorded);
            }

            // قفل گرفته شد (تراکنش اصلی پایان یافته) و مناقصه به قراردادی متصل نیست ⇒ Commit نشده
            return ex is ContractLockBusyException
                ? new TenderContractResult(false, "این مناقصه یا شماره‌ی قرارداد هم‌اکنون توسط عملیات دیگری در حال پردازش است؛ چند لحظه بعد دوباره تلاش کنید. هیچ تغییری ذخیره نشد.", null)
                : new TenderContractResult(false, "ساخت قرارداد انجام نشد؛ هیچ تغییری ذخیره نشد. دوباره تلاش کنید.", null);
        }

        if (!state.Transactional && state.GeneratedContractIds.Count > 0)
        {
            int? linkedId;
            bool eventExists;
            try
            {
                (linkedId, eventExists) = await ReadLinkAsync();
            }
            catch (Exception verifyEx) when (verifyEx is not OperationCanceledException)
            {
                _logger?.LogError(verifyEx, "Tender {TenderId}: could not verify the contract-creation outcome after an error (non-transactional provider).", tenderId);
                return new TenderContractResult(false,
                    "نتیجه‌ی ساخت قرارداد نامطمئن است (Provider بدون تراکنش و بررسی بعدی هم ممکن نبود). قبل از تلاش مجدد، فهرست قراردادها و گزارش تشخیص را بررسی کنید.",
                    null, OutcomeUnknown: true, CreatorEvent: ContractCreatorEventStatus.OutcomeUnknown);
            }

            if (linkedId.HasValue)
            {
                if (state.GeneratedContractIds.Contains(linkedId.Value))
                {
                    var status = !creatorUserId.HasValue ? ContractCreatorEventStatus.NotApplicable
                        : eventExists ? ContractCreatorEventStatus.Recorded : ContractCreatorEventStatus.NotRecorded;
                    return new TenderContractResult(true, null, linkedId, Committed: true, CreatorEvent: status);
                }

                return new TenderContractResult(false, "برای این مناقصه قبلاً قرارداد ساخته شده است.", linkedId, Committed: true,
                    CreatorEvent: eventExists ? ContractCreatorEventStatus.AlreadyRecorded : ContractCreatorEventStatus.NotRecorded);
            }
        }

        if (state.ExistingContractId is int existingId)
        {
            // شکست (بدون درخواست Commit) در مسیر «قرارداد از قبل هست» (ترمیم رویداد): قرارداد وجود دارد، پس «ساخت انجام نشد» پیام درستی نیست. وضعیت رویداد از
            // دیتابیس خوانده می‌شود؛ این خواندن فقط پیام را می‌سازد (ترمیم ما Commit نشده)، اگر ممکن نبود فقط «ثبت‌نشده» گزارش می‌شود.
            var eventStatus = ContractCreatorEventStatus.NotRecorded;
            try { if (await ContractCreatorEvents.ExistsAsync(_context, existingId)) eventStatus = ContractCreatorEventStatus.AlreadyRecorded; }
            catch (Exception verifyEx) when (verifyEx is not OperationCanceledException)
            {
                _logger?.LogError(verifyEx, "Tender {TenderId}: could not verify the creator event of the existing contract {ContractId}.", tenderId, existingId);
            }
            return new TenderContractResult(false, "برای این مناقصه قبلاً قرارداد ساخته شده است.", existingId, Committed: true, CreatorEvent: eventStatus);
        }

        return MapNotCreated(ex);
    }

    private static TenderContractResult MapNotCreated(Exception ex)
    {
        switch (ex)
        {
            case ContractLockBusyException:
                return new TenderContractResult(false,
                    "این مناقصه یا شماره‌ی قرارداد هم‌اکنون توسط عملیات دیگری در حال پردازش است؛ چند لحظه بعد دوباره تلاش کنید. هیچ تغییری ذخیره نشد.", null);
            case DbUpdateConcurrencyException:
                return new TenderContractResult(false,
                    "این مناقصه هم‌زمان توسط کاربر دیگری تغییر کرد (احتمالاً قرارداد قبلاً ساخته شده است). صفحه را تازه کنید.", null);
            case DbUpdateException:
            case RetryLimitExceededException:
                return new TenderContractResult(false, "ساخت قرارداد انجام نشد؛ هیچ تغییری ذخیره نشد. دوباره تلاش کنید.", null);
            default:
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();
                throw ex;   // به‌دست نمی‌رسد؛ برای کامپایلر
        }
    }

    public Task<ContractCreatorEventStatus> EnsureContractCreatorEventAsync(int contractId, string? ipAddress = null)
        => ContractCreatorEvents.EnsureAsync(_context, _scope, _logger, contractId, ipAddress);
}
