using ClosedXML.Excel;
using PEMS.Application.DTOs;
using PEMS.Web.Areas.Warehouse.Models;

namespace PEMS.Web.Helpers;

/// <summary>
/// SCRUM-478: the Part Catalogue report workbook (ClosedXML, styled with ExcelExportHelper like the list exports).
/// Display only: the rows were chosen by the report service. Sheets follow the page tabs:
/// summary (or none) = every sheet; otherwise only that tab's sheet (people, activity, equipment, parts, stages).
/// </summary>
public static class PartCatalogueReportExport
{
    public const string Summary = "summary", People = "people", Activity = "activity", Equipment = "equipment", Parts = "parts", Stages = "stages";

    public static byte[] Build(PartCatalogueReportDto r, PartCatalogueReportTexts t, string? sheet)
    {
        using var wb = new XLWorkbook();
        var all = string.IsNullOrEmpty(sheet) || sheet == Summary;

        if (all) AddSummary(wb, r, t);
        if (all || sheet == People) AddPeople(wb, r, t);
        if (all || sheet == Activity) AddActivity(wb, r, t);
        if (all || sheet == Equipment) AddEquipment(wb, r, t);
        if (all || sheet == Parts) AddParts(wb, r, t);
        if (all || sheet == Stages) AddStages(wb, r, t);
        if (wb.Worksheets.Count == 0) AddSummary(wb, r, t);

        foreach (var ws in wb.Worksheets) ws.RightToLeft = t.RightToLeft;
        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        return stream.ToArray();
    }

    private static IXLWorksheet Sheet(XLWorkbook wb, PartCatalogueReportTexts t, string key, params string[] headerKeys)
    {
        // Excel sheet names: at most 31 characters, no []:*?/\
        var name = new string(t[key].Where(c => !"[]:*?/\\".Contains(c)).ToArray());
        var ws = wb.Worksheets.Add(name.Length > 31 ? name[..31] : name);
        ws.AddStyledHeaderRow(headerKeys.Select(k => t[k]).ToArray());
        return ws;
    }

    private static void Done(IXLWorksheet ws, int lastDateColumn = 0)
    {
        if (lastDateColumn > 0) ws.Column(lastDateColumn).Style.DateFormat.Format = "yyyy-mm-dd";
        ws.FinalizeExportSheet();
    }

    private static void AddSummary(XLWorkbook wb, PartCatalogueReportDto r, PartCatalogueReportTexts t)
    {
        var ws = Sheet(wb, t, "PcRpt_Sheet_Summary", "PcRpt_Col_Item", "PcRpt_Col_Value");
        var p = r.Pipeline;
        var rows = new List<(string, XLCellValue)>
        {
            (t["PcRpt_Filter_Period"], t.PeriodDisplay(r)),
            (t["PcRpt_Kpi_Requests"], p.Total),
            (t["Pc_Type_Add"], p.Add),
            (t["Pc_Type_Change"], p.Change),
            (t["PcRpt_Kpi_Open"], p.Open),
            (t["PcRpt_Kpi_Completed"], p.CompletedInPeriod),
            (t["PcRpt_Kpi_Registered"], p.PartNumbersRegisteredInPeriod),
            (t["PcRpt_Kpi_AvgDays"], p.AverageDaysToComplete.HasValue ? (XLCellValue)p.AverageDaysToComplete.Value : ""),
            (t["PcRpt_Kpi_MedianDays"], p.MedianDaysToComplete.HasValue ? (XLCellValue)p.MedianDaysToComplete.Value : ""),
            (t["PcRpt_Kpi_Equipment"], r.Equipment.Equipment.Count),
            (t["PcRpt_Kpi_People"], r.Activity.People.Count)
        };
        if (!r.SeesAll) rows.Add((t["PcRpt_Note"], t["PcRpt_ScopeNote"]));
        if (r.Truncated) rows.Add((t["PcRpt_Note"], t.Format("PcRpt_Truncated", PartCatalogueReportDto.MaxRows)));
        var i = 2;
        foreach (var (label, value) in rows)
        {
            ws.Cell(i, 1).Value = label;
            ws.Cell(i, 2).Value = value;
            i++;
        }
        Done(ws);
    }

    private static void AddPeople(XLWorkbook wb, PartCatalogueReportDto r, PartCatalogueReportTexts t)
    {
        var ws = Sheet(wb, t, "PcRpt_Sheet_People", "PcRpt_Col_Person", "PcRpt_Col_Created", "PcRpt_Col_Submitted", "PcRpt_Col_Approved",
            "PcRpt_Col_Returned", "PcRpt_Col_Cancelled", "PcRpt_Col_Registered", "PcRpt_Col_Total");
        var i = 2;
        foreach (var p in r.Activity.People)
        {
            ws.Cell(i, 1).Value = p.EmployeeName;
            ws.Cell(i, 2).Value = p.Created;
            ws.Cell(i, 3).Value = p.Submitted;
            ws.Cell(i, 4).Value = p.Approved;
            ws.Cell(i, 5).Value = p.Returned;
            ws.Cell(i, 6).Value = p.Cancelled;
            ws.Cell(i, 7).Value = p.PartNumbersRegistered;
            ws.Cell(i, 8).Value = p.Total;
            i++;
        }
        Done(ws);
    }

    private static void AddActivity(XLWorkbook wb, PartCatalogueReportDto r, PartCatalogueReportTexts t)
    {
        var ws = Sheet(wb, t, "PcRpt_Sheet_Activity", "PcRpt_Col_Date", "PcRpt_Col_Person", "PcRpt_Col_Action", "PcRpt_Col_Request",
            "PcRpt_Col_Type", "PcRpt_Col_Mpn", "PcRpt_Col_PartNumber", "PcRpt_Col_TechClass", "PcRpt_Col_Status");
        var i = 2;
        foreach (var a in r.Activity.Rows)
        {
            ws.Cell(i, 1).Value = a.Date;
            ws.Cell(i, 2).Value = a.EmployeeName;
            ws.Cell(i, 3).Value = t.Action(a.Action);
            ws.Cell(i, 4).Value = a.RequestNumber ?? "#" + a.RequestId;
            ws.Cell(i, 5).Value = t.Type(a.RequestType);
            ws.Cell(i, 6).Value = a.ManufacturerPartNumber ?? "";
            ws.Cell(i, 7).Value = a.AssignedPartNumber ?? "";
            ws.Cell(i, 8).Value = a.TechnicalClass ?? "";
            ws.Cell(i, 9).Value = t.Status(a.Status);
            i++;
        }
        Done(ws, 1);
    }

    private static void AddEquipment(XLWorkbook wb, PartCatalogueReportDto r, PartCatalogueReportTexts t)
    {
        var ws = Sheet(wb, t, "PcRpt_Sheet_Equipment", "PcRpt_Col_Tag", "PcRpt_Col_EquipmentName", "PcRpt_Col_InRegister",
            "PcRpt_Col_Requests", "PcRpt_Col_RegisteredParts", "PcRpt_Col_InProgress", "PcRpt_Col_Critical");
        var i = 2;
        foreach (var e in r.Equipment.Equipment)
        {
            ws.Cell(i, 1).Value = e.EquipmentTag ?? "";
            ws.Cell(i, 2).Value = e.EquipmentName ?? "";
            ws.Cell(i, 3).Value = t.YesNo(e.InRegister);
            ws.Cell(i, 4).Value = e.Requests;
            ws.Cell(i, 5).Value = e.RegisteredPartNumbers;
            ws.Cell(i, 6).Value = e.InProgress;
            ws.Cell(i, 7).Value = e.Critical;
            i++;
        }
        Done(ws);
    }

    private static void AddParts(XLWorkbook wb, PartCatalogueReportDto r, PartCatalogueReportTexts t)
    {
        var ws = Sheet(wb, t, "PcRpt_Sheet_Parts", "PcRpt_Col_Tag", "PcRpt_Col_EquipmentName", "PcRpt_Col_InstalledQty",
            "PcRpt_Col_PartNumber", "PcRpt_Col_Request", "PcRpt_Col_Type", "PcRpt_Col_Status", "PcRpt_Col_Manufacturer", "PcRpt_Col_Mpn",
            "PcRpt_Col_Supplier", "PcRpt_Col_SupplierPn", "PcRpt_Col_TechClass", "PcRpt_Col_Commodity", "PcRpt_Col_Uom",
            "PcRpt_Col_Critical", "PcRpt_Col_Capital", "PcRpt_Col_Attributes", "PcRpt_Col_Requester", "PcRpt_Col_Created", "PcRpt_Col_CompletedDate");
        var i = 2;
        foreach (var p in r.Equipment.Parts)
        {
            ws.Cell(i, 1).Value = p.EquipmentTag ?? "";
            ws.Cell(i, 2).Value = p.EquipmentName ?? "";
            ws.Cell(i, 3).Value = p.InstalledQuantity;
            ws.Cell(i, 4).Value = p.AssignedPartNumber ?? "";
            ws.Cell(i, 5).Value = p.RequestNumber ?? "#" + p.RequestId;
            ws.Cell(i, 6).Value = t.Type(p.RequestType);
            ws.Cell(i, 7).Value = t.Status(p.Status);
            ws.Cell(i, 8).Value = p.ManufacturerName ?? "";
            ws.Cell(i, 9).Value = p.ManufacturerPartNumber ?? "";
            ws.Cell(i, 10).Value = p.SupplierName ?? "";
            ws.Cell(i, 11).Value = p.SupplierPartNumber ?? "";
            ws.Cell(i, 12).Value = p.TechnicalClass ?? "";
            ws.Cell(i, 13).Value = p.CommodityGroup ?? "";
            ws.Cell(i, 14).Value = p.UnitOfMeasure ?? "";
            ws.Cell(i, 15).Value = t.YesNo(p.IsCritical);
            ws.Cell(i, 16).Value = t.YesNo(p.IsCapital);
            ws.Cell(i, 17).Value = p.Attributes ?? "";
            ws.Cell(i, 18).Value = p.RequesterName ?? "";
            ws.Cell(i, 19).Value = p.CreatedDate;
            if (p.CompletedDate.HasValue) ws.Cell(i, 20).Value = p.CompletedDate.Value;
            i++;
        }
        ws.Column(19).Style.DateFormat.Format = "yyyy-mm-dd";
        Done(ws, 20);
    }

    private static void AddStages(XLWorkbook wb, PartCatalogueReportDto r, PartCatalogueReportTexts t)
    {
        var ws = Sheet(wb, t, "PcRpt_Sheet_Stages", "PcRpt_Col_Group", "PcRpt_Col_Item", "PcRpt_Col_Count");
        var i = 2;
        void Put(string group, string item, int count)
        {
            ws.Cell(i, 1).Value = group;
            ws.Cell(i, 2).Value = item;
            ws.Cell(i, 3).Value = count;
            i++;
        }
        var p = r.Pipeline;
        foreach (var s in p.ByStatus) Put(t["PcRpt_ByStatus"], t.Status(s.Key), s.Count);
        foreach (var s in p.ByStep) Put(t["PcRpt_ByStep"], t.Step(s), s.Count);
        foreach (var a in p.OpenAge) Put(t["PcRpt_OpenAge"], t.Days(a.Label), a.Count);
        foreach (var m in p.Months)
        {
            Put(t["PcRpt_Col_Created"], m.Month, m.Created);
            Put(t["PcRpt_Col_CompletedDate"], m.Month, m.Completed);
        }
        Done(ws);
    }
}
