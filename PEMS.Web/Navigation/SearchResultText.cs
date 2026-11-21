using Microsoft.Extensions.Localization;
using PEMS.Application.DTOs;
using PEMS.Web.Resources;

namespace PEMS.Web.Navigation;

public static class SearchResultText
{
    public static string? SubLabel(GlobalSearchResultItemDto item, IStringLocalizer<SharedResource>? localizer)
    {
        if (string.IsNullOrWhiteSpace(item.SubLabelSuffixKey)) return item.SubLabel;
        var suffix = localizer?[item.SubLabelSuffixKey].Value ?? item.SubLabelSuffixKey;
        return string.IsNullOrWhiteSpace(item.SubLabel) ? suffix : $"{item.SubLabel} — {suffix}";
    }
}
