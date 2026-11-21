using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using PEMS.Application.Definitions;
using PEMS.Application.Uploads;
using PEMS.Persistence.Context;
using PEMS.UI.Abstractions;

namespace PEMS.Web.Definitions;

/// <summary>
/// <c>entity.import</c> (batch 5): a spreadsheet of a registered entity is imported by a definition, not by a controller. Parameters: <c>entity</c>, <c>columns</c> (the property of each column, in order: the first column of the sheet is the
/// first property), <c>required</c> (properties that must have a value, else the row is skipped), <c>unique</c> (the same rules as <c>entity.create</c>: a row that already exists is skipped, also one that repeats earlier in the file),
/// <c>defaults</c>, <c>file</c> (the posted field, default <c>file</c>), <c>skipRows</c> (header rows, default 1), <c>maxRows</c> (default 5000, at most 20000), <c>redirect</c>. The file is checked by the upload guard (profile spreadsheet), the
/// entity's create permission and roles are checked by the code, every value goes through the declared length and range of the property, and a row is written like <c>entity.create</c>. The answer counts the rows added and skipped.
/// </summary>
public sealed class EntityImportAction(IServiceScopeFactory scopes, EntityCatalog catalog, IUploadGuard guard) : EntityWriteAction(scopes, catalog, "entity.import")
{
    public const int DefaultMaxRows = 5000;
    public const int AbsoluteMaxRows = 20000;
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal) { "entity", "columns", "required", "unique", "defaults", "file", "skipRows", "maxRows", "redirect", "deny", "validate", "map" };

    protected override string? PermissionOf(EntityRegistration e) => e.CreatePermission;
    protected override bool NeedsMap => false;

    public override IReadOnlyList<DefinitionFieldError> ValidateParameters(JsonObject? parameters)
    {
        var errors = base.ValidateParameters(parameters).ToList();
        foreach (var p in parameters ?? []) if (!Allowed.Contains(p.Key)) errors.Add(new("/" + p.Key, "unknown-property", "Def_Err_UnknownProperty", $"'{p.Key}' is not a setting of entity.import."));
        var entity = catalog.Get(EntityJson.Str(parameters?["entity"]));
        if (entity is null) return errors;
        var columns = EntityJson.Names(parameters!["columns"]);
        if (columns.Count == 0) errors.Add(new("/columns", "required", "Def_Err_Required", "columns is the list of the properties of the sheet columns, in order."));
        foreach (var name in columns)
        {
            var pi = entity.ClrType.GetProperty(name);
            if (!entity.Writable.Contains(name) || pi is null || !EntityJson.Supported(pi.PropertyType)) errors.Add(new("/columns", "not-writable", "Def_Err_NotWritable", $"'{name}' is not a writable property of {entity.Name}."));
        }
        if (parameters["skipRows"] is { } sr && !(sr is JsonValue sv && sv.TryGetValue<int>(out var skip) && skip is >= 0 and <= 10)) errors.Add(new("/skipRows", "range", "Def_Err_Range", "skipRows is a whole number from 0 to 10."));
        if (parameters["maxRows"] is { } mr && !(mr is JsonValue mv && mv.TryGetValue<int>(out var max) && max is >= 1 and <= AbsoluteMaxRows)) errors.Add(new("/maxRows", "range", "Def_Err_Range", $"maxRows is a whole number from 1 to {AbsoluteMaxRows}."));
        return errors;
    }

    protected override async Task<ActionOutcome> RunAsync(PEMSDbContext db, EntityRegistration entity, JsonObject p, ActionInvocation invocation, CancellationToken ct)
    {
        var field = EntityJson.Str(p["file"]) ?? "file";
        var file = invocation.Files?.FirstOrDefault(f => f.Field == field);
        if (file is null || file.Length == 0) return ActionOutcome.Refused(field, Msg("Choose a file.", "یک فایل انتخاب کنید."));
        var check = await guard.CheckAsync(UploadProfile.Spreadsheet, file.FileName, file.ContentType, file.Length, file.Open, ct);
        if (!check.Ok) return ActionOutcome.Refused(field, Msg("This file is not accepted: choose an .xlsx spreadsheet.", "این فایل پذیرفته نشد؛ یک فایل اکسل (xlsx) انتخاب کنید."));

        var columns = EntityJson.Names(p["columns"]);
        var properties = columns.Select(name => entity.ClrType.GetProperty(name)!).ToList();
        var skipRows = p["skipRows"] is JsonValue sv && sv.TryGetValue<int>(out var s) ? s : 1;
        var maxRows = p["maxRows"] is JsonValue mv && mv.TryGetValue<int>(out var m) ? m : DefaultMaxRows;
        var required = EntityJson.Names(p["required"]);

        int added = 0, skipped = 0;
        try
        {
            await using var stream = file.Open();
            using var workbook = new XLWorkbook(stream);
            var sheet = workbook.Worksheets.First();
            var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
            if (lastRow - skipRows > maxRows) return ActionOutcome.Refused(field, Msg($"The sheet has more than {maxRows} rows.", $"فایل بیش از {maxRows} ردیف دارد."));

            for (var r = skipRows + 1; r <= lastRow; r++)
            {
                var row = Activator.CreateInstance(entity.ClrType)!;
                var valid = true;
                for (var c = 0; c < properties.Count && valid; c++)
                {
                    var text = sheet.Cell(r, c + 1).GetString().Trim();
                    var pi = properties[c];
                    if (text.Length == 0 && required.Contains(pi.Name)) { valid = false; break; }
                    if (text.Length == 0) continue;
                    var value = EntityJson.Convert(pi.PropertyType, text);
                    if (value is EntityJson.NotConvertible || EntityConstraints.Check(pi, columns[c], text) is not null) { valid = false; break; }
                    EntityJson.SetValue(pi, row, value);
                }
                if (!valid) { skipped++; continue; }
                if (p["defaults"] is JsonObject defaults)
                    foreach (var (name, v) in defaults) { var pi = entity.ClrType.GetProperty(name)!; EntityJson.SetValue(pi, row, EntityJson.Convert(pi.PropertyType, v)); }
                if (await CheckUniqueAsync(db, entity, p, row, ct) is not null) { skipped++; continue; }
                entity.BeforeCreate?.Invoke(row);
                db.Add(row);
                await db.SaveChangesAsync(ct);      // one row at a time: a repeat later in the same file is found by the next unique check
                added++;
            }
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentException or IOException or FormatException)
        {
            return ActionOutcome.Refused(field, Msg("The file could not be read as a spreadsheet.", "فایل به‌عنوان صفحه‌گسترده خوانده نشد."));
        }

        var message = Msg($"Import finished: {added} added, {skipped} skipped (duplicates or invalid rows).", $"ایمپورت تمام شد: {added} اضافه شد، {skipped} ردیف تکراری یا نامعتبر رد شد.");
        return ActionOutcome.Done(message, EntityJson.Str(p["redirect"]), new Dictionary<string, string?> { ["added"] = added.ToString(CultureInfo.InvariantCulture), ["skipped"] = skipped.ToString(CultureInfo.InvariantCulture) });
    }
}
