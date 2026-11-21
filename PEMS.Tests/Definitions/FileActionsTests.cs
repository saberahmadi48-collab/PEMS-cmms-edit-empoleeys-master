using System.Text;
using System.Text.Json.Nodes;
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.Definitions;
using PEMS.Application.Grids;
using PEMS.Application.Interfaces;
using PEMS.Application.Uploads;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>SCRUM-596 (file.upload over the guard of SCRUM-202 and the storage service) and SCRUM-615 (file.export as CSV or Excel over a grid data source); fakes only, no database.</summary>
public sealed class FileActionsTests
{
    private sealed class Guard(UploadCheckResult? answer = null) : IUploadGuard
    {
        public List<(UploadProfile Profile, string? Name, long Length)> Calls { get; } = [];
        public Task<UploadCheckResult> CheckAsync(UploadProfile profile, string? fileName, string? declaredContentType, long length, Func<Stream> openRead, CancellationToken cancellationToken = default)
        {
            Calls.Add((profile, fileName, length));
            return Task.FromResult(answer ?? UploadCheckResult.Accept(UploadPolicy.SafeFileName(fileName)));
        }
    }

    private sealed class Storage : IFileStorageService
    {
        public List<(string Folder, string Name, int Bytes)> Saved { get; } = [];
        public async Task<string> SaveAsync(string logicalFolder, string originalFileName, Stream content)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy);
            Saved.Add((logicalFolder, originalFileName, (int)copy.Length));
            return logicalFolder + "/" + Guid.NewGuid().ToString("N");
        }
        public Task<Stream> OpenReadAsync(string key) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string key) => Task.FromResult(true);
        public Task DeleteAsync(string key) => Task.CompletedTask;
    }

    private sealed class Rows(int total, IReadOnlyCollection<string>? roles = null) : IGridDataProvider
    {
        public List<int> Pages { get; } = [];
        public string Key => "rows";
        public IReadOnlyCollection<string>? AllowedRoles => roles;
        public Task<(IReadOnlyList<object> Items, int Total)> QueryAsync(GridDataRequest request, CancellationToken ct)
        {
            Pages.Add(request.Grid.Page);
            var items = Enumerable.Range((request.Grid.Page - 1) * request.Grid.PageSize, request.Grid.PageSize).Where(i => i < total)
                .Select(i => (object)new { name = i == 0 ? "=cmd|' /C calc'!A0" : $"Row {i}, \"quoted\"", qty = i, open = i % 2 == 0 }).ToList();
            return Task.FromResult(((IReadOnlyList<object>)items, total));
        }
    }

    private sealed class OneSource : IDefinitionStore
    {
        public Task<PublishedDefinition?> GetPublishedAsync(string kind, string key, string appKey = "core", CancellationToken ct = default)
            => Task.FromResult<PublishedDefinition?>(kind == "data-source" && key == "rows-grid" ? new PublishedDefinition(kind, key, appKey, 1, """{"kind":"grid","provider":"rows"}""", "h") : null);
    }

    private sealed class Access(bool allow = true) : IDefinitionAccess
    {
        public Task<bool> CanOpenAsync(string? permission, IReadOnlyCollection<string> roles, CancellationToken ct = default) => Task.FromResult(allow);
    }

    private static IServiceScopeFactory Scopes(Storage? storage = null, Rows? rows = null, bool allow = true)
    {
        var services = new ServiceCollection();
        if (storage is not null) services.AddSingleton<IFileStorageService>(storage);
        if (rows is not null) services.AddScoped(_ => new DataSourceExecutor(new OneSource(), new Access(allow), [rows]));
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static ActionInvocation Invocation(JsonObject parameters, IReadOnlyList<UploadedFile>? files = null, params (string, string?)[] values)
        => new(values.ToDictionary(v => v.Item1, v => v.Item2, StringComparer.Ordinal), "en", ["Admin"], "hamid", parameters, null, null, files);

    private static JsonObject P(string json) => (JsonObject)JsonNode.Parse(json)!;

    private static UploadedFile File(string field, string name, int bytes) => new(field, name, "application/pdf", bytes, () => new MemoryStream(new byte[bytes]));

    private const string UploadJson = """{ "field": "attachment", "profile": "pdf", "folder": "vdr/comments", "required": true }""";

    // ---- 596: file.upload

    [Fact]
    public async Task An_uploaded_file_is_checked_by_the_guard_with_the_profile_of_the_definition_then_stored_and_its_key_is_returned()
    {
        var guard = new Guard();
        var storage = new Storage();
        var outcome = await new FileUploadAction(guard, Scopes(storage)).ExecuteAsync(Invocation(P(UploadJson), [File("attachment", "plan.pdf", 2048)]), default);
        Assert.True(outcome.Ok);
        Assert.Equal((UploadProfile.Pdf, "plan.pdf", 2048L), guard.Calls[0]);
        Assert.Equal(("vdr/comments", "plan.pdf", 2048), storage.Saved[0]);
        Assert.StartsWith("vdr/comments/", outcome.Returned!["key"]);
        Assert.Equal(("plan.pdf", "2048", "application/pdf"), (outcome.Returned["fileName"], outcome.Returned["size"], outcome.Returned["contentType"]));
    }

    [Fact]
    public async Task A_rejected_file_is_refused_on_its_field_with_a_text_in_both_languages_and_nothing_is_stored()
    {
        var storage = new Storage();
        var action = new FileUploadAction(new Guard(UploadCheckResult.Reject(UploadErrors.TypeNotAllowed, "run.exe")), Scopes(storage));
        var outcome = await action.ExecuteAsync(Invocation(P(UploadJson), [File("attachment", "run.exe", 100)]), default);
        Assert.False(outcome.Ok);
        var text = outcome.FieldErrors!["attachment"];
        Assert.Empty(text.MissingLanguages(["en", "fa"]));
        Assert.Contains("run.exe", text.Resolve(System.Globalization.CultureInfo.GetCultureInfo("en")));
        Assert.Empty(storage.Saved);
    }

    [Fact]
    public async Task A_smaller_limit_of_the_definition_wins_before_the_guard_is_asked()
    {
        var guard = new Guard();
        var json = """{ "field": "attachment", "profile": "pdf", "folder": "vdr/comments", "maxFileMb": 1 }""";
        var outcome = await new FileUploadAction(guard, Scopes(new Storage())).ExecuteAsync(Invocation(P(json), [File("attachment", "big.pdf", 2 * 1024 * 1024)]), default);
        Assert.False(outcome.Ok);
        Assert.Contains("1 MB", outcome.FieldErrors!["attachment"].Resolve(System.Globalization.CultureInfo.GetCultureInfo("en")));
        Assert.Empty(guard.Calls);
    }

    [Fact]
    public async Task A_missing_file_is_a_field_error_when_required_and_an_empty_result_when_it_is_not()
    {
        var action = new FileUploadAction(new Guard(), Scopes(new Storage()));
        var required = await action.ExecuteAsync(Invocation(P(UploadJson)), default);
        Assert.Contains("attachment", required.FieldErrors!.Keys);
        var optional = await action.ExecuteAsync(Invocation(P("""{ "field": "attachment", "profile": "pdf", "folder": "vdr/comments" }""")), default);
        Assert.True(optional.Ok);
        Assert.Equal("", optional.Returned!["key"]);
    }

    [Fact]
    public async Task Only_the_file_of_the_named_field_is_read()
    {
        var storage = new Storage();
        var outcome = await new FileUploadAction(new Guard(), Scopes(storage)).ExecuteAsync(Invocation(P(UploadJson), [File("other", "x.pdf", 10), File("attachment", "mine.pdf", 20)]), default);
        Assert.True(outcome.Ok);
        Assert.Equal("mine.pdf", Assert.Single(storage.Saved).Name);
    }

    [Theory]
    [InlineData("""{ "profile": "pdf", "folder": "a" }""", "/field")]
    [InlineData("""{ "field": "f", "profile": "exe", "folder": "a" }""", "/profile")]
    [InlineData("""{ "field": "f", "profile": "licencefile", "folder": "a" }""", "/profile")]
    [InlineData("""{ "field": "f", "profile": "pdf", "folder": "../etc" }""", "/folder")]
    [InlineData("""{ "field": "f", "profile": "pdf", "folder": "a", "maxFileMb": 0 }""", "/maxFileMb")]
    [InlineData("""{ "field": "f", "profile": "pdf", "folder": "a", "x": 1 }""", "/x")]
    public void A_bad_upload_setting_is_refused_when_the_definition_is_loaded(string parameters, string pointer)
        => Assert.Contains(new FileUploadAction(new Guard(), Scopes()).ValidateParameters(P(parameters)), e => e.Pointer == pointer);

    [Fact]
    public void A_good_upload_setting_is_valid()
        => Assert.Empty(new FileUploadAction(new Guard(), Scopes()).ValidateParameters(P(UploadJson)));

    // ---- 615: file.export

    private const string ExportJson = """
        { "dataSource": "rows-grid", "format": "csv", "fileName": "stock",
          "columns": [ { "field": "name", "header": { "en": "Name", "fa": "نام" } }, { "field": "qty", "header": { "en": "Qty", "fa": "تعداد" } }, { "field": "open", "header": { "en": "Open", "fa": "باز" } } ] }
        """;

    [Fact]
    public async Task A_csv_export_reads_every_page_with_the_rights_of_the_user_and_writes_headers_and_quoted_rows()
    {
        var rows = new Rows(1200);
        var outcome = await new FileExportAction(Scopes(rows: rows)).ExecuteAsync(Invocation(P(ExportJson)), default);
        Assert.True(outcome.Ok);
        Assert.Equal([1, 2, 3], rows.Pages);                                              // 500 + 500 + 200
        var file = outcome.File!;
        Assert.StartsWith("stock-", file.FileName);
        Assert.EndsWith(".csv", file.FileName);
        Assert.StartsWith("text/csv", file.ContentType);
        Assert.Equal([0xEF, 0xBB, 0xBF], file.Bytes.Take(3));                              // BOM, so Excel reads Persian as UTF-8
        var lines = Encoding.UTF8.GetString(file.Bytes, 3, file.Bytes.Length - 3).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("\"Name\",\"Qty\",\"Open\"", lines[0]);
        Assert.Equal(1201, lines.Length);                                                  // header + 1200 rows
        Assert.Equal("\"Row 1, \"\"quoted\"\"\",\"1\",\"false\"", lines[2]);              // commas and quotes survive
    }

    [Fact]
    public async Task A_cell_that_a_spreadsheet_would_run_as_a_formula_is_written_as_text()
    {
        var outcome = await new FileExportAction(Scopes(rows: new Rows(3))).ExecuteAsync(Invocation(P(ExportJson)), default);
        var text = Encoding.UTF8.GetString(outcome.File!.Bytes);
        Assert.Contains("\"'=cmd|' /C calc'!A0\"", text);
        Assert.Equal("'=1+1", FileExportAction.Safe("=1+1"));
        Assert.Equal("'@SUM(A1)", FileExportAction.Safe("@SUM(A1)"));
        Assert.Equal("'-5", FileExportAction.Safe("-5"));
        Assert.Equal("plain", FileExportAction.Safe("plain"));
    }

    [Fact]
    public async Task An_excel_export_is_a_workbook_with_bold_headers_and_text_cells_and_the_row_limit_applies()
    {
        var json = ExportJson.Replace("\"csv\"", "\"xlsx\"").Replace("\"fileName\": \"stock\",", "\"fileName\": \"stock\", \"maxRows\": 7,");
        var outcome = await new FileExportAction(Scopes(rows: new Rows(100))).ExecuteAsync(Invocation(P(json), null, ("q", "pump")), default);
        Assert.True(outcome.Ok);
        Assert.EndsWith(".xlsx", outcome.File!.FileName);
        using var workbook = new XLWorkbook(new MemoryStream(outcome.File.Bytes));
        var sheet = workbook.Worksheet(1);
        Assert.Equal(("Name", "Qty", true), (sheet.Cell(1, 1).GetString(), sheet.Cell(1, 2).GetString(), sheet.Cell(1, 1).Style.Font.Bold));
        Assert.Equal(8, sheet.LastRowUsed()!.RowNumber());                                // header + 7 rows
        Assert.Equal("Row 3, \"quoted\"", sheet.Cell(5, 1).GetString());
    }

    [Fact]
    public async Task A_headers_are_in_the_language_of_the_request()
    {
        var fa = Invocation(P(ExportJson)) with { Culture = "fa" };
        var outcome = await new FileExportAction(Scopes(rows: new Rows(1))).ExecuteAsync(fa, default);
        Assert.Contains("\"نام\"", Encoding.UTF8.GetString(outcome.File!.Bytes));
    }

    [Fact]
    public async Task A_user_who_may_not_read_the_data_source_gets_no_file()
    {
        var denied = await new FileExportAction(Scopes(rows: new Rows(5), allow: false)).ExecuteAsync(Invocation(P(ExportJson)), default);
        Assert.False(denied.Ok);
        Assert.Null(denied.File);
        var roleBound = await new FileExportAction(Scopes(rows: new Rows(5, ["Auditor"]))).ExecuteAsync(Invocation(P(ExportJson)), default);   // the provider's own roles apply (the user has Admin only)
        Assert.False(roleBound.Ok);
        var missing = await new FileExportAction(Scopes(rows: new Rows(5))).ExecuteAsync(Invocation(P(ExportJson.Replace("rows-grid", "nope"))), default);
        Assert.False(missing.Ok);
    }

    [Theory]
    [InlineData("""{ "format": "csv", "columns": [ { "field": "a", "header": { "en": "A", "fa": "آ" } } ] }""", "/dataSource")]
    [InlineData("""{ "dataSource": "d", "format": "pdf", "columns": [ { "field": "a", "header": { "en": "A", "fa": "آ" } } ] }""", "/format")]
    [InlineData("""{ "dataSource": "d", "format": "csv", "columns": [] }""", "/columns")]
    [InlineData("""{ "dataSource": "d", "format": "csv", "columns": [ { "header": { "en": "A", "fa": "آ" } } ] }""", "/columns/0/field")]
    [InlineData("""{ "dataSource": "d", "format": "csv", "columns": [ { "field": "a", "header": { "en": "A" } } ] }""", "/columns/0/header")]
    [InlineData("""{ "dataSource": "d", "format": "csv", "fileName": "../x", "columns": [ { "field": "a", "header": { "en": "A", "fa": "آ" } } ] }""", "/fileName")]
    [InlineData("""{ "dataSource": "d", "format": "csv", "maxRows": 999999, "columns": [ { "field": "a", "header": { "en": "A", "fa": "آ" } } ] }""", "/maxRows")]
    public void A_bad_export_setting_is_refused_when_the_definition_is_loaded(string parameters, string pointer)
        => Assert.Contains(new FileExportAction(Scopes()).ValidateParameters(P(parameters)), e => e.Pointer == pointer);

    [Fact]
    public void A_good_export_setting_is_valid()
        => Assert.Empty(new FileExportAction(Scopes()).ValidateParameters(P(ExportJson)));

    // ---- SCRUM-800: PDF output from the same definition

    [Fact]
    public async Task A_pdf_export_returns_a_pdf_built_from_the_columns_with_the_rights_of_the_user()
    {
        var json = ExportJson.Replace("\"format\": \"csv\"", "\"format\": \"pdf\", \"title\": { \"en\": \"Stock\", \"fa\": \"موجودی\" }");
        Assert.Empty(new FileExportAction(Scopes()).ValidateParameters(P(json)));
        var outcome = await new FileExportAction(Scopes(rows: new Rows(30))).ExecuteAsync(Invocation(P(json)), default);
        Assert.True(outcome.Ok);
        Assert.Equal("application/pdf", outcome.File!.ContentType);
        Assert.EndsWith(".pdf", outcome.File.FileName);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(outcome.File.Bytes, 0, 4));
        var denied = await new FileExportAction(Scopes(rows: new Rows(5), allow: false)).ExecuteAsync(Invocation(P(json)), default);
        Assert.False(denied.Ok);                                                           // the data source permission still decides
    }

    [Fact]
    public void A_pdf_setting_is_checked_when_the_definition_is_loaded()
    {
        Assert.Contains(new FileExportAction(Scopes()).ValidateParameters(P(ExportJson.Replace("\"format\": \"csv\"", "\"format\": \"pdf\", \"landscape\": 1"))), e => e.Pointer == "/landscape");
        Assert.Contains(new FileExportAction(Scopes()).ValidateParameters(P(ExportJson.Replace("\"format\": \"csv\"", "\"format\": \"doc\""))), e => e.Pointer == "/format");
    }

    // ---- SCRUM-801: the file of a record is served only when the user may read that record

    private sealed class ReadableStorage : IFileStorageService
    {
        public Task<string> SaveAsync(string logicalFolder, string originalFileName, Stream content) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(string key) => Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));
        public Task<bool> ExistsAsync(string key) => Task.FromResult(true);
        public Task DeleteAsync(string key) => Task.CompletedTask;
    }

    private const string DownloadJson = """{ "prefix": "vdr/comments", "authorizeBy": { "dataSource": "rows-grid", "filter": "fileKey" } }""";

    private static IServiceScopeFactory DownloadScopes(Rows rows, bool allow)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileStorageService>(new ReadableStorage());
        services.AddScoped(_ => new DataSourceExecutor(new OneSource(), new Access(allow), [rows]));
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    [Fact]
    public async Task A_file_is_downloaded_only_when_the_user_may_read_the_record_that_owns_it()
    {
        var download = (Rows rows, bool allow) => new FileDownloadAction(DownloadScopes(rows, allow)).ExecuteAsync(Invocation(P(DownloadJson), null, ("key", "vdr/comments/ab12.pdf")), default);
        var owned = await download(new Rows(1), true);
        Assert.True(owned.Ok);
        Assert.Equal([1, 2, 3], owned.File!.Bytes);
        Assert.False((await download(new Rows(0), true)).Ok);                              // no record of this user's view owns the file
        Assert.False((await download(new Rows(1), false)).Ok);                             // the user may not open the data source of the record
        Assert.False((await new FileDownloadAction(DownloadScopes(new Rows(1), true)).ExecuteAsync(Invocation(P(DownloadJson), null, ("key", "vdr/other/ab12.pdf")), default)).Ok);   // outside the folder
    }
}
