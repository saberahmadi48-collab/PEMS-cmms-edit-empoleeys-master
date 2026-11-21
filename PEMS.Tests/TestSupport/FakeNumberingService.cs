using PEMS.Application.Interfaces;

namespace PEMS.Tests.TestSupport;

/// <summary>
/// جایگزین سبک INumberingService برای تست‌های InMemory. NumberingService واقعی از یک تراکنش
/// صریح روی "UPDATE ... OUTPUT" خام T-SQL استفاده می‌کند که InMemory اصلاً پشتیبانی نمی‌کند
/// (طبق مستندات خودِ PemsDbContextFactory.CreateInMemory) - تست‌های منطق کسب‌وکاری که فقط به
/// یک شماره‌ی قطعی/منحصربه‌فرد نیاز دارند (نه به اثبات اتمیک‌بودن خودِ تخصیص، که
/// IrnNumberingRealSqlTests و مشابه‌هایش با SQL Server واقعی پوشش می‌دهند) از این استفاده می‌کنند.
/// </summary>
public class FakeNumberingService : INumberingService
{
    private int _counter;

    public Task<string> GetNextNumberAsync(string documentType, string? scopeKey = null)
        => Task.FromResult($"{documentType}-{++_counter:0000}");

    private readonly Dictionary<string, int> _scoped = new(StringComparer.OrdinalIgnoreCase);

    public Task<int> GetNextValueAsync(string documentType, string scopeKey)
    {
        var key = documentType + "|" + scopeKey;
        _scoped[key] = _scoped.TryGetValue(key, out var v) ? v + 1 : 1;
        return Task.FromResult(_scoped[key]);
    }
}
