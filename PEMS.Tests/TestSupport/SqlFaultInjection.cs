using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using PEMS.Persistence.Context;

namespace PEMS.Tests.TestSupport;

/// <summary>خطای گذرای «تزریق‌شده»؛ فقط <see cref="TestRetryingStrategy"/> آن را Retry می‌کند. این یک شبیه‌سازی است، نه قطع واقعی شبکه.</summary>
public sealed class InjectedTransientException : Exception
{
    public InjectedTransientException(string message) : base(message) { }
}

/// <summary>
/// Execution Strategy آزمایشی: همان SqlServerRetryingExecutionStrategy تولید (با تعداد Retry کم و تأخیر ناچیز) که خطای گذرای تزریق‌شده را هم گذرا می‌داند.
/// بدنه‌ی عملیات داخل strategy.ExecuteAsync دقیقاً همان کدی است که در تولید اجرا می‌شود.
/// </summary>
public sealed class TestRetryingStrategy : SqlServerRetryingExecutionStrategy
{
    public TestRetryingStrategy(ExecutionStrategyDependencies dependencies, int maxRetryCount = 3) : base(dependencies, maxRetryCount) { }

    protected override bool ShouldRetryOn(Exception exception) => exception is InjectedTransientException || base.ShouldRetryOn(exception);

    protected override TimeSpan? GetNextDelay(Exception lastException)
        => base.GetNextDelay(lastException) is null ? null : TimeSpan.FromMilliseconds(5);
}

/// <summary>وضعیت مشترک بین Interceptorهای یک سناریو (Commit نامطمئن).</summary>
public sealed class CommitProbe
{
    /// <summary>تعداد دفعاتی که باید «بعد از Commit واقعی» خطا پرتاب شود (تأیید Commit گم شده).</summary>
    public int ThrowAfterRealCommit;
    /// <summary>تعداد دفعاتی که باید «قبل از Commit واقعی» خطا پرتاب شود (Commit هرگز انجام نمی‌شود).</summary>
    public int ThrowBeforeRealCommit;
    /// <summary>بعد از اولین خطای Commit، این تعداد اجرای دستور خواندن با خطای گذرا شکست می‌خورد (تلاش‌های بعدی Strategy).</summary>
    public int TransientReadFailuresAfterCommitFailure;
    /// <summary>بعد از تمام شدن بودجه‌ی خطاهای گذرا، هر خواندنِ بعدی با خطای غیرگذرا شکست می‌خورد (بررسی تأییدی ناممکن).</summary>
    public bool FailReadsNonTransientAfterBudget;

    public int CommitCalls;            // تعداد CommitAsync هایی که EF واقعاً صدا زد (Committing)
    public int RealCommits;            // تعداد Commit واقعیِ انجام‌شده روی SQL Server (Committed)
    public bool CommitFailureTriggered;
    public int ReadFailuresInjected;
}

public sealed class CommitFaultInterceptor : DbTransactionInterceptor
{
    private readonly CommitProbe _probe;
    public CommitFaultInterceptor(CommitProbe probe) => _probe = probe;

    public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        _probe.CommitCalls++;
        if (_probe.ThrowBeforeRealCommit > 0)
        {
            _probe.ThrowBeforeRealCommit--;
            _probe.CommitFailureTriggered = true;
            throw new InjectedTransientException("injected: commit request failed before reaching the server");
        }
        return base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
    }

    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        _probe.RealCommits++;
        if (_probe.ThrowAfterRealCommit > 0)
        {
            _probe.ThrowAfterRealCommit--;
            _probe.CommitFailureTriggered = true;
            throw new InjectedTransientException("injected: the commit succeeded but its acknowledgement was lost (simulated)");
        }
        return base.TransactionCommittedAsync(transaction, eventData, cancellationToken);
    }
}

public sealed class ReadFaultInterceptor : DbCommandInterceptor
{
    private readonly CommitProbe _probe;
    public ReadFaultInterceptor(CommitProbe probe) => _probe = probe;

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (_probe.CommitFailureTriggered)
        {
            if (_probe.TransientReadFailuresAfterCommitFailure > 0)
            {
                _probe.TransientReadFailuresAfterCommitFailure--;
                _probe.ReadFailuresInjected++;
                throw new InjectedTransientException("injected: connection lost after the commit attempt");
            }
            if (_probe.FailReadsNonTransientAfterBudget)
            {
                _probe.ReadFailuresInjected++;
                throw new InvalidOperationException("injected: verification read unavailable");
            }
        }
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}

/// <summary>شکست در ذخیره‌ی فیزیکی n‌ام (شماره‌گذاری: ۱ = A داده‌ی کسب‌وکار، ۲ = B AuditLog عمومی، ۳ = C رویداد، ۴ = D AuditLog عمومی رویداد).</summary>
public sealed class NthSaveFaultInterceptor : SaveChangesInterceptor
{
    private Func<Exception>? _factory;
    public int Calls;
    public int FailOn;

    public void Arm(int failOn, Func<Exception>? factory = null)
    {
        Calls = 0;
        FailOn = failOn;
        _factory = factory ?? (() => new DbUpdateException("injected failure"));
    }

    public void Disarm() { Calls = 0; FailOn = 0; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Calls++;
        if (FailOn > 0 && Calls == FailOn) throw _factory!();
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}

/// <summary>Strategy بدون Retry: هر استثنا (از جمله SqlException خامِ قطع اتصال) بدون تغییر به فراخواننده می‌رسد.</summary>
public sealed class NoRetryStrategy : ExecutionStrategy
{
    public NoRetryStrategy(ExecutionStrategyDependencies dependencies) : base(dependencies, 0, TimeSpan.Zero) { }
    protected override bool ShouldRetryOn(Exception exception) => false;
}

/// <summary>Strategy آزمایشی که هر SqlException (مثل قطع واقعی اتصال) را گذرا می‌داند - فقط برای تست قطع اتصال واقعی روی دیتابیس یک‌بارمصرف.</summary>
public sealed class RetryAnySqlExceptionStrategy : SqlServerRetryingExecutionStrategy
{
    public RetryAnySqlExceptionStrategy(ExecutionStrategyDependencies dependencies, int maxRetryCount = 3) : base(dependencies, maxRetryCount) { }
    protected override bool ShouldRetryOn(Exception exception) => exception is Microsoft.Data.SqlClient.SqlException || base.ShouldRetryOn(exception);
    protected override TimeSpan? GetNextDelay(Exception lastException)
        => base.GetNextDelay(lastException) is null ? null : TimeSpan.FromMilliseconds(5);
}

/// <summary>
/// قطع «واقعی» اتصال: درست قبل از ارسال Commit، نشست SQL Server همین اتصال را با KILL می‌بندد (نه سرویس SQL؛ فقط یک نشست). حفاظ‌ها: نشست باید متعلق به
/// دیتابیس یک‌بارمصرفِ مورد انتظار باشد، کاربر-پردازش باشد و نشست خودِ اتصال مدیریتی نباشد؛ در غیر این صورت هیچ KILL‌ای اجرا نمی‌شود.
/// آنچه این تست واقعاً اجرا می‌کند: SqlException واقعی از SqlClient و Rollback سمت سرور. آنچه اجرا نمی‌کند: «Commit روی سرور انجام شد ولی تأییدش گم شد».
/// </summary>
public sealed class KillSessionAtCommitInterceptor : DbTransactionInterceptor
{
    private readonly string _masterConnectionString;
    private readonly string _expectedDatabase;
    public int KillBudget = 1;
    public int Kills;
    public string? LastCommitFailure;

    public KillSessionAtCommitInterceptor(string masterConnectionString, string expectedDatabase)
    {
        _masterConnectionString = masterConnectionString;
        _expectedDatabase = expectedDatabase;
    }

    public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        if (KillBudget > 0 && transaction is Microsoft.Data.SqlClient.SqlTransaction sqlTx && sqlTx.Connection is { } conn)
        {
            KillBudget--;
            await SessionKiller.KillAsync(_masterConnectionString, _expectedDatabase, conn.ServerProcessId, cancellationToken);
            Kills++;
        }
        return await base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
    }

    public override Task TransactionFailedAsync(DbTransaction transaction, TransactionErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        if (eventData.Action == "Commit")
            LastCommitFailure = eventData.Exception.GetType().FullName + (eventData.Exception is Microsoft.Data.SqlClient.SqlException se ? $" (number {se.Number})" : "");
        return base.TransactionFailedAsync(transaction, eventData, cancellationToken);
    }
}

/// <summary>
/// P0.5 - بستن یک نشست SQL Server با KILL، فقط وقتی نشست متعلق به دیتابیس یک‌بارمصرفِ مورد انتظار باشد، نشست کاربر باشد و نشست خودِ اتصال مدیریتی نباشد.
/// فقط یک نشست بسته می‌شود؛ سرویس SQL Server هرگز دست نمی‌خورد.
/// </summary>
public static class SessionKiller
{
    public static async Task KillAsync(string masterConnectionString, string expectedDatabase, int spid, CancellationToken cancellationToken = default)
    {
        await using var master = new Microsoft.Data.SqlClient.SqlConnection(masterConnectionString);
        await master.OpenAsync(cancellationToken);
        await using (var check = master.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(*) FROM sys.dm_exec_sessions WHERE session_id = @s AND is_user_process = 1 AND DB_NAME(database_id) = @d AND session_id <> @@SPID";
            check.Parameters.AddWithValue("@s", spid);
            check.Parameters.AddWithValue("@d", expectedDatabase);
            if ((int)(await check.ExecuteScalarAsync(cancellationToken))! != 1)
                throw new InvalidOperationException($"Refusing to KILL session {spid}: it does not belong to the expected disposable database.");
        }
        await using var kill = master.CreateCommand();
        kill.CommandText = $"KILL {spid}";      // spid یک عدد صحیح (SqlConnection.ServerProcessId) است؛ نه ورودی کاربر
        await kill.ExecuteNonQueryAsync(cancellationToken);
    }
}

/// <summary>
/// P0.5 - تراکنشِ «در حال انجام» روی یک نشست جدا که قفل کاربردی مشخصی را نگه می‌دارد (و اختیاراً یک قرارداد Commit‌نشده درج می‌کند). نقش «تراکنش اصلی که هنوز
/// تمام نشده» را بازی می‌کند: Commit می‌شود، با Rollback رها می‌شود، یا نشستش واقعاً با KILL بسته می‌شود. این یک نشست و قفل واقعی SQL Server است؛ اینکه
/// نماینده‌ی «تراکنش اصلی» است شبیه‌سازی است.
/// </summary>
public sealed class InFlightHolder : IAsyncDisposable
{
    private readonly PEMSDbContext _ctx;
    private readonly string _master;
    private readonly string _database;
    private Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? _tx;
    public int Spid { get; private set; }

    private InFlightHolder(PEMSDbContext ctx, string master, string database) { _ctx = ctx; _master = master; _database = database; }

    public static async Task<InFlightHolder> StartAsync(DisposableSqlDatabase db, string lockResource, PEMS.Domain.Entities.Contract? uncommittedContract = null)
    {
        // بدون Execution Strategy تلاش‌مجدد (پیش‌فرض SqlServer): تراکنش کاربر مستقیم مجاز است
        var options = new DbContextOptionsBuilder<PEMSDbContext>().UseSqlServer(db.ConnectionString).Options;
        var ctx = new PEMSDbContext(options);
        var master = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(db.ConnectionString) { InitialCatalog = "master" }.ConnectionString;
        var holder = new InFlightHolder(ctx, master, db.Name);
        holder._tx = await ctx.Database.BeginTransactionAsync();
        holder.Spid = ((Microsoft.Data.SqlClient.SqlConnection)ctx.Database.GetDbConnection()).ServerProcessId;
        var granted = await ctx.Database.SqlQueryRaw<int>(
            "DECLARE @r int; EXEC @r = sp_getapplock @Resource = {0}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 0; SELECT @r AS Value;",
            lockResource).ToListAsync();
        if (granted.Count != 1 || granted[0] < 0) throw new InvalidOperationException("The holder could not take the application lock.");
        if (uncommittedContract != null)
        {
            ctx.Contracts.Add(uncommittedContract);
            await ctx.SaveChangesAsync();          // درج Commit‌نشده، در تراکنش باز نگه داشته می‌شود
        }
        return holder;
    }

    public Task CommitAsync() => _tx!.CommitAsync();

    public Task KillSessionAsync() => SessionKiller.KillAsync(_master, _database, Spid);

    public async ValueTask DisposeAsync()
    {
        try { if (_tx != null) await _tx.DisposeAsync(); } catch { /* نشست ممکن است کشته شده باشد */ }
        try { await _ctx.DisposeAsync(); } catch { }
    }
}

/// <summary>
/// P0.5 - اولین دستور خواندنی که بعد از «شکست Commit» اجرا می‌شود (در این کد: قفل خواندن تأییدی) را یک‌بار معلق می‌کند و Callback را اجرا می‌کند؛ برای ساخت
/// وضعیت «تراکنش اصلی هنوز باز است» دقیقاً قبل از خواندن تأییدی.
/// </summary>
public sealed class FirstReaderAfterCommitFailureHook : DbCommandInterceptor
{
    private readonly CommitProbe _probe;
    private readonly Func<Task> _onFirst;
    private int _fired;
    public FirstReaderAfterCommitFailureHook(CommitProbe probe, Func<Task> onFirst) { _probe = probe; _onFirst = onFirst; }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (_probe.CommitFailureTriggered && Interlocked.Exchange(ref _fired, 1) == 0)
            await _onFirst();
        return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}
