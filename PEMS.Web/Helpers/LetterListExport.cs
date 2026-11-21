using System.Text;
using ClosedXML.Excel;
using PEMS.Application.DTOs;

namespace PEMS.Web.Helpers;

/// <summary>
/// خروجی CSV/Excel فهرست مکاتبات با همان ستون‌های Index. ردیف‌ها قبلاً توسط سرویس با همان فیلتر و
/// قانون محرمانگی انتخاب شده‌اند؛ این کلاس فقط قالب‌بندی می‌کند. متن‌های شروع‌شونده با = + - @ (Formula Injection)
/// با پیشوند ' خنثی می‌شوند، چون موضوع/فرستنده را کاربر وارد کرده است.
/// </summary>
public static class LetterListExport
{
    public static readonly string[] Headers =
    {
        "Letter No", "Type", "Date", "Subject", "Sender", "Receiver", "Project", "Discipline", "Priority",
        "Confidentiality", "Status", "Attachments", "Open Referrals", "Overdue Referral", "Response Due", "Last Activity"
    };

    /// <summary>Neutralises spreadsheet formulas in user-entered text.</summary>
    public static string Safe(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var first = value[0];
        return first is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;
    }

    private static string Day(DateTime? d) => d.HasValue ? d.Value.ToString("yyyy-MM-dd") : "";

    private static string[] Row(LetterDto l) => new[]
    {
        Safe(l.LetterNumber), l.LetterType, Day(l.LetterDate), Safe(l.Subject), Safe(l.Sender), Safe(l.Receiver),
        Safe(l.ProjectName), Safe(l.DisciplineName), l.Priority, l.ConfidentialityLevel, l.Status,
        l.AttachmentsCount.ToString(), l.OpenReferralsCount.ToString(), l.HasOverdueReferral ? "Yes" : "",
        Day(l.NextResponseDue), Day(l.LastActivityDate)
    };

    private static string CsvCell(string value)
        => value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;

    /// <summary>UTF-8 with BOM so Excel opens Persian text correctly.</summary>
    public static byte[] BuildCsv(IReadOnlyList<LetterDto> items)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(",", Headers.Select(CsvCell))).Append("\r\n");
        foreach (var l in items) sb.Append(string.Join(",", Row(l).Select(CsvCell))).Append("\r\n");
        return new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    public static byte[] BuildExcel(IReadOnlyList<LetterDto> items)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Correspondence");
        sheet.AddStyledHeaderRow(Headers);

        var r = 2;
        foreach (var l in items)
        {
            var values = Row(l);
            for (var c = 0; c < values.Length; c++)
                sheet.Cell(r, c + 1).SetValue(values[c]);   // always text: no auto-parsing of numbers/dates/formulas
            r++;
        }

        sheet.FinalizeExportSheet();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
