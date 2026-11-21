using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Localization;
using PEMS.Application.Interfaces;
using PEMS.Web.Api;
using PEMS.Web.Api.V1;
using PEMS.Web.Resources;

namespace PEMS.Tests.Api;

/// <summary>In-memory discipline store with a row version that changes on every write (EF's in-memory provider does not generate one).</summary>
public sealed class FakeDisciplineService : IDisciplineMasterService
{
    private int _nextId = 1;
    private long _version = 1;
    public List<DisciplineDetail> Rows { get; } = new();
    public bool DeleteInUse { get; set; }
    public bool ThrowOnQuery { get; set; }

    private byte[] NextVersion() => BitConverter.GetBytes(_version++);

    public DisciplineDetail Seed(string code, string name, bool active = true)
    {
        var d = new DisciplineDetail(_nextId++, code, name, null, null, null, active, new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc), NextVersion());
        Rows.Add(d);
        return d;
    }

    public Task<DisciplineDetail?> GetAsync(int id, CancellationToken ct = default) => Task.FromResult(Rows.FirstOrDefault(r => r.DisciplineId == id));

    public Task<DisciplinePageResult> QueryAsync(DisciplineQuery q, CancellationToken ct = default)
    {
        if (ThrowOnQuery) throw new InvalidOperationException("boom: secret detail");
        IEnumerable<DisciplineDetail> rows = Rows;
        if (q.Q != null) rows = rows.Where(r => r.Name.Contains(q.Q, StringComparison.OrdinalIgnoreCase) || r.Code.Contains(q.Q, StringComparison.OrdinalIgnoreCase));
        if (q.IsActive is { } a) rows = rows.Where(r => r.IsActive == a);
        var list = rows.OrderBy(r => r.Name).ToList();
        return Task.FromResult(new DisciplinePageResult(list.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToList(), list.Count));
    }

    public Task<DisciplineOpResult> CreateAsync(DisciplineWrite w, CancellationToken ct = default)
    {
        if (Rows.Any(r => r.Code.Equals(w.Code, StringComparison.OrdinalIgnoreCase))) return Task.FromResult(DisciplineOpResult.Fail("Disc_Err_DuplicateCode"));
        if (w.Code.Length == 0 || w.Name.Length == 0) return Task.FromResult(DisciplineOpResult.Fail("Disc_Err_Required"));
        var d = new DisciplineDetail(_nextId++, w.Code.ToUpperInvariant(), w.Name, w.Description, w.ImportAliases, w.TransmittalCode, w.IsActive, DateTime.UtcNow, NextVersion());
        Rows.Add(d);
        return Task.FromResult(DisciplineOpResult.Ok(d));
    }

    public Task<DisciplineOpResult> ReplaceAsync(int id, DisciplineWrite w, byte[]? expected, CancellationToken ct = default)
    {
        var i = Rows.FindIndex(r => r.DisciplineId == id);
        if (i < 0) return Task.FromResult(DisciplineOpResult.Fail("Disc_Err_NotFound"));
        if (expected != null && !expected.SequenceEqual(Rows[i].RowVersion!)) return Task.FromResult(DisciplineOpResult.Fail(DisciplineErrorKeys.Concurrency));
        var d = Rows[i] with { Code = w.Code, Name = w.Name, Description = w.Description, ImportAliases = w.ImportAliases, TransmittalCode = w.TransmittalCode, IsActive = w.IsActive, RowVersion = NextVersion() };
        Rows[i] = d;
        return Task.FromResult(DisciplineOpResult.Ok(d));
    }

    public Task<DisciplineOpResult> DeleteAsync(int id, byte[]? expected, CancellationToken ct = default)
    {
        var i = Rows.FindIndex(r => r.DisciplineId == id);
        if (i < 0) return Task.FromResult(DisciplineOpResult.Fail("Disc_Err_NotFound"));
        if (DeleteInUse) return Task.FromResult(DisciplineOpResult.Fail(DisciplineErrorKeys.InUse));
        Rows.RemoveAt(i);
        return Task.FromResult(DisciplineOpResult.Ok());
    }

    public Task<List<DisciplineRow>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<DisciplineOpResult> CreateAsync(string code, string name, string? description, string? importAliases, string? transmittalCode, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<DisciplineOpResult> UpdateAsync(int disciplineId, string code, string name, string? description, string? importAliases, string? transmittalCode, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<DisciplineOpResult> SetActiveAsync(int disciplineId, bool isActive, CancellationToken ct = default) => throw new NotSupportedException();
}

public sealed class AllowAllAuthorization : IAuthorizationService
{
    public bool Allow { get; set; } = true;
    public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements) =>
        Task.FromResult(Allow ? AuthorizationResult.Success() : AuthorizationResult.Failed());
    public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName) =>
        Task.FromResult(Allow ? AuthorizationResult.Success() : AuthorizationResult.Failed());
}

public sealed class KeyLocalizer : IStringLocalizer<SharedResource>
{
    public LocalizedString this[string name] => new(name, name);
    public LocalizedString this[string name, params object[] arguments] => new(name, name);
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Array.Empty<LocalizedString>();
}

public static class ApiTestHost
{
    public static DisciplinesApiController Controller(FakeDisciplineService service, AllowAllAuthorization? authorization = null, ApiIdempotencyStore? store = null,
        Action<HttpRequest>? configure = null)
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "tester") }, "test")) };
        configure?.Invoke(http.Request);
        return new DisciplinesApiController(service, authorization ?? new AllowAllAuthorization(), store ?? new ApiIdempotencyStore(new MemoryCache(new MemoryCacheOptions())), new KeyLocalizer())
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
    }

    public static ApiProblemDetails Problem(IActionResult result, int status)
    {
        var o = Assert.IsType<ObjectResult>(result);
        Assert.Equal(status, o.StatusCode);
        Assert.Contains(ApiProblem.ContentType, o.ContentTypes);
        return Assert.IsType<ApiProblemDetails>(o.Value);
    }
}
