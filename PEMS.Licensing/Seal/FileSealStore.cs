namespace PEMS.Licensing.Seal;

/// <summary>The protected seal file (design 4.2): outside the web root and outside the database backup set, service account only.</summary>
public sealed class FileSealStore : ISealStore
{
    private readonly string _path;

    public FileSealStore(string sealDirectory) => _path = Path.Combine(sealDirectory, "install.seal");

    public string Name => "file";

    public async Task<string?> ReadAsync(CancellationToken ct = default)
        => File.Exists(_path) ? await File.ReadAllTextAsync(_path, ct) : null;

    public async Task WriteAsync(string bundleJson, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        await File.WriteAllTextAsync(temp, bundleJson, ct);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temp, _path, overwrite: true);   // replace atomically so a crash never leaves half a seal
    }
}
