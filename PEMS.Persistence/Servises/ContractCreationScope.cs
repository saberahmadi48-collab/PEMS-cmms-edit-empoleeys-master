using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>قفل کاربردی در مهلت تعیین‌شده گرفته نشد؛ هیچ تغییری ذخیره نشده است (تراکنش Rollback می‌شود).</summary>
public sealed class ContractLockBusyException : Exception
{
    public ContractLockBusyException(string resource) : base($"Application lock '{resource}' could not be acquired within the timeout.") { }
}

/// <summary>
/// P0.3 / P0.4 - تراکنش صریح + Execution Strategy + قفل‌های کاربردیِ مسیرهای «ساخت قرارداد» (مسیر مناقصه و مسیر دستی)؛ از TenderService استخراج شد تا هر دو
/// مسیر دقیقاً یک پیاده‌سازی داشته باشند (الگوی PostingScope، با سه تفاوت: چند قفل، پاک‌سازی فقط مدخل‌های همین عملیات، ردیابی تلاش Commit).
/// قفل‌ها Exclusive با مالک Transaction هستند؛ ترتیب ثابت: مناقصه → شماره‌ی قرارداد → رویداد ایجادکننده (نام منابع: ContractCreationLocks).
/// روی Provider بدون تراکنش (InMemory) یا وقتی تراکنش بیرونی هست، فقط بدنه اجرا می‌شود (اتمیک‌بودن تضمین نمی‌شود).
/// ChangeTracker.Clear() هرگز استفاده نمی‌شود.
/// </summary>
public sealed class ContractCreationScope
{
    private readonly PEMSDbContext _context;
    private readonly ILogger? _logger;
    private readonly int _lockTimeoutMs;

    public ContractCreationScope(PEMSDbContext context, ILogger? logger, int lockTimeoutMs)
    {
        _context = context;
        _logger = logger;
        _lockTimeoutMs = lockTimeoutMs;
    }

    /// <summary>وضعیت یک عملیات (در تمام تلاش‌های Strategy مشترک است).</summary>
    public sealed class State
    {
        public int Attempts;
        /// <summary>حداقل یک تلاش به CommitAsync رسید (نتیجه‌اش ممکن است نامطمئن باشد).</summary>
        public bool CommitAttempted;
        /// <summary>روی SQL Server با تراکنش مالکِ همین عملیات اجرا شد؛ در غیر این صورت اتمیک‌بودن تضمین نمی‌شود.</summary>
        public bool Transactional;
        public bool EventWriteAttempted;
        /// <summary>قراردادی که مناقصه از قبل به آن متصل بود (مسیر تلاش مجدد/ترمیم)؛ برای طبقه‌بندی درست شکست‌های آن مسیر.</summary>
        public int? ExistingContractId;
        public HashSet<object> Baseline = new(ReferenceEqualityComparer.Instance);
        /// <summary>شناسه‌های قراردادی که ذخیره‌ی کسب‌وکار همین فراخوانی تولید کرد (حتی اگر بعداً Rollback شده باشد؛ شناسه‌ی Identity دوباره استفاده نمی‌شود).</summary>
        public HashSet<int> GeneratedContractIds = new();
    }

    public async Task<T> RunAsync<T>(State state, Func<Task<(T Value, bool Commit)>> body)
    {
        state.Baseline = _context.ChangeTracker.Entries().Select(e => (object)e.Entity).ToHashSet(ReferenceEqualityComparer.Instance);

        if (!_context.Database.IsSqlServer() || _context.Database.CurrentTransaction != null)
        {
            // Provider بدون تراکنش (InMemory) یا تراکنش بیرونی: فقط بدنه؛ تراکنش بیرونی (اگر هست) مسئول Commit/Rollback است
            state.Attempts = 1;
            try
            {
                var direct = await body();
                if (!direct.Commit) await DiscardOperationEntriesAsync(state);
                return direct.Value;
            }
            catch
            {
                await DiscardOperationEntriesAsync(state);
                throw;
            }
        }

        state.Transactional = true;
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            state.Attempts++;
            try
            {
                await using var tx = await _context.Database.BeginTransactionAsync();
                var (value, commit) = await body();
                if (commit)
                {
                    state.CommitAttempted = true;
                    await tx.CommitAsync();
                }
                else
                {
                    await DiscardOperationEntriesAsync(state);
                }
                return value;
            }
            catch
            {
                // Rollback انجام شده (یا Commit نامطمئن)؛ مدخل‌های ردیابی‌شده‌ی این تلاش دیگر با دیتابیس هم‌خوان نیستند
                await DiscardOperationEntriesAsync(state);
                throw;
            }
        });
    }

    /// <summary>
    /// P0.5 - نتیجه‌ی خواندن تأییدی زیر قفل. Verified=false یعنی قفل در مهلت گرفته نشد یا خواندن شکست خورد؛ در این حالت هیچ نتیجه‌گیری ممنوع است.
    /// </summary>
    public readonly record struct LockedRead<T>(bool Verified, T? Value);

    /// <summary>
    /// P0.5 / H-1 - خواندن تأییدیِ امن برای پس از «تلاش Commit». روی دیتابیس با READ_COMMITTED_SNAPSHOT=ON یک SELECT ساده هرگز پشت یک Commit در حال انجام
    /// منتظر نمی‌ماند و نسخه‌ی قبل از Commit را می‌بیند؛ پس «چیزی پیدا نشد» دلیل «Commit نشد» نیست. اینجا ابتدا همان قفل کاربردی که تراکنش اصلی از ابتدای
    /// خود تا پایانش (Commit یا Rollback یا مرگ نشست) نگه می‌داشت گرفته می‌شود: گرفتن قفل ثابت می‌کند تراکنش اصلی تمام شده است (قفل Transaction-owned فقط با پایان
    /// تراکنش آزاد می‌شود)، و خواندنِ بعدی (Statement جدید، پس از گرفتن قفل) نتیجه‌ی نهایی را می‌بیند. اگر قفل در مهلت گرفته نشد (تراکنش اصلی هنوز باز است، مثلاً
    /// نشست نیمه‌مرده تا تشخیص سرور) یا خواندن شکست خورد، Verified=false برمی‌گردد و فراخواننده باید «نامشخص» گزارش کند، نه «ساخته نشد».
    /// روی Provider بدون تراکنش (InMemory) قفل لازم نیست و همان خواندن ساده است. رفتار قفل برای تراکنش واقعی در ContractOutcomeVerificationSqlTests اثبات شده است.
    /// </summary>
    public async Task<LockedRead<T>> TryReadUnderLockAsync<T>(string lockResource, Func<Task<T>> read)
    {
        var state = new State();
        try
        {
            var value = await RunAsync(state, async () =>
            {
                await AcquireLockAsync(lockResource);
                var v = await read();
                return (v, false);
            });
            return new LockedRead<T>(true, value);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "The lock-protected verification read of {Resource} could not be completed; the outcome stays unknown.", lockResource);
            return new LockedRead<T>(false, default);
        }
    }

    public async Task AcquireLockAsync(string resource)
    {
        if (!_context.Database.IsSqlServer() || _context.Database.CurrentTransaction == null) return;

        var granted = await _context.Database.SqlQueryRaw<int>(
            "DECLARE @r int; EXEC @r = sp_getapplock @Resource = {0}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = {1}; SELECT @r AS Value;",
            resource, _lockTimeoutMs).ToListAsync();
        if (granted.Count != 1 || granted[0] < 0)
            throw new ContractLockBusyException(resource);
    }

    /// <summary>
    /// فقط مدخل‌هایی که این عملیات به Context اضافه کرده (قرارداد، ردیف‌های AuditLog، مناقصه‌ی خوانده‌شده در همین عملیات) Detach می‌شوند؛ هر مدخل
    /// قبلاً-ردیابی‌شده دست نمی‌خورد، مگر مناقصه/پیشنهاد/بسته‌ای که پیش از عملیات ردیابی شده بود: آن‌ها از دیتابیس بازخوانی می‌شوند تا مقدار
    /// Rollback‌شده نماند. ChangeTracker.Clear() عمداً استفاده نمی‌شود.
    /// </summary>
    private async Task DiscardOperationEntriesAsync(State state)
    {
        foreach (var entry in _context.ChangeTracker.Entries().ToList())
        {
            if (!state.Baseline.Contains(entry.Entity))
                entry.State = EntityState.Detached;
        }

        await ReloadPreTrackedAsync(state);

        // مناقصه‌ی قبلاً-ردیابی‌شده هنوز می‌تواند به نمونه‌ی Detach‌شده‌ی قرارداد اشاره کند؛ DetectChanges آن نمونه را دوباره Added می‌کرد.
        // اگر دیتابیس می‌گوید قراردادی متصل نیست (Rollback)، مرجع پاک می‌شود؛ اگر متصل است (Commit بدون تراکنش)، مناقصه Detach می‌شود تا
        // گراف نیمه‌هماهنگ بعداً تغییری ننویسد (فراخواننده در صورت نیاز دوباره می‌خواند).
        foreach (var entry in _context.ChangeTracker.Entries<Tender>().Where(e => state.Baseline.Contains(e.Entity)).ToList())
        {
            var tender = entry.Entity;
            if (tender.Contract == null || _context.Entry(tender.Contract).State != EntityState.Detached) continue;

            if (tender.ContractId == null)
            {
                tender.Contract = null;
                entry.State = EntityState.Unchanged;
            }
            else
            {
                entry.State = EntityState.Detached;
            }
        }
    }

    public async Task ReloadPreTrackedAsync(State state)
    {
        foreach (var entry in _context.ChangeTracker.Entries()
                     .Where(e => state.Baseline.Contains(e.Entity) && e.State != EntityState.Added
                                 && (e.Entity is Tender || e.Entity is Bid || e.Entity is ProcurementPackage)).ToList())
        {
            try { await entry.ReloadAsync(); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogWarning(ex, "Could not reload pre-tracked {EntityType}; detaching it.", entry.Entity.GetType().Name);
                entry.State = EntityState.Detached;
            }
        }
    }
}
