using PEMS.Domain.Entities;

namespace PEMS.Web.Helpers;

/// <summary>SCRUM-538: the details page of a document by its type (Documents is the shared base of four subtypes).</summary>
/// <remarks>Each target page checks its own permissions; this only chooses the page.</remarks>
public static class DocumentLinks
{
    public static (string Controller, int Id)? Target(Document? document) => document switch
    {
        EngineeringDocument e => ("EngineeringDocuments", e.DocumentId),
        VDRItem v => ("VDR", v.DocumentId),
        ContractDocument c => ("Contract", c.ContractId),
        MaintenanceDocument m when m.WorkOrderId is int workOrderId => ("WorkOrder", workOrderId),
        MaintenanceDocument m => ("Equipment", m.EquipmentId),
        _ => null
    };
}
