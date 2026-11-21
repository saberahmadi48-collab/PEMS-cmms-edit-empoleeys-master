using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using PEMS.Persistence.Context;

namespace PEMS.Tests.TestSupport;

/// <summary>
/// P0.3 - یک دیتابیس SQL Server یک‌بارمصرف با نام <c>PEMS_TEST_P03_&lt;12 hex&gt;</c> روی SQL Server Express «محلی» (همین ماشین). تمام Migrationهای واقعی
/// روی آن اعمال می‌شود (همان که روی PEMSDB اجرا می‌شود) و فقط همین دیتابیس دقیقاً با همین نام Drop می‌شود.
///
/// حفاظ‌ها (هرکدام شکست بخورد، پیش از هر دستور CREATE/DROP استثنا پرتاب می‌شود):
///  ۱) نام باید دقیقاً با ^PEMS_TEST_P03_[0-9A-F]{12}$ بخواند - هرگز PEMSDB یا هر دیتابیس غیرتستی.
///  ۲) Data Source رشته‌ی اتصال باید نمونه‌ی محلی SQLEXPRESS باشد (localhost / . / (local) / نام همین ماشین) و Integrated Security (بدون رمز در رشته).
///  ۳) نمونه‌ی متصل‌شده باید MachineName برابر نام همین ماشین و Edition = Express باشد (SERVERPROPERTY).
///  ۴) دیتابیسی با این نام نباید از قبل وجود داشته باشد (چیزی را که خودمان نساخته‌ایم Drop نمی‌کنیم).
/// P0.5: بعد از Migrate، READ_COMMITTED_SNAPSHOT=ON روی همین دیتابیس تنظیم و خوانده‌شده تأیید می‌شود (پیش‌فرض model سرور OFF است؛ PEMSDB ON است).
/// اگر نمونه‌ی امن قابل شناسایی نباشد، تست متوقف می‌شود (Skip نمی‌شود).
/// </summary>
public sealed class DisposableSqlDatabase : IAsyncDisposable
{
    public const string Prefix = "PEMS_TEST_P03_";
    private static readonly Regex NameRule = new("^PEMS_TEST_P03_[0-9A-F]{12}$", RegexOptions.CultureInvariant);

    private readonly bool _created;

    private static readonly object AuditGate = new();

    /// <summary>P0.5: اگر متغیر محیطی PEMS_TEST_DB_AUDIT_LOG تنظیم باشد، ساخت و حذف هر دیتابیس یک‌بارمصرف با نام دقیقش در آن فایل ثبت می‌شود (ردپای قابل‌ممیزی).</summary>
    private static void Audit(string action, string name, string? detail = null)
    {
        var path = Environment.GetEnvironmentVariable("PEMS_TEST_DB_AUDIT_LOG");
        if (string.IsNullOrWhiteSpace(path)) return;
        var tab = (char)9;
        try { lock (AuditGate) File.AppendAllText(path, $"{DateTime.Now:O}{tab}{action}{tab}{name}{tab}{detail}{Environment.NewLine}"); }
        catch { /* ممیزی هرگز تست را نمی‌شکند */ }
    }

    public string Name { get; }
    public string ConnectionString { get; }
    public string ServerName { get; }

    private DisposableSqlDatabase(string name, string connectionString, string serverName, bool created)
    {
        Name = name;
        ConnectionString = connectionString;
        ServerName = serverName;
        _created = created;
    }

    public static async Task<DisposableSqlDatabase> CreateAsync()
    {
        var name = Prefix + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        if (!NameRule.IsMatch(name)) throw new InvalidOperationException($"Refusing database name '{name}'.");

        var csb = new SqlConnectionStringBuilder(PemsDbContextFactory.ConnectionString);
        var host = csb.DataSource.Trim().ToLowerInvariant();
        var machine = Environment.MachineName.ToLowerInvariant();
        var allowedSources = new[] { "localhost\\sqlexpress", ".\\sqlexpress", "(local)\\sqlexpress", machine + "\\sqlexpress" };
        if (!allowedSources.Contains(host))
            throw new InvalidOperationException($"Refusing to run disposable-database tests: Data Source '{csb.DataSource}' is not the local SQLEXPRESS instance.");
        if (!csb.IntegratedSecurity)
            throw new InvalidOperationException("Refusing to run disposable-database tests: only Integrated Security connections are allowed.");

        var master = new SqlConnectionStringBuilder(csb.ConnectionString) { InitialCatalog = "master" }.ConnectionString;
        string serverName;
        await using (var conn = new SqlConnection(master))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT CAST(SERVERPROPERTY('MachineName') AS nvarchar(128)), CAST(SERVERPROPERTY('Edition') AS nvarchar(128)),
                                       CAST(SERVERPROPERTY('ServerName') AS nvarchar(128)), DB_ID(@n)";
            cmd.Parameters.AddWithValue("@n", name);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) throw new InvalidOperationException("Could not identify the SQL Server instance.");
            var serverMachine = r.GetString(0);
            var edition = r.GetString(1);
            serverName = r.GetString(2);
            if (!string.Equals(serverMachine, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing to run disposable-database tests: the SQL Server instance is not on this machine.");
            if (!edition.Contains("Express", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing to run disposable-database tests: the SQL Server instance is not an Express edition.");
            if (!r.IsDBNull(3))
                throw new InvalidOperationException($"Refusing to run: database '{name}' already exists.");
        }

        Audit("CREATE_REQUESTED", name, serverName);
        var dbConnection = new SqlConnectionStringBuilder(csb.ConnectionString) { InitialCatalog = name }.ConnectionString;
        var db = new DisposableSqlDatabase(name, dbConnection, serverName, created: true);
        try
        {
            await using (var ctx = db.NewContext())
            {
                await ctx.Database.MigrateAsync();      // تاریخچه‌ی Migration واقعی؛ نه EnsureCreated
                if (!string.Equals(ctx.Database.GetDbConnection().Database, name, StringComparison.Ordinal))
                    throw new InvalidOperationException("Unexpected database after migration.");
            }

            // P0.5: دیتابیس یک‌بارمصرف باید «صریحاً» READ_COMMITTED_SNAPSHOT=ON داشته باشد (مثل PEMSDB)، مستقل از تنظیم دیتابیس model سرور. فقط همین دیتابیسی که
            // خودمان ساخته‌ایم (نام با الگو اعتبارسنجی شده) تغییر می‌کند؛ اتصال‌های Pool این دیتابیس بسته می‌شوند و ROLLBACK IMMEDIATE فقط نشست‌های همین دیتابیس را می‌بندد.
            SqlConnection.ClearPool(new SqlConnection(dbConnection));
            await using (var conn = new SqlConnection(master))
            {
                await conn.OpenAsync();
                await using var alter = conn.CreateCommand();
                alter.CommandText = $"ALTER DATABASE [{name}] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE";
                await alter.ExecuteNonQueryAsync();
            }
            if (!await db.IsRcsiOnAsync())
                throw new InvalidOperationException($"Disposable database '{name}' does not have READ_COMMITTED_SNAPSHOT ON after setup.");
            Audit("CREATED", name, "migrations applied; READ_COMMITTED_SNAPSHOT=ON verified");
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
        return db;
    }

    /// <summary>Context با همان تنظیمات تولید (EnableRetryOnFailure) یا با Strategy آزمایشی؛ Interceptorها اختیاری.</summary>
    public PEMSDbContext NewContext(IEnumerable<IInterceptor>? interceptors = null, Func<ExecutionStrategyDependencies, IExecutionStrategy>? strategy = null)
    {
        var builder = new DbContextOptionsBuilder<PEMSDbContext>().UseSqlServer(ConnectionString, o =>
        {
            if (strategy != null) o.ExecutionStrategy(strategy);
            else o.EnableRetryOnFailure();
        });
        builder.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        if (interceptors != null) builder.AddInterceptors(interceptors);
        return new PEMSDbContext(builder.Options);
    }

    public async Task<List<T>> QueryAsync<T>(string sql, Func<SqlDataReader, T> map, params (string Name, object Value)[] parameters)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in parameters) cmd.Parameters.AddWithValue(n, v);
        var rows = new List<T>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) rows.Add(map(r));
        return rows;
    }

    /// <summary>مقدار واقعی READ_COMMITTED_SNAPSHOT این دیتابیس (از sys.databases).</summary>
    public async Task<bool> IsRcsiOnAsync()
        => (await QueryAsync("SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME()", r => r.GetBoolean(0))).Single();

    public async Task<int> ScalarIntAsync(string sql, params (string Name, object Value)[] parameters)
        => (await QueryAsync(sql, r => r.GetInt32(0), parameters)).Single();

    public async ValueTask DisposeAsync()
    {
        if (!_created) return;
        if (!NameRule.IsMatch(Name)) return;      // دفاع مضاعف: هرگز نامی غیر از الگو Drop نمی‌شود

        SqlConnection.ClearAllPools();
        var master = new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" }.ConnectionString;
        await using var c = new SqlConnection(master);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"IF DB_ID(@n) IS NOT NULL BEGIN ALTER DATABASE [{Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Name}]; END";
        cmd.Parameters.AddWithValue("@n", Name);
        await cmd.ExecuteNonQueryAsync();
        Audit("DROPPED", Name);
    }
}
