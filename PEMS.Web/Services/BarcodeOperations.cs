using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;

namespace PEMS.Web.Services;

/// <summary>SCRUM-671: what BarcodeController showed, answered and did, as operations over IBarcodeService and IBarcodeRenderingService.</summary>
public sealed class BarcodeOperations(IBarcodeService barcodes, IBarcodeRenderingService rendering, PEMSDbContext context) : IBarcodeOperations
{
    private const string Pdf = "application/pdf";
    private static BarcodeFileResult Refused(string error) => new(false, error, [], "", "text/plain");

    private static string TargetUrl(string? type, int? id) => type switch
    {
        "Equipment" => "/Equipment/Details/" + id, "MaterialItem" => "/p/material-item-details?id=" + id, "Warehouse" or "StorageLocation" => "/p/inventory-master-data", _ => ""
    };

    private static BarcodeLabelContent Label(BarcodeIdentityDto b)
        => new() { Code = b.Code, Symbology = b.Symbology, TitleLine = b.TargetDisplayLabel ?? $"{b.TargetType} #{b.TargetId}", SubtitleLine = b.TargetType, MetaLine = $"Generated {b.CreatedDate:yyyy-MM-dd}" };

    public async Task<List<BarcodeRow>> RecentAsync()
        => (await context.BarcodeIdentities.AsNoTracking().OrderByDescending(x => x.CreatedDate).Take(50).Select(x => new { x.BarcodeIdentityId, x.Code, x.Symbology, x.TargetType, x.TargetId, x.Status, x.CreatedDate, x.PrintCount }).ToListAsync())
            .Select(x => new BarcodeRow(x.BarcodeIdentityId, x.Code, x.Symbology, x.TargetType, x.TargetId, "", x.Status, x.CreatedDate, x.PrintCount)).ToList();

    public async Task<List<BarcodeResolutionRow>> ResolveAsync(string code, string? expectedTargetType = null, int? expectedTargetId = null)
    {
        var r = await barcodes.ResolveAsync(code);
        var mismatch = r.Success && expectedTargetType != null && expectedTargetId.HasValue
            && (!string.Equals(r.TargetType, expectedTargetType, StringComparison.OrdinalIgnoreCase) || r.TargetId != expectedTargetId);
        var barcode = r.BarcodeIdentityId.HasValue ? await barcodes.GetByIdAsync(r.BarcodeIdentityId.Value) : null;
        return [new BarcodeResolutionRow(r.Success, r.ErrorCode ?? "", r.ErrorMessage ?? "", r.TargetType ?? "", r.TargetId, barcode?.TargetDisplayLabel ?? "", r.Status ?? "", mismatch, r.Success ? TargetUrl(r.TargetType, r.TargetId) : "")];
    }

    public async Task<object> ResolveJsonAsync(string code)
    {
        var r = await barcodes.ResolveAsync(code);
        if (!r.Success) return new { success = false, errorCode = r.ErrorCode, errorMessage = r.ErrorMessage };
        var barcode = await barcodes.GetByIdAsync(r.BarcodeIdentityId!.Value);
        return new { success = true, targetType = r.TargetType, targetId = r.TargetId, label = barcode?.TargetDisplayLabel };
    }

    public async Task<BarcodeFileResult> SvgAsync(int id)
        => await barcodes.GetByIdAsync(id) is { } b ? new(true, null, Encoding.UTF8.GetBytes(rendering.RenderSvg(b.Code, b.Symbology)), $"{b.Code}.svg", "image/svg+xml") : Refused("Barcode not found.");

    public async Task<BarcodeFileResult> PngAsync(int id)
        => await barcodes.GetByIdAsync(id) is { } b ? new(true, null, rendering.RenderPng(b.Code, b.Symbology), $"{b.Code}.png", "image/png") : Refused("Barcode not found.");

    public async Task<BarcodeFileResult> LabelPdfAsync(int id)
        => await barcodes.GetByIdAsync(id) is { } b ? new(true, null, rendering.RenderLabelSheetPdf([Label(b)]), $"{b.Code}.pdf", Pdf) : Refused("Barcode not found.");

    private static List<int> Ids(string? text) => (text ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(s => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0).Where(n => n > 0).Distinct().ToList();

    public async Task<BarcodeFileResult> LabelSheetAsync(string targetType, string ids)
    {
        var labels = new List<BarcodeLabelContent>();
        foreach (var id in Ids(ids))
        {
            if (await barcodes.GetByIdAsync(id) is not { } b) continue;
            labels.Add(Label(b));
            await barcodes.RecordPrintAsync(id);
        }
        return labels.Count == 0 ? Refused("No barcode found.") : new(true, null, rendering.RenderLabelSheetPdf(labels), $"barcodes-{targetType}-{DateTime.Now:yyyyMMddHHmm}.pdf", Pdf);
    }

    public async Task<List<BarcodeCandidateRow>> CandidatesAsync(int? disciplineId = null, string? equipmentType = null, string? tagSearch = null)
    {
        if (disciplineId is null && string.IsNullOrWhiteSpace(equipmentType) && string.IsNullOrWhiteSpace(tagSearch)) return [];   // a filter is needed (the old page showed nothing without one)
        var query = context.Equipments.AsNoTracking().Where(x => x.IsActive);
        if (disciplineId.HasValue) query = query.Where(x => x.DisciplineId == disciplineId.Value);
        if (!string.IsNullOrWhiteSpace(equipmentType)) query = query.Where(x => x.EquipmentType == equipmentType);
        if (!string.IsNullOrWhiteSpace(tagSearch)) query = query.Where(x => x.TagNumber.Contains(tagSearch.Trim()));
        var list = await query.OrderBy(x => x.TagNumber).Take(200).Select(x => new { x.EquipmentId, x.TagNumber, x.Name, x.EquipmentType }).ToListAsync();
        var ids = list.Select(x => x.EquipmentId).ToList();
        var barcoded = (await context.BarcodeIdentities.AsNoTracking().Where(x => x.TargetType == "Equipment" && ids.Contains(x.TargetId) && x.Status == "Active").Select(x => x.TargetId).ToListAsync()).ToHashSet();
        return list.Select(x => new BarcodeCandidateRow(x.EquipmentId, x.TagNumber, x.Name ?? "", x.EquipmentType ?? "", barcoded.Contains(x.EquipmentId))).ToList();
    }

    public async Task<List<ReportChoiceRow>> DisciplinesAsync()
        => (await context.Disciplines.AsNoTracking().Where(x => x.IsActive).Select(x => new { x.DisciplineId, x.Name }).ToListAsync()).Select(x => new ReportChoiceRow(x.DisciplineId.ToString(CultureInfo.InvariantCulture), x.Name)).ToList();

    public async Task<List<ReportChoiceRow>> EquipmentTypesAsync()
        => (await context.Equipments.AsNoTracking().Where(x => x.IsActive && x.EquipmentType != "").Select(x => x.EquipmentType).Distinct().OrderBy(x => x).ToListAsync()).Select(x => new ReportChoiceRow(x, x)).ToList();

    public async Task<(bool Success, string? Error)> GenerateAsync(string targetType, int targetId, string symbology, int? employeeId)
    {
        try
        {
            await barcodes.GenerateAsync(targetType, targetId, string.IsNullOrWhiteSpace(symbology) ? "Code128" : symbology, employeeId);
            return (true, null);
        }
        catch (InvalidOperationException ex) { return (false, ex.Message); }
    }

    public async Task<BarcodeFileResult> PrintAsync(int id)
    {
        var (success, error) = await barcodes.RecordPrintAsync(id);
        return success ? await LabelPdfAsync(id) : Refused(error ?? "Barcode not found.");
    }

    public async Task<BarcodeFileResult> BulkGenerateAsync(string equipmentIds, string symbology, int? employeeId)
    {
        var ids = Ids(equipmentIds);
        if (ids.Count == 0) return Refused("Choose at least one equipment.");
        var result = await barcodes.BulkGenerateAsync("Equipment", ids, string.IsNullOrWhiteSpace(symbology) ? "Code128" : symbology, employeeId);
        return await LabelSheetAsync("Equipment", string.Join(",", result.BarcodeIdentityIds));
    }

    public Task<(bool Success, string? Error)> DisableAsync(int id, string? reason, int? employeeId) => barcodes.DisableAsync(id, reason, employeeId);
    public Task<(bool Success, string? Error)> RevokeAsync(int id, string? reason, int? employeeId) => barcodes.RevokeAsync(id, reason, employeeId);

    public async Task<(bool Success, string? Error)> ReplaceAsync(int id, string symbology, int? employeeId)
    {
        var (barcode, error) = await barcodes.ReplaceAsync(id, string.IsNullOrWhiteSpace(symbology) ? "Code128" : symbology, employeeId);
        return barcode is not null ? (true, null) : (false, error);
    }
}
