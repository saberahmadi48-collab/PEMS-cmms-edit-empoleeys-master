using System.Text.RegularExpressions;
using PEMS.Application.Interfaces;

namespace PEMS.Application.Uploads;

/// <summary>
/// Images shown inside pages (profile photo, drawn signature, letter editor image) are stored in the file store
/// outside wwwroot like every other upload and served only by the authenticated <c>Files/Image</c> action (SCRUM-202).
/// Files written before SCRUM-202 still sit in <c>wwwroot/uploads/...</c>; they are read from there through the same
/// action, never by the static-file middleware.
/// </summary>
public interface IProtectedImageStore
{
    /// <summary>Stores an image and returns the URL pages use for it (<c>/Files/Image/{folder}/{name}</c>).</summary>
    Task<string> SaveAsync(string folder, string originalFileName, Stream content);

    /// <summary>Opens a stored image, or null when the folder or name is not allowed or the file does not exist.</summary>
    Task<(Stream Content, string ContentType)?> OpenAsync(string folder, string name);
}

public sealed partial class ProtectedImageStore : IProtectedImageStore
{
    public const string ProfilePhotos = "profile-photos";
    public const string Signatures = "signatures";
    public const string LetterImages = "letter-images";

    /// <summary>Folder in the file store -> folder of the files written under wwwroot before SCRUM-202.</summary>
    private static readonly IReadOnlyDictionary<string, string> LegacyFolders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [ProfilePhotos] = Path.Combine("uploads", "profile-photos"),
        [Signatures] = Path.Combine("uploads", "signatures"),
        [LetterImages] = Path.Combine("uploads", "letters", "inline"),
    };

    [GeneratedRegex(@"^[A-Za-z0-9_\-]{1,100}\.(png|jpe?g|gif|webp)$", RegexOptions.IgnoreCase)]
    private static partial Regex SafeName();

    private readonly IFileStorageService _storage;
    private readonly string _legacyWebRoot;

    /// <param name="legacyWebRoot">wwwroot of the application, only read for images uploaded before SCRUM-202.</param>
    public ProtectedImageStore(IFileStorageService storage, string legacyWebRoot)
    {
        _storage = storage;
        _legacyWebRoot = legacyWebRoot;
    }

    public static bool IsAllowed(string? folder, string? name) =>
        folder != null && name != null && LegacyFolders.ContainsKey(folder) && SafeName().IsMatch(name);

    public async Task<string> SaveAsync(string folder, string originalFileName, Stream content)
    {
        if (!LegacyFolders.ContainsKey(folder)) throw new ArgumentOutOfRangeException(nameof(folder));
        var key = await _storage.SaveAsync(folder, "image" + UploadPolicy.ExtensionOf(originalFileName), content);
        return "/Files/Image/" + key;
    }

    public async Task<(Stream Content, string ContentType)?> OpenAsync(string folder, string name)
    {
        if (!IsAllowed(folder, name)) return null;
        var contentType = UploadPolicy.ContentTypeFor(name);

        var key = folder.ToLowerInvariant() + "/" + name;
        if (await _storage.ExistsAsync(key))
            return (await _storage.OpenReadAsync(key), contentType);

        var legacy = Path.Combine(_legacyWebRoot, LegacyFolders[folder], name);
        if (!File.Exists(legacy)) return null;
        return (new FileStream(legacy, FileMode.Open, FileAccess.Read, FileShare.Read), contentType);
    }
}
