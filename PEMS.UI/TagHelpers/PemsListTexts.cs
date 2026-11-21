namespace PEMS.UI.TagHelpers;

/// <summary>
/// SCRUM-337: the texts of the list-page frame (tool tips, screen-reader labels, buttons). The component holds no text
/// (SCRUM-416): the host localises them once and passes the record to <c>&lt;pems-list-page texts="..."&gt;</c>; every
/// child tag helper reads it from there. Page-specific texts (title, placeholder, New, empty message) stay attributes.
/// </summary>
public sealed record PemsListTexts(
    string Search,
    string SearchOpen,
    string SearchClose,
    string Filters,
    string ActiveFilters,
    string ApplyFilters,
    string ResetFilters,
    string Kpis,
    string Export,
    string ResetColumns,
    string Total,
    string RemoveFilter,
    string ResetAll,
    string NoResultsHint,
    string ClearAll);
