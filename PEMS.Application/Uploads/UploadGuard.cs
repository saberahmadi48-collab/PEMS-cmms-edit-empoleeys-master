namespace PEMS.Application.Uploads;

/// <summary>Upload settings from <c>env/*.env</c> (section <c>Uploads</c>, SCRUM-202).</summary>
public sealed class UploadOptions
{
    /// <summary>Largest single file in megabytes (<c>Uploads__MaxFileSizeMb</c>). An action may set a smaller limit of its own.</summary>
    public int MaxFileSizeMb { get; set; } = 25;

    /// <summary>Largest request body in megabytes (<c>Uploads__MaxRequestSizeMb</c>): Kestrel, IIS and the multipart form reader follow it.</summary>
    public int MaxRequestSizeMb { get; set; } = 100;

    /// <summary><c>None</c> (no scanner installed) or <c>Command</c> (run a local scanner on every upload) (<c>Uploads__MalwareScan__Mode</c>).</summary>
    public string MalwareScanMode { get; set; } = MalwareScanModes.None;

    /// <summary>Scanner executable, e.g. <c>C:\Program Files\ClamAV\clamscan.exe</c> (<c>Uploads__MalwareScan__Command</c>).</summary>
    public string MalwareScanCommand { get; set; } = "";

    /// <summary>Arguments; <c>{file}</c> is replaced by the quarantined copy (<c>Uploads__MalwareScan__Arguments</c>).</summary>
    public string MalwareScanArguments { get; set; } = "--no-summary \"{file}\"";

    /// <summary>The scan fails (and the upload is refused) after this many seconds (<c>Uploads__MalwareScan__TimeoutSeconds</c>).</summary>
    public int MalwareScanTimeoutSeconds { get; set; } = 60;

    /// <summary>Folder for the quarantined copy the scanner reads; empty = the system temp folder (<c>Uploads__MalwareScan__QuarantineFolder</c>).</summary>
    public string MalwareScanQuarantineFolder { get; set; } = "";

    public long MaxFileBytes => MaxFileSizeMb * 1024L * 1024L;
    public long MaxRequestBytes => MaxRequestSizeMb * 1024L * 1024L;
}

public static class MalwareScanModes
{
    public const string None = "None";
    public const string Command = "Command";
}

/// <summary>Result of a malware scan.</summary>
public enum MalwareScanVerdict { Clean, Infected, Failed }

/// <summary>
/// Malware-scan hook (SCRUM-202). Runs on-premise only: no cloud lookup, no external call. The default implementation
/// does nothing (no scanner installed); <c>Uploads__MalwareScan__Mode=Command</c> runs a local scanner such as ClamAV
/// <c>clamscan</c> or Windows Defender <c>MpCmdRun</c>. A failed scan refuses the upload (fail-closed).
/// </summary>
public interface IMalwareScanner
{
    /// <summary>True when a real scanner is configured.</summary>
    bool Enabled { get; }

    Task<MalwareScanVerdict> ScanAsync(Stream content, string fileName, CancellationToken cancellationToken = default);
}

/// <summary>No scanner installed: every file is treated as clean.</summary>
public sealed class NoMalwareScanner : IMalwareScanner
{
    public bool Enabled => false;
    public Task<MalwareScanVerdict> ScanAsync(Stream content, string fileName, CancellationToken cancellationToken = default) =>
        Task.FromResult(MalwareScanVerdict.Clean);
}

/// <summary>Stable error codes; the web layer shows the text <c>Upload_Err_{Code}</c>.</summary>
public static class UploadErrors
{
    public const string Empty = "Empty";
    public const string TypeNotAllowed = "TypeNotAllowed";
    public const string TooLarge = "TooLarge";
    public const string ContentMismatch = "ContentMismatch";
    public const string Infected = "Infected";
    public const string ScanFailed = "ScanFailed";
    public const string NoPolicy = "NoPolicy";
}

public sealed record UploadCheckResult(bool Ok, string? Error, string? FileName)
{
    public static UploadCheckResult Accept(string fileName) => new(true, null, fileName);
    public static UploadCheckResult Reject(string error, string? fileName) => new(false, error, fileName);
}

/// <summary>
/// The one check every uploaded file passes before an action sees it (SCRUM-202): extension allowlist of the action's
/// profile, size limit, declared content type, first bytes, then the malware-scan hook.
/// </summary>
public interface IUploadGuard
{
    Task<UploadCheckResult> CheckAsync(UploadProfile profile, string? fileName, string? declaredContentType, long length,
        Func<Stream> openRead, CancellationToken cancellationToken = default);
}

public sealed class UploadGuard : IUploadGuard
{
    private readonly UploadOptions _options;
    private readonly IMalwareScanner _scanner;

    public UploadGuard(UploadOptions options, IMalwareScanner scanner)
    {
        _options = options;
        _scanner = scanner;
    }

    public async Task<UploadCheckResult> CheckAsync(UploadProfile profile, string? fileName, string? declaredContentType, long length,
        Func<Stream> openRead, CancellationToken cancellationToken = default)
    {
        var name = UploadPolicy.SafeFileName(fileName);
        if (length <= 0 || name.Length == 0) return UploadCheckResult.Reject(UploadErrors.Empty, name);

        var ext = UploadPolicy.ExtensionOf(name);
        if (!UploadPolicy.IsAllowed(profile, ext)) return UploadCheckResult.Reject(UploadErrors.TypeNotAllowed, name);
        if (length > _options.MaxFileBytes) return UploadCheckResult.Reject(UploadErrors.TooLarge, name);
        if (!UploadPolicy.DeclaredTypeMatches(ext, declaredContentType)) return UploadCheckResult.Reject(UploadErrors.ContentMismatch, name);

        await using (var stream = openRead())
        {
            var head = new byte[Math.Max(UploadPolicy.HeadLength, TextProbeLength(ext))];
            var read = await stream.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, cancellationToken);
            if (!UploadPolicy.SignatureMatches(ext, head.AsSpan(0, read)))
                return UploadCheckResult.Reject(UploadErrors.ContentMismatch, name);
        }

        if (_scanner.Enabled)
        {
            MalwareScanVerdict verdict;
            try
            {
                await using var stream = openRead();
                verdict = await _scanner.ScanAsync(stream, name, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                verdict = MalwareScanVerdict.Failed;
            }

            if (verdict == MalwareScanVerdict.Infected) return UploadCheckResult.Reject(UploadErrors.Infected, name);
            if (verdict != MalwareScanVerdict.Clean) return UploadCheckResult.Reject(UploadErrors.ScanFailed, name);
        }

        return UploadCheckResult.Accept(name);
    }

    // Text types are checked for NUL bytes over a larger window than the 16-byte signature
    private static int TextProbeLength(string ext) =>
        UploadPolicy.Types.TryGetValue(ext, out var t) && t.Text ? 8192 : 0;
}
