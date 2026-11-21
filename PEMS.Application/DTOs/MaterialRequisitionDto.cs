namespace PEMS.Application.DTOs;

public class MRDto
{
    public int MRId { get; set; }
    public string MRNumber { get; set; } = "";
    public int ProjectId { get; set; }
    public string ProjectName { get; set; } = "";
    public int DisciplineId { get; set; }
    public string DisciplineName { get; set; } = "";
    public string? EquipmentTagNumber { get; set; }
    public string Description { get; set; } = "";
    public DateTime? RequiredDate { get; set; }
    public string? ResponsibleEngineerName { get; set; }
    public string Status { get; set; } = "";
    public DateTime CreatedDate { get; set; }
    public int QuotationsCount { get; set; }
    public List<MRItemDto> Items { get; set; } = new();
    public List<MRQDto> Quotations { get; set; } = new();
}

public class MRItemDto
{
    public int MRItemId { get; set; }
    public string ItemDescription { get; set; } = "";
    public string? PartNumber { get; set; }
    public double Quantity { get; set; }
    public string? Unit { get; set; }
    public string? Remarks { get; set; }

    /// <summary>SCRUM-428: linked to a registered part (Material Master or a completed part catalogue request).</summary>
    public bool IsCatalogued { get; set; }
}

public class MRQDto
{
    public int MRQId { get; set; }
    public string MRQNumber { get; set; } = "";
    public int MRId { get; set; }
    public string MRNumber { get; set; } = "";
    public string VendorName { get; set; } = "";
    public DateTime? InquiryDate { get; set; }
    public decimal? QuotedAmount { get; set; }
    public string? Currency { get; set; }
    public string QuotationStatus { get; set; } = "";
    public string TechnicalEvaluation { get; set; } = "";
    public string CommercialEvaluation { get; set; } = "";
    public string ApprovalStatus { get; set; } = "";
    public string? Notes { get; set; }
}
