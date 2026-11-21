using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using PEMS.Application.Definitions;
using PEMS.Application.Interfaces;
using PEMS.Application.Uploads;
using PEMS.UI.Abstractions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PEMS.Web.Definitions;

// SCRUM-596 (uploads) and SCRUM-615 (file results): a file that comes in and a file that goes out are settings of an action definition, not controller code. The upload pipeline of SCRUM-202 is reused as it is:
// the allow-list of the profile, the size limit, the content check and the malware-scan hook all run in IUploadGuard, and the file is stored by IFileStorageService (a generated name, never the user's).

/// <summary>A file of the request, for the action to read: the form field it came in, its name, declared type and size, and a way to open it.</summary>
public sealed record UploadedFile(string Field, string FileName, string? ContentType, long Length, Func<Stream> Open);

/// <summary>A file an action produced for the user to download (SCRUM-615).</summary>
public sealed record FileResult(byte[] Bytes, string FileName, string ContentType);

public static class HostFiles
{
    /// <summary>The files of a multipart request (null when there are none).</summary>
    public static IReadOnlyList<UploadedFile>? From(HttpRequest request)
        => request.HasFormContentType && request.Form.Files.Count > 0
            ? request.Form.Files.Select(f => new UploadedFile(f.Name, f.FileName, f.ContentType, f.Length, f.OpenReadStream)).ToList()
            : null;
}

/// <summary>
/// <c>file.upload</c>: stores one uploaded file and returns <c>key</c>, <c>fileName</c>, <c>size</c> and <c>contentType</c>, so a following step of <c>action.steps</c> (SCRUM-593, 594) can write them to a record.
/// Parameters: <c>field</c> (the form field of the file), <c>profile</c> (attachment, image, spreadsheet, csv or pdf), <c>folder</c> (logical folder, such as <c>vdr/comments</c>), <c>maxFileMb</c> (a limit smaller than the setting), <c>required</c>.
/// </summary>
public sealed class FileUploadAction(IUploadGuard guard, IServiceScopeFactory scopes) : IActionHandler
{
    public const string HandlerKey = "file.upload";
    private static readonly Regex Folder = new("^[a-z0-9][a-z0-9/_-]{0,60}$", RegexOptions.Compiled);
    private static readonly Regex FieldName = new("^[A-Za-z][A-Za-z0-9_.-]{0,60}$", RegexOptions.Compiled);

    private static readonly IReadOnlyDictionary<string, UploadProfile> Profiles = new Dictionary<string, UploadProfile>(StringComparer.Ordinal)
    {
        ["attachment"] = UploadProfile.Attachment, ["image"] = UploadProfile.Image, ["spreadsheet"] = UploadProfile.Spreadsheet, ["csv"] = UploadProfile.Csv, ["pdf"] = UploadProfile.Pdf,
    };

    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal) { "field", "profile", "folder", "maxFileMb", "required" };

    public string Key => HandlerKey;
    public IReadOnlyCollection<string>? AllowedRoles => null;

    public IReadOnlyList<DefinitionFieldError> ValidateParameters(JsonObject? parameters)
    {
        var errors = new List<DefinitionFieldError>();
        foreach (var p in parameters ?? []) if (!Allowed.Contains(p.Key)) errors.Add(new("/" + p.Key, "unknown-property", "Def_Err_UnknownProperty", $"'{p.Key}' is not a setting of file.upload."));
        if (EntityJson.Str(parameters?["field"]) is not { } field || !FieldName.IsMatch(field)) errors.Add(new("/field", "required", "Def_Err_Required", "field is the form field the file comes in."));
        if (EntityJson.Str(parameters?["profile"]) is not { } profile || !Profiles.ContainsKey(profile)) errors.Add(new("/profile", "unknown-profile", "Def_Err_Type", $"profile is one of: {string.Join(", ", Profiles.Keys)}."));
        if (EntityJson.Str(parameters?["folder"]) is not { } folder || !Folder.IsMatch(folder)) errors.Add(new("/folder", "invalid-folder", "Def_Err_Type", "folder is a logical folder of lower-case letters, digits, / _ - (for example vdr/comments)."));
        if (parameters?["maxFileMb"] is { } max && !(max is JsonValue v && v.TryGetValue<int>(out var mb) && mb is >= 1 and <= 500)) errors.Add(new("/maxFileMb", "range", "Def_Err_Range", "maxFileMb is a whole number from 1 to 500."));
        if (parameters?["required"] is { } req && !(req is JsonValue rv && rv.TryGetValue<bool>(out _))) errors.Add(new("/required", "wrong-type", "Def_Err_Type", "required is true or false."));
        return errors;
    }

    private static TranslatableText Text(string en, string fa) => TranslatableText.Of(("en", en), ("fa", fa));

    private static TranslatableText Reason(string error, string fileName, int maxMb) => error switch
    {
        UploadErrors.Empty => Text("The file is empty.", "فایل خالی است."),
        UploadErrors.TypeNotAllowed => Text($"File {fileName}: this file type is not allowed here.", $"فایل {fileName}: این نوع فایل در اینجا مجاز نیست."),
        UploadErrors.TooLarge => Text($"File {fileName} is larger than {maxMb} MB.", $"حجم فایل {fileName} بیش از {maxMb} مگابایت است."),
        UploadErrors.ContentMismatch => Text($"File {fileName}: the content does not match its type.", $"فایل {fileName}: محتوا با نوع فایل مطابقت ندارد."),
        UploadErrors.Infected => Text($"File {fileName} was refused by the virus scan.", $"فایل {fileName} توسط ضدویروس رد شد."),
        _ => Text($"File {fileName} could not be checked and was refused.", $"فایل {fileName} بررسی نشد و رد شد."),
    };

    public async Task<ActionOutcome> ExecuteAsync(ActionInvocation invocation, CancellationToken ct)
    {
        var p = invocation.Parameters;
        var field = EntityJson.Str(p?["field"]);
        if (field is null || !Profiles.TryGetValue(EntityJson.Str(p?["profile"]) ?? "", out var profile) || EntityJson.Str(p?["folder"]) is not { } folder)
            return ActionOutcome.Refused(Text("The action is not set up.", "کنش درست تنظیم نشده است."));

        var file = invocation.Files?.FirstOrDefault(f => f.Field == field);
        var required = p?["required"] is JsonValue rv && rv.TryGetValue<bool>(out var r) && r;
        if (file is null)
            return required ? ActionOutcome.Refused(field, Text("Choose a file.", "یک فایل انتخاب کنید.")) : ActionOutcome.Done(returned: new Dictionary<string, string?> { ["key"] = "", ["fileName"] = "", ["size"] = "0", ["contentType"] = "" });

        var limitMb = p?["maxFileMb"] is JsonValue mv && mv.TryGetValue<int>(out var mb) ? mb : 0;
        if (limitMb > 0 && file.Length > limitMb * 1024L * 1024L) return ActionOutcome.Refused(field, Reason(UploadErrors.TooLarge, file.FileName, limitMb));

        var check = await guard.CheckAsync(profile, file.FileName, file.ContentType, file.Length, file.Open, ct);
        if (!check.Ok) return ActionOutcome.Refused(field, Reason(check.Error ?? "", check.FileName ?? file.FileName, limitMb));

        using var scope = ActionScope.Open(scopes);
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
        await using var stream = file.Open();
        var name = check.FileName ?? UploadPolicy.SafeFileName(file.FileName);
        var key = await storage.SaveAsync(folder, name, stream);
        return ActionOutcome.Done(returned: new Dictionary<string, string?>
        {
            ["key"] = key, ["fileName"] = name, ["size"] = file.Length.ToString(CultureInfo.InvariantCulture), ["contentType"] = UploadPolicy.ContentTypeFor(name),
        });
    }
}

/// <summary>
/// <c>file.export</c>: the rows of a grid data source definition as a CSV, Excel or PDF file (SCRUM-615, SCRUM-800). Parameters: <c>dataSource</c> (key of a grid data source), <c>format</c> (csv, xlsx or pdf), <c>fileName</c> (without extension), <c>title</c> (pdf: the heading, a text in both languages), <c>landscape</c> (pdf, default true),
/// <c>columns</c> (<c>{ "field": "name", "header": {fa, en} }</c>), <c>maxRows</c> (default 5000, at most 50000). The data source is read with the rights of the user (its permission, the provider's roles, row-level rules); the posted values
/// (search, sort, filters, as in a list) narrow the rows. A cell that would be read as a formula is written as text.
/// </summary>
public sealed class FileExportAction(IServiceScopeFactory scopes) : IActionHandler
{
    public const string HandlerKey = "file.export";
    public const int DefaultMaxRows = 5000;
    public const int AbsoluteMaxRows = 50000;
    private static readonly Regex Name = new("^[A-Za-z0-9._ -]{1,80}$", RegexOptions.Compiled);
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal) { "dataSource", "format", "fileName", "columns", "maxRows", "title", "landscape" };
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public string Key => HandlerKey;
    public IReadOnlyCollection<string>? AllowedRoles => null;

    public IReadOnlyList<DefinitionFieldError> ValidateParameters(JsonObject? parameters)
    {
        var errors = new List<DefinitionFieldError>();
        foreach (var p in parameters ?? []) if (!Allowed.Contains(p.Key)) errors.Add(new("/" + p.Key, "unknown-property", "Def_Err_UnknownProperty", $"'{p.Key}' is not a setting of file.export."));
        if (string.IsNullOrWhiteSpace(EntityJson.Str(parameters?["dataSource"]))) errors.Add(new("/dataSource", "required", "Def_Err_Required", "dataSource is the key of a grid data source definition."));
        if (EntityJson.Str(parameters?["format"]) is not ("csv" or "xlsx" or "pdf")) errors.Add(new("/format", "unknown-format", "Def_Err_Type", "format is csv, xlsx or pdf."));
        if (parameters?["title"] is { } title) DataSourceDefinitionKind.CheckLabel(title, "/title", errors);
        if (parameters?["landscape"] is { } land && !(land is JsonValue lv && lv.TryGetValue<bool>(out _))) errors.Add(new("/landscape", "wrong-type", "Def_Err_Type", "landscape is true or false."));
        if (parameters?["fileName"] is { } fn && (EntityJson.Str(fn) is not { } name || !Name.IsMatch(name))) errors.Add(new("/fileName", "invalid-name", "Def_Err_Type", "fileName is letters, digits, space . _ - (no extension, no path)."));
        if (parameters?["maxRows"] is { } mr && !(mr is JsonValue mv && mv.TryGetValue<int>(out var rows) && rows is >= 1 and <= AbsoluteMaxRows)) errors.Add(new("/maxRows", "range", "Def_Err_Range", $"maxRows is a whole number from 1 to {AbsoluteMaxRows}."));
        if (parameters?["columns"] is not JsonArray columns || columns.Count == 0 || columns.Count > 60) errors.Add(new("/columns", "required", "Def_Err_Required", "columns is a list of 1 to 60 columns."));
        else
            for (var i = 0; i < columns.Count; i++)
            {
                if (columns[i] is not JsonObject c || string.IsNullOrWhiteSpace(EntityJson.Str(c["field"]))) { errors.Add(new($"/columns/{i}/field", "required", "Def_Err_Required", "A column names its field.")); continue; }
                DataSourceDefinitionKind.CheckLabel(c["header"], $"/columns/{i}/header", errors);
            }
        return errors;
    }

    private static TranslatableText Text(string en, string fa) => TranslatableText.Of(("en", en), ("fa", fa));

    /// <summary>A text that a spreadsheet program would read as a formula gets a leading quote, so an exported cell can never run.</summary>
    public static string Safe(string text) => text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + text : text;

    private static string Cell(JsonNode? value) => value switch
    {
        null => "",
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        JsonValue v when v.TryGetValue<bool>(out var b) => b ? "true" : "false",
        _ => value.ToJsonString().Trim('"'),
    };

    public static byte[] Csv(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        static string Quote(string cell) => "\"" + Safe(cell).Replace("\"", "\"\"") + "\"";
        var text = new StringBuilder();
        text.Append(string.Join(',', headers.Select(Quote))).Append("\r\n");
        foreach (var row in rows) text.Append(string.Join(',', row.Select(Quote))).Append("\r\n");
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text.ToString())).ToArray();      // a BOM so Excel reads Persian text as UTF-8
    }

    public static byte[] Xlsx(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Export");
        for (var c = 0; c < headers.Count; c++)
        {
            var cell = sheet.Cell(1, c + 1);
            cell.SetValue(headers[c]);
            cell.Style.Font.Bold = true;
        }
        for (var r = 0; r < rows.Count; r++)
            for (var c = 0; c < rows[r].Count; c++) sheet.Cell(r + 2, c + 1).SetValue(rows[r][c]);      // always a text value: nothing in a cell is evaluated
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>A PDF table of the rows (SCRUM-800): the heading, one header row and the rows, the page direction follows the language, every cell is plain text.</summary>
    public static byte[] Pdf(string? title, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows, bool rtl, bool landscape)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var document = Document.Create(container => container.Page(page =>
        {
            page.Size(landscape ? PageSizes.A4.Landscape() : PageSizes.A4);
            page.Margin(1f, Unit.Centimetre);
            page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial"));
            if (rtl) page.ContentFromRightToLeft();
            if (!string.IsNullOrWhiteSpace(title)) page.Header().PaddingBottom(6).Text(title).Bold().FontSize(13);
            page.Content().Table(table =>
            {
                table.ColumnsDefinition(c => { foreach (var _ in headers) c.RelativeColumn(); });
                foreach (var h in headers) table.Cell().Border(0.5f).Background(Colors.Grey.Lighten3).Padding(3).Text(h).SemiBold();
                foreach (var row in rows)
                    foreach (var cell in row) table.Cell().Border(0.5f).Padding(3).Text(string.IsNullOrEmpty(cell) ? " " : cell);
            });
            page.Footer().AlignCenter().Text(t => { t.CurrentPageNumber(); t.Span(" / "); t.TotalPages(); });
        }));
        return document.GeneratePdf();
    }

    public async Task<ActionOutcome> ExecuteAsync(ActionInvocation invocation, CancellationToken ct)
    {
        var p = invocation.Parameters;
        var key = EntityJson.Str(p?["dataSource"]);
        var format = EntityJson.Str(p?["format"]);
        var columns = (p?["columns"] as JsonArray)?.OfType<JsonObject>().ToList() ?? [];
        if (key is null || format is null || columns.Count == 0) return ActionOutcome.Refused(Text("The action is not set up.", "کنش درست تنظیم نشده است."));
        var maxRows = p?["maxRows"] is JsonValue mv && mv.TryGetValue<int>(out var m) ? m : DefaultMaxRows;

        using var scope = ActionScope.Open(scopes);
        var data = scope.ServiceProvider.GetRequiredService<DataSourceExecutor>();
        var culture = CultureInfo.GetCultureInfo(invocation.Culture);
        var narrowing = invocation.Values.Where(v => v.Key is not ("page" or "pageSize")).ToList();

        var rows = new List<IReadOnlyList<string>>();
        for (var page = 1; rows.Count < maxRows; page++)
        {
            var query = narrowing.Concat([new KeyValuePair<string, string?>("page", page.ToString(CultureInfo.InvariantCulture)), new KeyValuePair<string, string?>("pageSize", GridRequestPageSize())]);
            var result = await data.QueryAsync(key, query, invocation.Culture, invocation.Roles, invocation.UserName, ct: ct);
            switch (result.Status)
            {
                case DataStatus.NotFound: return ActionOutcome.Refused(Text("The data is not set up.", "منبع داده درست تنظیم نشده است."));
                case DataStatus.Forbidden: return ActionOutcome.Refused(Text("You may not do this.", "اجازه‌ی انجام این کار را ندارید."));
                case DataStatus.Invalid: return ActionOutcome.Refused(TranslatableText.Literal(result.Error ?? "The request is not valid."));
            }
            foreach (var item in result.Items ?? [])
            {
                var node = JsonSerializer.SerializeToNode(item, Web) as JsonObject ?? [];
                rows.Add(columns.Select(c => Cell(node[EntityJson.Str(c["field"])!])).ToList());
                if (rows.Count >= maxRows) break;
            }
            if ((result.Items?.Count ?? 0) < int.Parse(GridRequestPageSize(), CultureInfo.InvariantCulture) || rows.Count >= result.Total) break;
        }

        var headers = columns.Select(c => c["header"]?.Deserialize<TranslatableText>(PemsJson.Options)?.Resolve(culture) ?? EntityJson.Str(c["field"])!).ToList();
        var title = p?["title"]?.Deserialize<TranslatableText>(PemsJson.Options)?.Resolve(culture);
        var landscape = p?["landscape"] is not JsonValue lv || !lv.TryGetValue<bool>(out var l) || l;
        var bytes = format switch
        {
            "csv" => Csv(headers, rows),
            "pdf" => Pdf(title, headers, rows, culture.TextInfo.IsRightToLeft, landscape),
            _ => Xlsx(headers, rows),
        };
        var baseName = EntityJson.Str(p?["fileName"]) ?? "export";
        var fileName = $"{baseName}-{DateTime.UtcNow:yyyyMMdd}.{format}";
        var contentType = format switch { "csv" => "text/csv; charset=utf-8", "pdf" => "application/pdf", _ => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" };
        return ActionOutcome.Download(new FileResult(bytes, fileName, contentType));
    }

    private static string GridRequestPageSize() => PEMS.Application.Grids.GridRequest.MaxPageSize.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// <c>file.download</c>: a stored file for the user to download, by the key the upload gave (SCRUM-615). Parameters: <c>field</c> (the posted value that holds the key, default <c>key</c>), <c>prefix</c> (the logical folder the key must
/// be in, for example <c>vdr/comments</c>: a key outside it is refused, so a definition can only serve the files of the folder it names), <c>fileName</c> (optional name shown to the user; default the stored name), <c>maxMb</c> (default 50).
/// The action's own permission and roles decide who may run it. <c>authorizeBy</c> (SCRUM-801) is the record-level decision: <c>{ "dataSource": "key", "filter": "fileKey" }</c> reads the grid data source of the owning record, filtered by the file key (<c>f.fileKey=eq:key</c>),
/// with the rights of the user (its permission and row-level rules); the file is served only when that read returns a record, so a user who may not read the record that owns the file gets nothing.
/// </summary>
public sealed class FileDownloadAction(IServiceScopeFactory scopes) : IActionHandler
{
    public const string HandlerKey = "file.download";
    public const int DefaultMaxMb = 50;
    private static readonly Regex Prefix = new("^[a-z0-9][a-z0-9/_-]{0,60}$", RegexOptions.Compiled);
    private static readonly Regex FieldName = new("^[A-Za-z][A-Za-z0-9_.-]{0,60}$", RegexOptions.Compiled);
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal) { "field", "prefix", "fileName", "maxMb", "authorizeBy" };

    public string Key => HandlerKey;
    public IReadOnlyCollection<string>? AllowedRoles => null;

    /// <summary>A key is served only inside the folder the definition names: it starts with <c>prefix/</c> and has no way out of it.</summary>
    public static bool KeyAllowed(string prefix, string? key)
        => !string.IsNullOrWhiteSpace(key) && key.StartsWith(prefix + "/", StringComparison.Ordinal) && !key.Contains("..", StringComparison.Ordinal)
           && !key.Contains('\\') && !key.Contains(':') && !key.StartsWith('/');

    public IReadOnlyList<DefinitionFieldError> ValidateParameters(JsonObject? parameters)
    {
        var errors = new List<DefinitionFieldError>();
        foreach (var p in parameters ?? []) if (!Allowed.Contains(p.Key)) errors.Add(new("/" + p.Key, "unknown-property", "Def_Err_UnknownProperty", $"'{p.Key}' is not a setting of file.download."));
        if (parameters?["field"] is { } f && !(EntityJson.Str(f) is { } fs && FieldName.IsMatch(fs))) errors.Add(new("/field", "invalid-field", "Def_Err_Type", "field is the name of the posted value that holds the file key."));
        if (EntityJson.Str(parameters?["prefix"]) is not { } prefix || !Prefix.IsMatch(prefix) || prefix.Contains("//", StringComparison.Ordinal)) errors.Add(new("/prefix", "required", "Def_Err_Required", "prefix is the logical folder the files are in (lower-case letters, digits, / _ -)."));
        if (parameters?["fileName"] is { } n && !(EntityJson.Str(n) is { } ns && ns.Length is >= 1 and <= 120)) errors.Add(new("/fileName", "invalid-name", "Def_Err_Type", "fileName is a name of 1 to 120 characters."));
        if (parameters?["maxMb"] is { } max && !(max is JsonValue v && v.TryGetValue<int>(out var mb) && mb is >= 1 and <= 500)) errors.Add(new("/maxMb", "range", "Def_Err_Range", "maxMb is a whole number from 1 to 500."));
        if (parameters?["authorizeBy"] is { } by)
        {
            if (by is not JsonObject auth) errors.Add(new("/authorizeBy", "wrong-type", "Def_Err_Type", "authorizeBy is { dataSource, filter }."));
            else
            {
                foreach (var a in auth) if (a.Key is not ("dataSource" or "filter")) errors.Add(new("/authorizeBy/" + a.Key, "unknown-property", "Def_Err_UnknownProperty", $"'{a.Key}' is not a property of authorizeBy."));
                if (string.IsNullOrWhiteSpace(EntityJson.Str(auth["dataSource"]))) errors.Add(new("/authorizeBy/dataSource", "required", "Def_Err_Required", "dataSource is the key of the grid data source of the record that owns the file."));
                if (EntityJson.Str(auth["filter"]) is not { } filter || !FieldName.IsMatch(filter)) errors.Add(new("/authorizeBy/filter", "required", "Def_Err_Required", "filter is the field of the data source that holds the file key."));
            }
        }
        return errors;
    }

    public async Task<ActionOutcome> ExecuteAsync(ActionInvocation invocation, CancellationToken ct)
    {
        var p = invocation.Parameters;
        if (EntityJson.Str(p?["prefix"]) is not { } prefix) return ActionOutcome.Refused(Text("The action is not set up.", "کنش درست تنظیم نشده است."));
        var field = EntityJson.Str(p?["field"]) ?? "key";
        var key = invocation.Values.GetValueOrDefault(field);
        if (!KeyAllowed(prefix, key)) return ActionOutcome.Refused(field, Text("This file cannot be downloaded here.", "این فایل از اینجا قابل دریافت نیست."));

        using var scope = ActionScope.Open(scopes);
        if (p?["authorizeBy"] is JsonObject authorize)
        {
            var data = scope.ServiceProvider.GetRequiredService<DataSourceExecutor>();
            var query = new List<KeyValuePair<string, string?>>
            {
                new("f." + EntityJson.Str(authorize["filter"]), "eq:" + key), new("page", "1"), new("pageSize", "1"),
            };
            var owner = await data.QueryAsync(EntityJson.Str(authorize["dataSource"])!, query, invocation.Culture, invocation.Roles, invocation.UserName, ct: ct);
            if (owner.Status != DataStatus.Ok || (owner.Items?.Count ?? 0) == 0)      // not found, not allowed or no record of this user's view owns the file: the same answer, so nothing is revealed
                return ActionOutcome.Refused(field, Text("This file cannot be downloaded here.", "این فایل از اینجا قابل دریافت نیست."));
        }
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
        if (!await storage.ExistsAsync(key!)) return ActionOutcome.Refused(field, Text("The file was not found.", "فایل پیدا نشد."));

        var limit = (p?["maxMb"] is JsonValue mv && mv.TryGetValue<int>(out var mb) ? mb : DefaultMaxMb) * 1024L * 1024L;
        await using var stream = await storage.OpenReadAsync(key!);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        if (buffer.Length > limit) return ActionOutcome.Refused(field, Text("The file is too large to download here.", "حجم فایل برای دریافت از اینجا بیش از حد است."));

        var name = UploadPolicy.SafeFileName(EntityJson.Str(p?["fileName"]) ?? invocation.Values.GetValueOrDefault("fileName") ?? Path.GetFileName(key!));
        return ActionOutcome.Download(new FileResult(buffer.ToArray(), name, UploadPolicy.ContentTypeFor(name)));
    }

    private static TranslatableText Text(string en, string fa) => TranslatableText.Of(("en", en), ("fa", fa));
}
