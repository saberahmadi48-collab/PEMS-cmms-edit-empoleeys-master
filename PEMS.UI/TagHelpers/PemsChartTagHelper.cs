using PEMS.UI.Rendering;
using Microsoft.AspNetCore.Razor.TagHelpers;
using PEMS.UI.Components;
using PEMS.Web.Models;

namespace PEMS.UI.TagHelpers;

// SCRUM-337 Chart: one chart host for every report and dashboard. The page describes the chart (BkChartSpec: kind, labels,
// series, colours from the --pems-chart-* tokens); the Bklit island in dist/dashboards/dashboards.js draws it from
// data-bk-chart. The visually hidden table (or line, for a gauge) is the accessible and no-script copy of the same numbers.
// The page loads the bundle once with the _BkChartStyles / _BkChartScripts partials. Usage: docs/design/LIST_PAGE_COMPONENT.md (Chart).

/// <summary><c>&lt;pems-chart spec="@Model.VolumeChart" /&gt;</c>: the chart host and its accessible table (the <c>chart</c> component writes both).</summary>
[HtmlTargetElement("pems-chart", TagStructure = TagStructure.WithoutEndTag)]
[PemsFacade(UiKeys.Chart)]
public sealed class PemsChartTagHelper(IPemsComponentRenderer renderer) : TagHelper
{
    [HtmlAttributeName("spec")] public BkChartSpec? Spec { get; set; }

    public override Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
        => ListRender.Replace<PemsChart>(renderer, output, new ChartOptions { Spec = Spec });
}
