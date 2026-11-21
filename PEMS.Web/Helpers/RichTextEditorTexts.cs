using System.Text.Json;
using Microsoft.Extensions.Localization;
using PEMS.Web.Resources;

namespace PEMS.Web.Helpers;

/// <summary>
/// The localised texts of the rich-text editor toolbar (SCRUM-59) as the JSON that <c>pems-rte.js</c> reads from
/// <c>data-pems-rte-texts</c>. One place, used by the layout and by the Letter adapter partial (which can run inside the embed layout).
/// The keys are the Quill toolbar control names.
/// </summary>
public static class RichTextEditorTexts
{
    private static readonly (string Key, string Resource)[] Map =
    {
        ("editor", "Rte_Editor"), ("bold", "Rte_Bold"), ("italic", "Rte_Italic"), ("underline", "Rte_Underline"), ("strike", "Rte_Strike"),
        ("sub", "Rte_Sub"), ("super", "Rte_Super"), ("ordered", "Rte_Ordered"), ("bullet", "Rte_Bullet"), ("outdent", "Rte_IndentLess"),
        ("indent", "Rte_IndentMore"), ("direction", "Rte_Direction"), ("blockquote", "Rte_Quote"), ("link", "Rte_Link"), ("image", "Rte_Image"),
        ("clean", "Rte_Clean"), ("font", "Rte_Font"), ("size", "Rte_Size"), ("color", "Rte_Color"), ("background", "Rte_Background"),
        ("header", "Rte_Header"), ("align", "Rte_Align"), ("uploadFailed", "Rte_UploadFailed")
    };

    public static string Json(IStringLocalizer<SharedResource> localizer) =>
        JsonSerializer.Serialize(Map.ToDictionary(m => m.Key, m => localizer[m.Resource].Value));
}
