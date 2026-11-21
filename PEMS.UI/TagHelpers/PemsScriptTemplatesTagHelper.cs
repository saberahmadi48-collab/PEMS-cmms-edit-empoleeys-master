using Microsoft.AspNetCore.Razor.TagHelpers;
using PEMS.UI.Components;
using PEMS.UI.Rendering;

namespace PEMS.UI.TagHelpers;

/// <summary>
/// MVC facade for <see cref="PemsScriptTemplates"/>: <c>&lt;pems-script-templates set="dcc-register" /&gt;</c> renders the inert
/// <c>&lt;template data-pems-template&gt;</c>s that one page script clones (pems-template.js). Sets: dcc-register, letter-compose,
/// global-search, org-chart, org-position-chart.
/// </summary>
[HtmlTargetElement("pems-script-templates")]
[PemsFacade(UiKeys.ScriptTemplates)]
public sealed class PemsScriptTemplatesTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("set")] public string Set { get; set; } = "";

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        BlockTagHelper.Replace(output, await renderer.RenderAsync<PemsScriptTemplates>(new Dictionary<string, object?>
        {
            [nameof(PemsScriptTemplates.Options)] = new ScriptTemplatesOptions
            {
                Set = Enum.Parse<ScriptTemplateSet>(Set.Replace("-", "", StringComparison.Ordinal), ignoreCase: true)
            }
        }));
    }
}
