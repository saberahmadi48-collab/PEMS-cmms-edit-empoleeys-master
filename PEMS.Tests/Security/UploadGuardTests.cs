using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using PEMS.Application.Uploads;
using PEMS.Infrastructure.FileStorage;
using PEMS.Web.Resources;
using PEMS.Web.Security.Uploads;

namespace PEMS.Tests.Security;

/// <summary>
/// SCRUM-202: every upload goes through one guard. The coverage tests fail when an action that receives a file
/// (parameter, collection or a model property) has no [UploadPolicy], when the global filter is not registered, or when
/// code writes uploads under wwwroot again. The other tests prove the guard, the filter, the malware-scan hook and the
/// protected image store do what the policy says.
/// </summary>
public class UploadGuardTests
{
    // ---------------------------------------------------------------- coverage: no upload action skips the guard

    private static readonly Assembly Web = typeof(UploadGuardFilter).Assembly;

    private static bool ReceivesFile(Type type, int depth = 0)
    {
        if (typeof(IFormFile).IsAssignableFrom(type) || typeof(IFormFileCollection).IsAssignableFrom(type)) return true;
        if (type != typeof(string) && typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
        {
            var element = type.IsArray ? type.GetElementType() : type.GetGenericArguments().FirstOrDefault();
            return element != null && ReceivesFile(element, depth);
        }
        // a bound model (for example IncomingRegisterRequest.Files)
        if (depth < 2 && type.IsClass && type.Assembly.GetName().Name!.StartsWith("PEMS"))
            return type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Any(p => p.CanWrite && ReceivesFile(p.PropertyType, depth + 1));
        return false;
    }

    private static IEnumerable<MethodInfo> Actions() =>
        Web.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.DeclaringType!.Assembly == Web && !m.IsSpecialName && m.GetCustomAttribute<NonActionAttribute>() == null)
                .Select(m => (Type: t, Method: m)))
            .Select(x => x.Method)
            .DistinctBy(m => (m.DeclaringType, m.MetadataToken));

    private static bool HasPolicy(MethodInfo m) =>
        m.GetCustomAttribute<UploadPolicyAttribute>() != null || m.DeclaringType!.GetCustomAttribute<UploadPolicyAttribute>(inherit: true) != null;

    private static string Name(MethodInfo m) => $"{m.DeclaringType!.Name}.{m.Name}";

    [Fact]
    public void EveryActionThatReceivesAFile_DeclaresAnUploadPolicy()
    {
        var offenders = Actions().Where(m => m.GetParameters().Any(p => ReceivesFile(p.ParameterType)) && !HasPolicy(m))
            .Select(Name).Distinct().ToList();

        Assert.True(offenders.Count == 0,
            "These actions receive files without [UploadPolicy(...)] (SCRUM-202): " + string.Join(", ", offenders));
    }

    [Fact]
    public void UploadPolicy_IsOnlyOnActionsThatReceiveFiles()
    {
        var stray = Actions().Where(m => m.GetCustomAttribute<UploadPolicyAttribute>() != null && !m.GetParameters().Any(p => ReceivesFile(p.ParameterType)))
            .Select(Name).ToList();
        Assert.Empty(stray);
    }

    [Fact]
    public void ReflectionFindsEveryUploadAction_CrossCheckedWithTheSource()
    {
        // independent count: the attribute written in the controller sources (learnings 2026-10-28, SCRUM-432)
        var sourceCount = Directory.GetFiles(Path.Combine(RepoRoot(), "PEMS.Web"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Sum(f => Regex.Matches(File.ReadAllText(f), @"\[(PEMS\.Web\.Security\.Uploads\.)?UploadPolicy\(").Count);
        var reflected = Actions().Count(m => m.GetCustomAttribute<UploadPolicyAttribute>() != null && m.GetParameters().Any(p => ReceivesFile(p.ParameterType)));

        Assert.True(reflected >= 34, $"expected at least the 34 upload actions of SCRUM-202, found {reflected}");
        Assert.Equal(sourceCount, reflected);
    }

    [Fact]
    public void TheGuardFilter_IsRegisteredForEveryMvcAction_AndStaticFilesNeverServeUploads()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot(), "PEMS.Web", "Program.cs"));
        Assert.Contains("options.Filters.Add<PEMS.Web.Security.Uploads.UploadGuardFilter>();", program);
        Assert.Matches(@"app\.UseWhen\(ctx => !ctx\.Request\.Path\.StartsWithSegments\(""/uploads""[^)]*\),\s*branch => branch\.UseStaticFiles\(\)\);", program);
        Assert.DoesNotMatch(@"(?m)^app\.UseStaticFiles\(\);", program);
    }

    [Fact]
    public void NoCode_WritesUploadsUnderWwwroot()
    {
        var root = RepoRoot();
        var offenders = new[] { "PEMS.Web", "PEMS.Application", "PEMS.Persistence", "PEMS.Infrastructure" }
            .SelectMany(p => Directory.GetFiles(Path.Combine(root, p), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"""wwwroot""\s*,\s*""uploads""|WebRootPath\s*,\s*""uploads"""))
            .Select(Path.GetFileName).ToList();
        Assert.Empty(offenders);
    }

    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string thisFile = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(thisFile)!);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.Web", "Program.cs"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    // ---------------------------------------------------------------- allowlist

    private static readonly string[] Dangerous =
        { ".exe", ".dll", ".com", ".scr", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".jse", ".wsf", ".msi", ".jar", ".hta", ".html", ".htm", ".xhtml", ".svg", ".php", ".aspx", ".cshtml", ".lnk" };

    [Fact]
    public void NoProfile_AllowsExecutablesScriptsOrMarkup()
    {
        foreach (var profile in Enum.GetValues<UploadProfile>())
            foreach (var ext in Dangerous)
                Assert.False(UploadPolicy.IsAllowed(profile, ext), $"{profile} allows {ext}");
    }

    [Fact]
    public void EveryAllowedExtension_HasATypeEntry()
    {
        foreach (var profile in Enum.GetValues<UploadProfile>())
            foreach (var ext in UploadPolicy.ExtensionsOf(profile))
                Assert.True(UploadPolicy.Types.ContainsKey(ext), $"{profile}: {ext} has no type entry");
    }

    [Theory]
    [InlineData("a.pdf", true)]
    [InlineData("a.PNG", true)]
    [InlineData("a.txt", true)]
    [InlineData("a.docx", false)]
    [InlineData("a.html", false)]
    [InlineData("a.svg", false)]
    [InlineData("a", false)]
    public void OnlyPdfImagesAndText_AreShownInline(string name, bool inline) => Assert.Equal(inline, UploadPolicy.IsInlineSafe(name));

    // ---------------------------------------------------------------- guard

    private static readonly byte[] PdfBytes = "%PDF-1.7\n%\u00e2\u00e3"u8.ToArray();
    private static readonly byte[] PngBytes = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D };
    private static readonly byte[] ExeBytes = { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0, 0, 0 };

    private sealed class FakeScanner : IMalwareScanner
    {
        public bool Enabled { get; init; } = true;
        public MalwareScanVerdict Verdict { get; init; } = MalwareScanVerdict.Clean;
        public bool Throws { get; init; }
        public int Calls { get; private set; }
        public Task<MalwareScanVerdict> ScanAsync(Stream content, string fileName, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Throws) throw new IOException("scanner crashed");
            return Task.FromResult(Verdict);
        }
    }

    private static Task<UploadCheckResult> Check(UploadProfile profile, string name, byte[] bytes, string? type = null,
        IMalwareScanner? scanner = null, int maxMb = 25, long? length = null) =>
        new UploadGuard(new UploadOptions { MaxFileSizeMb = maxMb }, scanner ?? new NoMalwareScanner())
            .CheckAsync(profile, name, type, length ?? bytes.Length, () => new MemoryStream(bytes));

    [Fact]
    public async Task AnAllowedFileWithMatchingContent_IsAccepted()
    {
        var result = await Check(UploadProfile.Attachment, @"C:\Users\x\Report.pdf", PdfBytes, "application/pdf");
        Assert.True(result.Ok);
        Assert.Equal("Report.pdf", result.FileName); // the client path is dropped
    }

    [Theory]
    [InlineData(UploadProfile.Attachment, "setup.exe")]
    [InlineData(UploadProfile.Attachment, "page.html")]
    [InlineData(UploadProfile.Image, "logo.svg")]
    [InlineData(UploadProfile.Image, "scan.pdf")]
    [InlineData(UploadProfile.Spreadsheet, "list.csv")]
    [InlineData(UploadProfile.Spreadsheet, "old.xls")]
    [InlineData(UploadProfile.Attachment, "report.pdf.exe")]
    [InlineData(UploadProfile.Attachment, "noextension")]
    public async Task ATypeOutsideTheProfile_IsRefused(UploadProfile profile, string name)
    {
        var result = await Check(profile, name, PdfBytes);
        Assert.Equal(UploadErrors.TypeNotAllowed, result.Error);
    }

    [Fact]
    public async Task AnExecutableRenamedToPdf_IsRefused()
        => Assert.Equal(UploadErrors.ContentMismatch, (await Check(UploadProfile.Attachment, "invoice.pdf", ExeBytes)).Error);

    [Theory]
    [InlineData("text/html")]
    [InlineData("image/svg+xml")]
    [InlineData("application/x-msdownload")]
    [InlineData("image/png")]
    public async Task ADeclaredContentTypeThatDoesNotFit_IsRefused(string declared)
        => Assert.Equal(UploadErrors.ContentMismatch, (await Check(UploadProfile.Attachment, "a.pdf", PdfBytes, declared)).Error);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("application/octet-stream")]
    [InlineData("application/pdf; charset=binary")]
    public async Task UnknownOrMatchingDeclaredTypes_AreAccepted(string? declared)
        => Assert.True((await Check(UploadProfile.Attachment, "a.pdf", PdfBytes, declared)).Ok);

    [Fact]
    public async Task AFileOverTheSizeSetting_IsRefused()
    {
        Assert.Equal(UploadErrors.TooLarge, (await Check(UploadProfile.Attachment, "a.pdf", PdfBytes, maxMb: 1, length: 1024 * 1024 + 1)).Error);
        Assert.True((await Check(UploadProfile.Attachment, "a.pdf", PdfBytes, maxMb: 1, length: 1024 * 1024)).Ok);
    }

    [Fact]
    public async Task AnEmptyFile_IsRefused()
        => Assert.Equal(UploadErrors.Empty, (await Check(UploadProfile.Attachment, "a.pdf", Array.Empty<byte>())).Error);

    [Fact]
    public async Task TextFiles_MustBeText()
    {
        Assert.Equal(UploadErrors.ContentMismatch, (await Check(UploadProfile.Csv, "list.csv", ExeBytes)).Error);
        Assert.True((await Check(UploadProfile.Csv, "list.csv", "name,code\nAli,1\n"u8.ToArray())).Ok);
        Assert.True((await Check(UploadProfile.Csv, "list.txt", new byte[] { 0xFF, 0xFE, 0x41, 0x00 })).Ok); // UTF-16 with BOM
    }

    [Fact]
    public async Task AWavRenamedToWebp_IsRefused()
    {
        var wav = "RIFF\x24\0\0\0WAVEfmt "u8.ToArray();
        var webp = "RIFF\x24\0\0\0WEBPVP8 "u8.ToArray();
        Assert.Equal(UploadErrors.ContentMismatch, (await Check(UploadProfile.Image, "a.webp", wav)).Error);
        Assert.True((await Check(UploadProfile.Image, "a.webp", webp)).Ok);
    }

    [Fact]
    public async Task TheMalwareScanner_DecidesAfterTheOtherChecks()
    {
        var infected = new FakeScanner { Verdict = MalwareScanVerdict.Infected };
        Assert.Equal(UploadErrors.Infected, (await Check(UploadProfile.Image, "a.png", PngBytes, scanner: infected)).Error);

        var failed = new FakeScanner { Verdict = MalwareScanVerdict.Failed };
        Assert.Equal(UploadErrors.ScanFailed, (await Check(UploadProfile.Image, "a.png", PngBytes, scanner: failed)).Error);

        var crashing = new FakeScanner { Throws = true };
        Assert.Equal(UploadErrors.ScanFailed, (await Check(UploadProfile.Image, "a.png", PngBytes, scanner: crashing)).Error);

        // a file already refused by type is never sent to the scanner
        var counting = new FakeScanner();
        await Check(UploadProfile.Image, "a.exe", ExeBytes, scanner: counting);
        Assert.Equal(0, counting.Calls);

        var disabled = new FakeScanner { Enabled = false, Verdict = MalwareScanVerdict.Infected };
        Assert.True((await Check(UploadProfile.Image, "a.png", PngBytes, scanner: disabled)).Ok);
        Assert.Equal(0, disabled.Calls);
    }

    // ---------------------------------------------------------------- filter

    private sealed class KeyLocalizer : IStringLocalizer<SharedResource>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, name + ":" + string.Join("|", arguments));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Array.Empty<LocalizedString>();
    }

    private sealed class MemoryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class TempDataFactory : ITempDataDictionaryFactory
    {
        public ITempDataDictionary? Last { get; private set; }
        public ITempDataDictionary GetTempData(HttpContext context) => Last ??= new TempDataDictionary(context, new MemoryTempDataProvider());
    }

    private static (ResourceExecutingContext Context, TempDataFactory TempData) FilterContext(
        UploadProfile? policy, string? fileName, byte[] bytes, string accept = "text/html", string path = "/MOC/UploadAttachment",
        string referer = "https://localhost/MOC/Details/5")
    {
        var tempData = new TempDataFactory();
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(new UploadOptions { MaxFileSizeMb = 25 })
            .AddSingleton<IMalwareScanner, NoMalwareScanner>()
            .AddSingleton<IUploadGuard, UploadGuard>()
            .AddSingleton<IStringLocalizer<SharedResource>, KeyLocalizer>()
            .AddSingleton<ITempDataDictionaryFactory>(tempData)
            .BuildServiceProvider();

        var http = new DefaultHttpContext { RequestServices = services };
        http.Request.Method = "POST";
        http.Request.Path = path;
        http.Request.Host = new HostString("localhost");
        http.Request.ContentType = "multipart/form-data; boundary=----pems";
        http.Request.Headers.Accept = accept;
        http.Request.Headers.Referer = referer;

        var files = new FormFileCollection();
        if (fileName != null)
            files.Add(new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", fileName) { Headers = new HeaderDictionary() });
        http.Features.Set<IFormFeature>(new FormFeature(new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>(), files)));

        var descriptor = new ActionDescriptor { EndpointMetadata = policy == null ? new List<object>() : new List<object> { new UploadPolicyAttribute(policy.Value) } };
        var context = new ResourceExecutingContext(new ActionContext(http, new RouteData(), descriptor), new List<IFilterMetadata>(), new List<IValueProviderFactory>());
        return (context, tempData);
    }

    private static async Task<bool> Run(ResourceExecutingContext context)
    {
        var reached = false;
        await new UploadGuardFilter().OnResourceExecutionAsync(context, () =>
        {
            reached = true;
            return Task.FromResult(new ResourceExecutedContext(context, new List<IFilterMetadata>()));
        });
        return reached;
    }

    [Fact]
    public async Task Filter_LetsAGoodFileThrough()
    {
        var (context, _) = FilterContext(UploadProfile.Attachment, "a.pdf", PdfBytes);
        Assert.True(await Run(context));
        Assert.Null(context.Result);
    }

    [Fact]
    public async Task Filter_RefusesFilesSentToAnActionWithoutAPolicy()
    {
        var (context, _) = FilterContext(null, "a.pdf", PdfBytes, accept: "*/*");
        Assert.False(await Run(context));
        var json = Assert.IsType<JsonResult>(context.Result);
        Assert.Equal(400, json.StatusCode);
        Assert.Contains("NoPolicy", System.Text.Json.JsonSerializer.Serialize(json.Value));
    }

    [Fact]
    public async Task Filter_SendsAPagePostBackWithTheMessage()
    {
        var (context, tempData) = FilterContext(UploadProfile.Attachment, "invoice.pdf", ExeBytes);
        Assert.False(await Run(context));
        var redirect = Assert.IsType<LocalRedirectResult>(context.Result);
        Assert.Equal("/MOC/Details/5", redirect.Url);
        Assert.Equal("Upload_Err_ContentMismatch:invoice.pdf|25", tempData.Last!["Error"]);
    }

    [Fact]
    public async Task Filter_NeverRedirectsToAnotherHost()
    {
        var (context, _) = FilterContext(UploadProfile.Attachment, "a.exe", ExeBytes, referer: "https://evil.example/phish");
        await Run(context);
        Assert.Equal("/", Assert.IsType<LocalRedirectResult>(context.Result).Url);
    }

    [Fact]
    public async Task Filter_AnswersApiCallsWithAProblem()
    {
        var (context, _) = FilterContext(UploadProfile.Attachment, "a.exe", ExeBytes, accept: "application/json", path: "/api/v1/documents/1/files");
        Assert.False(await Run(context));
        var problem = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(400, problem.StatusCode);
        Assert.Equal("TypeNotAllowed", Assert.IsType<ProblemDetails>(problem.Value).Extensions["code"]);
    }

    [Fact]
    public async Task Filter_IgnoresFormsWithoutFiles()
    {
        var (context, _) = FilterContext(null, null, Array.Empty<byte>());
        Assert.True(await Run(context));
    }

    // ---------------------------------------------------------------- command-line scanner (a stand-in for ClamAV / Defender)

    private static UploadOptions ScannerOptions(string script, string quarantine, int timeout = 30) => OperatingSystem.IsWindows()
        ? new UploadOptions { MalwareScanCommand = "cmd.exe", MalwareScanArguments = "/c " + script.Replace("$F", "\"{file}\""), MalwareScanTimeoutSeconds = timeout, MalwareScanQuarantineFolder = quarantine }
        : new UploadOptions { MalwareScanCommand = "/bin/sh", MalwareScanArguments = "-c \"" + script.Replace("$F", "'{file}'").Replace(">nul", ">/dev/null").Replace("findstr /c:", "grep -q ") + "\"", MalwareScanTimeoutSeconds = timeout, MalwareScanQuarantineFolder = quarantine };

    [Fact]
    public async Task CommandScanner_ReadsTheQuarantinedCopy_AndDeletesItAfterwards()
    {
        var quarantine = Path.Combine(Path.GetTempPath(), "pems-scan-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            // a fake scanner that "finds" files containing the marker, the way clamscan exits with 1
            var scanner = new CommandLineMalwareScanner(ScannerOptions("findstr /c:PEMSMARKER $F >nul && exit 1 || exit 0", quarantine), NullLogger<CommandLineMalwareScanner>.Instance);

            Assert.Equal(MalwareScanVerdict.Infected, await scanner.ScanAsync(new MemoryStream("xx PEMSMARKER xx"u8.ToArray()), "a.txt"));
            Assert.Equal(MalwareScanVerdict.Clean, await scanner.ScanAsync(new MemoryStream("harmless"u8.ToArray()), "b.txt"));
            Assert.Empty(Directory.GetFiles(quarantine));
        }
        finally
        {
            if (Directory.Exists(quarantine)) Directory.Delete(quarantine, recursive: true);
        }
    }

    [Fact]
    public async Task CommandScanner_FailsClosed_OnErrorsTimeoutsAndMissingScanner()
    {
        var quarantine = Path.Combine(Path.GetTempPath(), "pems-scan-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var error = new CommandLineMalwareScanner(ScannerOptions("exit 2", quarantine), NullLogger<CommandLineMalwareScanner>.Instance);
            Assert.Equal(MalwareScanVerdict.Failed, await error.ScanAsync(new MemoryStream(PdfBytes), "a.pdf"));

            var slow = new CommandLineMalwareScanner(
                ScannerOptions(OperatingSystem.IsWindows() ? "ping -n 6 127.0.0.1 >nul" : "sleep 6", quarantine, timeout: 1), NullLogger<CommandLineMalwareScanner>.Instance);
            Assert.Equal(MalwareScanVerdict.Failed, await slow.ScanAsync(new MemoryStream(PdfBytes), "a.pdf"));

            var missing = new CommandLineMalwareScanner(new UploadOptions { MalwareScanCommand = Path.Combine(quarantine, "no-such-scanner.exe"), MalwareScanQuarantineFolder = quarantine },
                NullLogger<CommandLineMalwareScanner>.Instance);
            Assert.Equal(MalwareScanVerdict.Failed, await missing.ScanAsync(new MemoryStream(PdfBytes), "a.pdf"));
        }
        finally
        {
            if (Directory.Exists(quarantine)) Directory.Delete(quarantine, recursive: true);
        }
    }

    // ---------------------------------------------------------------- protected images (photo, signature, editor image)

    [Fact]
    public async Task ProtectedImages_AreStoredOutsideWwwroot_AndOnlyServedForSafeNames()
    {
        var root = Path.Combine(Path.GetTempPath(), "pems-images-test-" + Guid.NewGuid().ToString("N"));
        var store = Path.Combine(root, "filestore");
        var webRoot = Path.Combine(root, "wwwroot");
        try
        {
            var images = new ProtectedImageStore(new LocalFileStorageProvider(store), webRoot);

            var url = await images.SaveAsync(ProtectedImageStore.ProfilePhotos, "me.png", new MemoryStream(PngBytes));
            Assert.Matches(@"^/Files/Image/profile-photos/[0-9a-f\-]{36}\.png$", url);
            Assert.False(Directory.Exists(webRoot));
            Assert.Single(Directory.GetFiles(Path.Combine(store, "profile-photos")));

            var opened = await images.OpenAsync(ProtectedImageStore.ProfilePhotos, url.Split('/').Last());
            Assert.NotNull(opened);
            Assert.Equal("image/png", opened!.Value.ContentType);
            await opened.Value.Content.DisposeAsync();

            // images uploaded before SCRUM-202 are still found, through the action, not the static-file middleware
            Directory.CreateDirectory(Path.Combine(webRoot, "uploads", "signatures"));
            await File.WriteAllBytesAsync(Path.Combine(webRoot, "uploads", "signatures", "emp1_old.png"), PngBytes);
            var legacy = await images.OpenAsync(ProtectedImageStore.Signatures, "emp1_old.png");
            Assert.NotNull(legacy);
            await legacy!.Value.Content.DisposeAsync();

            Assert.Null(await images.OpenAsync("documents", "a.png"));                 // only the three image folders
            Assert.Null(await images.OpenAsync(ProtectedImageStore.Signatures, "..%2Fx.png"));
            Assert.Null(await images.OpenAsync(ProtectedImageStore.Signatures, "../../appsettings.png"));
            Assert.Null(await images.OpenAsync(ProtectedImageStore.Signatures, "page.html"));
            Assert.Null(await images.OpenAsync(ProtectedImageStore.Signatures, "missing.png"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
