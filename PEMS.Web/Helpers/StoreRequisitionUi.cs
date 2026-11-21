using Microsoft.Extensions.Localization;
using PEMS.Web.Resources;

namespace PEMS.Web.Helpers;

public static class StoreRequisitionUi
{
    private const string IntakePrefix = "Warehouse intake of work order ";

    public static string Purpose(IStringLocalizer<SharedResource> localizer, string? purpose)
        => purpose?.StartsWith(IntakePrefix, StringComparison.Ordinal) == true
            ? localizer["WarehouseIntakePurpose", purpose[IntakePrefix.Length..]].Value
            : purpose ?? string.Empty;
}
