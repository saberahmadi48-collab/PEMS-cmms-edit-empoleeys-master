namespace PEMS.Web.Helpers;

/// <summary>
/// نگاشت (Module, BusinessObjectType) موتور گردش‌کار به صفحه‌ی جزئیات رکورد. فقط نگاشت‌هایی که مقصدشان در کد بررسی شده آمده است؛
/// برای هر ترکیب ناشناخته null برمی‌گردد (UI بدون لینک نمایش می‌دهد، لینک حدسی ساخته نمی‌شود). مجوز دسترسی به صفحه‌ی مقصد همچنان
/// با خود همان کنترلر اعمال می‌شود؛ این کلاس مجوزی نمی‌دهد.
/// </summary>
public static class WorkflowTaskLinks
{
    public static string? TryGetPath(string? module, string? businessObjectType, int id)
    {
        if (id <= 0 || string.IsNullOrWhiteSpace(businessObjectType)) return null;

        return businessObjectType switch
        {
            "Permit" => $"/Permit/Details/{id}",
            "MOC" => $"/p/moc-details?id={id}",
            "TechnicalQuery" => $"/p/technical-query-details?id={id}",
            "VDRItem" => $"/VDR/Details/{id}",
            "Document" => $"/EngineeringDocuments/Details/{id}",
            "PartCatalogueRequest" => $"/p/pc-request-details?id={id}",
            "NonConformanceReport" => $"/QC/NcrDetails/{id}",
            "Contract" => $"/p/contract-details?id={id}",
            "IrnReceiptDisposition" => $"/Warehouse/IrnReceipt/Details/{id}",
            "IrnHold" => $"/Warehouse/IrnReceipt/Details/{id}",   // SCRUM-473: id = IRN
            "WorkOrder" or "WorkOrderLifecycle" or "WorkOrderMultiDisciplineReview" => $"/WorkOrder/Details/{id}",
            "WorkRequest" => $"/WorkRequest/Details/{id}",
            "WorkOrderMaterialIntake" => $"/p/warehouse-intake?id={id}",   // SCRUM-472: id = work order
            "ProcurementDemand" => $"/p/pre-procurement-details?id={id}",                      // SCRUM-471
            "PurchaseOrder" => $"/p/purchase-order-details?id={id}",                          // SCRUM-470
            _ => null
        };
    }
}
