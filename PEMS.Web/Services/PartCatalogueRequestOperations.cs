using System.Globalization;
using Microsoft.Extensions.Localization;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Web.Areas.Warehouse.Models;
using PEMS.Web.Helpers;
using PEMS.Web.Resources;

namespace PEMS.Web.Services;

/// <summary>SCRUM-662: the list, the details, the workflow commands and the files of PartCatalogueRequestController, as operations over IPartCatalogueService (the same calls; the service keeps every rule).</summary>
public sealed class PartCatalogueRequestOperations(IPartCatalogueService service, IPartCatalogueLookupService lookups, IRoleModuleActionService actions, IStringLocalizer<SharedResource> localizer) : IPartCatalogueRequestOperations
{
    private static readonly string[] AnyAction = ["View", "Create", "Review", "Approve", "Admin"];
    private const string FftName = "FREE FORMAT TEXT";

    private async Task<PcActor> ActorAsync(List<string> roles, int? employeeId, int? userId, string[]? allowed = null)
    {
        if (employeeId is not int emp) throw new InvalidOperationException(localizer["Pc_Err_NoEmployee"].Value);
        foreach (var a in allowed ?? AnyAction) if (await actions.HasActionAsync(roles, "PartCatalogue", a)) return new PcActor(emp, userId, roles);
        throw new UnauthorizedAccessException();
    }

    private (bool Success, string? Error) Back(PcResult r) => r.Success ? (true, null) : (false, string.Join(" | ", PcMessages.FormatAll(localizer, r.Errors)));

    private async Task<PartCatalogueDetailsDto?> DetailsAsync(List<string> roles, int? employeeId, int? userId, int id) => await service.GetDetailsAsync(id, await ActorAsync(roles, employeeId, userId));

    public async Task<List<PcRequestRow>> ListAsync(List<string> userRoles, int? employeeId, int? userId, string? search = null, string? status = null, string? requestType = null, bool? urgent = null, int? sectionId = null, DateTime? fromDate = null, DateTime? toDate = null, string? view = null)
    {
        var actor = await ActorAsync(userRoles, employeeId, userId);
        var filter = new PartCatalogueListFilter { Search = search, Status = status, RequestType = requestType, Urgent = urgent, SectionId = sectionId, FromDate = fromDate, ToDate = toDate, View = view is "mine" or "awaiting" ? view : "all", Page = 1, PageSize = 500 };
        var page = await service.ListAsync(filter, actor);
        return page.Items.Select(i => new PcRequestRow(i.Id, i.RequestNumber ?? "", i.RequestType, i.Status, i.CurrentStepKey ?? "", i.Urgent, i.SectionCode ?? "", i.TechnicalClassCode ?? "", i.TechnicalClassDescription ?? "", i.RequesterName ?? "", i.AssignedPartNumber ?? "", i.CreatedDate, i.SubmittedDate)).ToList();
    }

    public async Task<List<PcRequestSummaryRow>> SummaryAsync(List<string> userRoles, int? employeeId, int? userId)
    {
        var s = await service.GetSummaryAsync(await ActorAsync(userRoles, employeeId, userId));
        return [new PcRequestSummaryRow(s.Total, s.Draft, s.InApproval, s.Returned, s.Completed, s.Cancelled, s.Urgent, s.AwaitingMe)];
    }

    public async Task<List<ReportChoiceRow>> SectionChoicesAsync(List<string> userRoles)
    {
        foreach (var a in AnyAction) if (await actions.HasActionAsync(userRoles, "PartCatalogue", a)) return (await lookups.GetSectionsAsync()).Select(s => new ReportChoiceRow(s.Id.ToString(CultureInfo.InvariantCulture), s.Text)).ToList();
        throw new UnauthorizedAccessException();
    }

    public async Task<List<PcRequestHeadRow>> HeadAsync(List<string> userRoles, int? employeeId, int? userId, int id)
    {
        var d = await DetailsAsync(userRoles, employeeId, userId, id);
        if (d is null) return [];
        var f = d.Form; var a = d.Actions;
        string Label(string key) => d.Labels.TryGetValue(key, out var v) ? v : "";
        string N(decimal? v) => v?.ToString("0.####", CultureInfo.InvariantCulture) ?? "";
        var fft = f.Attributes.FirstOrDefault(x => string.Equals(x.Name, FftName, StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ?? "";
        // the long definition of the technical class only helps while the draft is filled in
        var definition = d.Status == "Draft" ? d.TechnicalClassLongDescription ?? "" : "";
        var price = f.PartPrice.HasValue ? f.PartPrice.Value.ToString("N2", CultureInfo.InvariantCulture) + " " + Label("CurrencyId") : "";
        return [new PcRequestHeadRow(d.Id, d.RequestNumber ?? "", d.RequestType, d.Status, f.Urgent, d.RequesterName ?? "", d.AssignedPartNumber ?? "", d.IsCritical, d.IsCapital, d.CreatedDate, d.SubmittedDate, d.CompletedDate,
            Label("SectionId"), Label("TechnicalClassId"), definition, f.AssetType, Label("CommodityGroup1Id"), Label("CommodityGroup2Id"), Label("ProductFamilyId"), Label("UnitOfMeasureId"), Label("ManufacturerId"), f.IsNewManufacturer, f.ManufacturerPartNumber ?? "",
            Label("SupplierId"), f.SupplierPartNumber ?? "", price, Label("TypeDesignationId"), f.SparePartSerialNo ?? "", f.EquipmentSerialNo ?? "", f.ManufacturerDrawingNo ?? "", f.DocumentNo ?? "",
            Label("PlanningMethodId"), N(f.MinimumStock), N(f.MaxConsumptionRate), N(f.FirstPurchaseQuantity), f.PrimaryIssuedRequired, N(f.IssuedQty), Label("StockGradeId"), f.WantedReceiptDate, f.StorageVolume ?? "",
            f.IsRd, f.ExistingPartNumber ?? "", f.ChangesMatchInventory?.ToString() ?? "", fft, a.ActiveStepKey ?? "", a.CanEdit, a.CanApprove, a.CanReturn, a.CanCancel, a.RequiresPartNumber, a.IsAdminOverride, a.IsOrganizationBlocked, a.OrganizationBlockReason ?? "", a.CanRetryRouting)];
    }

    public async Task<List<PcRequestAttributeRow>> AttributesAsync(List<string> userRoles, int? employeeId, int? userId, int id)
        => (await DetailsAsync(userRoles, employeeId, userId, id))?.Form.Attributes.Select(x => new PcRequestAttributeRow(x.Name, x.Value ?? "")).ToList() ?? [];

    public async Task<List<PcRequestStepRow>> StepsAsync(List<string> userRoles, int? employeeId, int? userId, int id)
        => (await DetailsAsync(userRoles, employeeId, userId, id))?.Steps.Select(s => new PcRequestStepRow(s.Order, s.StepKey, s.Kind, s.Status, s.SkipReason ?? "", s.AssignedName ?? "", s.DecidedDate)).ToList() ?? [];

    public async Task<List<PcRequestApprovalRow>> ApprovalsAsync(List<string> userRoles, int? employeeId, int? userId, int id)
        => (await DetailsAsync(userRoles, employeeId, userId, id))?.Approvals.Select(a => new PcRequestApprovalRow(a.ActionDate, a.StepKey ?? "", a.Action, a.ActorName ?? "", a.Comment ?? "", a.ToStatus ?? "")).ToList() ?? [];

    public async Task<List<PcRequestNoteRow>> NotesAsync(List<string> userRoles, int? employeeId, int? userId, int id)
        => (await DetailsAsync(userRoles, employeeId, userId, id))?.Notes.Select(n => new PcRequestNoteRow(n.CreatedDate, n.AuthorName ?? "", n.StepKey ?? "", n.Text)).ToList() ?? [];

    public async Task<List<PcRequestEquipmentRow>> EquipmentAsync(List<string> userRoles, int? employeeId, int? userId, int id)
        => (await DetailsAsync(userRoles, employeeId, userId, id))?.Equipment.Select(e => new PcRequestEquipmentRow(e.Id, e.Text, e.InstalledQuantity)).ToList() ?? [];

    public async Task<List<PcRequestAttachmentRow>> AttachmentsAsync(List<string> userRoles, int? employeeId, int? userId, int id)
        => (await DetailsAsync(userRoles, employeeId, userId, id))?.Attachments.Select(a => new PcRequestAttachmentRow(a.Id, a.FileName, a.Kind, a.FileSize, a.UploadedBy ?? "", a.UploadedDate)).ToList() ?? [];

    public async Task<(bool Success, string? Error)> NewDraftAsync(List<string> userRoles, int? employeeId, int? userId, string requestType)
    {
        var actor = await ActorAsync(userRoles, employeeId, userId, ["Create"]);
        await service.CreateDraftAsync(requestType, actor);
        return (true, null);
    }

    // the version is read now (the page does not carry it): a stale page is not refused, the same gap as the other operation pages
    private async Task<byte[]?> VersionAsync(int id, PcActor actor) => (await service.GetDetailsAsync(id, actor))?.RowVersion;

    public async Task<(bool Success, string? Error)> ExecuteAsync(List<string> userRoles, int? employeeId, int? userId, int id, string action, string? comment = null, string? partNumber = null)
    {
        var actor = await ActorAsync(userRoles, employeeId, userId, AnyAction);
        return Back(await service.ExecuteAsync(id, action, comment, partNumber, await VersionAsync(id, actor), actor));
    }

    public async Task<(bool Success, string? Error)> RetryRoutingAsync(List<string> userRoles, int? employeeId, int? userId, int id)
    {
        var actor = await ActorAsync(userRoles, employeeId, userId);
        return Back(await service.RetryRoutingAsync(id, await VersionAsync(id, actor), actor));
    }

    public async Task<(bool Success, string? Error)> AddNoteAsync(List<string> userRoles, int? employeeId, int? userId, int id, string text)
        => Back(await service.AddNoteAsync(id, text, await ActorAsync(userRoles, employeeId, userId)));

    private static PcFileResult Missing() => new(false, "NotFound", [], "", "application/octet-stream");

    public async Task<PcFileResult> AttachmentAsync(List<string> userRoles, int? employeeId, int? userId, int id, int attachmentId)
    {
        var file = await service.OpenAttachmentAsync(id, attachmentId, await ActorAsync(userRoles, employeeId, userId));
        if (file is null) return Missing();
        await using var stream = file.Value.Stream;
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return new PcFileResult(true, null, memory.ToArray(), file.Value.FileName, file.Value.ContentType);
    }

    public async Task<PcFileResult> ExportXlsxAsync(List<string> userRoles, int? employeeId, int? userId, int id)
    {
        var model = await service.GetPrintModelAsync(id, await ActorAsync(userRoles, employeeId, userId));
        if (model is null) return Missing();
        var name = string.IsNullOrWhiteSpace(model.RequestNumber) ? $"PCR-{id}" : model.RequestNumber;
        return new PcFileResult(true, null, PartCataloguePrintExport.BuildXlsx(model), name + ".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }

    public async Task<PcFileResult> ExportPdfAsync(List<string> userRoles, int? employeeId, int? userId, int id)
    {
        var model = await service.GetPrintModelAsync(id, await ActorAsync(userRoles, employeeId, userId));
        if (model is null) return Missing();
        var name = string.IsNullOrWhiteSpace(model.RequestNumber) ? $"PCR-{id}" : model.RequestNumber;
        return new PcFileResult(true, null, PartCataloguePrintExport.BuildPdf(model), name + ".pdf", "application/pdf");
    }
}
