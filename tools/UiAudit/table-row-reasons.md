# Row-loop tables that stay `pems-table` (SCRUM-570)

Generated from the converter's refusals; the ratchet counter `table-row` holds exactly these. Since slice 3, `pems-data-table` has `header="false"` (no thead) and row attributes (`rowAttrs`), and a cell may be a Razor template with the row's statements; what stays needs a feature below. Tables inside `pems-list-table` and `pems-related-table` are not counted: they are those families' rows.

## head-extra (6): conditional header cells (@if around a column) or markup in the header
- Areas/Admin/Views/PermissionMatrix/Index.cshtml:122
- Areas/Warehouse/Views/StoreRequisition/Details.cshtml:78
- Views/MaintenanceCalendar/Index.cshtml:143
- Views/PurchaseOrder/Details.cshtml:66
- Views/TaskManager/AdminSpace.cshtml:154
- Views/TaskManager/_ItemTable.cshtml:18

## loop-row (6): a row with statements that use await or a local render function, or a partial row
- Areas/Admin/Views/Users/Details.cshtml:219
- Areas/Admin/Views/Users/Details.cshtml:235
- Views/Letter/Dashboard.cshtml:95
- Views/Letter/Dashboard.cshtml:106
- Views/Letter/Dashboard.cshtml:114
- Views/Letter/Dashboard.cshtml:122

## colcount (5): cells and headers do not line up (colspan or a spanning cell)
- Areas/Admin/Views/Organization/Positions.cshtml:61
- Views/Discipline/Details.cshtml:35
- Views/Discipline/Manage.cshtml:33
- Views/MaterialRequisition/Details.cshtml:52
- Views/TaskManager/AdminSpace.cshtml:50

## loop-shape (4): the loop is inside an if/else or iterates dynamic items
- Views/Barcode/Index.cshtml:37
- Views/DashboardSettings/Index.cshtml:14
- Views/TableExtraction/Analyze.cshtml:86
- Views/Team/TeamDetails.cshtml:165

## header (3): header text with markup
- Areas/Admin/Views/Numbering/_VdrNumbers.cshtml:31
- Areas/Admin/Views/Organization/Index.cshtml:37
- Views/VDR/Details.cshtml:350

## unbalanced (2): inline C# the converter cannot balance
- Areas/Warehouse/Views/IrnReceipt/Osd.cshtml:32
- Views/EquipmentClassification/Coverage.cshtml:42

## attrq (2): attribute value with quotes the converter cannot carry
- Areas/Warehouse/Views/MrsImport/Review.cshtml:110
- Views/TableExtraction/Review.cshtml:69

## attr (2): cell attributes other than class, dir, title
- Views/Letter/Index.cshtml:561
- Views/Team/TeamDetails.cshtml:285

## attr:data-grid-key (2): the table carries data-grid-key for the grid script
- Views/MaterialRequisition/Quotations.cshtml:13
- Views/TableExtraction/Index.cshtml:21

## attrmix (1): attribute value mixing text and Razor
- Areas/Admin/Views/NavigationMenu/Index.cshtml:31

## classmix (1): class value mixing text and Razor
- Areas/Admin/Views/ProjectIntelligence/Index.cshtml:122

## loop-tail (1): text after the row inside the loop
- Views/EngineeringDocuments/Details.cshtml:692

## char& (1): a literal ampersand entity in a cell
- Views/EquipmentImport/Review.cshtml:43

## cells-extra (1): text outside cells in the row
- Views/Equipment/Details.cshtml:482
