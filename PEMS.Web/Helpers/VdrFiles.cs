using PEMS.Application.DTOs;
using PEMS.Application.Uploads;

namespace PEMS.Web.Helpers;

/// <summary>SCRUM-530: one stored file of a VDR item, as listed in the Files tab.</summary>
/// <param name="Kind">Main, CommentSheet or ReplySheet (resource key Vd_FileKind_{Kind}).</param>
public sealed record VdrFile(int AttachmentId, string FileName, int RevisionNo, string Kind, bool CanPreview);

/// <summary>SCRUM-530: the files of every submission of a VDR item (main file, comment sheet, reply sheet), newest revision first.</summary>
public static class VdrFiles
{
    /// <summary>SCRUM-531: the kinds a file can be uploaded as in the Files tab, in the order of the kind list.</summary>
    public static readonly string[] Kinds = ["Main", "CommentSheet", "ReplySheet"];

    /// <summary>SCRUM-531: the storage folder under vdr/ for an upload kind; null for an unknown kind.</summary>
    public static string? FolderOf(string? kind) => kind switch
    {
        "Main" => "documents",
        "CommentSheet" => "comment-sheets",
        "ReplySheet" => "reply-sheets",
        _ => null
    };

    public static IReadOnlyList<VdrFile> From(IEnumerable<VDRSubmissionDto> submissions)
    {
        var files = new List<VdrFile>();
        foreach (var s in submissions.OrderByDescending(s => s.RevisionNo))
        {
            Add(files, s.AttachmentId, Path.GetFileName(s.MainFilePath ?? ""), s.RevisionNo, "Main");
            Add(files, s.CommentSheetAttachmentId, s.CommentSheetFileName ?? Path.GetFileName(s.CommentSheetFilePath ?? ""), s.RevisionNo, "CommentSheet");
            Add(files, s.ReplySheetAttachmentId, s.ReplySheetFileName ?? Path.GetFileName(s.ReplySheetFilePath ?? ""), s.RevisionNo, "ReplySheet");
        }
        return files;
    }

    /// <summary>The file to show: the requested one when it belongs to the item and can be previewed, else the newest previewable file.</summary>
    public static VdrFile? Selected(IReadOnlyList<VdrFile> files, int? requestedId) =>
        files.FirstOrDefault(f => f.AttachmentId == requestedId && f.CanPreview) ?? files.FirstOrDefault(f => f.CanPreview);

    private static void Add(List<VdrFile> files, int? id, string name, int revision, string kind)
    {
        if (id is not int attachmentId || files.Any(f => f.AttachmentId == attachmentId)) return;
        files.Add(new VdrFile(attachmentId, string.IsNullOrWhiteSpace(name) ? "#" + attachmentId : name, revision, kind, UploadPolicy.IsInlineSafe(name)));
    }
}
