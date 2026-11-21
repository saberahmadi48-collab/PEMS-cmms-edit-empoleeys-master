using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Application.Numbering;
using PEMS.Persistence.Context;

namespace PEMS.Web.Services;

public sealed class NumberingPageService(INumberingSchemeService schemes, IVDRService vdr, IVdrDocumentNumberingService vdrNumbers, PEMSDbContext context) : INumberingPageService
{
    public async Task<List<NumberingSchemeView>> SchemesAsync() => (await schemes.GetAllAsync()).ToList();

    public async Task<List<NumberingSchemeFormRowDto>> SchemeOfAsync(string documentType)
    {
        var s = (await schemes.GetAllAsync()).FirstOrDefault(x => x.DocumentType == documentType);
        return s == null ? [] : [new NumberingSchemeFormRowDto { DocumentType = s.DocumentType, Pattern = s.Pattern, PadWidth = s.PadWidth, Calendar = s.Calendar, ResetPeriod = s.ResetPeriod, IsActive = s.IsActive, AllowManualEntry = s.AllowManualEntry }];
    }

    public async Task<NumberingActionResult> SaveAsync(NumberingSchemeInput input)
    {
        var error = await schemes.SaveAsync(input);
        return error == null ? new NumberingActionResult(true, "Num_Saved") : new NumberingActionResult(false, error);
    }

    public Task<List<NumberingPreviewRowDto>> PreviewAsync(string documentType, string pattern, int padWidth, string calendar, string resetPeriod)
    {
        var (number, error) = schemes.Preview(new NumberingSchemeInput(documentType, pattern, padWidth, calendar, resetPeriod, true, false, null), new NumberingContext("PRJ", "MEC"));
        return Task.FromResult(new List<NumberingPreviewRowDto> { new() { DocumentType = documentType, Pattern = pattern, PadWidth = padWidth, Calendar = calendar, ResetPeriod = resetPeriod, Number = number, Error = error } });
    }

    public async Task<List<VdrNumberSearchRowDto>> VdrSearchAsync(string search)
    {
        if (string.IsNullOrWhiteSpace(search)) return [];
        var found = await vdr.GetPagedAsync(null, null, null, 1, 20, search.Trim());
        return found.Items.Select(i => new VdrNumberSearchRowDto { VDRItemId = i.VDRItemId, Search = search.Trim(), DocumentNumber = i.DocumentNumber, DocumentDescription = i.DocumentDescription, VendorName = i.VendorName }).ToList();
    }

    // The two generators below are the logic of VDRController.GenerateContractorNumber / GenerateVendorNumber (the reference fields that are filled are saved on the vendor, project and item, then the number is built); the VDR conversion (batch 9) removes the controller copy.
    public async Task<NumberingActionResult> GenerateContractorNumberAsync(int vdrItemId, string? vendorCode, string? plantCode, string? unitCode, string? materialCode)
    {
        var item = await context.VDRItems.Include(x => x.Vendor).Include(x => x.Project).FirstOrDefaultAsync(x => x.DocumentId == vdrItemId);
        if (item == null) return new NumberingActionResult(false, "The VDR item was not found.");
        if (!string.IsNullOrWhiteSpace(vendorCode) && item.Vendor != null) item.Vendor.VendorCode = vendorCode.Trim();
        if (!string.IsNullOrWhiteSpace(plantCode) && item.Project != null) item.Project.PlantCode = plantCode.Trim();
        if (!string.IsNullOrWhiteSpace(unitCode) && item.Project != null) item.Project.UnitCode = unitCode.Trim();
        if (!string.IsNullOrWhiteSpace(materialCode)) item.MaterialCode = materialCode.Trim();
        await context.SaveChangesAsync();
        try
        {
            var number = await vdrNumbers.GenerateContractorNumberAsync(vdrItemId);
            item.DocumentNumber = number;
            item.DocumentNo = number;
            await context.SaveChangesAsync();
            return new NumberingActionResult(true, $"شماره‌ی رسمی مدرک ساخته شد: {number}");
        }
        catch (InvalidOperationException ex) { return new NumberingActionResult(false, ex.Message); }
    }

    public async Task<NumberingActionResult> GenerateVendorNumberAsync(int vdrItemId, string? jobNumber, string? unitCode, string? typologyCode)
    {
        var item = await context.VDRItems.Include(x => x.Project).FirstOrDefaultAsync(x => x.DocumentId == vdrItemId);
        if (item == null) return new NumberingActionResult(false, "The VDR item was not found.");
        if (!string.IsNullOrWhiteSpace(jobNumber) && item.Project != null) item.Project.JobNumber = jobNumber.Trim();
        if (!string.IsNullOrWhiteSpace(unitCode) && item.Project != null) item.Project.UnitCode = unitCode.Trim();
        if (!string.IsNullOrWhiteSpace(typologyCode)) item.TypologyCode = typologyCode.Trim();
        await context.SaveChangesAsync();
        try
        {
            var number = await vdrNumbers.GenerateVendorNumberAsync(vdrItemId);
            item.VendorFormatDocumentNumber = number;
            await context.SaveChangesAsync();
            return new NumberingActionResult(true, $"شماره‌ی فرمت Vendor ساخته شد: {number}");
        }
        catch (InvalidOperationException ex) { return new NumberingActionResult(false, ex.Message); }
    }
}
