using Microsoft.AspNetCore.Razor.TagHelpers;
using PEMS.UI.Rendering;

namespace PEMS.UI.TagHelpers;

// SCRUM-339 (slice 6): the parts of a table whose cells hold markup (links, buttons, forms, conditionals). They are registry Parts of the table components
// (pems-table, pems-list-table, pems-related-table): each writes its HTML element with the page's attributes and content as they are, in the tag helper
// pipeline itself, so a table with thousands of cells renders no component per cell. A table whose cells are plain values uses pems-data-table instead.

/// <summary>Base of the table parts: writes the element named by <see cref="Element"/>; attributes (class, colspan, data-*, title ...) and content are the page's own.</summary>
public abstract class TablePartTagHelper(string element) : TagHelper
{
    public string Element { get; } = element;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = Element;
        output.TagMode = TagMode.StartTagAndEndTag;
    }
}

/// <summary><c>&lt;pems-table-head&gt;</c> writes <c>&lt;thead&gt;</c>.</summary>
[HtmlTargetElement("pems-table-head")]
[PemsFacade(FacadeKind.Part)]
public sealed class PemsTableHeadTagHelper() : TablePartTagHelper("thead");

/// <summary><c>&lt;pems-table-body&gt;</c> writes <c>&lt;tbody&gt;</c>.</summary>
[HtmlTargetElement("pems-table-body")]
[PemsFacade(FacadeKind.Part)]
public sealed class PemsTableBodyTagHelper() : TablePartTagHelper("tbody");

/// <summary><c>&lt;pems-table-foot&gt;</c> writes <c>&lt;tfoot&gt;</c>.</summary>
[HtmlTargetElement("pems-table-foot")]
[PemsFacade(FacadeKind.Part)]
public sealed class PemsTableFootTagHelper() : TablePartTagHelper("tfoot");

/// <summary><c>&lt;pems-table-caption&gt;</c> writes <c>&lt;caption&gt;</c>.</summary>
[HtmlTargetElement("pems-table-caption")]
[PemsFacade(FacadeKind.Part)]
public sealed class PemsTableCaptionTagHelper() : TablePartTagHelper("caption");

/// <summary><c>&lt;pems-table-row&gt;</c> writes <c>&lt;tr&gt;</c>.</summary>
[HtmlTargetElement("pems-table-row")]
[PemsFacade(FacadeKind.Part)]
public sealed class PemsTableRowTagHelper() : TablePartTagHelper("tr");

/// <summary><c>&lt;pems-table-header&gt;</c> writes <c>&lt;th&gt;</c> (<c>scope</c>, <c>colspan</c>, <c>class</c> are the page's own).</summary>
[HtmlTargetElement("pems-table-header")]
[PemsFacade(FacadeKind.Part)]
public sealed class PemsTableHeaderTagHelper() : TablePartTagHelper("th");

/// <summary><c>&lt;pems-table-cell&gt;</c> writes <c>&lt;td&gt;</c> (<c>colspan</c>, <c>rowspan</c>, <c>class</c> are the page's own).</summary>
[HtmlTargetElement("pems-table-cell")]
[PemsFacade(FacadeKind.Part)]
public sealed class PemsTableCellTagHelper() : TablePartTagHelper("td");
