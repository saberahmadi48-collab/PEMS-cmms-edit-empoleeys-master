using ClosedXML.Excel;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Web.Resources;
using PEMS.Web.Security;

namespace PEMS.Web.Services;

/// <summary>SCRUM-706: the import center cards (the same visibility as before: role sets and the View permission of the module) and the employee and instrument imports moved out of their controllers unchanged.</summary>
public sealed class ImportCenterOperations(PEMSDbContext context, IPermissionService permissions, IStringLocalizer<SharedResource> loc, LinkGenerator links) : IImportCenterOperations
{
    public async Task<List<ImportCenterRow>> EntriesAsync(List<string> userRoles)
    {
        var rows = new List<ImportCenterRow>();
        foreach (var category in ImportCatalog.CategoryOrder)
            foreach (var e in await ImportCatalog.VisibleForRolesAsync(userRoles, permissions))
            {
                if (e.Category != category) continue;
                var url = e.Url ?? links.GetPathByAction(e.Action, e.Controller, new { area = e.Area }) ?? "";
                rows.Add(new ImportCenterRow(e.Key, loc["IC_Cat_" + category].Value, loc["IC_" + e.Key + "_Title"].Value, loc["IC_" + e.Key + "_Desc"].Value, e.FileTypes, loc["IC_" + e.Key + "_Flow"].Value, url));
            }
        return rows;
    }

    public async Task<ImportCenterResult> ImportEmployeesAsync(byte[] file, List<string> userRoles)
    {
        if (!ImportCatalog.CanUse("Employee", userRoles)) return new(false, "Access denied.");
        var departments = await context.Departments.Where(d => d.IsActive).ToListAsync();
        var disciplines = await context.Disciplines.Where(d => d.IsActive).ToListAsync();
        var existingCodes = new HashSet<string>(await context.Employees.Where(x => x.PersonnelCode != "").Select(x => x.PersonnelCode).ToListAsync(), StringComparer.OrdinalIgnoreCase);

        int added = 0, skippedDuplicate = 0, skippedInvalid = 0;
        var reasons = new List<string>();
        void Reject(int row, string why) { skippedInvalid++; if (reasons.Count < 10) reasons.Add($"#{row}: {why}"); }

        using var stream = new MemoryStream(file);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.First();
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;

        for (int r = 2; r <= lastRow; r++)
        {
            var code = sheet.Cell(r, 1).GetString().Trim();
            var firstName = sheet.Cell(r, 2).GetString().Trim();
            var lastName = sheet.Cell(r, 3).GetString().Trim();
            if (string.IsNullOrWhiteSpace(firstName) && string.IsNullOrWhiteSpace(lastName)) continue;
            if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName)) { Reject(r, "نام/نام خانوادگی خالی"); continue; }
            if (code != "" && existingCodes.Contains(code)) { skippedDuplicate++; continue; }

            var deptName = sheet.Cell(r, 7).GetString().Trim();
            var discName = sheet.Cell(r, 8).GetString().Trim();
            var deptMatches = departments.Where(d => d.Name.Equals(deptName, StringComparison.OrdinalIgnoreCase)
                                                     || (!string.IsNullOrEmpty(d.Code) && d.Code.Equals(deptName, StringComparison.OrdinalIgnoreCase))).ToList();
            if (deptMatches.Count == 0) { Reject(r, $"دپارتمان «{deptName}» یافت نشد/غیرفعال است"); continue; }
            if (deptMatches.Count > 1) { Reject(r, $"دپارتمان «{deptName}» مبهم است"); continue; }

            var discMatches = disciplines.Where(d => d.Name.Equals(discName, StringComparison.OrdinalIgnoreCase)
                                                     || d.Code.Equals(discName, StringComparison.OrdinalIgnoreCase)
                                                     || (!string.IsNullOrEmpty(d.ImportAliases) && d.ImportAliases.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                                         .Any(a => a.Equals(discName, StringComparison.OrdinalIgnoreCase)))).ToList();
            if (discName == "") { Reject(r, "دیسیپلین الزامی است"); continue; }
            if (discMatches.Count == 0) { Reject(r, $"دیسیپلین «{discName}» یافت نشد/غیرفعال است"); continue; }
            if (discMatches.Count > 1) { Reject(r, $"دیسیپلین «{discName}» مبهم است (چند رکورد)"); continue; }

            await context.Employees.AddAsync(new Employee
            {
                PersonnelCode = code, FirstName = firstName, LastName = lastName,
                Position = sheet.Cell(r, 4).GetString().Trim(), Email = sheet.Cell(r, 5).GetString().Trim(), Phone = sheet.Cell(r, 6).GetString().Trim(),
                DepartmentId = deptMatches[0].DepartmentId, DisciplineId = discMatches[0].DisciplineId, IsActive = true, CreatedDate = DateTime.Now
            });
            if (code != "") existingCodes.Add(code);
            added++;
        }

        await context.SaveChangesAsync();
        return new(true, $"ایمپورت تمام شد: {added} اضافه شد، {skippedDuplicate} تکراری رد شد، {skippedInvalid} نامعتبر رد شد." + (reasons.Count > 0 ? " | " + string.Join(" ; ", reasons) : ""));
    }

    public async Task<ImportCenterResult> ImportInstrumentsAsync(byte[] file, List<string> userRoles)
    {
        if (!ImportCatalog.CanUse("Instrument", userRoles)) return new(false, "Access denied.");
        var equipmentLookup = (await context.Equipments.Where(x => x.IsActive).Select(x => new { x.EquipmentId, x.TagNumber }).ToListAsync())
            .GroupBy(x => x.TagNumber.Trim().ToUpperInvariant()).ToDictionary(g => g.Key, g => g.First().EquipmentId);
        var equipmentIdsWithInstrument = (await context.Instruments.Select(x => x.EquipmentId).ToListAsync()).ToHashSet();

        int created = 0, updated = 0, skippedNoMatch = 0;
        var notFoundTags = new List<string>();
        using (var stream = new MemoryStream(file))
        using (var workbook = new XLWorkbook(stream))
        {
            var sheet = workbook.Worksheets.First();
            var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
            for (int r = 2; r <= lastRow; r++)
            {
                var tag = sheet.Cell(r, 1).GetString().Trim();
                if (string.IsNullOrWhiteSpace(tag)) continue;
                if (!equipmentLookup.TryGetValue(tag.ToUpperInvariant(), out var equipmentId)) { skippedNoMatch++; notFoundTags.Add(tag); continue; }

                var instrumentType = sheet.Cell(r, 2).GetString().Trim();
                var loopNumber = sheet.Cell(r, 3).GetString().Trim();
                var system = sheet.Cell(r, 4).GetString().Trim();
                int? calibrationInterval = int.TryParse(sheet.Cell(r, 7).GetString().Trim(), out var ci) ? ci : null;

                if (equipmentIdsWithInstrument.Contains(equipmentId))
                {
                    var existing = await context.Instruments.FirstAsync(x => x.EquipmentId == equipmentId);
                    if (string.IsNullOrWhiteSpace(existing.InstrumentType) && !string.IsNullOrWhiteSpace(instrumentType)) existing.InstrumentType = instrumentType;
                    existing.LoopNumber ??= string.IsNullOrWhiteSpace(loopNumber) ? null : loopNumber;
                    existing.System ??= string.IsNullOrWhiteSpace(system) ? null : system;
                    existing.CalibrationInterval ??= calibrationInterval;
                    updated++;
                }
                else
                {
                    await context.Instruments.AddAsync(new Instrument
                    {
                        EquipmentId = equipmentId, InstrumentType = instrumentType,
                        LoopNumber = string.IsNullOrWhiteSpace(loopNumber) ? null : loopNumber, System = string.IsNullOrWhiteSpace(system) ? null : system,
                        CalibrationInterval = calibrationInterval, IsActive = true, CreatedDate = DateTime.Now
                    });
                    equipmentIdsWithInstrument.Add(equipmentId);
                    created++;
                }
            }
        }

        await context.SaveChangesAsync();
        var text = $"{created} instrument ایجاد شد، {updated} به‌روزرسانی شد.";
        if (skippedNoMatch > 0) text += $" | {skippedNoMatch} ردیف رد شد (تگ در Equipment پیدا نشد): " + string.Join(", ", notFoundTags.Take(10));
        return new(true, text);
    }
}
