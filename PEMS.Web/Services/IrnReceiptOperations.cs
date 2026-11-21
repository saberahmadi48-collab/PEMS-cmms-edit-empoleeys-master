using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Seed;
using PEMS.WorkflowPlatform.Interfaces;

namespace PEMS.Web.Services;

/// <summary>SCRUM-655: what IrnReceiptController read and did, as operations; the workflow orchestration of the disposition and the QA approval is the controller's own logic, moved here unchanged.</summary>
public sealed class IrnReceiptOperations(PEMSDbContext context, IIrnReceiptService lifecycle, IPurchaseOrderReceivingService receiving, INumberingService numbering, IWorkflowRuntime runtime) : IIrnReceiptOperations
{
    private const int ListLimit = 2000;
    private const string IrnModule = IrnDispositionWorkflowSeeder.Module;
    private const string IrnBusinessObjectType = IrnDispositionWorkflowSeeder.BusinessObjectType;

    private static ReportChoiceRow Choice(int id, string label) => new(id.ToString(CultureInfo.InvariantCulture), label);
    private static string Name(Employee? e) => e is null ? "" : $"{e.FirstName} {e.LastName}".Trim();

    public async Task<List<IrnReceiptRow>> ListAsync(string? status = null)
    {
        var query = context.IrnReceipts.AsNoTracking().Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);
        var rows = await query.OrderByDescending(x => x.ReceivingDate).ThenByDescending(x => x.IrnReceiptId).Take(ListLimit)
            .Select(x => new { x.IrnReceiptId, x.IrnNumber, x.Status, Vendor = x.Vendor!.Name, Warehouse = x.Warehouse!.Code, By = x.ReceivedBy!.FirstName + " " + x.ReceivedBy!.LastName, x.ReceivingDate, Lines = x.Lines.Count }).ToListAsync();
        return rows.Select(x => new IrnReceiptRow(x.IrnReceiptId, x.IrnNumber, x.Status, x.Vendor, x.Warehouse, x.By, x.ReceivingDate, x.Lines)).ToList();
    }

    public async Task<List<IrnReceiptDetailRow>> DetailAsync(int id)
    {
        var r = await context.IrnReceipts.AsNoTracking().Include(x => x.Vendor).Include(x => x.Warehouse).Include(x => x.PurchaseOrder).Include(x => x.ReceivedBy).FirstOrDefaultAsync(x => x.IrnReceiptId == id);
        return r is null ? [] : [new IrnReceiptDetailRow(r.IrnReceiptId, r.IrnNumber, r.Status, r.Vendor?.Name ?? "", r.Warehouse?.Code ?? "", r.PurchaseOrder?.PoNumber ?? "", Name(r.ReceivedBy), r.ReceivingDate, r.Notes ?? "")];
    }

    public async Task<List<IrnReceiptLineRow>> LinesAsync(int id)
        => (await context.IrnReceiptLines.AsNoTracking().Include(x => x.MaterialItem).Include(x => x.Uom).Where(x => x.IrnReceiptId == id && x.IsActive).OrderBy(x => x.IrnReceiptLineId).ToListAsync())
            .Select(l => new IrnReceiptLineRow(l.IrnReceiptLineId, $"{l.MaterialItem?.MaterialCode} - {l.MaterialItem?.Description}", l.ReceivedQuantity, l.AcceptedQuantity, l.RejectedQuantity, l.HoldQuantity, l.Uom?.Code ?? "",
                l.RejectionAction ?? "", l.HoldResolvedAt.HasValue ? $"{l.HoldAcceptedQuantity:0.###} / {l.HoldRejectedQuantity:0.###}" : "", l.Notes ?? "")).ToList();

    public async Task<List<IrnReceiptHistoryRow>> HistoryAsync(int id)
        => (await context.IrnReceiptStatusHistories.AsNoTracking().Include(x => x.ChangedBy).Where(x => x.IrnReceiptId == id).OrderByDescending(x => x.ChangedDate).ToListAsync())
            .Select(h => new IrnReceiptHistoryRow(h.IrnReceiptStatusHistoryId, h.ChangedDate, h.FromStatus, h.ToStatus, Name(h.ChangedBy), h.Reason ?? "")).ToList();

    public async Task<List<IrnDispositionRow>> DispositionsAsync(int id)
        => (await context.IrnDispositionRecords.AsNoTracking().Include(x => x.DispositionedBy).Where(x => x.IrnReceiptId == id).OrderByDescending(x => x.DispositionedDate).ToListAsync())
            .Select(d => new IrnDispositionRow(d.IrnDispositionRecordId, d.DispositionedDate, Name(d.DispositionedBy), d.OverallResult, d.Notes ?? "", d.WorkflowOutcome)).ToList();

    public async Task<List<PurchaseOrderReceivingRow>> PoLinesAsync(int id)
    {
        var poId = await context.IrnReceipts.AsNoTracking().Where(x => x.IrnReceiptId == id).Select(x => x.PurchaseOrderId).FirstOrDefaultAsync();
        if (!poId.HasValue) return [];
        return (await receiving.GetPoLinesAsync(poId.Value)).Select(l => new PurchaseOrderReceivingRow(l.PurchaseOrderLineId, l.LineNo, l.Description, l.MaterialCode ?? "", l.WarehouseCode ?? "", l.Ordered, l.Received, l.InQc, l.Accepted, l.OnHold,
            l.Outstanding, l.OverReceived, l.InspectionRequired, l.InspectionReleasedAt, l.InspectionTestPlanNumber ?? "")).ToList();
    }

    public async Task<List<IrnOsdRow>> OsdAsync(int id)
        => (await receiving.GetOsdReportAsync(id))?.Lines.Select(l => new IrnOsdRow(l.IrnReceiptLineId, l.PoLineNo, l.MaterialCode, l.Ordered, l.ReceivedBefore, l.ThisReceipt, l.Over, l.Short, l.Accepted, l.Rejected, l.Hold, l.RejectionAction ?? "", l.Notes ?? "")).ToList() ?? [];

    public async Task<List<ReportChoiceRow>> VendorsAsync()
        => (await context.Vendors.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new { x.VendorId, x.Name }).ToListAsync()).Select(x => Choice(x.VendorId, x.Name)).ToList();

    public async Task<List<ReportChoiceRow>> WarehousesAsync()
        => (await context.Warehouses.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Code).Select(x => new { x.WarehouseId, x.Code }).ToListAsync()).Select(x => Choice(x.WarehouseId, x.Code)).ToList();

    public async Task<List<ReportChoiceRow>> PurchaseOrdersAsync()
        => (await context.PurchaseOrders.AsNoTracking().Where(p => p.IsActive && p.Status == PurchaseOrderStatuses.Issued).OrderByDescending(p => p.PoId).Take(500).Select(p => new { p.PoId, p.PoNumber, VendorName = p.Vendor!.Name }).ToListAsync())
            .Select(p => Choice(p.PoId, $"{p.PoNumber} - {p.VendorName}")).ToList();

    public async Task<List<ReportChoiceRow>> StockMaterialsAsync()
        => (await context.MaterialItems.AsNoTracking().Where(x => x.IsActive && x.IsStockItem).OrderBy(x => x.MaterialCode).Select(x => new { x.MaterialItemId, x.MaterialCode, x.Description }).Take(5000).ToListAsync())
            .Select(x => Choice(x.MaterialItemId, $"{x.MaterialCode} - {x.Description}")).ToList();

    public async Task<(bool Success, string? Error)> CreateAsync(int vendorId, int warehouseId, DateTime? receivingDate, int? purchaseOrderId, string? notes, int? employeeId)
    {
        if (!await context.Vendors.AnyAsync(x => x.VendorId == vendorId)) return (false, "Selected vendor was not found.");
        if (!await context.Warehouses.AnyAsync(x => x.WarehouseId == warehouseId)) return (false, "Selected warehouse was not found.");
        if (purchaseOrderId.HasValue && !await context.PurchaseOrders.AnyAsync(x => x.PoId == purchaseOrderId.Value)) return (false, "Selected purchase order was not found.");
        // goods of a PO are received from the PO's vendor
        if (purchaseOrderId.HasValue && !await context.PurchaseOrders.AnyAsync(x => x.PoId == purchaseOrderId.Value && x.VendorId == vendorId)) return (false, "Rcv_Err_VendorMismatch");
        if (!employeeId.HasValue) return (false, "No employee record is linked to the current user.");

        await context.IrnReceipts.AddAsync(new IrnReceipt
        {
            IrnNumber = await numbering.GetNextNumberAsync("IRN"), Status = "Draft", VendorId = vendorId, WarehouseId = warehouseId, ReceivingDate = receivingDate ?? DateTime.Now, PurchaseOrderId = purchaseOrderId,
            ReceivedById = employeeId.Value, Notes = notes, IsActive = true, CreatedDate = DateTime.Now
        });
        await context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> AddLineAsync(int irnReceiptId, int materialItemId, decimal receivedQuantity)
    {
        var receipt = await context.IrnReceipts.FirstOrDefaultAsync(x => x.IrnReceiptId == irnReceiptId);
        if (receipt is null) return (false, "IRN not found.");
        // no silent change of a submitted receipt: a line can only be added in Draft
        if (receipt.Status != "Draft") return (false, "Lines can only be added while the IRN is in Draft status.");
        if (receivedQuantity <= 0) return (false, "Received quantity must be greater than zero.");
        if (!await context.MaterialItems.AnyAsync(x => x.MaterialItemId == materialItemId)) return (false, "Selected material was not found.");
        await context.IrnReceiptLines.AddAsync(new IrnReceiptLine { IrnReceiptId = irnReceiptId, MaterialItemId = materialItemId, ReceivedQuantity = receivedQuantity, IsActive = true, CreatedDate = DateTime.Now });
        await context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> DispositionAsync(int id, string mode, string? notes, int? employeeId)
    {
        var lines = await context.IrnReceiptLines.AsNoTracking().Where(x => x.IrnReceiptId == id && x.IsActive).ToListAsync();
        if (lines.Count == 0) return (false, "The receipt has no lines.");
        var dispositions = mode switch
        {
            "AcceptAll" => lines.Select(l => (l.IrnReceiptLineId, l.ReceivedQuantity, 0m, 0m)).ToList(),
            "RejectAll" => lines.Select(l => (l.IrnReceiptLineId, 0m, l.ReceivedQuantity, 0m)).ToList(),
            "HoldAll" => lines.Select(l => (l.IrnReceiptLineId, 0m, 0m, l.ReceivedQuantity)).ToList(),
            _ => null
        };
        return dispositions is null ? (false, "The mode is AcceptAll, RejectAll or HoldAll.") : await RecordAsync(id, dispositions, notes, employeeId);
    }

    public async Task<(bool Success, string? Error)> DispositionLinesAsync(int id, string lines, string? notes, int? employeeId)
    {
        var parsed = new List<(int LineId, decimal Accepted, decimal Rejected, decimal Hold)>();
        foreach (var part in (lines ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var cells = part.Split(':', StringSplitOptions.TrimEntries);
            if (cells.Length != 4 || !int.TryParse(cells[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var lineId)
                || !decimal.TryParse(cells[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var accepted) || !decimal.TryParse(cells[2], NumberStyles.Number, CultureInfo.InvariantCulture, out var rejected)
                || !decimal.TryParse(cells[3], NumberStyles.Number, CultureInfo.InvariantCulture, out var hold)) return (false, "Disposition input is malformed.");
            parsed.Add((lineId, accepted, rejected, hold));
        }
        return parsed.Count == 0 ? (false, "Disposition input is malformed.") : await RecordAsync(id, parsed, notes, employeeId);
    }

    /// <summary>Saves the quantities and starts the QA approval workflow. The two are separate commits: a running instance left by an earlier attempt is reused, and when the workflow cannot start the disposition is compensated (rejected back to Submitted) so the inspector records it again; no stock is posted on this path.</summary>
    private async Task<(bool Success, string? Error)> RecordAsync(int id, IReadOnlyList<(int LineId, decimal Accepted, decimal Rejected, decimal Hold)> lines, string? notes, int? employeeId)
    {
        var (success, error, _) = await lifecycle.RecordDispositionAsync(id, lines, notes, employeeId);
        if (!success) return (false, error);

        var existing = await runtime.GetInstanceAsync(IrnBusinessObjectType, id);
        if (existing == null || existing.Status != "Running")
        {
            try { await runtime.StartInstanceAsync(IrnModule, IrnBusinessObjectType, id, IrnDispositionWorkflowSeeder.Category, null, employeeId); }
            catch (Exception ex)
            {
                var (compensated, compensationError) = await lifecycle.CompleteDispositionAsync(id, false, employeeId);
                return (false, compensated
                    ? $"گردش‌کار تایید شروع نشد ({ex.Message}). Disposition برگردانده شد؛ دوباره ثبت کنید."
                    : $"گردش‌کار تایید شروع نشد ({ex.Message}) و بازگردانی Disposition هم ناموفق بود: {compensationError}");
            }
        }
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ApproveAsync(List<string> userRoles, int id, string decision, string? comment, int? employeeId, string? ip, string? userAgent)
    {
        var receipt = await context.IrnReceipts.FirstOrDefaultAsync(x => x.IrnReceiptId == id);
        if (receipt is null) return (false, "IRN not found.");
        if (receipt.Status != "PendingApproval") return (false, "This IRN is not awaiting approval.");

        var instance = await runtime.GetInstanceAsync(IrnBusinessObjectType, id);

        // the workflow step and the stock posting are two commits: when the step was done but the posting failed, the instance is Completed or Rejected while the IRN is still PendingApproval. The recorded outcome of the
        // workflow (not the decision of this request) then decides and only the posting is repeated; CompleteDispositionAsync is idempotent through the PendingApproval guard. The instance must belong to this turn of the
        // disposition (started after the last entry into PendingApproval): a stale instance of an earlier turn never decides a new posting.
        var pendingSince = await context.IrnReceiptStatusHistories.Where(h => h.IrnReceiptId == id && h.ToStatus == "PendingApproval").OrderByDescending(h => h.ChangedDate).Select(h => (DateTime?)h.ChangedDate).FirstOrDefaultAsync();
        var recoveringPosting = instance != null && (instance.Status == "Completed" || instance.Status == "Rejected") && pendingSince.HasValue && instance.StartDate >= pendingSince.Value;
        if (instance == null || (instance.Status != "Running" && !recoveringPosting)) return (false, "The approval workflow for this IRN was not found.");

        try
        {
            if (!recoveringPosting) await runtime.ExecuteActionAsync(instance.WorkflowInstanceId, decision, employeeId, userRoles, comment, ip, userAgent ?? "");
            var approved = recoveringPosting ? instance.Status == "Completed" : decision == "Approve";
            var (success, error) = await lifecycle.CompleteDispositionAsync(id, approved, employeeId);
            // Hold left on the approved receipt goes to the Warehouse Manager in My Work
            if (success && approved) await receiving.StartHoldResolutionAsync(id, employeeId);
            return success ? (true, null) : (false, $"{error} (تصمیم گردش‌کار ثبت شده است؛ پس از رفع مشکل دوباره «تایید» را بزنید تا ثبت موجودی تکرار شود.)");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public Task<PoReceivingResult> ReceiveFromPoAsync(int id, int purchaseOrderLineId, decimal quantity, PoReceivingActor actor)
        => receiving.ReceiveAgainstPoAsync(id, [new IrnPoLineInput(purchaseOrderLineId, quantity)], actor);
}
