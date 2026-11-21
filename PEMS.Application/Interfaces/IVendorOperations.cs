namespace PEMS.Application.Interfaces;

/// <summary>One vendor of the list.</summary>
public sealed record VendorRow(int VendorId, string Name, string VendorCode, string Country, string ContactName, string ContactEmail, string ContactPhone, string RiskLevel, double? RiskScore, bool IsActive,
    int Contracts, int VdrItems, int OverdueVdrItems);

/// <summary>One vendor with its risk and qualification (one row).</summary>
public sealed record VendorDetailRow(int VendorId, string Name, string VendorCode, string Country, string ContactName, string ContactEmail, string ContactPhone, string RiskLevel, double? RiskScore, DateTime? RiskAssessedDate,
    bool IsActive, int Contracts, int VdrItems, int OverdueVdrItems, string QualificationStatus, string QualificationCategory, DateTime? QualificationEffective, DateTime? QualificationExpiry, string QualificationEvidence, string QualificationNotes);

/// <summary>One change of the qualification history (newest first).</summary>
public sealed record VendorQualificationHistoryItem(int Index, DateTime Date, string From, string To, string Category, DateTime? Effective, DateTime? Expiry, string Evidence, string Reason, string By);

/// <summary>
/// The vendors (SCRUM-763) as read operations over IVendorService and IVendorQualificationService (the permission is Vendors.View as in the old controller); create, edit and activate / deactivate are
/// the generic entity actions on the Vendor registration, recalculating the risk and setting the qualification are the operations marked on the two services.
/// </summary>
public interface IVendorOperations
{
    [PEMS.Application.Common.PemsOperation("Vendor", "List", "Vendors.View")]
    Task<List<VendorRow>> ListAsync();

    [PEMS.Application.Common.PemsOperation("Vendor", "Detail", "Vendors.View")]
    Task<List<VendorDetailRow>> DetailAsync(int id);

    [PEMS.Application.Common.PemsOperation("Vendor", "QualificationHistory", "Vendors.View")]
    Task<List<VendorQualificationHistoryItem>> QualificationHistoryAsync(int id);
}
