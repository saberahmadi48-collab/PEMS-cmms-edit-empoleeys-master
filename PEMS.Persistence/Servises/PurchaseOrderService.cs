using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

public class PurchaseOrderService : IPurchaseOrderService
{
    private readonly PEMSDbContext _context;

    public PurchaseOrderService(PEMSDbContext context)
    {
        _context = context;
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
        var normalizedNumber = poNumber?.Trim() ?? string.Empty;
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

        if (po.Status != PurchaseOrderStatuses.Draft)
            throw new InvalidOperationException(
                $"فقط Purchase Order در وضعیت Draft قابل صدور است (وضعیت فعلی: {po.Status}).");

        if (string.IsNullOrWhiteSpace(po.PoNumber))
            throw new InvalidOperationException("Purchase Order بدون شماره قابل صدور نیست.");

        po.Status = PurchaseOrderStatuses.Issued;
        await _context.SaveChangesAsync();
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
