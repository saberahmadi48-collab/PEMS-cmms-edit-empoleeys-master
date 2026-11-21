using Microsoft.Extensions.Localization;
using PEMS.UI.TagHelpers;
using PEMS.Web.Resources;

namespace PEMS.Web.Helpers;

/// <summary>
/// SCRUM-337: the localised texts of the list-page component (PEMS.UI holds no text). One call per list page:
/// <c>&lt;pems-list-page texts="@ListPageTexts.From(Localizer)"&gt;</c>.
/// </summary>
public static class ListPageTexts
{
    public static PemsListTexts From(IStringLocalizer<SharedResource> l) => new(
        Search: l["Search"],
        SearchOpen: l["Search"],
        SearchClose: l["Lp_SearchClose"],
        Filters: l["Lp_Filters"],
        ActiveFilters: l["Lp_ActiveFilters"],
        ApplyFilters: l["Lp_ApplyFilters"],
        ResetFilters: l["Reset"],
        Kpis: l["Lp_Kpis"],
        Export: l["ExportFilteredList"],
        ResetColumns: l["ResetColumns"],
        Total: l["Lp_Total"],
        RemoveFilter: l["Lp_RemoveFilter"],
        ResetAll: l["Lp_ResetAll"],
        NoResultsHint: l["Lp_NoResultsHint"],
        ClearAll: l["Lt_ClearAll"]);
}
