using System.Globalization;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Web.Services;

/// <summary>SCRUM-657: what MrsImportController showed and did, as operations over IMrsImportService (the source file is stored the way the controller stored it).</summary>
public sealed class MrsImportOperations(IMrsImportService service, IFileStorageService storage, PEMSDbContext context) : IMrsImportOperations
{
    private const int RowLimit = 5000;
    private const string FileName = "mrs-import.xlsx";

    private static MrsImportBatchRow Row(PEMS.Application.DTOs.MrsImportBatchSummaryDto b)
        => new(b.MrsImportBatchId, b.SourceFileName, b.SourceSheet ?? "", b.Status, b.TotalRows, b.NewCount, b.MatchedCount, b.DuplicateCount, b.InvalidCount, b.ApprovedCount, b.ImportDate, b.SourceFileDownloadUrl ?? "");

    public async Task<List<MrsImportBatchRow>> BatchesAsync() => (await service.GetBatchesAsync()).Select(Row).ToList();

    public async Task<List<MrsImportBatchRow>> BatchAsync(int batchId) => (await service.GetBatchDetailAsync(batchId, null, 1, 1)) is { } d ? [Row(d.Batch)] : [];

    public async Task<List<MrsImportRowItem>> RowsAsync(int batchId, string? matchStatus = null)
        => (await service.GetBatchDetailAsync(batchId, string.IsNullOrWhiteSpace(matchStatus) ? null : matchStatus, 1, RowLimit))?.Rows.Items
            .Select(r => new MrsImportRowItem(r.MrsImportRowId, r.RawMrsNo ?? "", r.RawItemCode ?? "", r.RawSupplier ?? "", r.RawPoNo ?? "", r.RawDescription ?? "", r.RawUom ?? "", r.RawQtyReceived, r.RawQtyIssued, r.RawInventory,
                r.RawDateReceived, r.ResolvedWarehouseCode ?? "", r.MatchStatus, r.MatchedMaterialCode ?? "", r.ReviewStatus, r.ErrorMessage ?? "")).ToList() ?? [];

    public async Task<MrsImportUploadResult> UploadAsync(byte[] file, string? sheetName, int? employeeId)
    {
        if (file is null || file.Length == 0) return new(false, "No file was chosen.", null);
        // the file itself becomes a real document attachment (not only a source file name in text), then the same bytes are parsed
        string storageKey;
        await using (var upload = new MemoryStream(file)) storageKey = await storage.SaveAsync("mrs-import", FileName, upload);
        var attachment = new DocumentAttachment
        {
            FileName = FileName, FileExtension = ".xlsx", FilePath = storageKey, FileSize = file.Length, MimeType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            UploadedById = employeeId, UploadDate = DateTime.Now, IsActive = true, AIStatus = "NotApplicable"
        };
        await context.DocumentAttachments.AddAsync(attachment);
        await context.SaveChangesAsync();

        await using var stream = new MemoryStream(file);
        var (success, error, batchId) = await service.StageFromExcelAsync(stream, FileName, string.IsNullOrWhiteSpace(sheetName) ? null : sheetName, employeeId, attachment.AttachmentId);
        return new(success, error, batchId);
    }

    private static List<int> Ids(string text) => (text ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(s => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0).Where(n => n > 0).Distinct().ToList();

    public async Task<(bool Success, string? Error)> CommitAsync(int batchId, string rowIds, int? employeeId)
    {
        var ids = Ids(rowIds);
        if (ids.Count == 0) return (false, "No row was chosen.");
        var (success, error, _, _) = await service.CommitAsync(batchId, ids, employeeId);
        return (success, error);
    }

    public async Task<(bool Success, string? Error)> RejectAsync(int batchId, string rowIds, int? employeeId)
    {
        var ids = Ids(rowIds);
        if (ids.Count == 0) return (false, "No row was chosen.");
        return await service.RejectRowsAsync(batchId, ids, employeeId);
    }

    public async Task<(bool Success, string? Error)> CommitByStatusAsync(int batchId, string matchStatus, int? employeeId)
    {
        var (success, error, _, _) = await service.CommitByStatusAsync(batchId, matchStatus, employeeId);
        return (success, error);
    }

    public async Task<(bool Success, string? Error)> RejectByStatusAsync(int batchId, string matchStatus, int? employeeId)
    {
        var (success, error, _) = await service.RejectByStatusAsync(batchId, matchStatus, employeeId);
        return (success, error);
    }
}
