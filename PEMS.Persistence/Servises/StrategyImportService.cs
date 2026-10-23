using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Common;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>
/// ورود Excel الگوهای استراتژی و تخصیص تگ‌ها. هدرها به‌صورت نام (بدون حساسیت به حروف/فاصله/خط‌تیره)
/// تشخیص داده می‌شوند؛ هر ردیف از همان موتور قوانین فرم (StrategyRules) و سرویس فرمان می‌گذرد، پس
/// هیچ مسیر دورزدن قواعد RCM از طریق Import وجود ندارد. تکرار داخل فایل نیز شناسایی می‌شود.
/// </summary>
public class StrategyImportService : IStrategyImportService
{
    private static readonly string[] StrategyHeaders =
    {
        "EquipmentClassCode", "StrategyType", "FailureModeCode", "ConsequenceCategory", "IsHiddenFailure", "Justification",
        "Frequency", "DefaultIntervalDays", "TaskType", "OwnerDisciplineCode", "CbmParameter", "ThresholdUnit",
        "AlarmThreshold", "TripThreshold", "PfIntervalDays", "InspectionIntervalDays", "Source", "Title",
        "FunctionalFailure", "FailureEffect", "RequiredSkills", "EstimatedDurationHours", "ReviewDate"
    };

    private static readonly string[] AssignmentHeaders = { "StrategyId", "EquipmentClassCode", "StrategyType", "FailureModeCode", "Tag" };

    private readonly PEMSDbContext _context;
    private readonly IMaintenanceStrategyCommandService _commands;
    private readonly IStrategyAssignmentService _assignments;

    public StrategyImportService(PEMSDbContext context, IMaintenanceStrategyCommandService commands, IStrategyAssignmentService assignments)
    {
        _context = context;
        _commands = commands;
        _assignments = assignments;
    }

    // ==================== Strategies ====================

    public async Task<StrategyImportResultDto> ImportStrategiesAsync(Stream xlsx, bool commit, StrategyActor actor)
    {
        var result = new StrategyImportResultDto();
        var (rows, headerError) = ReadSheet(xlsx, StrategyHeaders);
        if (headerError != null)
        {
            result.Rows.Add(new StrategyImportRowResultDto { RowNumber = 1, Errors = { headerError } });
            return result;
        }

        var classes = await _context.EquipmentClasses.AsNoTracking().Where(c => c.IsActive)
            .ToDictionaryAsync(c => c.Code, c => c.EquipmentClassId, StringComparer.OrdinalIgnoreCase);
        var modes = await _context.FailureModes.AsNoTracking().Where(m => m.IsActive)
            .Select(m => new { m.FailureModeId, m.Code }).ToListAsync();
        var disciplines = await _context.Disciplines.AsNoTracking().Where(d => d.IsActive)
            .Select(d => new { d.DisciplineId, d.Code }).ToListAsync();

        var seen = new HashSet<(int?, int?, string?)>();

        foreach (var row in rows)
        {
            var r = new StrategyImportRowResultDto { RowNumber = row.Number };
            result.RowsRead++;
            result.Rows.Add(r);

            var input = new StrategyInputDto
            {
                StrategyType = LegacyMaps.StrategyType(row["StrategyType"]) ?? row["StrategyType"],
                ConsequenceCategory = LegacyMaps.Consequence(row["ConsequenceCategory"]) ?? row["ConsequenceCategory"],
                IsHiddenFailure = IsTrue(row["IsHiddenFailure"]),
                Justification = row["Justification"],
                Frequency = LegacyMaps.Frequency(row["Frequency"]) ?? Blank(row["Frequency"]),
                DefaultIntervalDays = Int(row["DefaultIntervalDays"], r, "DefaultIntervalDays"),
                TaskType = Blank(row["TaskType"]),
                CbmParameter = Blank(row["CbmParameter"]),
                ThresholdUnit = Blank(row["ThresholdUnit"]),
                AlarmThreshold = Dec(row["AlarmThreshold"], r, "AlarmThreshold"),
                TripThreshold = Dec(row["TripThreshold"], r, "TripThreshold"),
                PfIntervalDays = Int(row["PfIntervalDays"], r, "PfIntervalDays"),
                InspectionIntervalDays = Int(row["InspectionIntervalDays"], r, "InspectionIntervalDays"),
                Source = Blank(row["Source"]),
                Title = Blank(row["Title"]),
                FunctionalFailure = Blank(row["FunctionalFailure"]),
                FailureEffect = Blank(row["FailureEffect"]),
                RequiredSkills = Blank(row["RequiredSkills"]),
                EstimatedDurationHours = Dec(row["EstimatedDurationHours"], r, "EstimatedDurationHours"),
                ReviewDate = Date(row["ReviewDate"], r, "ReviewDate"),
                RecommendedTriggerType = RcmCodes.Trigger.Calendar
            };

            var classCode = Blank(row["EquipmentClassCode"]);
            if (classCode == null || !classes.TryGetValue(classCode, out var classId))
                r.Errors.Add(new RuleError("Rcm_Err_ClassNotFound", "EquipmentClassCode", classCode ?? ""));
            else input.EquipmentClassId = classId;

            var modeCode = Blank(row["FailureModeCode"]);
            var mode = modes.FirstOrDefault(m => string.Equals(m.Code, modeCode, StringComparison.OrdinalIgnoreCase));
            if (mode == null) r.Errors.Add(new RuleError("Rcm_Err_FailureModeNotFound", "FailureModeCode", modeCode ?? ""));
            else input.FailureModeId = mode.FailureModeId;

            var disciplineCode = Blank(row["OwnerDisciplineCode"]);
            if (disciplineCode != null)
            {
                var d = disciplines.FirstOrDefault(x => string.Equals(x.Code, disciplineCode, StringComparison.OrdinalIgnoreCase));
                if (d == null) r.Errors.Add(new RuleError("Rcm_Err_DisciplineNotFound", "OwnerDisciplineCode", disciplineCode));
                else input.OwnerDisciplineId = d.DisciplineId;
            }

            r.Reference = $"{classCode}/{modeCode}/{input.StrategyType}";

            if (r.Errors.Count == 0 && !seen.Add((input.EquipmentClassId, input.FailureModeId, input.StrategyType)))
                r.Errors.Add(new RuleError("Rcm_Err_DuplicateInFile", "StrategyType"));

            if (r.Errors.Count == 0)
            {
                var validation = await _commands.ValidateAsync(input);
                r.Errors.AddRange(validation);
            }

            r.Accepted = r.Errors.Count == 0;
            if (r.Accepted) result.RowsAccepted++;

            if (commit && r.Accepted)
            {
                var created = await _commands.CreateAsync(input, actor);
                if (!created.Success)
                {
                    r.Accepted = false;
                    result.RowsAccepted--;
                    r.Errors.AddRange(created.Errors);
                }
            }
        }

        result.Committed = commit;
        return result;
    }

    // ==================== Assignments ====================

    public async Task<StrategyImportResultDto> ImportAssignmentsAsync(Stream xlsx, bool commit, StrategyActor actor)
    {
        var result = new StrategyImportResultDto();
        var (rows, headerError) = ReadSheet(xlsx, AssignmentHeaders, requireAll: false, required: new[] { "Tag" });
        if (headerError != null)
        {
            result.Rows.Add(new StrategyImportRowResultDto { RowNumber = 1, Errors = { headerError } });
            return result;
        }

        // (strategyId → tag ids) برای ثبت اتمیک به‌ازای هر استراتژی
        var groups = new Dictionary<int, List<(int EquipmentId, StrategyImportRowResultDto Row)>>();

        var equipmentByTag = (await _context.Equipments.AsNoTracking().Where(e => e.IsActive)
            .Select(e => new { e.EquipmentId, e.TagNumber }).ToListAsync())
            .GroupBy(e => e.TagNumber, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => x.EquipmentId).ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            var r = new StrategyImportRowResultDto { RowNumber = row.Number };
            result.RowsRead++;
            result.Rows.Add(r);

            var tag = Blank(row["Tag"]);
            r.Reference = tag;
            if (tag == null || !equipmentByTag.TryGetValue(tag, out var tagIds))
            {
                r.Errors.Add(new RuleError("Rcm_Err_TagNotFound", "Tag", tag ?? ""));
                continue;
            }
            if (tagIds.Count > 1)
            {
                r.Errors.Add(new RuleError("Rcm_Err_TagAmbiguous", "Tag", tag));
                continue;
            }

            var strategyId = await ResolveStrategyAsync(row, r);
            if (strategyId == null) continue;

            r.Accepted = true;
            result.RowsAccepted++;
            if (!groups.TryGetValue(strategyId.Value, out var list)) groups[strategyId.Value] = list = new();
            list.Add((tagIds[0], r));
        }

        if (commit)
        {
            foreach (var (strategyId, items) in groups)
            {
                var res = await _assignments.AssignAsync(strategyId, items.Select(i => i.EquipmentId).ToList(), actor);
                if (res.Errors.Count > 0 || res.RolledBack)
                {
                    foreach (var (_, row) in items)
                    {
                        if (row.Accepted) { row.Accepted = false; result.RowsAccepted--; }
                        row.Errors.AddRange(res.Errors);
                    }
                }
            }
        }
        else
        {
            // پیش‌نمایش: تعارض/بحرانی‌بودن همان‌گونه که در ثبت واقعی اعمال می‌شود گزارش می‌شود
            foreach (var (strategyId, items) in groups)
            {
                var evaluated = await _assignments.EvaluateAsync(strategyId, items.Select(i => i.EquipmentId).ToList());
                foreach (var (equipmentId, row) in items)
                {
                    var c = evaluated.FirstOrDefault(x => x.Equipment.EquipmentId == equipmentId);
                    if (c != null && !c.WillReceive)
                    {
                        row.Accepted = false;
                        result.RowsAccepted--;
                        row.Errors.Add(new RuleError("Rcm_Err_Bulk_" + c.Conflict, "Tag", row.Reference ?? ""));
                    }
                }
            }
        }

        result.Committed = commit;
        return result;
    }

    private async Task<int?> ResolveStrategyAsync(SheetRow row, StrategyImportRowResultDto r)
    {
        var idText = Blank(row["StrategyId"]);
        if (idText != null)
        {
            if (!int.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                r.Errors.Add(new RuleError("Rcm_Err_InvalidNumber", "StrategyId", idText));
                return null;
            }
            var usable = await _context.MaintenanceStrategies.AnyAsync(s => s.MaintenanceStrategyId == id && s.IsActive
                && (s.Status == RcmCodes.Status.Approved || s.Status == RcmCodes.Status.Active));
            if (!usable) { r.Errors.Add(new RuleError("Rcm_Err_StrategyNotApproved", "StrategyId")); return null; }
            return id;
        }

        var classCode = Blank(row["EquipmentClassCode"]);
        var modeCode = Blank(row["FailureModeCode"]);
        var type = LegacyMaps.StrategyType(row["StrategyType"]) ?? Blank(row["StrategyType"]);
        if (classCode == null || modeCode == null || type == null)
        {
            r.Errors.Add(new RuleError("Rcm_Err_StrategyReferenceMissing", "StrategyId"));
            return null;
        }

        var matches = await _context.MaintenanceStrategies.AsNoTracking()
            .Where(s => s.IsActive && (s.Status == RcmCodes.Status.Approved || s.Status == RcmCodes.Status.Active)
                        && s.StrategyType == type && s.EquipmentClass!.Code == classCode && s.FailureMode!.Code == modeCode)
            .Select(s => s.MaintenanceStrategyId).ToListAsync();
        if (matches.Count == 1) return matches[0];

        r.Errors.Add(new RuleError(matches.Count == 0 ? "Rcm_Err_StrategyNotApproved" : "Rcm_Err_StrategyAmbiguous", "StrategyId"));
        return null;
    }

    // ==================== Templates / error report ====================

    public byte[] BuildStrategyTemplate() => BuildTemplate("Strategies", StrategyHeaders, new[]
    {
        "PUMP_CENT", "CBM", "PUMP_EXCESSIVE_VIBRATION", "Operational", "", "Vibration trend shows bearing wear before functional failure (OEM manual §7).",
        "Monthly", "30", "ConditionMonitoring", "MECH", "Vibration velocity", "mm/s", "4.5", "7.1", "90", "30", "OEM",
        "Centrifugal pump - vibration - CBM", "Pump cannot deliver rated flow", "Bearing failure, seal damage", "Vibration analyst", "2", ""
    });

    public byte[] BuildAssignmentTemplate() => BuildTemplate("Assignments", AssignmentHeaders, new[] { "", "PUMP_CENT", "CBM", "PUMP_EXCESSIVE_VIBRATION", "80-P-101A" });

    public byte[] BuildErrorReport(StrategyImportResultDto result, Func<RuleError, string> localize)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Errors");
        ws.Cell(1, 1).Value = "Row"; ws.Cell(1, 2).Value = "Reference"; ws.Cell(1, 3).Value = "Status"; ws.Cell(1, 4).Value = "Message";
        ws.Row(1).Style.Font.Bold = true;
        var r = 2;
        foreach (var row in result.Rows.Where(x => !x.Accepted || x.Errors.Count > 0))
        {
            foreach (var e in row.Errors.DefaultIfEmpty(new RuleError("-")))
            {
                ws.Cell(r, 1).Value = row.RowNumber;
                ws.Cell(r, 2).Value = row.Reference ?? "";
                ws.Cell(r, 3).Value = row.Accepted ? "Accepted" : "Rejected";
                ws.Cell(r, 4).Value = e.Code == "-" ? "" : localize(e);
                r++;
            }
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static byte[] BuildTemplate(string sheet, string[] headers, string[] example)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet(sheet);
        for (var i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Cell(2, i + 1).Value = i < example.Length ? example[i] : "";
        }
        ws.Row(1).Style.Font.Bold = true;
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ==================== Sheet reading ====================

    private sealed class SheetRow
    {
        public int Number { get; init; }
        public Dictionary<string, string?> Cells { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public string? this[string key] => Cells.TryGetValue(key, out var v) ? v : null;
    }

    private static string NormHeader(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static (List<SheetRow> Rows, RuleError? Error) ReadSheet(Stream xlsx, string[] headers, bool requireAll = true, string[]? required = null)
    {
        try
        {
            using var wb = new XLWorkbook(xlsx);
            var ws = wb.Worksheets.First();
            var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
            if (lastCol == 0 || lastRow < 2) return (new(), new RuleError("Rcm_Err_ImportEmpty"));

            var map = new Dictionary<string, int>();
            for (var c = 1; c <= lastCol; c++)
            {
                var h = NormHeader(ws.Cell(1, c).GetString());
                var canonical = headers.FirstOrDefault(x => NormHeader(x) == h);
                if (canonical != null) map[canonical] = c;
            }

            var needed = requireAll ? headers : (required ?? Array.Empty<string>());
            var missing = needed.Where(h => !map.ContainsKey(h)).ToList();
            if (missing.Count > 0) return (new(), new RuleError("Rcm_Err_ImportMissingColumns", null, string.Join(", ", missing)));

            var rows = new List<SheetRow>();
            for (var r = 2; r <= lastRow; r++)
            {
                var row = new SheetRow { Number = r };
                foreach (var (name, col) in map) row.Cells[name] = ws.Cell(r, col).GetString().Trim();
                if (row.Cells.Values.All(string.IsNullOrWhiteSpace)) continue;
                rows.Add(row);
            }
            return (rows, null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return (new(), new RuleError("Rcm_Err_ImportUnreadable"));
        }
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static bool IsTrue(string? s) => s != null && (s.Equals("1") || s.Equals("true", StringComparison.OrdinalIgnoreCase) || s.Equals("yes", StringComparison.OrdinalIgnoreCase));

    private static int? Int(string? s, StrategyImportRowResultDto r, string field)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return v;
        r.Errors.Add(new RuleError("Rcm_Err_InvalidNumber", field, s));
        return null;
    }

    private static decimal? Dec(string? s, StrategyImportRowResultDto r, string field)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var v)) return v;
        r.Errors.Add(new RuleError("Rcm_Err_InvalidNumber", field, s));
        return null;
    }

    private static DateTime? Date(string? s, StrategyImportRowResultDto r, string field)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var v)) return v;
        r.Errors.Add(new RuleError("Rcm_Err_InvalidDate", field, s));
        return null;
    }
}
