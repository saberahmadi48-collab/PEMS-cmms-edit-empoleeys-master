using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.Common;
using PEMS.Application.Definitions;
using PEMS.Application.Interfaces;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>6b: the file source of a definition (provider operation-file, GET /x/{key}) names a read operation; the answer is a file or an object, the operation keeps its permission, the route table knows the host.</summary>
public class FileHostTests
{
    private static readonly OperationCatalog Operations = OperationCatalog.Scan(typeof(PemsOperationAttribute).Assembly);

    private sealed class Service : IBarcodeOperations
    {
        public Task<List<BarcodeRow>> RecentAsync() => Task.FromResult(new List<BarcodeRow>());
        public Task<List<BarcodeResolutionRow>> ResolveAsync(string code, string? t = null, int? i = null) => Task.FromResult(new List<BarcodeResolutionRow>());
        public Task<object> ResolveJsonAsync(string code) => Task.FromResult<object>(new { success = true, code });
        public Task<BarcodeFileResult> SvgAsync(int id) => Task.FromResult(new BarcodeFileResult(true, null, [1, 2, 3], "x.svg", "image/svg+xml"));
        public Task<BarcodeFileResult> PngAsync(int id) => throw new NotSupportedException();
        public Task<BarcodeFileResult> LabelPdfAsync(int id) => throw new NotSupportedException();
        public Task<BarcodeFileResult> LabelSheetAsync(string targetType, string ids) => throw new NotSupportedException();
        public Task<List<BarcodeCandidateRow>> CandidatesAsync(int? d = null, string? e = null, string? s = null) => throw new NotSupportedException();
        public Task<List<ReportChoiceRow>> DisciplinesAsync() => throw new NotSupportedException();
        public Task<List<ReportChoiceRow>> EquipmentTypesAsync() => throw new NotSupportedException();
        public Task<(bool Success, string? Error)> GenerateAsync(string targetType, int targetId, string symbology, int? employeeId) => throw new NotSupportedException();
        public Task<BarcodeFileResult> PrintAsync(int id) => throw new NotSupportedException();
        public Task<BarcodeFileResult> BulkGenerateAsync(string equipmentIds, string symbology, int? employeeId) => throw new NotSupportedException();
        public Task<(bool Success, string? Error)> DisableAsync(int id, string? reason, int? employeeId) => throw new NotSupportedException();
        public Task<(bool Success, string? Error)> RevokeAsync(int id, string? reason, int? employeeId) => throw new NotSupportedException();
        public Task<(bool Success, string? Error)> ReplaceAsync(int id, string symbology, int? employeeId) => throw new NotSupportedException();
    }

    private sealed class Access(bool allowed) : IDefinitionAccess
    {
        public Task<bool> CanOpenAsync(string? permission, IReadOnlyCollection<string> roles, CancellationToken ct = default) => Task.FromResult(allowed);
    }

    private static OperationReader Reader(bool allowed)
        => new(new ServiceCollection().AddSingleton<IBarcodeOperations>(new Service()).AddSingleton<IDefinitionAccess>(new Access(allowed)).BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), Operations);

    private static Dictionary<string, string?> Parameters(string action, string args) => new() { ["model"] = "Barcode", ["action"] = action, ["args"] = args };

    [Fact]
    public async Task A_file_operation_answers_its_bytes_and_a_json_operation_its_object()
    {
        var file = await Reader(true).InvokeAsync(Parameters("Svg", """{"id":"param:id"}"""), "en", ["Viewer"], "u", default, new Dictionary<string, string?> { ["id"] = "7" });
        Assert.True(file.Allowed);
        var answer = Assert.IsType<BarcodeFileResult>(file.Answer);
        Assert.Equal("image/svg+xml", answer.ContentType);
        Assert.Equal(3, answer.Bytes.Length);

        var json = await Reader(true).InvokeAsync(Parameters("ResolveJson", """{"code":"param:code"}"""), "en", ["Viewer"], "u", default, new Dictionary<string, string?> { ["code"] = "ABC" });
        Assert.NotNull(json.Answer);
    }

    [Fact]
    public async Task The_operation_permission_decides_and_a_missing_argument_calls_nothing()
    {
        var refused = await Reader(false).InvokeAsync(Parameters("Svg", """{"id":"param:id"}"""), "en", ["Viewer"], "u", default, new Dictionary<string, string?> { ["id"] = "7" });
        Assert.False(refused.Allowed);

        var none = await Reader(true).InvokeAsync(Parameters("Svg", """{"id":"param:id"}"""), "en", ["Viewer"], "u", default, new Dictionary<string, string?>());
        Assert.True(none.NoInput);
        Assert.Null(none.Answer);
    }

    [Fact]
    public void The_provider_checks_the_operation_and_the_disposition_and_the_route_table_accepts_the_host()
    {
        var provider = new OperationFileProvider(Reader(true));
        Assert.Equal("operation-file", provider.Key);
        Assert.Empty(provider.ValidateParameters(JsonNode.Parse("""{"model":"Barcode","action":"Svg","args":{"id":"param:id"},"disposition":"inline"}""")!.AsObject()));
        Assert.NotEmpty(provider.ValidateParameters(JsonNode.Parse("""{"model":"Barcode","action":"Svg","args":{"id":"param:id"},"disposition":"sideways"}""")!.AsObject()));
        Assert.NotEmpty(provider.ValidateParameters(JsonNode.Parse("""{"model":"Barcode","action":"Nope"}""")!.AsObject()));
        Assert.Contains("/x/", RouteDefinitionKind.HostPrefixes);
    }

    [Fact]
    public void The_barcode_operations_keep_the_permissions_of_the_old_controller()
    {
        Assert.Equal("Barcode.View", Operations.Get("Barcode", "Svg")!.Permission);
        Assert.Equal("Barcode.Create", Operations.Get("Barcode", "Print")!.Permission);
        Assert.Equal("Barcode.Approve", Operations.Get("Barcode", "BulkGenerate")!.Permission);
        Assert.Equal("Barcode.Delete", Operations.Get("Barcode", "Revoke")!.Permission);
    }
}
