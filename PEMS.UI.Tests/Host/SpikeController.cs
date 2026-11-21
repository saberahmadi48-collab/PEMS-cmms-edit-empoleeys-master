using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PEMS.UI.Abstractions;

namespace PEMS.UI.Tests.Host;

public sealed class SpikeModel
{
    [Required, StringLength(50)] public string? Name { get; set; }
    [Range(1, 100)] public int Quantity { get; set; }
    public DateTime? Due { get; set; }
    public bool Active { get; set; }
    public decimal Price { get; set; }
    public IFormFile? Attachment { get; set; }
}

public sealed class TypedModel
{
    public int Quantity { get; set; }
    public int? OptionalCount { get; set; }
    public long Big { get; set; }
    public decimal Price { get; set; }
    public decimal? OptionalPrice { get; set; }
    public double Ratio { get; set; }
    public double? OptionalRatio { get; set; }
    public DateTime? Due { get; set; }
    public DateOnly? Day { get; set; }
}

[AutoValidateAntiforgeryToken]
public sealed class SpikeController(IAntiforgery antiforgery) : Controller
{
    private const int Rows = 1000;

    // Option A: View Component
    [HttpGet("/a/table")] public IActionResult TableA() => View("TableA");

    // Option B: static Razor component rendered from an MVC view
    [HttpGet("/b/table")] public IActionResult TableB() => View("TableB");
    [HttpGet("/b/table-percell")] public IActionResult TableBPerCell() => View("TableBPerCell");
    [HttpGet("/b/descriptor-table")] public IActionResult DescriptorTable() => View("DescriptorTable");
    [HttpGet("/b/template-cell")] public IActionResult TemplateCell() => View("TemplateCell");   // SCRUM-570
    [HttpGet("/b/perf-hand")] public IActionResult PerfHand() => View("PerfHand");   // SCRUM-570: the same 1000 rows as hand-written pems-table markup ...
    [HttpGet("/b/perf-template")] public IActionResult PerfTemplate() => View("PerfTemplate");   // ... and as pems-data-table with a template cell
    [HttpGet("/b/antiforgery-probe")] public IActionResult AntiforgeryProbe() => View("AntiforgeryProbe");

    // Form through the Tag Helper facade: model binding + data-annotation validation + antiforgery by the MVC form
    [HttpGet("/b/form")] public IActionResult Form() => View("Form", new SpikeModel());

    [HttpPost("/b/form")]
    public IActionResult FormPost(SpikeModel model)
    {
        if (!ModelState.IsValid) return View("Form", model);
        return Content($"saved:{model.Name}:{model.Quantity}:{model.Due:yyyy-MM-dd}:{model.Active}:{model.Attachment?.Length}:{model.Price.ToString(System.Globalization.CultureInfo.InvariantCulture)}");   // invariant: the output is a test probe, not display
    }

    // Form rendered entirely from a FormDefinition; the antiforgery token is passed in as a parameter
    [HttpGet("/b/descriptor-form")]
    public IActionResult DescriptorForm()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        ViewData["Token"] = tokens.RequestToken;
        ViewData["TokenName"] = tokens.FormFieldName;
        return View("DescriptorForm");
    }

    [HttpPost("/b/descriptor-form")]
    public IActionResult DescriptorFormPost([FromForm] SpikeModel model, [FromForm] string? Name)
        => Content($"saved:{model.Name}:{model.Attachment?.Length}");

    // Plain typed binding, no validation attributes: shows exactly what the model binder made of each posted value
    [HttpPost("/b/typed")]
    public IActionResult Typed(TypedModel m)
    {
        var errors = string.Join(";", ModelState.Where(e => e.Value!.Errors.Count > 0).Select(e => $"{e.Key}={e.Value!.Errors[0].ErrorMessage}"));
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return Content($"Quantity={m.Quantity.ToString(inv)}|OptionalCount={m.OptionalCount?.ToString(inv) ?? "null"}|Big={m.Big.ToString(inv)}|Price={m.Price.ToString(inv)}|OptionalPrice={m.OptionalPrice?.ToString(inv) ?? "null"}|Ratio={m.Ratio.ToString(inv)}|OptionalRatio={m.OptionalRatio?.ToString(inv) ?? "null"}|Due={m.Due?.ToString("yyyy-MM-dd HH:mm", inv) ?? "null"}|Day={m.Day?.ToString("yyyy-MM-dd", inv) ?? "null"}|errors={errors}");
    }

    public static int RowCount => Rows;
}
