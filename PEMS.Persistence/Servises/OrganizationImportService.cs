using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Organization;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>
/// Import کنترل‌شده‌ی ساختار سازمانی. کل فایل ابتدا در حافظه اعتبارسنجی می‌شود؛ هر خطا ⇒ هیچ چیزی ذخیره نمی‌شود (همه یا هیچ).
/// سلسله‌مراتب فقط از ParentPositionCode خودِ فایل ساخته می‌شود: مدیری حدس زده نمی‌شود، دپارتمان/کارمند ساخته نمی‌شود،
/// دارنده‌ی فعلیِ یک سمت بی‌صدا جایگزین نمی‌شود، و تخصیص اصلیِ موجود کارمند بی‌صدا تغییر نمی‌کند.
/// ستون‌ها: Department, Position|PositionTitle, PositionCode, ParentPositionCode, EmployeePersonnelCode, IsPrimary, AssignmentType, EffectiveFrom, EffectiveTo.
/// </summary>
public class OrganizationImportService : IOrganizationImportService
{
    public static readonly string[] Columns =
        { "Department", "Position", "PositionCode", "PositionTitle", "ParentPositionCode", "EmployeePersonnelCode", "IsPrimary", "AssignmentType", "EffectiveFrom", "EffectiveTo" };

    private readonly PEMSDbContext _ctx;

    public OrganizationImportService(PEMSDbContext ctx) => _ctx = ctx;

    private sealed class Row
    {
        public int No;
        public string Department = "", Title = "", Code = "", ParentCode = "", Personnel = "", AssignmentType = "";
        public bool IsPrimary = true;
        public DateTime? From, To;
    }

    public async Task<byte[]> BuildTemplateAsync(CancellationToken ct = default)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Organization");
        for (var i = 0; i < Columns.Length; i++) ws.Cell(1, i + 1).Value = Columns[i];
        ws.Row(1).Style.Font.Bold = true;

        // برگه‌های مرجع (فقط خواندنی برای تهیه‌کننده‌ی فایل؛ Import فقط برگه‌ی اول را می‌خواند)
        var depts = wb.AddWorksheet("Ref-Departments");
        depts.Cell(1, 1).Value = "Department"; depts.Row(1).Style.Font.Bold = true;
        var r = 2;
        foreach (var d in await _ctx.Departments.AsNoTracking().Where(d => d.IsActive).OrderBy(d => d.Name).Select(d => d.Name).ToListAsync(ct)) depts.Cell(r++, 1).Value = d;

        var emps = wb.AddWorksheet("Ref-Employees");
        emps.Cell(1, 1).Value = "EmployeePersonnelCode"; emps.Cell(1, 2).Value = "FirstName"; emps.Cell(1, 3).Value = "LastName"; emps.Cell(1, 4).Value = "Active"; emps.Row(1).Style.Font.Bold = true;
        r = 2;
        foreach (var e in await _ctx.Employees.AsNoTracking().OrderBy(e => e.LastName).ThenBy(e => e.FirstName)
                     .Select(e => new { e.PersonnelCode, e.FirstName, e.LastName, e.IsActive }).ToListAsync(ct))
        {
            emps.Cell(r, 1).Value = e.PersonnelCode ?? ""; emps.Cell(r, 2).Value = e.FirstName; emps.Cell(r, 3).Value = e.LastName; emps.Cell(r, 4).Value = e.IsActive ? "Yes" : "No"; r++;
        }

        var poss = wb.AddWorksheet("Ref-Positions");
        poss.Cell(1, 1).Value = "PositionCode"; poss.Cell(1, 2).Value = "PositionTitle"; poss.Cell(1, 3).Value = "Active"; poss.Row(1).Style.Font.Bold = true;
        r = 2;
        foreach (var p in await _ctx.Positions.AsNoTracking().Where(p => p.PositionCode != null).OrderBy(p => p.PositionCode).Select(p => new { p.PositionCode, p.Title, p.IsActive }).ToListAsync(ct))
        { poss.Cell(r, 1).Value = p.PositionCode; poss.Cell(r, 2).Value = p.Title; poss.Cell(r, 3).Value = p.IsActive ? "Yes" : "No"; r++; }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<OrganizationImportResult> ImportAsync(Stream xlsx, bool commit, CancellationToken ct = default)
    {
        var result = new OrganizationImportResult();
        var rows = Read(xlsx, result);
        if (result.Errors.Count > 0) return result;
        if (rows.Count == 0) { result.Errors.Add("The file has no data rows."); return result; }

        var now = DateTime.Now;
        var depts = await _ctx.Departments.AsNoTracking().Select(d => new { d.DepartmentId, d.Name, d.IsActive }).ToListAsync(ct);
        var deptByName = depts.GroupBy(d => d.Name.Trim().ToUpperInvariant()).ToDictionary(g => g.Key, g => g.First());
        var positions = await _ctx.Positions.Where(p => p.PositionCode != null).ToListAsync(ct);          // tracked (updates)
        var posByCode = positions.ToDictionary(p => p.PositionCode!.ToUpperInvariant());
        var allPositionParents = await _ctx.Positions.AsNoTracking().Select(p => new { p.PositionId, p.PositionCode, p.ParentPositionId }).ToListAsync(ct);
        var employees = await _ctx.Employees.AsNoTracking().Where(e => e.PersonnelCode != null)
            .Select(e => new Emp(e.EmployeeId, e.PersonnelCode!, e.IsActive)).ToListAsync(ct);
        var empByCode = employees.GroupBy(e => e.PersonnelCode.Trim().ToUpperInvariant()).ToDictionary(g => g.Key, g => g.First());
        var codeById = allPositionParents.ToDictionary(p => p.PositionId, p => p.PositionCode);
        var deptIds = deptByName.ToDictionary(k => k.Key, k => k.Value.DepartmentId);
        var activeAssigns = await _ctx.EmployeeAssignments.Where(a => a.IsActive).ToListAsync(ct);          // tracked (updates)

        void Err(Row r, string msg) => result.Errors.Add($"Row {r.No}: {msg}");

        // ---- ۱) تعریف سمت‌ها (یکنواخت بودن ردیف‌های هم‌کد)
        var defs = new Dictionary<string, Row>();
        foreach (var r in rows)
        {
            if (r.Code.Length == 0) { Err(r, "PositionCode is required."); continue; }
            if (r.Code.Length > 50) { Err(r, "PositionCode is longer than 50 characters."); continue; }
            var key = r.Code.ToUpperInvariant();
            if (defs.TryGetValue(key, out var first))
            {
                if (!string.Equals(first.Title, r.Title, StringComparison.Ordinal) || !string.Equals(first.Department, r.Department, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(first.ParentCode, r.ParentCode, StringComparison.OrdinalIgnoreCase))
                    Err(r, $"PositionCode '{r.Code}' is defined again with different title/department/parent (first defined in row {first.No}).");
                continue;
            }
            defs[key] = r;
            if (posByCode.TryGetValue(key, out var inDb) && !inDb.IsActive) Err(r, $"Position '{r.Code}' exists but is inactive.");
            if (r.Title.Length == 0) Err(r, "Position title is required.");
            // Department اختیاری است (Position.DepartmentId nullable: مثلاً مدیرعامل/سطح اجرایی). خالی = «تعیین نشده»؛ اگر پر باشد باید موجود و فعال باشد (هرگز ساخته نمی‌شود).
            if (r.Department.Length > 0)
            {
                if (!deptByName.TryGetValue(r.Department.ToUpperInvariant(), out var d)) Err(r, $"Unknown department '{r.Department}'.");
                else if (!d.IsActive) Err(r, $"Department '{r.Department}' is inactive.");
            }
        }

        // ---- ۲) والدها + دور (گراف نهایی = DB با override فایل)
        var finalParent = allPositionParents.Where(p => p.PositionCode != null && !defs.ContainsKey(p.PositionCode.ToUpperInvariant()))
            .ToDictionary(p => p.PositionCode!.ToUpperInvariant(), p => p.ParentPositionId.HasValue
                ? allPositionParents.FirstOrDefault(x => x.PositionId == p.ParentPositionId.Value)?.PositionCode?.ToUpperInvariant() : null);
        foreach (var kv in defs)
        {
            var r = kv.Value;
            string? parentKey = null;
            if (r.ParentCode.Length > 0)
            {
                parentKey = r.ParentCode.ToUpperInvariant();
                if (parentKey == kv.Key) Err(r, $"Position '{r.Code}' is its own parent (SELF_PARENT).");
                else if (!defs.ContainsKey(parentKey) && !posByCode.ContainsKey(parentKey)) Err(r, $"Unknown parent position code '{r.ParentCode}'.");
                else if (!defs.ContainsKey(parentKey) && posByCode.TryGetValue(parentKey, out var pp) && !pp.IsActive) Err(r, $"Parent position '{r.ParentCode}' is inactive.");
            }
            finalParent[kv.Key] = parentKey;
        }
        foreach (var start in defs.Keys)
        {
            var seen = new HashSet<string> { start };
            var cur = finalParent.GetValueOrDefault(start);
            while (cur != null)
            {
                if (cur == start || !seen.Add(cur)) { Err(defs[start], $"The parent chain of '{defs[start].Code}' contains a cycle (HIERARCHY_CYCLE)."); break; }
                cur = finalParent.GetValueOrDefault(cur);
            }
        }

        // ---- ۳) تخصیص‌ها
        var holderRows = rows.Where(r => r.Personnel.Length > 0 && r.Code.Length > 0).ToList();
        foreach (var grp in holderRows.GroupBy(r => r.Code.ToUpperInvariant()))
            if (grp.Select(r => r.Personnel.ToUpperInvariant()).Distinct().Count() > 1)
                Err(grp.First(), $"Position '{grp.First().Code}' has more than one holder in the file.");

        foreach (var grp in holderRows.Where(r => r.IsPrimary).GroupBy(r => r.Personnel.ToUpperInvariant()))
            if (grp.Select(r => r.Code.ToUpperInvariant()).Distinct().Count() > 1)
                Err(grp.First(), $"Employee '{grp.First().Personnel}' is marked as primary on more than one position (duplicate primary).");

        foreach (var r in holderRows)
        {
            if (!empByCode.TryGetValue(r.Personnel.ToUpperInvariant(), out var emp)) { Err(r, $"Unknown employee personnel code '{r.Personnel}'."); continue; }
            if (!emp.IsActive) { Err(r, $"Employee '{r.Personnel}' is inactive."); continue; }
            var from = r.From ?? now.Date;
            if (r.To.HasValue && r.To.Value < from) { Err(r, "EffectiveTo is earlier than EffectiveFrom."); continue; }
            if (r.To.HasValue && r.To.Value < now.Date) { Err(r, "The assignment has already ended; historical assignments are not imported."); continue; }

            var expectedType = r.IsPrimary ? EmployeeAssignmentTypes.Primary : EmployeeAssignmentTypes.Secondary;
            if (r.AssignmentType.Length > 0 && !string.Equals(r.AssignmentType, expectedType, StringComparison.OrdinalIgnoreCase))
            { Err(r, $"AssignmentType '{r.AssignmentType}' is not valid for IsPrimary={r.IsPrimary} (supported: Primary, Secondary; Acting is not supported yet)."); continue; }

            // دارنده‌ی فعلی در DB
            if (posByCode.TryGetValue(r.Code.ToUpperInvariant(), out var existingPos))
            {
                var holder = activeAssigns.FirstOrDefault(a => a.PositionId == existingPos.PositionId);
                if (holder != null && holder.EmployeeId != emp.EmployeeId)
                    Err(r, $"Position '{r.Code}' is already occupied by another employee; end that assignment in Organization administration first.");
            }
            // تخصیص اصلی فعال همین کارمند در سمت دیگر
            if (r.IsPrimary)
            {
                var otherPrimary = activeAssigns.FirstOrDefault(a => a.EmployeeId == emp.EmployeeId && a.IsPrimary
                    && !(posByCode.TryGetValue(r.Code.ToUpperInvariant(), out var p2) && p2.PositionId == a.PositionId));
                if (otherPrimary != null) Err(r, $"Employee '{r.Personnel}' already has an active primary position; end or change it in Organization administration first (duplicate primary).");
            }
        }

        if (result.Errors.Count > 0) return result;
        if (!commit) { Summarize(result, defs, posByCode, codeById, deptIds, holderRows, empByCode, activeAssigns, false, _ctx, now); return result; }

        // ---- ۴) ذخیره (یک SaveChanges ⇒ اتمیک)
        // SCRUM-497: the app runs with EnableRetryOnFailure, which accepts a user transaction only inside the execution strategy.
        // The changes are applied to the context once; each attempt saves them without accepting them (EF Core connection-resiliency
        // pattern), so a retry after a transient failure saves the same changes again and nothing is applied twice.
        try
        {
            Summarize(result, defs, posByCode, codeById, deptIds, holderRows, empByCode, activeAssigns, true, _ctx, now);
            if (_ctx.Database.IsRelational())
            {
                await _ctx.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
                {
                    await using var tx = await _ctx.Database.BeginTransactionAsync(ct);
                    await _ctx.SaveChangesAsync(acceptAllChangesOnSuccess: false, ct);
                    await tx.CommitAsync(ct);
                });
                _ctx.ChangeTracker.AcceptAllChanges();
            }
            else await _ctx.SaveChangesAsync(ct);
            result.Committed = true;
        }
        catch (Exception ex)
        {
            _ctx.ChangeTracker.Clear();
            result.Errors.Add("Nothing was saved: " + ex.Message);
            result.Committed = false;
        }
        return result;
    }

    private sealed record Emp(int EmployeeId, string PersonnelCode, bool IsActive);

    /// <summary>شمارش (و در apply، اعمال روی Context) تغییرات؛ شمارش preview و commit یکسان است.</summary>
    private static void Summarize(OrganizationImportResult result, Dictionary<string, Row> defs, Dictionary<string, Position> posByCode,
        Dictionary<int, string?> codeById, Dictionary<string, int> deptByName, List<Row> holderRows, Dictionary<string, Emp> empByCode,
        List<EmployeeAssignment> activeAssigns, bool apply, PEMSDbContext ctx, DateTime now)
    {
        var entity = new Dictionary<string, Position>();
        foreach (var kv in defs)
        {
            var r = kv.Value;
            int? deptId = r.Department.Length == 0 ? null : deptByName[r.Department.ToUpperInvariant()];
            if (posByCode.TryGetValue(kv.Key, out var existing))
            {
                entity[kv.Key] = existing;
                var oldParentCode = existing.ParentPositionId.HasValue ? codeById.GetValueOrDefault(existing.ParentPositionId.Value)?.ToUpperInvariant() : null;
                var newParentCode = r.ParentCode.Length > 0 ? r.ParentCode.ToUpperInvariant() : null;
                var changed = existing.Title != r.Title || (deptId.HasValue && existing.DepartmentId != deptId) || oldParentCode != newParentCode
                             ;
                if (changed) { result.PositionsUpdated++; if (apply) { existing.Title = r.Title; if (deptId.HasValue) existing.DepartmentId = deptId; } }
                else result.Unchanged++;
            }
            else
            {
                result.PositionsCreated++;
                var created = new Position { Title = r.Title, PositionCode = r.Code, DepartmentId = deptId, IsActive = true, CreatedDate = now };
                entity[kv.Key] = created;
                if (apply) ctx.Positions.Add(created);
            }
        }
        if (apply)
            foreach (var kv in defs)
                entity[kv.Key].ParentPosition = kv.Value.ParentCode.Length == 0 ? null
                    : entity.TryGetValue(kv.Value.ParentCode.ToUpperInvariant(), out var parent) ? parent : posByCode[kv.Value.ParentCode.ToUpperInvariant()];

        foreach (var r in holderRows.GroupBy(x => x.Code.ToUpperInvariant()).Select(g => g.First()))
        {
            var emp = empByCode[r.Personnel.ToUpperInvariant()];
            var pos = entity[r.Code.ToUpperInvariant()];
            var holder = pos.PositionId != 0 ? activeAssigns.FirstOrDefault(a => a.PositionId == pos.PositionId) : null;
            var from = r.From ?? now.Date;
            if (holder != null)
            {
                var changed = holder.IsPrimary != r.IsPrimary || (r.From.HasValue && holder.StartDate.Date != r.From.Value.Date) || holder.EndDate?.Date != r.To?.Date;
                if (!changed) { result.Unchanged++; continue; }
                result.AssignmentsUpdated++;
                if (apply)
                {
                    holder.IsPrimary = r.IsPrimary;
                    holder.AssignmentType = r.IsPrimary ? EmployeeAssignmentTypes.Primary : EmployeeAssignmentTypes.Secondary;
                    if (r.From.HasValue) holder.StartDate = r.From.Value;
                    holder.EndDate = r.To;
                }
                continue;
            }
            result.AssignmentsCreated++;
            if (apply)
                ctx.EmployeeAssignments.Add(new EmployeeAssignment
                {
                    Position = pos, EmployeeId = emp.EmployeeId, IsActive = true, IsPrimary = r.IsPrimary,
                    AssignmentType = r.IsPrimary ? EmployeeAssignmentTypes.Primary : EmployeeAssignmentTypes.Secondary,
                    StartDate = from, EndDate = r.To, CreatedDate = now
                });
        }
    }

    private static List<Row> Read(Stream xlsx, OrganizationImportResult result)
    {
        var rows = new List<Row>();
        try
        {
            using var wb = new XLWorkbook(xlsx);
            var ws = wb.Worksheets.First();
            var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var c = 1; c <= lastCol; c++)
            {
                var h = ws.Cell(1, c).GetString().Trim();
                if (h.Length > 0 && !map.TryAdd(h, c)) result.Errors.Add($"Duplicate column header '{h}'.");
            }
            foreach (var required in new[] { "PositionCode" })
                if (!map.ContainsKey(required)) result.Errors.Add($"Missing required column '{required}'.");
            if (!map.ContainsKey("PositionTitle") && !map.ContainsKey("Position")) result.Errors.Add("Missing required column 'PositionTitle' (or 'Position').");
            foreach (var h in map.Keys.Where(h => !Columns.Contains(h, StringComparer.OrdinalIgnoreCase)))
                result.Warnings.Add($"Unknown column '{h}' is ignored.");
            if (result.Errors.Count > 0) return rows;

            string S(int r, string col) => map.TryGetValue(col, out var c) ? ws.Cell(r, c).GetString().Trim() : "";
            DateTime? D(int r, string col, out bool bad)
            {
                bad = false;
                if (!map.TryGetValue(col, out var c)) return null;
                var cell = ws.Cell(r, c);
                if (cell.IsEmpty()) return null;
                if (cell.DataType == XLDataType.DateTime) return cell.GetDateTime();
                if (DateTime.TryParse(cell.GetString().Trim(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d)) return d;
                bad = true; return null;
            }

            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            for (var r = 2; r <= lastRow; r++)
            {
                if (map.Keys.All(k => S(r, k).Length == 0)) continue;
                var row = new Row
                {
                    No = r, Department = S(r, "Department"), Code = S(r, "PositionCode"), ParentCode = S(r, "ParentPositionCode"),
                    Personnel = S(r, "EmployeePersonnelCode"), AssignmentType = S(r, "AssignmentType")
                };
                row.Title = S(r, "PositionTitle").Length > 0 ? S(r, "PositionTitle") : S(r, "Position");
                var ip = S(r, "IsPrimary");
                if (ip.Length > 0)
                {
                    if (ip.Equals("true", StringComparison.OrdinalIgnoreCase) || ip == "1" || ip.Equals("yes", StringComparison.OrdinalIgnoreCase)) row.IsPrimary = true;
                    else if (ip.Equals("false", StringComparison.OrdinalIgnoreCase) || ip == "0" || ip.Equals("no", StringComparison.OrdinalIgnoreCase)) row.IsPrimary = false;
                    else result.Errors.Add($"Row {r}: IsPrimary '{ip}' is not a boolean.");
                }
                row.From = D(r, "EffectiveFrom", out var badFrom);
                if (badFrom) result.Errors.Add($"Row {r}: EffectiveFrom is not a valid date.");
                row.To = D(r, "EffectiveTo", out var badTo);
                if (badTo) result.Errors.Add($"Row {r}: EffectiveTo is not a valid date.");
                rows.Add(row);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result.Errors.Add("The file could not be read as an Excel workbook: " + ex.Message);
        }
        return rows;
    }
}
