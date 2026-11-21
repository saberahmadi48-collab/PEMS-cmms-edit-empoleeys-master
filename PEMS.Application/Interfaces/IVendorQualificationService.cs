namespace PEMS.Application.Interfaces;

/// <summary>
/// طبق ممیزی Package Procurement Contract Finalization (§C/§E، Phase 1.1 + Phase 3): فاز ۱ فقط
/// مدل داده (Vendor.QualificationStatus/... + VendorQualificationHistory) را ساخت - این سرویس
/// لایه‌ی کسب‌وکاری روی آن است: بدون این، هیچ راهی برای واقعاً Qualified کردن یک فروشنده نبود و
/// گیت TenderService.InviteVendorAsync هرگز راضی نمی‌شد.
/// </summary>
public interface IVendorQualificationService
{
    /// <summary>SCRUM-544: the statuses a vendor can have, in the order the form offers them.</summary>
    public static readonly IReadOnlyList<string> Statuses = new[] { "NotQualified", "Pending", "Qualified", "Suspended", "Expired" };

    /// <summary>SCRUM-544: the current qualification of a vendor and its history (newest first); null when the vendor does not exist.</summary>
    Task<VendorQualificationView?> GetAsync(int vendorId);

    /// <summary>Error is a resource key (Vq_Err_*).</summary>
    [PEMS.Application.Common.PemsOperation("Vendor", "SetQualification", "Vendors.Edit")]
    Task<(bool Success, string? Error)> SetQualificationAsync(
        int vendorId, string newStatus, string? category, DateTime? effectiveDate, DateTime? expiryDate,
        string? evidenceReference, string? reason, int? actorEmployeeId);
}

public sealed record VendorQualificationView(string Status, string? Category, DateTime? EffectiveDate, DateTime? ExpiryDate,
    string? EvidenceReference, string? Notes, IReadOnlyList<VendorQualificationHistoryRow> History);

public sealed record VendorQualificationHistoryRow(DateTime ChangeDate, string? PreviousStatus, string NewStatus, string? Category,
    DateTime? EffectiveDate, DateTime? ExpiryDate, string? EvidenceReference, string? Reason, string? ChangedBy);
