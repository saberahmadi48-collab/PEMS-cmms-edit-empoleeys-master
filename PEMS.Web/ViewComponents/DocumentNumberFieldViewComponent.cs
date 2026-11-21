using Microsoft.AspNetCore.Mvc;
using PEMS.Application.Interfaces;

namespace PEMS.Web.ViewComponents;

/// <summary>What the document-number field needs to render.</summary>
public sealed record DocumentNumberFieldModel(string Name, string Label, string? Scale, string? Css, bool Compact, DocumentNumberField Field);

/// <summary>
/// SCRUM-40: the number field of a create form. When the administrator's numbering scheme is active the number is assigned on save:
/// the field shows the next number as a hint and posts nothing (or posts an optional manual number when the scheme allows it).
/// Without an active scheme it is the old required text box.
/// Usage: <c>@await Component.InvokeAsync("DocumentNumberField", new { documentType = "PurchaseOrder", name = "poNumber", label = Localizer["PO Number"].Value })</c>
/// </summary>
public class DocumentNumberFieldViewComponent : ViewComponent
{
    private readonly INumberingSchemeService _schemes;

    public DocumentNumberFieldViewComponent(INumberingSchemeService schemes) => _schemes = schemes;

    /// <param name="compact">inline forms: the label is for screen readers only and the hint is a tool tip</param>
    public async Task<IViewComponentResult> InvokeAsync(string documentType, string name, string label, string? scale = null, string? css = null, bool compact = false)
        => View(new DocumentNumberFieldModel(name, label, scale, css, compact, await _schemes.GetFieldAsync(documentType)));
}
