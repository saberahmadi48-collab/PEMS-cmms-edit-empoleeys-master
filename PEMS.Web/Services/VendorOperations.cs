using PEMS.Application.Interfaces;

namespace PEMS.Web.Services;

/// <summary>SCRUM-763: what VendorController showed, as operations over IVendorService and IVendorQualificationService.</summary>
public sealed class VendorOperations(IVendorService vendors, IVendorQualificationService qualification) : IVendorOperations
{
    public async Task<List<VendorRow>> ListAsync()
        => (await vendors.GetPagedAsync(1, 2000)).Items.Select(v => new VendorRow(v.VendorId, v.Name, v.VendorCode ?? "", v.Country ?? "", v.ContactName ?? "", v.ContactEmail ?? "", v.ContactPhone ?? "", v.RiskLevel, v.RiskScore, v.IsActive,
            v.ContractsCount, v.VDRItemsCount, v.OverdueVDRItemsCount)).ToList();

    public async Task<List<VendorDetailRow>> DetailAsync(int id)
    {
        var v = await vendors.GetByIdAsync(id);
        if (v is null) return [];
        var q = await qualification.GetAsync(id);
        return [new VendorDetailRow(v.VendorId, v.Name, v.VendorCode ?? "", v.Country ?? "", v.ContactName ?? "", v.ContactEmail ?? "", v.ContactPhone ?? "", v.RiskLevel, v.RiskScore, v.RiskAssessedDate, v.IsActive,
            v.ContractsCount, v.VDRItemsCount, v.OverdueVDRItemsCount, q?.Status ?? "NotQualified", q?.Category ?? "", q?.EffectiveDate, q?.ExpiryDate, q?.EvidenceReference ?? "", q?.Notes ?? "")];
    }

    public async Task<List<VendorQualificationHistoryItem>> QualificationHistoryAsync(int id)
    {
        var q = await qualification.GetAsync(id);
        var i = 0;
        return q?.History.Select(h => new VendorQualificationHistoryItem(++i, h.ChangeDate, h.PreviousStatus ?? "", h.NewStatus, h.Category ?? "", h.EffectiveDate, h.ExpiryDate, h.EvidenceReference ?? "", h.Reason ?? "", h.ChangedBy ?? "")).ToList() ?? [];
    }
}
