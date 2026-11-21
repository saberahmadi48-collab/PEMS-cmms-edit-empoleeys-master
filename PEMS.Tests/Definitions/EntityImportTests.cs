using System.Globalization;
using System.Text.Json.Nodes;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.Definitions;
using PEMS.Application.Uploads;
using PEMS.Domain.Entities;
using PEMS.Persistence.Seed;
using PEMS.Tests.TestSupport;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>Batch 5: <c>entity.import</c> runs with the parameters of the shipped definition departments-import: rows are added, duplicates and invalid rows are skipped and counted, and a repeat inside the file is found.</summary>
public sealed class EntityImportTests
{
    private static readonly EntityCatalog Catalog = EntityCatalog.Default();

    private sealed class Access : IDefinitionAccess
    {
        public Task<bool> CanOpenAsync(string? permission, IReadOnlyCollection<string> roles, CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class Guard(bool accept = true) : IUploadGuard
    {
        public Task<UploadCheckResult> CheckAsync(UploadProfile profile, string? fileName, string? declaredContentType, long length, Func<Stream> openRead, CancellationToken cancellationToken = default)
            => Task.FromResult(accept ? UploadCheckResult.Accept(UploadPolicy.SafeFileName(fileName)) : UploadCheckResult.Reject(UploadErrors.TypeNotAllowed, fileName));
    }

    private static (IServiceScopeFactory Scopes, string Db) Setup()
    {
        var db = "import-" + Guid.NewGuid();
        var sp = new ServiceCollection().AddScoped(_ => PemsDbContextFactory.CreateInMemory(db)).AddSingleton<IDefinitionAccess>(new Access()).BuildServiceProvider();
        return (sp.GetRequiredService<IServiceScopeFactory>(), db);
    }

    private static JsonObject Shipped(string key)
        => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, SystemDefinitionSeeder.FolderName, "action", "core", key + ".json")))!["payload"]!["parameters"]!;

    private static byte[] Sheet(params string[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Departments");
        sheet.Cell(1, 1).Value = "Code"; sheet.Cell(1, 2).Value = "Name"; sheet.Cell(1, 3).Value = "Description";
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < rows[r].Length; c++) sheet.Cell(r + 2, c + 1).Value = rows[r][c];
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static ActionInvocation Invoke(byte[] bytes, string fileName = "departments.xlsx")
        => new(new Dictionary<string, string?>(), "en", ["SystemAdmin"], "hamid", Shipped("departments-import"), Files: [new UploadedFile("file", fileName, null, bytes.Length, () => new MemoryStream(bytes))]);

    [Fact]
    public async Task Rows_are_added_and_duplicates_invalid_rows_and_a_repeat_in_the_file_are_skipped_and_counted()
    {
        var (scopes, name) = Setup();
        using (var db = PemsDbContextFactory.CreateInMemory(name)) { db.Departments.Add(new Department { Code = "OLD", Name = "Operations", IsActive = true }); db.SaveChanges(); }
        var sheet = Sheet(["ENG", "Engineering", "Design office"], ["OPS", "operations", "Already there (other case)"], ["", "", "No name"], ["FIN", "Finance", ""], ["ENG2", "Engineering", "Repeat in the file"]);

        var outcome = await new EntityImportAction(scopes, Catalog, new Guard()).ExecuteAsync(Invoke(sheet), CancellationToken.None);

        Assert.True(outcome.Ok, outcome.FormError?.ToString());
        Assert.Equal(("2", "3"), (outcome.Returned!["added"], outcome.Returned["skipped"]));
        using var read = PemsDbContextFactory.CreateInMemory(name);
        var names = await read.Departments.OrderBy(d => d.Name).Select(d => d.Name).ToListAsync();
        Assert.Equal(["Engineering", "Finance", "Operations"], names);
        Assert.All(await read.Departments.Where(d => d.Name != "Operations").ToListAsync(), d => Assert.True(d.IsActive && d.CreatedDate != default));
    }

    [Fact]
    public async Task A_missing_file_a_refused_file_and_a_non_spreadsheet_are_refused_on_the_file_field()
    {
        var (scopes, _) = Setup();
        var none = await new EntityImportAction(scopes, Catalog, new Guard()).ExecuteAsync(Invoke([], "empty.xlsx") with { Files = [] }, CancellationToken.None);
        Assert.False(none.Ok);
        Assert.Contains("file", none.FieldErrors!.Keys);

        var refused = await new EntityImportAction(scopes, Catalog, new Guard(accept: false)).ExecuteAsync(Invoke(Sheet(["A", "B", "C"])), CancellationToken.None);
        Assert.Contains("file", refused.FieldErrors!.Keys);

        var notExcel = await new EntityImportAction(scopes, Catalog, new Guard()).ExecuteAsync(Invoke([1, 2, 3, 4], "x.xlsx"), CancellationToken.None);
        Assert.Contains("file", notExcel.FieldErrors!.Keys);
    }

    [Fact]
    public void The_settings_of_the_shipped_definition_are_valid_and_a_bad_column_is_refused()
    {
        var action = new EntityImportAction(Setup().Scopes, Catalog, new Guard());
        Assert.Empty(action.ValidateParameters(Shipped("departments-import")));
        var bad = (JsonObject)Shipped("departments-import").DeepClone();
        bad["columns"] = new JsonArray("Code", "NotAProperty");
        Assert.Contains(action.ValidateParameters(bad), e => e.Pointer == "/columns");
        _ = CultureInfo.InvariantCulture;
    }
}
