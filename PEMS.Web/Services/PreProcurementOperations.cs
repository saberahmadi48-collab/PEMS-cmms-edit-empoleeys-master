using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;

namespace PEMS.Web.Services;

/// <summary>SCRUM-735: what PreProcurementController read, as operations over IPreProcurementService and the lookups of the decision form.</summary>
public sealed class PreProcurementOperations(IPreProcurementService service, PEMSDbContext context) : IPreProcurementOperations
{
    private static string Number(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    public async Task<List<PreProcurementQueueRow>> QueueAsync(string? status = null, string? source = null)
        => (await service.GetQueueAsync(string.IsNullOrWhiteSpace(status) ? null : status, string.IsNullOrWhiteSpace(source) ? null : source))
            .Select(d => new PreProcurementQueueRow(d.ProcurementDemandId, d.SourceType, d.SourceNumber ?? "", (d.MaterialCode is null ? "" : d.MaterialCode + " - ") + d.Description, d.Quantity, d.UnitName ?? "", d.WarehouseCode ?? "",
                d.Status, d.RaisedAt, d.RaisedBy ?? "", d.MaterialRequisitionNumber ?? "")).ToList();

    public async Task<List<PreProcurementDetailRow>> DetailAsync(int id)
    {
        var details = await service.GetAsync(id);
        if (details is null) return [];
        var d = details.Row;
        var isMr = d.SourceType == "MaterialRequisition";
        return [new PreProcurementDetailRow(d.ProcurementDemandId, d.SourceType, d.SourceNumber ?? "", (d.MaterialCode is null ? "" : d.MaterialCode + " - ") + d.Description, d.Quantity, d.UnitName ?? "", d.WarehouseCode ?? "",
            d.Status, d.RaisedAt, d.RaisedBy ?? "", d.MaterialRequisitionNumber ?? "", details.MaterialItemId.HasValue, details.DecisionReason ?? "", details.DecidedAt, details.DecidedBy ?? "", isMr, d.Status == "Open")];
    }

    public async Task<List<ReportChoiceRow>> DraftRequisitionsAsync()
        => (await context.MaterialRequisitions.AsNoTracking().Where(m => m.Status == "Draft" && m.IsActive).OrderByDescending(m => m.MRId).Take(200).Select(m => new { m.MRId, m.MRNumber, m.Description }).ToListAsync())
            .Select(m => new ReportChoiceRow(m.MRId.ToString(CultureInfo.InvariantCulture), $"{m.MRNumber} - {m.Description}")).ToList();

    public async Task<List<ReportChoiceRow>> ProjectsAsync()
        => (await context.Projects.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.ProjectCode).Select(p => new { p.ProjectId, p.ProjectCode, p.ProjectName }).ToListAsync())
            .Select(p => new ReportChoiceRow(p.ProjectId.ToString(CultureInfo.InvariantCulture), $"{p.ProjectCode} - {p.ProjectName}")).ToList();

    public async Task<List<ReportChoiceRow>> DisciplinesAsync()
        => (await context.Disciplines.AsNoTracking().Where(d => d.IsActive).OrderBy(d => d.Code).Select(d => new { d.DisciplineId, d.Code, d.Name }).ToListAsync())
            .Select(d => new ReportChoiceRow(d.DisciplineId.ToString(CultureInfo.InvariantCulture), $"{d.Code} - {d.Name}")).ToList();
}
