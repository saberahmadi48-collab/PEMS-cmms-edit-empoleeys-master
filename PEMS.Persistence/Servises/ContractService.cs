using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

public class ContractService : IContractService
{
    private readonly PEMSDbContext _context;
    private readonly IAccessControlService? _accessControl;
    private readonly ILogger<ContractService>? _logger;
    private readonly ContractCreationScope _scope;
    private readonly INumberingService _numbering;

    // P0.4: پارامترهای اضافه اختیاری‌اند تا سازنده‌ی تک‌پارامتری موجود (تست‌ها) سازگار بماند؛ DI همه را تأمین می‌کند. بدون IAccessControlService مسیر
    // CreateWithCreatorEventAsync بسته (Fail-Closed) است. lockTimeoutMs مهلت قفل‌های کاربردی (پیش‌فرض ۱۵ ثانیه، هم‌اندازه با مسیر مناقصه)؛ فقط تست‌ها کوتاهش می‌کنند.
    public ContractService(PEMSDbContext context, IAccessControlService? accessControl = null, ILogger<ContractService>? logger = null, int lockTimeoutMs = 15000,
        INumberingService? numbering = null)
    {
        _context = context;
        _numbering = numbering ?? new NumberingService(context);
        _accessControl = accessControl;
        _logger = logger;
        _scope = new ContractCreationScope(context, logger, lockTimeoutMs);
    }

    public async Task<List<ContractDto>> GetAllAsync()
    {
        return await _context.Contracts
            .Include(x => x.Project)
            .Include(x => x.Vendor)
            .OrderByDescending(x => x.CreatedDate)
            .Select(x => new ContractDto
            {
                ContractId = x.ContractId,
                ContractNo = x.ContractNo,
                ProjectId = x.ProjectId,
                ProjectName = x.Project!.ProjectName,
                VendorId = x.VendorId,
                VendorName = x.Vendor!.Name,
                ScopeOfWork = x.ScopeOfWork,
                ContractValue = x.ContractValue,
                Currency = x.Currency,
                StartDate = x.StartDate,
                EndDate = x.EndDate,
                Status = x.Status,
                PurchaseOrdersCount = x.PurchaseOrders.Count,
                RequiresInspection = x.RequiresInspection
            })
            .ToListAsync();
    }

    public async Task<ContractDto?> GetByIdAsync(int id)
    {
        var list = await GetAllAsync();
        return list.FirstOrDefault(x => x.ContractId == id);
    }

    // طبق «PEMS — Global Grid Hardening» P1: جایگزین GetAllAsync برای Index - شمارش/مرتب‌سازی/
    // صفحه‌بندی روی IQueryable قبل از Select. GetAllAsync/GetByIdAsync بالا دست‌نخورده ماندند.
    public async Task<PagedResult<ContractDto>> GetPagedAsync(int page, int pageSize)
    {
        var query = _context.Contracts.AsNoTracking().AsQueryable();

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(x => x.CreatedDate)
            .ThenByDescending(x => x.ContractId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new ContractDto
            {
                ContractId = x.ContractId,
                ContractNo = x.ContractNo,
                ProjectId = x.ProjectId,
                ProjectName = x.Project!.ProjectName,
                VendorId = x.VendorId,
                VendorName = x.Vendor!.Name,
                ScopeOfWork = x.ScopeOfWork,
                ContractValue = x.ContractValue,
                Currency = x.Currency,
                StartDate = x.StartDate,
                EndDate = x.EndDate,
                Status = x.Status,
                PurchaseOrdersCount = x.PurchaseOrders.Count,
                RequiresInspection = x.RequiresInspection
            })
            .ToListAsync();

        return new PagedResult<ContractDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public async Task<int> CreateAsync(string contractNo, int projectId, int vendorId, string? scope,
        decimal? value, DateTime? startDate, DateTime? endDate, bool requiresInspection = false)
    {
        // P0.4: مسیر قدیمیِ بدون کاربر؛ همان تراکنش و همان قفل شماره‌ی قرارداد مسیر مناقصه، اما بدون مجوز و بدون رویداد (کاربر نامشخص)
        var result = await CreateCoreAsync(contractNo, projectId, vendorId, scope, value, startDate, endDate, requiresInspection, creatorUserId: null, ipAddress: null);
        if (!result.Success)
            throw new InvalidOperationException(result.Error ?? "ساخت قرارداد انجام نشد.");
        return result.ContractId!.Value;
    }

    public Task<ContractCreateResult> CreateWithCreatorEventAsync(string contractNo, int projectId, int vendorId, string? scope,
        decimal? value, DateTime? startDate, DateTime? endDate, bool requiresInspection, int creatorUserId, string? ipAddress)
        => CreateCoreAsync(contractNo, projectId, vendorId, scope, value, startDate, endDate, requiresInspection, creatorUserId, ipAddress);

    public Task<ContractCreatorEventStatus> EnsureCreatorEventAsync(int contractId, string? ipAddress = null)
        => ContractCreatorEvents.EnsureAsync(_context, _scope, _logger, contractId, ipAddress);

    private async Task<ContractCreateResult> CreateCoreAsync(string contractNo, int projectId, int vendorId, string? scope,
        decimal? value, DateTime? startDate, DateTime? endDate, bool requiresInspection, int? creatorUserId, string? ipAddress)
    {
        // P0 / شناسه‌ها: اعتبارسنجی بدون دیتابیس (شماره‌ی خالی، مبلغ منفی، بازه‌ی تاریخ نامعتبر)
        // SCRUM-40: شماره یک بار پیش از تلاش‌های Execution Strategy گرفته می‌شود تا تلاش مجدد همان شماره را ببیند
        contractNo = await _numbering.ResolveNumberAsync("Contract", contractNo, projectId);
        if (contractNo.Length == 0)
            return new ContractCreateResult(false, "شماره‌ی قرارداد الزامی است.", null);
        if (value.HasValue && value.Value < 0)
            return new ContractCreateResult(false, "ارزش قرارداد نمی‌تواند منفی باشد.", null);
        if (startDate.HasValue && endDate.HasValue && endDate.Value < startDate.Value)
            return new ContractCreateResult(false, "تاریخ پایان قرارداد نمی‌تواند قبل از تاریخ شروع باشد.", null);

        // P0.4: مجوز آگاه-از-کاربر، Fail-Closed، پیش از هر درج و خارج از تراکنش؛ ردیف Allow/Deny خودش یک ممیزی مستقل (ResourceId = null) است و با رویداد
        // ایجادکننده (ResourceId = شناسه‌ی قرارداد) متمایز. «رد» و «نامشخص (استثنا / نتیجه‌ی ناسازگار)» هر دو قبل از ساخت قرارداد متوقف می‌شوند. رد در همان
        // AuthorizeAsync ثبت می‌شود؛ شکست ثبت ممیزی هرگز به Allow تبدیل نمی‌شود.
        AccessDecision? decision = null;
        if (creatorUserId.HasValue)
        {
            if (_accessControl == null)
                return new ContractCreateResult(false, "مجوز قابل‌ارزیابی نیست؛ هیچ قراردادی ساخته نشد.", null, AuthorizationIndeterminate: true);
            try
            {
                decision = await _accessControl.AuthorizeAsync(new AccessControlRequest
                {
                    UserId = creatorUserId.Value, ResourceName = "Contracts", ActionName = "Create", ResourceId = null, IpAddress = ipAddress
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogError(ex, "Authorization evaluation failed for Contracts.Create (user {UserId}); no contract was created.", creatorUserId);
                return new ContractCreateResult(false, "مجوز قابل‌ارزیابی نبود؛ هیچ قراردادی ساخته نشد.", null, AuthorizationIndeterminate: true);
            }

            if (decision is not { Allowed: true, Reason: AccessDecisionReason.Allow })
                return new ContractCreateResult(false, "شما مجاز به ساخت قرارداد نیستید.", null, AuthorizationDenied: true);
        }

        // P0.4: ساخت قرارداد + AuditLogهای عمومی + رویداد ایجادکننده روی SQL Server یک تراکنش داخل Execution Strategy است و زیر قفل کاربردی شماره‌ی
        // نرمال‌شده (همان قفل مسیر مناقصه) اجرا می‌شود؛ هر بدنه‌ی تلاش، وجود قراردادِ همین شماره را دوباره از دیتابیس می‌خواند.
        var state = new ContractCreationScope.State();
        try
        {
            return await _scope.RunAsync(state, () => CreateAttemptAsync(state, contractNo, projectId, vendorId, scope, value, startDate, endDate,
                requiresInspection, creatorUserId, ipAddress, decision));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return await ClassifyFailureAsync(state, ex, contractNo, creatorUserId);
        }
    }

    private async Task<(ContractCreateResult Value, bool Commit)> CreateAttemptAsync(ContractCreationScope.State state, string contractNo, int projectId,
        int vendorId, string? scope, decimal? value, DateTime? startDate, DateTime? endDate, bool requiresInspection, int? creatorUserId,
        string? ipAddress, AccessDecision? decision)
    {
        await _scope.AcquireLockAsync(ContractCreationLocks.ContractNoResource(contractNo));

        // این بررسی عمداً زیر قفل و قبل از درج است. اگر قراردادِ همین شماره را یکی از تلاش‌های قبلیِ همین فراخوانی ساخته (Commit نامطمئن)، موفقیتِ
        // بازیابی‌شده است، نه «شماره‌ی تکراری»؛ قرارداد هر درخواست/مسیر دیگر «شماره‌ی تکراری» است.
        var existingId = await _context.Contracts.AsNoTracking()
            .Where(x => x.ContractNo == contractNo).Select(x => (int?)x.ContractId).FirstOrDefaultAsync();
        if (existingId.HasValue)
        {
            if (state.GeneratedContractIds.Contains(existingId.Value))
            {
                var recovered = creatorUserId.HasValue
                    ? (await ContractCreatorEvents.ExistsAsync(_context, existingId.Value) ? ContractCreatorEventStatus.Recorded : ContractCreatorEventStatus.NotRecorded)
                    : ContractCreatorEventStatus.NotApplicable;
                return (new ContractCreateResult(true, null, existingId, Committed: true, CreatorEvent: recovered, RecoveredAfterUncertainCommit: true), false);
            }

            return (new ContractCreateResult(false, $"قراردادی با شماره‌ی «{contractNo}» از قبل وجود دارد.", null), false);
        }

        var contract = new Contract
        {
            ContractNo = contractNo,
            ProjectId = projectId,
            VendorId = vendorId,
            ScopeOfWork = scope,
            ContractValue = value,
            StartDate = startDate,
            EndDate = endDate,
            Status = "Draft",
            RequiresInspection = requiresInspection,
            CreatedDate = DateTime.Now
        };

        _context.Contracts.Add(contract);
        try
        {
            await _context.SaveChangesAsync();
        }
        finally
        {
            // اگر ذخیره‌ی اول (A) موفق بود ولی AuditLog عمومی (B) شکست خورد، شناسه روی موجودیت ردیابی‌شده می‌ماند؛ برای طبقه‌بندی شکست ثبتش می‌کنیم
            if (contract.ContractId > 0) state.GeneratedContractIds.Add(contract.ContractId);
        }

        // (C) رویداد «Contracts/Create/Allow» برای کاربر احرازهویت‌شده‌ی همین درخواست (ایجادکننده‌ی واقعی - نه کسی که بعداً تلاش مجدد/ترمیم می‌کند) و (D)
        // AuditLog عمومی آن، در همان تراکنش. شکست هرکدام کل عملیات را Rollback می‌کند.
        var creatorEvent = ContractCreatorEventStatus.NotApplicable;
        if (creatorUserId.HasValue)
        {
            ContractCreatorEvents.Add(_context, contract.ContractId, creatorUserId.Value, ipAddress,
                "Creator attribution: contract created manually", decision?.MatchedScopeType, decision?.MatchedScopeValue);
            await _context.SaveChangesAsync();
            creatorEvent = ContractCreatorEventStatus.Recorded;
        }

        return (new ContractCreateResult(true, null, contract.ContractId, Committed: true, CreatorEvent: creatorEvent), true);
    }

    /// <summary>
    /// طبقه‌بندی شکست پس از پایان Execution Strategy (P0.5 / H-1، سازگار با READ_COMMITTED_SNAPSHOT=ON).
    /// ۱) اگر درخواست Commit صادر شده بود: سرنوشت تراکنش نامعلوم است. فقط خواندنِ تأییدی زیر همان قفل شماره‌ی قرارداد (ContractCreationScope.TryReadUnderLockAsync)
    ///    تصمیم می‌گیرد: قراردادِ همین فراخوانی = موفقیتِ بازیابی‌شده؛ قرارداد دیگر = «شماره‌ی تکراری»؛ هیچ قراردادی = «ساخته نشد» (قفل گرفته شد، پس تراکنش اصلی تمام
    ///    شده و Commit نکرده است)؛ و اگر قفل گرفته نشد یا خواندن شکست خورد = OutcomeUnknown. «چیزی پیدا نشد» به‌تنهایی هرگز «ساخته نشد» نیست.
    /// ۲) Provider بدون تراکنش (InMemory/تراکنش بیرونی): مرز Commit وجود ندارد؛ از خواندن مستقیم طبقه‌بندی می‌شود (بدون ادعای اتمیک‌بودن).
    /// ۳) شکست پیش از هر درخواست Commit: تراکنش قطعاً Rollback شده (Commit هرگز فراخوانی نشد)؛ بدون خواندن، فقط نگاشت نوع خطا.
    /// </summary>
    private async Task<ContractCreateResult> ClassifyFailureAsync(ContractCreationScope.State state, Exception ex, string contractNo, int? creatorUserId)
    {
        _logger?.LogError(ex, "Creating the manual contract {ContractNo} reported an error (attempts={Attempts}, commitAttempted={CommitAttempted}).",
            contractNo, state.Attempts, state.CommitAttempted);

        if (state.CommitAttempted)
        {
            var check = await _scope.TryReadUnderLockAsync(ContractCreationLocks.ContractNoResource(contractNo), async () =>
            {
                var id = await _context.Contracts.AsNoTracking()
                    .Where(x => x.ContractNo == contractNo).Select(x => (int?)x.ContractId).FirstOrDefaultAsync();
                var hasEvent = id.HasValue && await ContractCreatorEvents.ExistsAsync(_context, id.Value);
                return (Id: id, HasEvent: hasEvent);
            });

            if (!check.Verified)
                return new ContractCreateResult(false,
                    "نتیجه‌ی ساخت قرارداد نامطمئن است (درخواست Commit صادر شد و بررسی امن نتیجه ممکن نبود). قبل از تلاش مجدد، فهرست قراردادها را بررسی کنید؛ تلاش مجدد قرارداد تکراری نمی‌سازد.",
                    null, OutcomeUnknown: true, CreatorEvent: ContractCreatorEventStatus.OutcomeUnknown);

            var (foundId, eventExists) = check.Value;
            if (foundId.HasValue)
            {
                if (state.GeneratedContractIds.Contains(foundId.Value))
                {
                    var status = !creatorUserId.HasValue ? ContractCreatorEventStatus.NotApplicable
                        : eventExists ? ContractCreatorEventStatus.Recorded : ContractCreatorEventStatus.NotRecorded;
                    return new ContractCreateResult(true, null, foundId, Committed: true, CreatorEvent: status, RecoveredAfterUncertainCommit: true);
                }

                // قراردادی با همین شماره را درخواست/مسیر دیگری ساخته و Commit کرده است
                return new ContractCreateResult(false, $"قراردادی با شماره‌ی «{contractNo}» از قبل وجود دارد.", null);
            }

            // قفل گرفته شد (تراکنش اصلی پایان یافته) و قراردادی نیست ⇒ Commit نشده؛ این تنها حالتی است که پس از تلاش Commit «ساخته نشد» اثبات‌شده است
            return ex is ContractLockBusyException
                ? new ContractCreateResult(false, "این شماره‌ی قرارداد هم‌اکنون توسط عملیات دیگری در حال پردازش است؛ چند لحظه بعد دوباره تلاش کنید. هیچ تغییری ذخیره نشد.", null)
                : new ContractCreateResult(false, "ساخت قرارداد انجام نشد؛ هیچ تغییری ذخیره نشد. دوباره تلاش کنید.", null);
        }

        if (!state.Transactional && state.GeneratedContractIds.Count > 0)
        {
            int? foundId = null;
            var eventExists = false;
            try
            {
                foundId = await _context.Contracts.AsNoTracking()
                    .Where(x => x.ContractNo == contractNo).Select(x => (int?)x.ContractId).FirstOrDefaultAsync();
                if (foundId.HasValue) eventExists = await ContractCreatorEvents.ExistsAsync(_context, foundId.Value);
            }
            catch (Exception verifyEx) when (verifyEx is not OperationCanceledException)
            {
                _logger?.LogError(verifyEx, "Could not verify the manual contract-creation outcome for {ContractNo} after an error (non-transactional provider).", contractNo);
                return new ContractCreateResult(false,
                    "نتیجه‌ی ساخت قرارداد نامطمئن است (Provider بدون تراکنش و بررسی بعدی هم ممکن نبود). قبل از تلاش مجدد، فهرست قراردادها را بررسی کنید.",
                    null, OutcomeUnknown: true, CreatorEvent: ContractCreatorEventStatus.OutcomeUnknown);
            }

            if (foundId.HasValue)
            {
                if (state.GeneratedContractIds.Contains(foundId.Value))
                {
                    var status = !creatorUserId.HasValue ? ContractCreatorEventStatus.NotApplicable
                        : eventExists ? ContractCreatorEventStatus.Recorded : ContractCreatorEventStatus.NotRecorded;
                    return new ContractCreateResult(true, null, foundId, Committed: true, CreatorEvent: status);
                }

                return new ContractCreateResult(false, $"قراردادی با شماره‌ی «{contractNo}» از قبل وجود دارد.", null);
            }
        }

        if (ex is ContractLockBusyException)
            return new ContractCreateResult(false,
                "این شماره‌ی قرارداد هم‌اکنون توسط عملیات دیگری در حال پردازش است؛ چند لحظه بعد دوباره تلاش کنید. هیچ تغییری ذخیره نشد.", null);
        if (ex is DbUpdateException or Microsoft.EntityFrameworkCore.Storage.RetryLimitExceededException)
            return new ContractCreateResult(false, "ساخت قرارداد انجام نشد؛ هیچ تغییری ذخیره نشد. ورودی‌ها (پروژه/فروشنده) را بررسی و دوباره تلاش کنید.", null);

        // خطای پیش‌بینی‌نشده‌ی بدون هیچ درخواست Commit: همان استثنا (با Stack اصلی) بالا می‌رود
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();
        throw ex;   // به‌دست نمی‌رسد؛ برای کامپایلر
    }

    public async Task ChangeStatusAsync(int contractId, string status)
    {
        var contract = await _context.Contracts.FirstOrDefaultAsync(x => x.ContractId == contractId);
        if (contract == null) return;
        contract.Status = status;
        await _context.SaveChangesAsync();
    }
}
