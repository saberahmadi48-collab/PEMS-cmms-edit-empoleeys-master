using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>
/// طبق ممیزی Package Procurement Contract Finalization (§C/§D، Phase 3): چرخه‌ی کامل
/// Tender→Bid→Evaluation→Negotiation→Winner Selection→Contract Draft. ProcurementPackage همچنان
/// سرصفحه‌ی بسته می‌ماند - این سرویس یک معماری موازی نیست، فقط مرحله‌ی مناقصه‌ی رسمی را که پیش
/// از این اصلاً وجود نداشت اضافه می‌کند.
/// </summary>
public interface ITenderService
{
    Task<int> CreateAsync(int procurementPackageId, string title, DateTime? issueDate, DateTime? responseDeadline,
        bool requiresVendorQualification, string? technicalRequirements, string? commercialRequirements,
        string? inspectionRequirements, string? contractualRequirements);

    Task<(bool Success, string? Error)> InviteVendorAsync(int tenderId, int vendorId);

    Task<(bool Success, string? Error)> OpenForBiddingAsync(int tenderId);

    Task<(bool Success, string? Error)> CloseBiddingAsync(int tenderId);

    Task<(bool Success, string? Error, int? BidId)> SubmitBidAsync(
        int tenderId, int vendorId, decimal originalValue, string currency,
        string? technicalSubmissionReference, string? commercialSubmissionReference);

    Task<(bool Success, string? Error)> RecordBidEvaluationAsync(
        int bidId, string evaluationType, int? evaluatorEmployeeId, string? disciplineRole, string decision, string? comment);

    Task<(bool Success, string? Error, int? NegotiationId)> RequestNegotiationAsync(
        int bidId, decimal negotiatedValue, string? participants, string? commercialChanges, string? technicalChanges,
        string? contractualChanges, string? justification, string? supportingDocumentReference, int? actorEmployeeId);

    Task<(bool Success, string? Error)> ApproveNegotiationAsync(int bidNegotiationId, int? actorEmployeeId);

    Task<(bool Success, string? Error)> SelectWinnerAsync(int tenderId, int bidId, int? actorEmployeeId);

    /// <summary>
    /// P0-10 / D-OPEN-04: «ایجادکننده‌ی مناقصه ≠ تأییدکننده‌ی آن». ایجادکننده از Tender.CreatedBy (که DbContext هنگام درج از کاربر جاری
    /// می‌نویسد) خوانده می‌شود، پس به ثبت جداگانه‌ی رویداد وابسته نیست. SystemAdmin مستثناست و استثنا در AuthorizationAuditLog ثبت می‌شود؛
    /// اگر آن ثبت ممکن نباشد، نتیجه رد است. فقط یکی از tenderId یا bidNegotiationId لازم است (مذاکره به مناقصه‌ی خودش نگاشت می‌شود).
    /// </summary>
    Task<TenderApprovalSeparation> CheckCreatorApproverSeparationAsync(
        int? tenderId, int? bidNegotiationId, int? userId, string? userName, bool isSystemAdmin, string? ipAddress = null);

    /// <summary>
    /// ساخت قرارداد از پیشنهاد برنده. creatorUserId = کاربری که این اقدام را انجام می‌دهد؛ اگر داده شود، رویداد «Contracts.Create» برای
    /// همان کاربر ثبت می‌شود. اگر قرارداد از قبل وجود داشته باشد، رویداد ناقص برای ایجادکننده‌ی اصلی (Contract.CreatedBy) ترمیم می‌شود،
    /// نه برای کاربری که تلاش مجدد می‌کند.
    /// P0.3: روی SQL Server ساخت قرارداد، اتصال مناقصه، AuditLogهای عمومی و رویداد ایجادکننده یک تراکنش‌اند (همه یا هیچ)؛ پس از Commit نامطمئن
    /// وضعیت واقعی از دیتابیس خوانده می‌شود و «ساخته نشد» فقط با اطمینان گزارش می‌شود (OutcomeUnknown در غیر این صورت).
    /// </summary>
    Task<TenderContractResult> CreateContractFromWinningBidAsync(
        int tenderId, string contractNo, DateTime? startDate, DateTime? endDate, int? creatorUserId = null, string? ipAddress = null);

    /// <summary>
    /// اگر برای قرارداد رویداد ایجاد (Allow) ثبت نشده باشد، آن را برای ایجادکننده‌ی اصلی (Contract.CreatedBy) می‌نویسد؛ در غیر این صورت چیزی
    /// نمی‌نویسد. P0.3: با قفل کاربردی به‌ازای قرارداد در یک تراکنش اجرا می‌شود، پس ترمیم هم‌زمان رویداد تکراری نمی‌نویسد (بدون Index یکتا؛ مسیرهای
    /// بدون قفل، مثل SQL مستقیم، تحت پوشش نیستند و تکرار برای SoD بی‌ضرر است چون بررسی SoD فقط «وجود» رویداد را می‌پرسد).
    /// </summary>
    Task<ContractCreatorEventStatus> EnsureContractCreatorEventAsync(int contractId, string? ipAddress = null);
}
