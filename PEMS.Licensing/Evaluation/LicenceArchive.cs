using System.IO.Compression;
using System.Text;
using PEMS.Licensing.Formats;

namespace PEMS.Licensing.Evaluation;

/// <summary>Reads a .pemslic.zip bundle of several licences (design 5.1). Hostile archives are rejected before any licence is parsed.</summary>
public static class LicenceArchive
{
    public const int MaxEntries = 16;
    public const int MaxTotalBytes = 1024 * 1024;

    public static bool LooksLikeZip(byte[] data) => data.Length > 3 && data[0] == 'P' && data[1] == 'K';

    public static LicenseResult<List<string>> Extract(byte[] data)
    {
        if (data.Length > MaxTotalBytes) return LicenseResult<List<string>>.Fail(LicenseFailure.TooLarge);
        var texts = new List<string>();
        try
        {
            using var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
            if (zip.Entries.Count > MaxEntries) return LicenseResult<List<string>>.Fail(LicenseFailure.UnsafeArchive, "too many entries");
            long total = 0;
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName;
                var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
                if (name.Contains("..") || name.StartsWith('/') || name.Contains('\\') || name.Contains(':') || unixType == 0xA000)
                    return LicenseResult<List<string>>.Fail(LicenseFailure.UnsafeArchive, "entry name");
                if (name.EndsWith('/')) return LicenseResult<List<string>>.Fail(LicenseFailure.UnsafeArchive, "directory entry");
                if (name.Contains('/')) return LicenseResult<List<string>>.Fail(LicenseFailure.UnsafeArchive, "nested entry");
                if (!name.EndsWith(".pemslic", StringComparison.OrdinalIgnoreCase)) continue;   // manifest.json and the like are never trusted nor read
                if (entry.Length > FileFormats.MaxLicenceBytes) return LicenseResult<List<string>>.Fail(LicenseFailure.TooLarge);
                total += entry.Length;
                if (total > MaxTotalBytes) return LicenseResult<List<string>>.Fail(LicenseFailure.TooLarge);

                using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                texts.Add(reader.ReadToEnd());
            }
        }
        catch (InvalidDataException) { return LicenseResult<List<string>>.Fail(LicenseFailure.UnsafeArchive, "not a zip"); }
        return texts.Count == 0
            ? LicenseResult<List<string>>.Fail(LicenseFailure.NoLicenceInArchive)
            : LicenseResult<List<string>>.Success(texts);
    }
}
