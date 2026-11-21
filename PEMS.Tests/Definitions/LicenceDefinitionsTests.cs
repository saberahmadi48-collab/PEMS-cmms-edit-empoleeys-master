using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using PEMS.Application.Common;
using PEMS.Application.Definitions;
using PEMS.Application.Interfaces;
using PEMS.Application.Uploads;
using PEMS.Persistence.Seed;
using PEMS.Tests.TestSupport;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>SCRUM-635 (and the generic inputs of SCRUM-640): an operation takes an uploaded file as bytes (checked by the upload guard with the profile the action names) and returns a file to download; the licence page is built from them.</summary>
public sealed class LicenceDefinitionsTests
{
    private static readonly OperationCatalog Operations = OperationCatalog.Scan(typeof(PemsOperationAttribute).Assembly);
    private static readonly string[] Admin = ["SystemAdmin"];

    private sealed class Access : IDefinitionAccess
    {
        public Task<bool> CanOpenAsync(string? permission, IReadOnlyCollection<string> roles, CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class Guard(bool accept) : IUploadGuard
    {
        public UploadProfile? Asked;
        public Task<UploadCheckResult> CheckAsync(UploadProfile profile, string? fileName, string? declaredContentType, long length, Func<Stream> openRead, CancellationToken cancellationToken = default)
        { Asked = profile; return Task.FromResult(accept ? UploadCheckResult.Accept(fileName ?? "x") : UploadCheckResult.Reject(UploadErrors.TypeNotAllowed, fileName)); }
    }

    private sealed class Licences : ILicencePageService
    {
        public byte[]? Applied;
        public Task<List<LicenceStatusRowDto>> StatusAsync(CancellationToken ct = default) => Task.FromResult(new List<LicenceStatusRowDto> { new() { State = "Active" } });
        public Task<LicenceFileResult> CreateRequestAsync(string requestType, string customerRef, string? orderRef, string? reason, CancellationToken ct = default)
            => Task.FromResult(string.IsNullOrWhiteSpace(customerRef) ? new LicenceFileResult { Success = false, Error = "Lic_Err_RequestFields" } : new LicenceFileResult { Success = true, Bytes = [1, 2, 3], FileName = "r.pemsreq" });
        public Task<LicenceApplyResult> ApplyAsync(byte[] file, CancellationToken ct = default) { Applied = file; return Task.FromResult(new LicenceApplyResult(true, "Lic_Applied")); }
    }

    private static (IServiceScopeFactory Scopes, Licences Licences, Guard Guard) Setup(bool accept = true)
    {
        var licences = new Licences(); var guard = new Guard(accept);
        var sp = new ServiceCollection().AddScoped(_ => PemsDbContextFactory.CreateInMemory("lic-" + Guid.NewGuid())).AddSingleton<IDefinitionAccess>(new Access())
            .AddSingleton<ILicencePageService>(licences).AddSingleton<IUploadGuard>(guard).AddSingleton(Operations).BuildServiceProvider();
        return (sp.GetRequiredService<IServiceScopeFactory>(), licences, guard);
    }

    private static JsonObject Params(string action) => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, SystemDefinitionSeeder.FolderName, "action", "core", action + ".json")))!["payload"]!["parameters"]!.AsObject();

    private static ActionInvocation Run(string action, IReadOnlyList<UploadedFile>? files, params (string, string?)[] values)
        => new(values.ToDictionary(v => v.Item1, v => v.Item2), "en", Admin, "hamid", Params(action), Files: files);

    private static UploadedFile Upload(byte[] bytes) => new("file", "a.pemslic", "application/octet-stream", bytes.Length, () => new MemoryStream(bytes));

    [Fact]
    public async Task An_uploaded_file_is_checked_by_the_guard_with_the_named_profile_and_reaches_the_service_as_bytes()
    {
        var (scopes, licences, guard) = Setup();
        var ops = new OperationInvokeAction(scopes, Operations);
        Assert.False((await ops.ExecuteAsync(Run("license-upload", null), CancellationToken.None)).Ok);                       // no file: refused
        var done = await ops.ExecuteAsync(Run("license-upload", [Upload([9, 8, 7])]), CancellationToken.None);
        Assert.True(done.Ok, done.FormError?.ToString());
        Assert.Equal(UploadProfile.LicenceFile, guard.Asked);
        Assert.Equal([9, 8, 7], licences.Applied);

        var (refusingScopes, refusing, _) = Setup(accept: false);
        var refused = await new OperationInvokeAction(refusingScopes, Operations).ExecuteAsync(Run("license-upload", [Upload([1])]), CancellationToken.None);
        Assert.False(refused.Ok);
        Assert.Null(refusing.Applied);                                                                                         // the service never sees a refused file
    }

    [Fact]
    public async Task A_result_with_bytes_is_a_download_and_a_refusal_is_a_message()
    {
        var (scopes, _, _) = Setup();
        var ops = new OperationInvokeAction(scopes, Operations);
        var file = await ops.ExecuteAsync(Run("license-request", null, ("requestType", "activation"), ("customerRef", "C-1")), CancellationToken.None);
        Assert.True(file.Ok);
        Assert.Equal("r.pemsreq", file.File!.FileName);
        var refused = await ops.ExecuteAsync(Run("license-request", null, ("requestType", "activation"), ("customerRef", "")), CancellationToken.None);
        Assert.False(refused.Ok);
        Assert.Null(refused.File);
    }

    [Fact]
    public void The_shipped_actions_validate_and_a_file_parameter_needs_an_upload_profile()
    {
        var invoke = new OperationInvokeAction(null!, Operations);
        Assert.Empty(invoke.ValidateParameters(Params("license-upload")));
        Assert.Empty(invoke.ValidateParameters(Params("license-request")));
        Assert.Contains(invoke.ValidateParameters((JsonObject)JsonNode.Parse("""{"model":"Licence","action":"Apply","args":{"file":"file:file"}}""")!), e => e.Pointer == "/upload");
    }
}
