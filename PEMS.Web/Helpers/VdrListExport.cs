using ClosedXML.Excel;
using PEMS.Application.DTOs;

namespace PEMS.Web.Helpers;

/// <summary>
/// SCRUM-409: ساخت فایل Excel فهرست VDR (همان ستون‌های فهرست Index) با ExcelExportHelper مشترک پروژه - مثل EngineeringDocumentListExport؛
/// بدون منطق کسب‌وکاری، فقط نمایش. ردیف‌ها قبلاً با فیلترهای فهرست انتخاب شده‌اند.
/// </summary>
public static class VdrListExport
{
    public static readonly string[] Headers =
        { "Document No", "Description", "Type", "Vendor", "Project", "Discipline", "Revision", "Latest Category", "Status", "Overdue" };

    public static byte[] Build(IReadOnlyList<VDRListItemDto> items)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("VDR");
        sheet.AddStyledHeaderRow(Headers);

        var row = 2;
        foreach (var i in items)
        {
            sheet.Cell(row, 1).Value = i.DocumentNumber;
            sheet.Cell(row, 2).Value = i.DocumentDescription;
            sheet.Cell(row, 3).Value = i.DocumentTypeCode;
            sheet.Cell(row, 4).Value = i.VendorName;
            sheet.Cell(row, 5).Value = i.ProjectName;
            sheet.Cell(row, 6).Value = i.DisciplineName;
            sheet.Cell(row, 7).Value = i.LatestRevisionNo;
            sheet.Cell(row, 8).Value = i.LatestReviewCategory;
            sheet.Cell(row, 9).Value = i.WorkflowStatus;
            sheet.Cell(row, 10).Value = i.IsOverdue ? "Yes" : "";
            row++;
        }

        sheet.FinalizeExportSheet();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
