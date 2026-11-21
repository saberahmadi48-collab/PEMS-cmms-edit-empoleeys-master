using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>
/// طبق ممیزی Material Management (Gap A) + Step 2: پیاده‌سازی چرخه‌ی عمر IRN. طبق مرز صریح
/// Step 2، این سرویس عمداً هیچ InventoryTransaction/MaterialStock ای نمی‌سازد یا تغییر
/// نمی‌دهد - Submitted فقط یعنی «آماده‌ی بازرسی»، نه «موجودی Available شد» (آن تصمیم به
/// Step 3 تعلق دارد).
/// </summary>
public class IrnReceiptService : IIrnReceiptService
{
    private readonly PEMSDbContext _context;

    private static readonly HashSet<string> CancellableFromStatuses = new(StringComparer.Ordinal) { "Draft", "Submitted" };

    public IrnReceiptService(PEMSDbContext context)
    {
        _context = context;
    }

    public async Task<(bool Success, string? Error)> SubmitAsync(int irnReceiptId, string? reason, int? changedByEmployeeId)
    {
        var receipt = await _context.IrnReceipts
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.IrnReceiptId == irnReceiptId);

        if (receipt == null) return (false, "IRN پیدا نشد.");

        if (receipt.Status != "Draft")
            return (false, $"فقط IRN در وضعیت Draft قابل Submit است (وضعیت فعلی: {receipt.Status}).");

        if (!receipt.Lines.Any(x => x.ReceivedQuantity > 0))
            return (false, "IRN باید حداقل یک ردیف با مقدار دریافتی بزرگ‌تر از صفر داشته باشد.");

        var fromStatus = receipt.Status;
        receipt.Status = "Submitted";

        await _context.IrnReceiptStatusHistories.AddAsync(new IrnReceiptStatusHistory
        {
            IrnReceiptId = receipt.IrnReceiptId,
            FromStatus = fromStatus,
            ToStatus = "Submitted",
            ChangedById = changedByEmployeeId,
            ChangedDate = DateTime.Now,
            Reason = reason
        });

        await _context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> CancelAsync(int irnReceiptId, string? reason, int? changedByEmployeeId)
    {
        var receipt = await _context.IrnReceipts.FirstOrDefaultAsync(x => x.IrnReceiptId == irnReceiptId);
        if (receipt == null) return (false, "IRN پیدا نشد.");

        if (!CancellableFromStatuses.Contains(receipt.Status))
            return (false, $"IRN در وضعیت {receipt.Status} قابل لغو نیست.");

        var fromStatus = receipt.Status;
        receipt.Status = "Cancelled";

        await _context.IrnReceiptStatusHistories.AddAsync(new IrnReceiptStatusHistory
        {
            IrnReceiptId = receipt.IrnReceiptId,
            FromStatus = fromStatus,
            ToStatus = "Cancelled",
            ChangedById = changedByEmployeeId,
            ChangedDate = DateTime.Now,
            Reason = reason
        });

        await _context.SaveChangesAsync();
        return (true, null);
    }

    // طبق ممیزی Material Management (Gap B) + Step 3: بازرس مقدار Accepted/Rejected/Hold هر
    // خط را در یک اقدام واحد ثبت می‌کند (یک بازدید کامل روی کل رسید) - نه به‌مرور روی خطوط
    // جداگانه، تا هیچ خطی نیمه‌بررسی‌شده نماند. جمع سه مقدار باید دقیقاً برابر ReceivedQuantity
    // باشد. هیچ InventoryTransaction/MaterialStock ای این‌جا ساخته نمی‌شود - فقط بعد از تایید
    // واقعی QA (CompleteDispositionAsync).
    public async Task<(bool Success, string? Error, int? DispositionRecordId)> RecordDispositionAsync(
        int irnReceiptId,
        IReadOnlyList<(int LineId, decimal Accepted, decimal Rejected, decimal Hold)> lineDispositions,
        string? notes, int? dispositionedByEmployeeId)
    {
        var receipt = await _context.IrnReceipts
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.IrnReceiptId == irnReceiptId);

        if (receipt == null) return (false, "IRN پیدا نشد.", null);

        if (receipt.Status != "Submitted")
            return (false, $"فقط IRN در وضعیت Submitted قابل ثبت Disposition است (وضعیت فعلی: {receipt.Status}).", null);

        if (lineDispositions.Count != receipt.Lines.Count || receipt.Lines.Any(l => lineDispositions.All(d => d.LineId != l.IrnReceiptLineId)))
            return (false, "باید برای همه‌ی خطوط این IRN، Disposition ثبت شود.", null);

        foreach (var d in lineDispositions)
        {
            if (d.Accepted < 0 || d.Rejected < 0 || d.Hold < 0)
                return (false, "مقادیر Disposition نمی‌توانند منفی باشند.", null);

            var line = receipt.Lines.First(l => l.IrnReceiptLineId == d.LineId);
            if (d.Accepted + d.Rejected + d.Hold != line.ReceivedQuantity)
                return (false, $"جمع Accepted+Rejected+Hold برای خط {line.IrnReceiptLineId} باید دقیقاً برابر مقدار دریافتی ({line.ReceivedQuantity}) باشد.", null);
        }

        foreach (var d in lineDispositions)
        {
            var line = receipt.Lines.First(l => l.IrnReceiptLineId == d.LineId);
            line.AcceptedQuantity = d.Accepted;
            line.RejectedQuantity = d.Rejected;
            line.HoldQuantity = d.Hold;
        }

        var overallResult = ComputeOverallResult(receipt.Lines);

        var dispositionRecord = new IrnDispositionRecord
        {
            IrnReceiptId = receipt.IrnReceiptId,
            DispositionedById = dispositionedByEmployeeId ?? 0,
            DispositionedDate = DateTime.Now,
            OverallResult = overallResult,
            Notes = notes,
            WorkflowOutcome = "Pending",
            IsActive = true,
            CreatedDate = DateTime.Now
        };
        await _context.IrnDispositionRecords.AddAsync(dispositionRecord);

        var fromStatus = receipt.Status;
        receipt.Status = "PendingApproval";

        await _context.IrnReceiptStatusHistories.AddAsync(new IrnReceiptStatusHistory
        {
            IrnReceiptId = receipt.IrnReceiptId,
            FromStatus = fromStatus,
            ToStatus = "PendingApproval",
            ChangedById = dispositionedByEmployeeId,
            ChangedDate = DateTime.Now,
            Reason = notes
        });

        await _context.SaveChangesAsync();
        return (true, null, dispositionRecord.IrnDispositionRecordId);
    }

    // طبق ممیزی Material Management (Gap B) + Step 3 بخش ۹/۲۰: فقط این‌جا - بعد از تایید واقعی
    // QA - مقدار Accepted به InventoryTransaction (TransactionType="Acceptance") + افزایش
    // MaterialStock.OnHandQuantity تبدیل می‌شود، فقط برای همان مقدار. Rejected/Hold هرگز به
    // هیچ Transaction ای تبدیل نمی‌شوند - نه یک ستون Condition جدید روی MaterialStock، بلکه با
    // اینکه اصلاً چیزی برای آن‌ها Post نمی‌شود، اصل «Received ≠ Available» برقرار می‌ماند.
    // اتمیک: Disposition + Stock + Ledger همه در یک SaveChangesAsync (یک تراکنش) با هم Commit
    // می‌شوند. Idempotent: اگر IRN از قبل در وضعیت PendingApproval نباشد (مثلاً یک Retry بعد از
    // موفقیت قبلی)، بدون خطا و بدون Post دوباره، Success=false برمی‌گرداند.
    public async Task<(bool Success, string? Error)> CompleteDispositionAsync(int irnReceiptId, bool approved, int? actorEmployeeId)
    {
        var receipt = await _context.IrnReceipts
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.IrnReceiptId == irnReceiptId);

        if (receipt == null) return (false, "IRN پیدا نشد.");

        if (receipt.Status != "PendingApproval")
            return (false, $"این IRN در وضعیت انتظار تایید نیست (وضعیت فعلی: {receipt.Status}) - احتمالاً قبلاً پردازش شده.");

        var dispositionRecord = await _context.IrnDispositionRecords
            .Where(x => x.IrnReceiptId == irnReceiptId)
            .OrderByDescending(x => x.DispositionedDate)
            .FirstOrDefaultAsync();

        if (dispositionRecord == null) return (false, "رکورد Disposition این IRN پیدا نشد.");

        var fromStatus = receipt.Status;

        if (approved)
        {
            // Phase 2 / P4: ابتدا (قبل از هر تغییر) همه‌ی ردیف‌های موجودی لازم با کلید کامل (متریال، انبار، محل) پیدا یا - در صورت نبود -
            // به‌صورت ایدمپوتنت و هم‌زمانی-امن ساخته می‌شوند. ردیف‌های مبهم (چند ردیف برای یک کلید) ⇒ توقف بدون هیچ اثر؛ دو خط هم‌کلید در
            // یک IRN هم دیگر دو ردیف تکراری نمی‌سازند (قبلاً ردیفِ ساخته‌نشده‌ی هنوز ذخیره‌نشده پیدا نمی‌شد).
            var stocksByLine = new Dictionary<int, MaterialStock>();

            // گذر اول فقط‌خواندنی: هر ردیف مبهم، قبل از ساخت هر ردیف جدید برای خط‌های سالم، کل عملیات را متوقف می‌کند
            foreach (var line in receipt.Lines.Where(l => l.AcceptedQuantity is > 0))
            {
                var probe = await StockRowResolver.FindExactAsync(_context, line.MaterialItemId, receipt.WarehouseId, line.StorageLocationId, activeOnly: false);
                if (probe.IsAmbiguous)
                    return (false, StockRowResolver.AmbiguousMessage(line.MaterialItemId, receipt.WarehouseId, line.StorageLocationId, probe.CandidateIds));
            }

            foreach (var line in receipt.Lines.Where(l => l.AcceptedQuantity is > 0))
            {
                var (resolved, stockError) = await StockRowResolver.GetOrCreateAsync(_context, line.MaterialItemId, receipt.WarehouseId, line.StorageLocationId);
                if (resolved == null) return (false, stockError);
                stocksByLine[line.IrnReceiptLineId] = resolved;
            }

            foreach (var line in receipt.Lines.Where(l => l.AcceptedQuantity is > 0))
            {
                var stock = stocksByLine[line.IrnReceiptLineId];

                stock.OnHandQuantity += line.AcceptedQuantity!.Value;
                stock.LastUpdated = DateTime.Now;

                await _context.InventoryTransactions.AddAsync(new InventoryTransaction
                {
                    MaterialItemId = line.MaterialItemId,
                    WarehouseId = receipt.WarehouseId,
                    StorageLocationId = line.StorageLocationId,
                    TransactionType = "Acceptance",
                    Quantity = line.AcceptedQuantity.Value,
                    UomId = line.UomId,
                    ReferenceType = "IrnReceipt",
                    ReferenceId = receipt.IrnReceiptId,
                    EmployeeId = actorEmployeeId,
                    TransactionDate = DateTime.Now,
                    Reason = $"IRN {receipt.IrnNumber} - disposition accepted"
                });
            }

            receipt.Status = "Closed";
            dispositionRecord.WorkflowOutcome = "Approved";
        }
        else
        {
            foreach (var line in receipt.Lines)
            {
                line.AcceptedQuantity = null;
                line.RejectedQuantity = null;
                line.HoldQuantity = null;
            }

            receipt.Status = "Submitted";
            dispositionRecord.WorkflowOutcome = "Rejected";
        }

        await _context.IrnReceiptStatusHistories.AddAsync(new IrnReceiptStatusHistory
        {
            IrnReceiptId = receipt.IrnReceiptId,
            FromStatus = fromStatus,
            ToStatus = receipt.Status,
            ChangedById = actorEmployeeId,
            ChangedDate = DateTime.Now,
            Reason = approved ? "QA approved disposition" : "QA rejected disposition - must be re-inspected"
        });

        await _context.SaveChangesAsync();
        return (true, null);
    }

    // SCRUM-473 / D7: the Warehouse Manager resolves Hold. Same rule as the disposition: only the accepted part becomes stock, in the
    // same SaveChanges as the resolution (a retry after success finds no open Hold and posts nothing).
    public async Task<(bool Success, string? Error, int? IrnReceiptId)> ResolveHoldAsync(
        int irnReceiptLineId, decimal accepted, decimal rejected, string? notes, string? userName, int? actorEmployeeId)
    {
        var line = await _context.IrnReceiptLines.Include(l => l.IrnReceipt)
            .FirstOrDefaultAsync(l => l.IrnReceiptLineId == irnReceiptLineId);
        if (line?.IrnReceipt == null) return (false, "Rcv_Err_NotFound", null);
        var receipt = line.IrnReceipt;

        if (receipt.Status != "Closed") return (false, "Rcv_Err_HoldNotClosed", receipt.IrnReceiptId);
        if (!line.HasOpenHold) return (false, "Rcv_Err_NoOpenHold", receipt.IrnReceiptId);
        if (accepted < 0 || rejected < 0) return (false, "Rcv_Err_Negative", receipt.IrnReceiptId);
        if (accepted + rejected != line.HoldQuantity) return (false, "Rcv_Err_HoldSplit", receipt.IrnReceiptId);

        if (accepted > 0)
        {
            var (stock, stockError) = await StockRowResolver.GetOrCreateAsync(_context, line.MaterialItemId, receipt.WarehouseId, line.StorageLocationId);
            if (stock == null) return (false, stockError, receipt.IrnReceiptId);

            stock.OnHandQuantity += accepted;
            stock.LastUpdated = DateTime.Now;
            await _context.InventoryTransactions.AddAsync(new InventoryTransaction
            {
                MaterialItemId = line.MaterialItemId,
                WarehouseId = receipt.WarehouseId,
                StorageLocationId = line.StorageLocationId,
                TransactionType = "Acceptance",
                Quantity = accepted,
                UomId = line.UomId,
                ReferenceType = "IrnReceipt",
                ReferenceId = receipt.IrnReceiptId,
                EmployeeId = actorEmployeeId,
                TransactionDate = DateTime.Now,
                Reason = $"IRN {receipt.IrnNumber} - hold released"
            });
        }

        line.HoldAcceptedQuantity = accepted;
        line.HoldRejectedQuantity = rejected;
        line.HoldResolvedAt = DateTime.Now;
        line.HoldResolvedByUserName = userName;
        line.HoldResolutionNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

        await _context.IrnReceiptStatusHistories.AddAsync(new IrnReceiptStatusHistory
        {
            IrnReceiptId = receipt.IrnReceiptId,
            FromStatus = receipt.Status,
            ToStatus = receipt.Status,
            ChangedById = actorEmployeeId,
            ChangedDate = DateTime.Now,
            Reason = $"Hold resolved on line {line.IrnReceiptLineId}: accepted {accepted:0.###}, rejected {rejected:0.###}"
        });

        await _context.SaveChangesAsync();
        return (true, null, receipt.IrnReceiptId);
    }

    private static string ComputeOverallResult(IEnumerable<IrnReceiptLine> lines)
    {
        var list = lines.ToList();
        if (list.All(l => l.RejectedQuantity == 0 && l.HoldQuantity == 0)) return "Accepted";
        if (list.All(l => l.AcceptedQuantity == 0 && l.HoldQuantity == 0)) return "Rejected";
        if (list.All(l => l.AcceptedQuantity == 0 && l.RejectedQuantity == 0)) return "Hold";
        return "PartiallyAccepted";
    }
}
