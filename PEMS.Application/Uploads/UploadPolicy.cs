namespace PEMS.Application.Uploads;

/// <summary>What an upload action accepts. Every action that receives a file names one profile (SCRUM-202).</summary>
public enum UploadProfile
{
    /// <summary>Business attachments: office files, PDF, images, mail, CAD, archives. No executables, scripts, HTML or SVG.</summary>
    Attachment,
    /// <summary>Raster images shown in the page (profile photo, letter editor image).</summary>
    Image,
    /// <summary>Excel workbooks read by an import (ClosedXML; macros are never run).</summary>
    Spreadsheet,
    /// <summary>Plain text lists (CSV / TXT).</summary>
    Csv,
    /// <summary>A single PDF (OCR scan).</summary>
    Pdf,
    /// <summary>A licence file or licence bundle (SCRUM-26).</summary>
    LicenceFile,
    /// <summary>A signed migration package (SCRUM-581, 582): a zip the migrator reads; its signature and checksums are checked after the upload checks.</summary>
    MigrationPackage,
}

/// <summary>
/// The upload allowlist (SCRUM-202): per extension the content types a browser may declare, the first bytes the
/// content must start with, and whether the file may be shown inline. This is a security policy, so it lives in
/// code, not in the env files; only the size limits are settings (<see cref="UploadOptions"/>).
/// </summary>
public static class UploadPolicy
{
    /// <summary>One allowed file type.</summary>
    /// <param name="ContentType">Content type PEMS sends when it serves the file (never the browser's value).</param>
    /// <param name="DeclaredTypes">Extra content types a browser may declare for it; <c>application/octet-stream</c> and empty are always accepted.</param>
    /// <param name="Signatures">Accepted first bytes; empty = no reliable signature (text, CAD), checked by extension only.</param>
    /// <param name="InlineSafe">May be shown inside the browser by <c>Files/Preview</c>; everything else is a download.</param>
    /// <param name="Text">Plain text: must not contain NUL bytes.</param>
    public sealed record FileType(string ContentType, string[] DeclaredTypes, byte[][] Signatures, bool InlineSafe = false, bool Text = false);

    private static readonly byte[] Zip = { 0x50, 0x4B, 0x03, 0x04 };
    private static readonly byte[] ZipEmpty = { 0x50, 0x4B, 0x05, 0x06 };
    private static readonly byte[] Ole = { 0xD0, 0xCF, 0x11, 0xE0 };
    private static readonly byte[][] None = Array.Empty<byte[]>();

    private static FileType Office(string type, params string[] declared) => new(type, declared, new[] { Zip });
    private static FileType Legacy(string type, params string[] declared) => new(type, declared, new[] { Ole });

    /// <summary>Every extension PEMS accepts anywhere, lower case with the dot.</summary>
    public static readonly IReadOnlyDictionary<string, FileType> Types = new Dictionary<string, FileType>
    {
        [".pdf"] = new("application/pdf", new[] { "application/x-pdf" }, new[] { new byte[] { 0x25, 0x50, 0x44, 0x46 } }, InlineSafe: true),
        [".png"] = new("image/png", Array.Empty<string>(), new[] { new byte[] { 0x89, 0x50, 0x4E, 0x47 } }, InlineSafe: true),
        [".jpg"] = new("image/jpeg", new[] { "image/pjpeg" }, new[] { new byte[] { 0xFF, 0xD8, 0xFF } }, InlineSafe: true),
        [".jpeg"] = new("image/jpeg", new[] { "image/pjpeg" }, new[] { new byte[] { 0xFF, 0xD8, 0xFF } }, InlineSafe: true),
        [".gif"] = new("image/gif", Array.Empty<string>(), new[] { "GIF87a"u8.ToArray(), "GIF89a"u8.ToArray() }, InlineSafe: true),
        [".webp"] = new("image/webp", Array.Empty<string>(), new[] { "RIFF"u8.ToArray() }, InlineSafe: true),
        [".bmp"] = new("image/bmp", new[] { "image/x-ms-bmp" }, new[] { "BM"u8.ToArray() }),
        [".tif"] = new("image/tiff", Array.Empty<string>(), new[] { new byte[] { 0x49, 0x49, 0x2A, 0x00 }, new byte[] { 0x4D, 0x4D, 0x00, 0x2A } }),
        [".tiff"] = new("image/tiff", Array.Empty<string>(), new[] { new byte[] { 0x49, 0x49, 0x2A, 0x00 }, new byte[] { 0x4D, 0x4D, 0x00, 0x2A } }),
        [".docx"] = Office("application/vnd.openxmlformats-officedocument.wordprocessingml.document", "application/zip", "application/x-zip-compressed"),
        [".xlsx"] = Office("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "application/zip", "application/x-zip-compressed", "application/vnd.ms-excel"),
        [".xlsm"] = Office("application/vnd.ms-excel.sheet.macroEnabled.12", "application/zip", "application/x-zip-compressed", "application/vnd.ms-excel"),
        [".pptx"] = Office("application/vnd.openxmlformats-officedocument.presentationml.presentation", "application/zip", "application/x-zip-compressed"),
        [".odt"] = Office("application/vnd.oasis.opendocument.text", "application/zip"),
        [".ods"] = Office("application/vnd.oasis.opendocument.spreadsheet", "application/zip"),
        [".zip"] = new("application/zip", new[] { "application/x-zip-compressed", "multipart/x-zip" }, new[] { Zip, ZipEmpty }),
        [".doc"] = Legacy("application/msword"),
        [".xls"] = Legacy("application/vnd.ms-excel"),
        [".ppt"] = Legacy("application/vnd.ms-powerpoint"),
        [".msg"] = Legacy("application/vnd.ms-outlook", "application/x-ole-storage"),
        [".rtf"] = new("application/rtf", new[] { "text/rtf" }, new[] { "{\\rtf"u8.ToArray() }),
        [".7z"] = new("application/x-7z-compressed", Array.Empty<string>(), new[] { new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C } }),
        [".rar"] = new("application/vnd.rar", new[] { "application/x-rar-compressed", "application/x-rar" }, new[] { "Rar!"u8.ToArray() }),
        [".dwg"] = new("image/vnd.dwg", new[] { "application/acad", "application/dwg", "image/x-dwg", "application/x-dwg" }, new[] { "AC10"u8.ToArray() }),
        [".dxf"] = new("image/vnd.dxf", new[] { "application/dxf", "image/x-dxf", "application/x-dxf", "text/plain" }, None, Text: true),
        [".step"] = new("model/step", new[] { "application/step", "text/plain" }, new[] { "ISO-10303-21"u8.ToArray() }),
        [".stp"] = new("model/step", new[] { "application/step", "text/plain" }, new[] { "ISO-10303-21"u8.ToArray() }),
        [".ifc"] = new("application/x-step", new[] { "text/plain" }, new[] { "ISO-10303-21"u8.ToArray() }),
        [".eml"] = new("message/rfc822", Array.Empty<string>(), None, Text: true),
        [".txt"] = new("text/plain", Array.Empty<string>(), None, InlineSafe: true, Text: true),
        [".csv"] = new("text/csv", new[] { "text/plain", "application/vnd.ms-excel", "application/csv", "text/x-csv" }, None, Text: true),
        [".pemslic"] = new("application/octet-stream", new[] { "text/plain", "application/json" }, None, Text: true),
    };

    private static readonly IReadOnlyDictionary<UploadProfile, string[]> ProfileExtensions = new Dictionary<UploadProfile, string[]>
    {
        [UploadProfile.Attachment] = new[]
        {
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".xlsm", ".ppt", ".pptx", ".odt", ".ods", ".rtf", ".txt", ".csv",
            ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".tif", ".tiff",
            ".zip", ".7z", ".rar", ".msg", ".eml", ".dwg", ".dxf", ".step", ".stp", ".ifc",
        },
        [UploadProfile.Image] = new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp" },
        [UploadProfile.Spreadsheet] = new[] { ".xlsx", ".xlsm" },
        [UploadProfile.Csv] = new[] { ".csv", ".txt" },
        [UploadProfile.Pdf] = new[] { ".pdf" },
        [UploadProfile.LicenceFile] = new[] { ".pemslic", ".zip" },
        [UploadProfile.MigrationPackage] = new[] { ".zip" },
    };

    /// <summary>Content types that are never accepted, whatever the extension says (HTML, SVG, scripts, executables).</summary>
    private static readonly string[] ForbiddenDeclaredTypes =
    {
        "text/html", "application/xhtml+xml", "image/svg+xml", "text/javascript", "application/javascript", "application/x-javascript",
        "application/x-msdownload", "application/x-msdos-program", "application/x-executable", "application/x-sh", "application/x-bat",
        "application/hta", "application/x-ms-shortcut", "application/java-archive",
    };

    /// <summary>Number of bytes the guard reads to check the signature.</summary>
    public const int HeadLength = 16;

    public static IReadOnlyCollection<string> ExtensionsOf(UploadProfile profile) => ProfileExtensions[profile];

    /// <summary>Lower-case extension of a file name, with the dot ("" when there is none).</summary>
    public static string ExtensionOf(string? fileName) =>
        Path.GetExtension(SafeFileName(fileName)).ToLowerInvariant();

    /// <summary>File name without any client path ("C:\x\a.pdf" becomes "a.pdf").</summary>
    public static string SafeFileName(string? fileName)
    {
        var name = (fileName ?? "").Replace('\\', '/');
        var slash = name.LastIndexOf('/');
        return (slash >= 0 ? name[(slash + 1)..] : name).Trim();
    }

    public static bool IsAllowed(UploadProfile profile, string extension) => ProfileExtensions[profile].Contains(extension);

    /// <summary>The browser's declared type is accepted for this extension.</summary>
    public static bool DeclaredTypeMatches(string extension, string? declaredType)
    {
        var declared = (declaredType ?? "").Split(';')[0].Trim().ToLowerInvariant();
        if (ForbiddenDeclaredTypes.Contains(declared)) return false;
        if (declared.Length == 0 || declared == "application/octet-stream") return true;
        if (!Types.TryGetValue(extension, out var type)) return false;
        return declared == type.ContentType || type.DeclaredTypes.Contains(declared);
    }

    /// <summary>The first bytes fit the extension. Extensions without a signature pass (text files are checked for NUL bytes instead).</summary>
    public static bool SignatureMatches(string extension, ReadOnlySpan<byte> head)
    {
        if (!Types.TryGetValue(extension, out var type)) return false;
        if (type.Text)
        {
            // UTF-16 text (Excel "Unicode text") has a byte-order mark and NUL bytes by design
            var utf16 = head.Length >= 2 && ((head[0] == 0xFF && head[1] == 0xFE) || (head[0] == 0xFE && head[1] == 0xFF));
            return utf16 || head.IndexOf((byte)0) < 0;
        }
        if (type.Signatures.Length == 0) return true;
        // RIFF is shared by WAV/AVI: a WebP has "WEBP" at offset 8
        if (extension == ".webp" && (head.Length < 12 || !head[8..12].SequenceEqual("WEBP"u8))) return false;
        foreach (var sig in type.Signatures)
            if (head.Length >= sig.Length && head[..sig.Length].SequenceEqual(sig)) return true;
        return false;
    }

    /// <summary>Content type PEMS sends for a stored file; unknown extensions are served as a download of bytes.</summary>
    public static string ContentTypeFor(string? fileName) =>
        Types.TryGetValue(ExtensionOf(fileName), out var type) ? type.ContentType : "application/octet-stream";

    /// <summary>The file may be shown inline by the preview (PDF, raster images, plain text).</summary>
    public static bool IsInlineSafe(string? fileName) =>
        Types.TryGetValue(ExtensionOf(fileName), out var type) && type.InlineSafe;
}
