using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

public class PurchaseOrderService : IPurchaseOrderService
{
    private readonly PEMSDbContext _context;

    private readonly INumberingService _numbering;

    // SCRUM-40: شماره‌گذاری اختیاری در سازنده تا تست‌های تک‌پارامتری سازگار بمانند؛ DI آن را می‌دهد
    public PurchaseOrderService(PEMSDbContext context, INumberingService? numbering = null)
    {
        _context = context;
        _numbering = numbering ?? new NumberingService(context);
    }

    public async Task<List<PurchaseOrderDto>> GetAllAsync()
    {
        return await _context.PurchaseOrders
            .Include(x => x.Project)
            .Include(x => x.Vendor)
            .Include(x => x.Contract)
            .OrderByDescending(x => x.CreatedDate)
            .Select(x => new PurchaseOrderDto
            {
                PoId = x.PoId,
                PoNumber = x.PoNumber,
                ProjectId = x.ProjectId,
                ProjectName = x.Project!.ProjectName,
                VendorId = x.VendorId,
                VendorName = x.Vendor!.Name,
                ContractId = x.ContractId,
                ContractNo = x.Contract != null ? x.Contract.ContractNo : null,
                Description = x.Description,
                Value = x.Value,
                Status = x.Status,
                VDRItemsCount = x.VDRItems.Count
            })
            .ToListAsync();
    }

    public async Task<PurchaseOrderDto?> GetByIdAsync(int id)
    {
        var list = await GetAllAsync();
        return list.FirstOrDefault(x => x.PoId == id);
    }

    public async Task<int> CreateAsync(string poNumber, int projectId, int vendorId, int? contractId,
        string? description, decimal? value)
    {
        // P0 / شناسه‌ها: شماره‌ی PO دستی بود، بدون اعتبارسنجی و بدون یکتایی (ایندکس یکتا نیازمند Migration جداگانه است و
        // این‌جا اضافه نشده). تا آن زمان، بررسی در لایه‌ی سرویس انجام می‌شود (بدون محافظ در برابر رقابت هم‌زمان دقیق).
        // SCRUM-40: شماره خودکار از الگوی مدیر (شماره‌ی واردشده فقط اگر الگو ورود دستی را مجاز کرده باشد). مسیر Import جداست و دست نمی‌خورد.
        var normalizedNumber = await _numbering.ResolveNumberAsync("PurchaseOrder", poNumber, projectId);
        if (normalizedNumber.Length == 0)
            throw new InvalidOperationException("شماره‌ی سفارش خرید الزامی است.");
        if (value.HasValue && value.Value < 0)
            throw new InvalidOperationException("مبلغ سفارش خرید نمی‌تواند منفی باشد.");

        var numberKey = normalizedNumber.ToLower();
        if (await _context.PurchaseOrders.AnyAsync(x => x.ProjectId == projectId && x.PoNumber.ToLower() == numberKey))
            throw new InvalidOperationException($"سفارش خریدی با شماره‌ی «{normalizedNumber}» در این پروژه از قبل وجود دارد.");

        poNumber = normalizedNumber;
        var po = new PurchaseOrder
        {
            PoNumber = poNumber,
            ProjectId = projectId,
            VendorId = vendorId,
            ContractId = contractId,
            Description = description,
            Value = value,
            // PO دستی با ثبت اولیه فقط Draft است؛ صدور (Issued) یک انتقال مجزا با مجوز Approve و SoD
            // است (IssueAsync). مسیر Import (VDRImportService / FindOrCreateByNumberAsync) عمداً
            // وضعیت قدیمی Issued را حفظ می‌کند چون شماره‌ی PO از یک سند صادرشده‌ی بیرونی می‌آید.
            Status = PurchaseOrderStatuses.Draft,
            CreatedDate = DateTime.Now
        };

        await _context.PurchaseOrders.AddAsync(po);
        await _context.SaveChangesAsync();

        return po.PoId;
    }

    public async Task IssueAsync(int poId)
    {
        var po = await _context.PurchaseOrders.FirstOrDefaultAsync(x => x.PoId == poId);
        if (po == null)
            throw new InvalidOperationException("Purchase Order یافت نشد.");

        if (!po.IsActive)
            throw new InvalidOperationException("Purchase Order غیرفعال است و قابل صدور نیست.");

        // SCRUM-470 (owner decision 2026-10-29): a PO is issued only after approval by the PO authority table
        // (Draft → Submitted → Approved → Issued); a Draft can no longer be issued directly.
        if (po.Status != PurchaseOrderStatuses.Approved)
            throw new InvalidOperationException(
                $"فقط Purchase Order تأییدشده (Approved) قابل صدور است (وضعیت فعلی: {po.Status}).");

        if (string.IsNullOrWhiteSpace(po.PoNumber))
            throw new InvalidOperationException("Purchase Order بدون شماره قابل صدور نیست.");

        po.Status = PurchaseOrderStatuses.Issued;
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // R6: a concurrent Issue changed the row first; a clear message instead of the generic error page
            _context.ChangeTracker.Clear();
            throw new InvalidOperationException("این Purchase Order هم‌زمان توسط کاربر دیگری تغییر کرد؛ صفحه را دوباره باز کنید.");
        }
    }

    // Import/Reconciliation-only (نگاه کنید به IPurchaseOrderService): ساخت PO با Issued بدون Approve/SoD است.
    public async Task<int> FindOrCreateByNumberAsync(string poNumber, int projectId, int vendorId)
    {
        var trimmed = poNumber.Trim();

        var existing = await _context.PurchaseOrders
            .FirstOrDefaultAsync(x => x.PoNumber.ToLower() == trimmed.ToLower() && x.ProjectId == projectId);

        if (existing != null)
            return existing.PoId;

        var po = new PurchaseOrder
        {
            PoNumber = trimmed,
            ProjectId = projectId,
            VendorId = vendorId,
            Status = PurchaseOrderStatuses.Issued, // مسیر Import: وضعیت قدیمی حفظ می‌شود (بند بالا)
            CreatedDate = DateTime.Now
        };

        await _context.PurchaseOrders.AddAsync(po);
        await _context.SaveChangesAsync();

        return po.PoId;
    }
}
