using Microsoft.EntityFrameworkCore;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

public class VendorQualificationService : IVendorQualificationService
{
    private static IReadOnlyList<string> ValidStatuses => IVendorQualificationService.Statuses;

    private readonly PEMSDbContext _context;

    public VendorQualificationService(PEMSDbContext context)
    {
        _context = context;
    }

    public async Task<VendorQualificationView?> GetAsync(int vendorId)
    {
        var v = await _context.Vendors.AsNoTracking().Where(x => x.VendorId == vendorId)
            .Select(x => new { x.QualificationStatus, x.QualificationCategory, x.QualificationEffectiveDate, x.QualificationExpiryDate, x.QualificationEvidenceReference, x.QualificationNotes })
            .FirstOrDefaultAsync();
        if (v == null) return null;
        var history = await _context.VendorQualificationHistories.AsNoTracking().Where(h => h.VendorId == vendorId)
            .OrderByDescending(h => h.ChangeDate).ThenByDescending(h => h.VendorQualificationHistoryId)
            .Select(h => new VendorQualificationHistoryRow(h.ChangeDate, h.PreviousStatus, h.NewStatus, h.Category, h.EffectiveDate, h.ExpiryDate,
                h.EvidenceReference, h.Reason, h.ChangedBy == null ? null : h.ChangedBy.FirstName + " " + h.ChangedBy.LastName))
            .ToListAsync();
        return new VendorQualificationView(v.QualificationStatus, v.QualificationCategory, v.QualificationEffectiveDate, v.QualificationExpiryDate,
            v.QualificationEvidenceReference, v.QualificationNotes, history);
    }

    public async Task<(bool Success, string? Error)> SetQualificationAsync(
        int vendorId, string newStatus, string? category, DateTime? effectiveDate, DateTime? expiryDate,
        string? evidenceReference, string? reason, int? actorEmployeeId)
    {
        // SCRUM-544: errors are resource keys, so the page shows them in its language
        if (!ValidStatuses.Contains(newStatus))
            return (false, "Vq_Err_Status");
        if (effectiveDate.HasValue && expiryDate.HasValue && expiryDate.Value.Date < effectiveDate.Value.Date)
            return (false, "Vq_Err_Dates");

        var vendor = await _context.Vendors.FirstOrDefaultAsync(x => x.VendorId == vendorId);
        if (vendor == null) return (false, "Vq_Err_NotFound");

        category = Clean(category, 100);
        evidenceReference = Clean(evidenceReference, 500);
        reason = Clean(reason, 1000);

        // طبق §G: تغییر وضعیت append-only ثبت می‌شود - فیلدهای روی Vendor فقط Cache آخرین ردیف‌اند
        _context.VendorQualificationHistories.Add(new VendorQualificationHistory
        {
            VendorId = vendorId,
            PreviousStatus = vendor.QualificationStatus,
            NewStatus = newStatus,
            Category = category,
            EffectiveDate = effectiveDate,
            ExpiryDate = expiryDate,
            EvidenceReference = evidenceReference,
            ChangedById = actorEmployeeId,
            ChangeDate = DateTime.Now,
            Reason = reason
        });

        vendor.QualificationStatus = newStatus;
        vendor.QualificationCategory = category;
        vendor.QualificationEffectiveDate = effectiveDate;
        vendor.QualificationExpiryDate = expiryDate;
        vendor.QualificationEvidenceReference = evidenceReference;
        vendor.QualificationReviewedById = actorEmployeeId;
        vendor.QualificationNotes = reason;

        await _context.SaveChangesAsync();
        return (true, null);
    }

    private static string? Clean(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        return s.Length <= max ? s : s[..max];
    }
}
