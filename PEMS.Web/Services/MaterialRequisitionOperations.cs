using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;

namespace PEMS.Web.Services;

/// <summary>SCRUM-726: what MaterialRequisitionController showed and did, as operations over IMaterialRequisitionService.</summary>
public sealed class MaterialRequisitionOperations(IMaterialRequisitionService service, IPartCatalogueDuplicateService parts, IPreProcurementService preProcurement, IPermissionService permissions, PEMSDbContext context) : IMaterialRequisitionOperations
{
    private const int ListLimit = 2000;

    public async Task<List<MaterialRequisitionRow>> ListAsync()
        => (await service.GetAllAsync(1, ListLimit)).Items.Select(m => new MaterialRequisitionRow(m.MRId, m.MRNumber, m.ProjectName, m.DisciplineName, m.Description, m.RequiredDate, m.Status, m.CreatedDate, m.QuotationsCount)).ToList();

    public async Task<List<MaterialRequisitionDetailRow>> DetailAsync(int id)
    {
        var m = await service.GetByIdAsync(id);
        return m is null ? [] : [new MaterialRequisitionDetailRow(m.MRId, m.MRNumber, m.ProjectName, m.DisciplineName, m.EquipmentTagNumber ?? "", m.RequiredDate, m.ResponsibleEngineerName ?? "", m.Status, m.Description)];
    }

    public async Task<List<MaterialRequisitionItemRow>> ItemsAsync(int id)
        => (await service.GetByIdAsync(id))?.Items.Select(i => new MaterialRequisitionItemRow(i.MRItemId, i.ItemDescription, i.PartNumber ?? "", i.Quantity, i.Unit ?? "", i.IsCatalogued)).ToList() ?? [];

    private static MaterialRequisitionQuotationRow Row(PEMS.Application.DTOs.MRQDto q)
        => new(q.MRQId, q.MRQNumber, q.MRId, q.MRNumber, q.VendorName, q.InquiryDate, q.QuotedAmount, q.Currency ?? "", q.QuotationStatus, q.TechnicalEvaluation, q.CommercialEvaluation, q.ApprovalStatus, q.Notes ?? "");

    public async Task<List<MaterialRequisitionQuotationRow>> QuotationsAsync(int id) => (await service.GetByIdAsync(id))?.Quotations.Select(Row).ToList() ?? [];

    public async Task<List<MaterialRequisitionQuotationRow>> AllQuotationsAsync()
        => (await context.MaterialRequisitionQuotations.AsNoTracking().OrderByDescending(x => x.CreatedDate).ThenByDescending(x => x.MRQId).Take(ListLimit)
            .Select(q => new MaterialRequisitionQuotationRow(q.MRQId, q.MRQNumber, q.MRId, q.MR!.MRNumber, q.VendorName, q.InquiryDate, q.QuotedAmount, q.Currency ?? "", q.QuotationStatus, q.TechnicalEvaluation, q.CommercialEvaluation, q.ApprovalStatus, q.Notes ?? "")).ToListAsync());

    public async Task<List<MaterialRequisitionPartRow>> PartLookupAsync(string? q = null)
        => (await parts.SearchExistingPartsAsync(q)).Select(h => new MaterialRequisitionPartRow(h.PartNumber, string.IsNullOrWhiteSpace(h.Description) ? h.PartNumber : $"{h.PartNumber} — {h.Description}", h.Description ?? "", h.Unit ?? "")).ToList();

    public async Task<List<ReportChoiceRow>> ProjectsAsync()
        => (await context.Projects.AsNoTracking().Where(x => x.IsActive).Select(x => new { x.ProjectId, x.ProjectName }).ToListAsync()).Select(x => new ReportChoiceRow(x.ProjectId.ToString(CultureInfo.InvariantCulture), x.ProjectName)).ToList();

    public async Task<List<ReportChoiceRow>> DisciplinesAsync()
        => (await context.Disciplines.AsNoTracking().Where(x => x.IsActive).Select(x => new { x.DisciplineId, x.Name }).ToListAsync()).Select(x => new ReportChoiceRow(x.DisciplineId.ToString(CultureInfo.InvariantCulture), x.Name)).ToList();

    public async Task<List<ReportChoiceRow>> EngineersAsync()
        => (await context.Employees.AsNoTracking().Where(x => x.IsActive).Select(x => new { x.EmployeeId, Name = x.FirstName + " " + x.LastName }).ToListAsync()).Select(x => new ReportChoiceRow(x.EmployeeId.ToString(CultureInfo.InvariantCulture), x.Name)).ToList();

    public async Task<(bool Success, string? Error)> CreateAsync(string mrNumber, int projectId, int disciplineId, string? equipmentTagNumber, string description, DateTime? requiredDate, int? responsibleEngineerId, string? items)
    {
        var list = new List<(string desc, string? part, double qty, string? unit)>();
        foreach (var line in (items ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var cells = line.Split('|').Select(c => c.Trim()).ToArray();
            var desc = cells.Length > 0 ? cells[0] : "";
            var part = cells.Length > 1 && cells[1].Length > 0 ? cells[1] : null;
            if (desc.Length == 0 && part is null) continue;
            var qty = cells.Length > 2 && double.TryParse(cells[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : 1;
            list.Add((desc, part, qty, cells.Length > 3 && cells[3].Length > 0 ? cells[3] : null));
        }
        var (success, error, _) = await service.CreateAsync(mrNumber, projectId, disciplineId, equipmentTagNumber, description, requiredDate, responsibleEngineerId, null, list);
        return (success, error);
    }

    public async Task<(bool Success, string? Error)> ChangeStatusAsync(List<string> userRoles, int mrId, string status, PreProcurementActor actor)
    {
        if (status == "Approved" && !await permissions.HasPermissionAsync(userRoles, "MaterialRequisition", "Approve")) return (false, "You do not have permission for this action.");
        var (success, error) = await service.ChangeStatusAsync(mrId, status);
        if (success && status == "Issued to Procurement") await preProcurement.RaiseMaterialRequisitionAsync(mrId, actor);
        return (success, error);
    }

    public async Task<(bool Success, string? Error)> AddQuotationAsync(int mrId, string mrqNumber, string vendorName, DateTime? inquiryDate, decimal? quotedAmount, string? currency, string? notes)
    {
        var (success, error, _) = await service.AddQuotationAsync(mrId, mrqNumber, vendorName, inquiryDate, quotedAmount, currency, notes);
        return (success, error);
    }

    public async Task<(bool Success, string? Error)> UpdateQuotationEvaluationAsync(int mrqId, string technical, string commercial, string approval)
    {
        await service.UpdateQuotationEvaluationAsync(mrqId, technical, commercial, approval);
        return (true, null);
    }
}
