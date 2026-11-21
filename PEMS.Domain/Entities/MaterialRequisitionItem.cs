using System.ComponentModel.DataAnnotations;

namespace PEMS.Domain.Entities;

public class MaterialRequisitionItem
{
    [Key]
    public int MRItemId { get; set; }

    public int MRId { get; set; }
    public MaterialRequisition? MR { get; set; }

    public string ItemDescription { get; set; } = string.Empty;

    public string? PartNumber { get; set; }

    // SCRUM-428: پیوند به قطعه‌ی ثبت‌شده (Material Master یا درخواست تکمیل‌شده‌ی کاتالوگ قطعه) که سرویس از روی PartNumber پیدا کرد.
    // هر دو null = «کاتالوگ‌نشده» (متن آزاد). شرح و واحد همان لحظه‌ی ثبت در خود ردیف می‌ماند (snapshot).
    public int? MaterialItemId { get; set; }
    public MaterialItem? MaterialItem { get; set; }

    public int? PartCatalogueRequestId { get; set; }
    public PartCatalogueRequest? PartCatalogueRequest { get; set; }

    /// <summary>SCRUM-471: the pre-procurement item this line was added from (two-way origin link); null for lines typed by hand.</summary>
    public int? ProcurementDemandId { get; set; }
    public ProcurementDemand? ProcurementDemand { get; set; }

    public bool IsCatalogued => MaterialItemId != null || PartCatalogueRequestId != null;

    public double Quantity { get; set; } = 1;

    public string? Unit { get; set; }

    public string? Remarks { get; set; }
}
