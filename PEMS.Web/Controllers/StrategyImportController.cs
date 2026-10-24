using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Localization;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Web.Resources;
using PEMS.Web.Security;
using PEMS.Web.ViewModels;

namespace PEMS.Web.Controllers;

/// <summary>
/// ورود Excel الگوهای استراتژی و تخصیص تگ‌ها - دو مرحله با تایید انسانی: Preview (فقط اعتبارسنجی؛ فایل با توکن
/// موقت نگه داشته می‌شود) و سپس Commit صریح. استراتژی‌های واردشده Draft‌اند و باید از گردش‌کار تایید بگذرند.
/// مجوز: MaintenanceStrategy/Create (Import الگو) و Approve (ثبت تخصیص تگ‌ها). فایل حداکثر ۵ مگابایت و فقط xlsx.
/// </summary>
public class StrategyImportController : Controller
{
    private const string Module = "MaintenanceStrategy";
    private const long MaxBytes = 5 * 1024 * 1024;
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(30);

    private readonly PEMSDbContext _context;
    private readonly IStrategyImportService _import;
    private readonly IMemoryCache _cache;
    private readonly ICurrentUserService _currentUser;
    private readonly IPermissionService _permissionService;
    private readonly IStringLocalizer<SharedResource>? _l;

    public StrategyImportController(PEMSDbContext context, IStrategyImportService import, IMemoryCache cache,
        ICurrentUserService currentUser, IPermissionService permissionService, IStringLocalizer<SharedResource>? localizer = null)
    {
        _context = context;
        _import = import;
        _cache = cache;
        _currentUser = currentUser;
        _permissionService = permissionService;
        _l = localizer;
    }

    private Task<bool> Can(string kind, string action) =>
        _permissionService.HasPermissionAsync(_currentUser.Roles, Module, kind == "assignments" ? "Approve" : action);

    private sealed record Staged(string Kind, byte[] Bytes, int? UserId, StrategyImportResultDto? Result = null);

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (!await _permissionService.HasPermissionAsync(_currentUser.Roles, Module, "Create")) return Forbid();
        ViewData["Title"] = "Import Strategies";
        ViewData["Section"] = "CMMS Integration";
        return View(new StrategyImportViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxBytes)]
    public async Task<IActionResult> Preview(string kind, IFormFile? file)
    {
        kind = Normalize(kind);
        if (!await Can(kind, "Create")) return Forbid();

        if (file == null || file.Length == 0 || file.Length > MaxBytes ||
            !string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            TempData["Error"] = T("Rcm_Err_ImportFileInvalid");
            return await ViewAsync(new StrategyImportViewModel { Kind = kind });
        }

        var bytes = new byte[file.Length];
        await using (var s = file.OpenReadStream()) { await s.ReadExactlyAsync(bytes); }

        var actor = await _context.ResolveStrategyActorAsync(_currentUser);
        using var ms = new MemoryStream(bytes);
        var result = kind == "assignments"
            ? await _import.ImportAssignmentsAsync(ms, commit: false, actor)
            : await _import.ImportStrategiesAsync(ms, commit: false, actor);

        var token = Guid.NewGuid().ToString("N");
        _cache.Set(CacheKey(token), new Staged(kind, bytes, _currentUser.UserId), TokenLifetime);

        return await ViewAsync(new StrategyImportViewModel { Kind = kind, Token = token, Result = result, Messages = Messages(result) });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Commit(string token, string kind)
    {
        kind = Normalize(kind);
        if (!await Can(kind, "Create")) return Forbid();

        if (!_cache.TryGetValue<Staged>(CacheKey(token), out var staged) || staged == null
            || staged.Kind != kind || staged.UserId != _currentUser.UserId)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            TempData["Error"] = T("Rcm_Err_ImportTokenExpired");
            return await ViewAsync(new StrategyImportViewModel { Kind = kind });
        }

        var actor = await _context.ResolveStrategyActorAsync(_currentUser);
        using var ms = new MemoryStream(staged.Bytes);
        var result = kind == "assignments"
            ? await _import.ImportAssignmentsAsync(ms, commit: true, actor)
            : await _import.ImportStrategiesAsync(ms, commit: true, actor);

        _cache.Remove(CacheKey(token));
        // گزارش خطای نتیجه‌ی ثبت نیز قابل دریافت است
        var newToken = Guid.NewGuid().ToString("N");
        _cache.Set(CacheKey(newToken), staged with { Result = result }, TokenLifetime);

        TempData["Message"] = T("Rcm_Info_ImportDone", result.RowsAccepted, result.RowsRejected);
        return await ViewAsync(new StrategyImportViewModel { Kind = kind, Token = newToken, Result = result, Messages = Messages(result) });
    }

    [HttpGet]
    public async Task<IActionResult> Template(string kind)
    {
        kind = Normalize(kind);
        if (!await Can(kind, "Create")) return Forbid();
        var bytes = kind == "assignments" ? _import.BuildAssignmentTemplate() : _import.BuildStrategyTemplate();
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"rcm-{kind}-template.xlsx");
    }

    [HttpGet]
    public async Task<IActionResult> Errors(string token, string kind)
    {
        kind = Normalize(kind);
        if (!await Can(kind, "Create")) return Forbid();
        if (!_cache.TryGetValue<Staged>(CacheKey(token), out var staged) || staged == null || staged.UserId != _currentUser.UserId)
            return NotFound();

        var result = staged.Result;
        if (result == null)
        {
            var actor = await _context.ResolveStrategyActorAsync(_currentUser);
            using var ms = new MemoryStream(staged.Bytes);
            result = kind == "assignments"
                ? await _import.ImportAssignmentsAsync(ms, commit: false, actor)
                : await _import.ImportStrategiesAsync(ms, commit: false, actor);
        }
        var report = _import.BuildErrorReport(result, e => _l.Translate(e));
        return File(report, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"rcm-{kind}-errors.xlsx");
    }

    private async Task<IActionResult> ViewAsync(StrategyImportViewModel vm)
    {
        ViewData["Title"] = "Import Strategies";
        ViewData["Section"] = "CMMS Integration";
        await Task.CompletedTask;
        return View("Index", vm);
    }

    private Dictionary<int, List<string>> Messages(StrategyImportResultDto r)
        => r.Rows.ToDictionary(x => x.RowNumber, x => _l.TranslateAll(x.Errors));

    private static string Normalize(string? kind) => string.Equals(kind, "assignments", StringComparison.OrdinalIgnoreCase) ? "assignments" : "strategies";
    private string CacheKey(string token) => "rcm-import:" + token;
    private string T(string key, params object[] args) => _l == null ? key : _l[key, args].Value;
}
