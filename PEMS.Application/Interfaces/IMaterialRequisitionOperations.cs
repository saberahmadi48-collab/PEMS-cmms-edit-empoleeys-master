namespace PEMS.Application.Interfaces;

/// <summary>One material requisition (MR) of the list.</summary>
public sealed record MaterialRequisitionRow(int MRId, string MRNumber, string Project, string Discipline, string Description, DateTime? RequiredDate, string Status, DateTime CreatedDate, int Quotations);

/// <summary>The head of one MR (one row).</summary>
public sealed record MaterialRequisitionDetailRow(int MRId, string MRNumber, string Project, string Discipline, string EquipmentTag, DateTime? RequiredDate, string Responsible, string Status, string Description);

/// <summary>One item line of an MR; InCatalogue tells whether the part number is registered.</summary>
public sealed record MaterialRequisitionItemRow(int MRItemId, string Description, string PartNumber, double Quantity, string Unit, bool InCatalogue);

/// <summary>One quotation (MRQ); the list of all quotations and the quotations of one MR share the row.</summary>
public sealed record MaterialRequisitionQuotationRow(int MRQId, string MRQNumber, int MRId, string MRNumber, string Vendor, DateTime? InquiryDate, decimal? QuotedAmount, string Currency, string Status, string Technical, string Commercial, string Approval, string Notes);

/// <summary>A registered part found by the part-number lookup.</summary>
public sealed record MaterialRequisitionPartRow(string PartNumber, string Text, string Description, string Unit);

/// <summary>
/// The material requisition (MR) and its quotations (MRQ) as operations (SCRUM-726): reads over IMaterialRequisitionService, the part lookup over IPartCatalogueDuplicateService, the lookups of the form, and
/// the commands. The item lines are typed one per line as "description | part number | quantity | unit" (a row with only a registered part number keeps; the service takes the description from the part).
/// </summary>
public interface IMaterialRequisitionOperations
{
    [PEMS.Application.Common.PemsOperation("MaterialRequisition", "List", "MaterialRequisition.View")]
    Task<List<MaterialRequisitionRow>> ListAsync();

    [PEMS.Application.Common.PemsOperation("MaterialRequisition", "Detail", "MaterialRequisition.View")]
    Task<List<MaterialRequisitionDetailRow>> DetailAsync(int id);

    [PEMS.Application.Common.PemsOperation("MaterialRequisition", "Items", "MaterialRequisition.View")]
    Task<List<MaterialRequisitionItemRow>> ItemsAsync(int id);

    [PEMS.Application.Common.PemsOperation("MaterialRequisition", "Quotations", "MaterialRequisition.View")]
    Task<List<MaterialRequisitionQuotationRow>> QuotationsAsync(int id);

    [PEMS.Application.Common.PemsOperation("MaterialRequisition", "AllQuotations", "MaterialRequisition.View")]
    Task<List<MaterialRequisitionQuotationRow>> AllQuotationsAsync();

    [PEMS.Application.Common.PemsOperation("MaterialRequisition", "PartLookup", "MaterialRequisition.Create")]
    Task<List<MaterialRequisitionPartRow>> PartLookupAsync(string? q = null);

    [PEMS.Application.Common.PemsOperation("MaterialRequisition", "Projects", "MaterialRequisition.Create")]
    Task<List<ReportChoiceRow>> ProjectsAsync();

    [PEMS.Application.Common.PemsOperation("MaterialRequisition", "Disciplines", "MaterialRequisition.Create")]
    Task<List<ReportChoiceRow>> DisciplinesAsync();

    [PEMS.Application.Common.PemsOperation("MaterialRequisition", "Engineers", "MaterialRequisition.Create")]
    Task<List<ReportChoiceRow>> EngineersAsync();

    [PEMS.Application.Common.PemsOperation("MaterialRequisition", "Create", "MaterialRequisition.Create")]
    Task<(bool Success, string? Error)> CreateAsync(string mrNumber, int projectId, int disciplineId, string? equipmentTagNumber, string description, DateTime? requiredDate, int? responsibleEngineerId, string? items);

    /// <summary>Changes the status; reaching Approved also needs the Approve permission, and an MR issued to procurement enters the procurement queue (SCRUM-471).</summary>
    [PEMS.Application.Common.PemsOperation("MaterialRequisition", "ChangeStatus", "MaterialRequisition.Edit")]
    Task<(bool Success, string? Error)> ChangeStatusAsync(List<string> userRoles, int mrId, string status, PreProcurementActor actor);

    [PEMS.Application.Common.PemsOperation("MaterialRequisition", "AddQuotation", "MaterialRequisition.Edit")]
    Task<(bool Success, string? Error)> AddQuotationAsync(int mrId, string mrqNumber, string vendorName, DateTime? inquiryDate, decimal? quotedAmount, string? currency, string? notes);

    [PEMS.Application.Common.PemsOperation("MaterialRequisition", "UpdateQuotationEvaluation", "MaterialRequisition.Edit")]
    Task<(bool Success, string? Error)> UpdateQuotationEvaluationAsync(int mrqId, string technical, string commercial, string approval);
}
