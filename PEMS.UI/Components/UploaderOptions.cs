using PEMS.UI.Abstractions;

namespace PEMS.UI.Components;

// SCRUM-580 (#580): the uploader. It is a real <input type="file"> inside the page's form, so it posts and works without scripting; pems-upload.js adds drag and drop, a list of the chosen
// files with their sizes, and the same size and type check the server guard makes (UploadOptions), so a user learns before the post and not after it.

/// <summary>The texts of the uploader; <see cref="Default"/> is used when a page gives none, so the 30 import and attachment inputs do not each repeat them.</summary>
public sealed record UploaderTexts
{
    public required TranslatableText Drop { get; init; }
    public required TranslatableText Choose { get; init; }
    /// <summary>"{0}" is the file name, "{1}" the limit in MB.</summary>
    public required TranslatableText TooBig { get; init; }
    /// <summary>"{0}" is the file name, "{1}" the allowed types.</summary>
    public required TranslatableText WrongType { get; init; }
    public required TranslatableText Remove { get; init; }

    public static UploaderTexts Default { get; } = new()
    {
        Drop = TranslatableText.Of(("en", "Drop the file here or"), ("fa", "فایل را اینجا رها کنید یا")),
        Choose = TranslatableText.Of(("en", "choose"), ("fa", "انتخاب کنید")),
        TooBig = TranslatableText.Of(("en", "{0} is larger than {1} MB."), ("fa", "اندازه‌ی {0} از {1} مگابایت بیشتر است.")),
        WrongType = TranslatableText.Of(("en", "{0}: this file type is not allowed ({1})."), ("fa", "{0}: این نوع فایل مجاز نیست ({1}).")),
        Remove = TranslatableText.Of(("en", "Remove"), ("fa", "حذف"))
    };
}

/// <summary>Inputs of <see cref="PemsUploader"/>.</summary>
public sealed record UploaderOptions : FrameOptions
{
    public required string Name { get; init; }
    public string? Id { get; init; }
    /// <summary>The allowed extensions and types as in the HTML <c>accept</c> attribute (<c>.xlsx,.xlsm</c>); empty allows any type the server allows.</summary>
    public string? Accept { get; init; }
    public bool Multiple { get; init; }
    public bool Required { get; init; }
    /// <summary>small for the compact inputs of tables and modals.</summary>
    public string? Scale { get; init; }
    /// <summary>The largest single file in MB; the page leaves it out and the tag helper fills it from the upload setting.</summary>
    public int? MaxMb { get; init; }
    public UploaderTexts? Texts { get; init; }
    public string? Css { get; init; }
}

/// <summary>The upload limits the uploader shows; implemented by the host from its upload setting (<c>Uploads__MaxFileSizeMb</c>).</summary>
public interface IUploadLimits
{
    int MaxFileSizeMb { get; }
}
