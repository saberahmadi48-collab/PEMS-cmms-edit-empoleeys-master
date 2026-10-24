using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>
/// طبق CMMS Production Baseline v1 بند P0-04: افزایش شماره با یک UPDATE...OUTPUT اتمیک روی
/// دیتابیس انجام می‌شود - نه با خواندن Count() در حافظه و نوشتن دوباره (که چون EnableRetryOnFailure
/// روشن است، مثل الگوی موجود در MDRImportService.CommitBatchAsync داخل یک تراکنش صریح با
/// CreateExecutionStrategy اجرا می‌شود تا در صورت Retry، عملیات غیر-Idempotent دوبار اجرا نشود).
/// </summary>
public class NumberingService : INumberingService
{
    private readonly PEMSDbContext _context;

    private sealed record Config(string Prefix, int PadWidth, bool IncludeScope, Func<string> DefaultScope);

    // پیش‌فرض‌ها عمداً با فرمت‌های زنده‌ی فعلی هم‌خوان هستند تا شماره‌های قدیمی و جدید ناهماهنگ
    // به‌نظر نرسند. تنها استثنا StoreRequisition است (قبلاً پسوند GUID، حالا ترتیبی سالانه) -
    // چون این فقط روی رکوردهای جدید اثر می‌گذارد و سابقه‌ی موجود دست‌نخورده می‌ماند، بی‌خطر است.
    // PlanNumber عمداً "PM" مانده، نه "MP" پیشنهادی سند Baseline - چون داده‌ی زنده هم‌اکنون با
    // "PM" تولید شده و تغییر آن فقط ناهماهنگی ظاهری بین شماره‌های قدیم و جدید ایجاد می‌کرد.
    private static readonly Dictionary<string, Config> Defaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WorkOrder"] = new("WO", 4, true, () => DateTime.Now.Year.ToString()),
        ["MaintenancePlan"] = new("PM", 3, true, () => "GEN"),
        ["StoreRequisition"] = new("SR", 4, true, () => DateTime.Now.Year.ToString()),
        // طبق CMMS-02: Job Plan یک الگوی قابل استفاده‌ی مجدد است، نه یک سند سالانه - بدون
        // بخش Scope در فرمت شماره (IncludeScope=false)، مثل JP-0001.
        ["JobPlan"] = new("JP", 4, false, () => "GLOBAL"),
        // طبق CMMS-03 بند P0-05: Work Request سالانه، هم‌الگو با WorkOrder.
        ["WorkRequest"] = new("WR", 4, true, () => DateTime.Now.Year.ToString()),
        // Warehouse / Part Catalogue Request - سالانه، مثل WorkRequest (PCR-2026-0001)
        ["PartCatalogueRequest"] = new("PCR", 4, true, () => DateTime.Now.Year.ToString()),
        // طبق CMMS-10a: Permit to Work سالانه، هم‌الگو با WorkOrder/WorkRequest.
        ["Permit"] = new("PTW", 4, true, () => DateTime.Now.Year.ToString()),
        // طبق Phase E-01 (رفع P0 ممیزی شماره‌گذاری مدارک مهندسی): این‌جا فقط برای گرفتن توالی
        // اتمیک به‌ازای هر Scope (Project+Discipline+DocumentType) استفاده می‌شود - DocumentNumberingService
        // خودِ رشته‌ی نمایشی نهایی را از روی کدهای پروژه/دیسیپلین/نوع می‌سازد، نه از این Prefix.
        // IncludeScope=false چون Scope واقعی از قبل داخل ScopeKey کدگذاری شده، نه در متن شماره.
        ["EngineeringDocument"] = new("ENG", 4, false, () => ""),
        // طبق «VENDOR DOCUMENTATION MANAGEMENT PROCEDURE» بخش ۳: فقط بخش سریال (NNNN) از این‌جا
        // گرفته می‌شود - رشته‌ی نمایشی کامل (TV-X-UU-AAAA-NNNN) را TransmittalNumberingService
        // می‌سازد، دقیقاً مثل الگوی EngineeringDocument/DocumentNumberingService.
        ["Transmittal"] = new("TV", 4, false, () => ""),
        // طبق بخش ۴.۳.۱ همان سند: فقط بخش سریال ۳-رقمی از این‌جا گرفته می‌شود -
        // VdrDocumentNumberingService رشتهٔ کامل CONTRACTOR (J-85-XXX-ME-DSH-0611-0001-001-0) را می‌سازد.
        ["VdrDocument"] = new("VD", 3, false, () => ""),
        // طبق بخش ۴.۳.۲ همان سند: سریال ۳-رقمی مستقل از فرمت CONTRACTOR بالا -
        // VdrDocumentNumberingService.GenerateVendorNumberAsync رشتهٔ کامل SUB-CONTRACTOR/Vendor
        // (3863-XX-VD-TT-UU-MMMM-NNNNNN-YYY) را می‌سازد.
        ["VdrVendorDocument"] = new("VD", 3, false, () => ""),
        // طبق «PEMS Operations & Production Architecture Spec v1» فاز ۱ (بند ۲۶ - Numbering):
        // سه نوع سند جدید، هم‌الگو با WorkOrder (سالانه) برای Order، Scope به‌ازای هر سفارش برای
        // Run (شماره‌ی اجرا زیرمجموعه‌ی همان سفارش معنی دارد)، و سالانه با پدینگ بزرگ‌تر برای
        // Batch (حجم بالاتر انتظار می‌رود).
        ["ProductionOrder"] = new("PRO", 4, true, () => DateTime.Now.Year.ToString()),
        ["ProductionRun"] = new("PRN", 3, true, () => "GEN"),
        ["ProductionBatch"] = new("PB", 5, true, () => DateTime.Now.Year.ToString()),
        // طبق ممیزی Material Management (Gap A) + Step 2: هم‌الگو با WorkOrder/StoreRequisition
        // (سالانه) - نه یک الگوی جدید اختراعی.
        ["IRN"] = new("IRN", 4, true, () => DateTime.Now.Year.ToString()),
        // طبق ممیزی Package Procurement Contract Finalization (§C) - هم‌الگو با بالا (سالانه)
        ["Tender"] = new("TND", 4, true, () => DateTime.Now.Year.ToString()),
        ["Bid"] = new("BID", 4, true, () => DateTime.Now.Year.ToString()),
        // عمداً «NTP» نه «IRN» - طبق §K ریسک ۹ ممیزی: پرهیز از برخورد نام با
        // IrnReceipt (Incoming Receipt Note) موجود در ماژول Material Management
        ["NTP"] = new("NTP", 4, true, () => DateTime.Now.Year.ToString()),
        // Task Manager: شمارنده به‌ازای هر فضا (ScopeKey = کلید فضا)؛ قالب کلید «{فضا}-{n}» را خود ماژول می‌سازد (GetNextValueAsync)
        ["WorkItem"] = new("WI", 1, true, () => throw new InvalidOperationException("WorkItem numbering requires the workspace key as scope.")),
    };

    public async Task<int> GetNextValueAsync(string documentType, string scopeKey)
    {
        if (string.IsNullOrWhiteSpace(scopeKey)) throw new ArgumentException("A scope key is required.", nameof(scopeKey));
        var formatted = await GetNextNumberAsync(documentType, scopeKey);
        // قالب ثابت Prefix-Scope-Value؛ مقدار آخرین بخش است (کلید فضا خط تیره ندارد - اعتبارسنجی WorkSpace.Key)
        return int.Parse(formatted[(formatted.LastIndexOf('-') + 1)..], System.Globalization.CultureInfo.InvariantCulture);
    }

    public NumberingService(PEMSDbContext context)
    {
        _context = context;
    }

    public async Task<string> GetNextNumberAsync(string documentType, string? scopeKey = null)
    {
        if (!Defaults.TryGetValue(documentType, out var config))
            throw new InvalidOperationException($"نوع سند '{documentType}' برای NumberingService تعریف نشده است.");

        var scope = string.IsNullOrWhiteSpace(scopeKey) ? config.DefaultScope() : scopeKey;

        int value;
        if (_context.Database.CurrentTransaction != null)
        {
            // A7 (بدون حفره): فراخوان یک تراکنش بازِ مشترک با ثبت خودِ سند دارد؛ افزایش شمارنده در همان تراکنش انجام می‌شود، پس اگر ثبت سند شکست بخورد
            // (Rollback) شماره هم برمی‌گردد و حفره‌ای ایجاد نمی‌شود. ردیف شمارنده تا Commit قفل می‌ماند، پس ثبت‌های هم‌زمان سریالی و بدون تکرار هستند.
            // (فراخوان باید خودش داخل CreateExecutionStrategy().ExecuteAsync باشد؛ اینجا Retry/Commit نمی‌کنیم.)
            value = await IncrementAsync(documentType, scope, config, ambient: true);
        }
        else
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            value = await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    var next = await IncrementAsync(documentType, scope, config);
                    await transaction.CommitAsync();
                    return next;
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });
        }

        var padded = value.ToString().PadLeft(config.PadWidth, '0');

        return config.IncludeScope
            ? $"{config.Prefix}-{scope}-{padded}"
            : $"{config.Prefix}-{padded}";
    }

    private async Task<int> IncrementAsync(string documentType, string scope, Config config, bool ambient = false)
    {
        const int maxAttempts = 3;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var updated = await _context.Database.SqlQueryRaw<int>(
                "UPDATE NumberingSequences SET CurrentValue = CurrentValue + 1 " +
                "OUTPUT INSERTED.CurrentValue " +
                "WHERE DocumentType = {0} AND ScopeKey = {1}",
                documentType, scope).ToListAsync();

            if (updated.Count > 0)
                return updated[0];

            // ردیف هنوز برای این (DocumentType, ScopeKey) ساخته نشده - اولین استفاده.
            try
            {
                _context.NumberingSequences.Add(new NumberingSequence
                {
                    DocumentType = documentType,
                    ScopeKey = scope,
                    CurrentValue = 1,
                    Prefix = config.Prefix,
                    PadWidth = config.PadWidth,
                    IncludeScopeInFormat = config.IncludeScope,
                    CreatedDate = DateTime.Now
                });

                await _context.SaveChangesAsync();
                return 1;
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex) && attempt < maxAttempts && !ambient)
            {
                // هم‌زمان یک درخواست دیگر همین ردیف را ساخت؛ دور بعد UPDATE موفق می‌شود.
                // (در تراکنش مشترک فراخوان، Clear() موجودیت‌های فراخوان را هم می‌پراند؛ آنجا خطا بالا می‌رود و فراخوان کل تراکنش را دوباره اجرا می‌کند.)
                _context.ChangeTracker.Clear();
            }
        }

        throw new InvalidOperationException($"شماره‌گذاری برای {documentType}/{scope} پس از {maxAttempts} تلاش ناموفق بود.");
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        return ex.InnerException is Microsoft.Data.SqlClient.SqlException sqlEx
            && (sqlEx.Number == 2601 || sqlEx.Number == 2627);
    }
}
