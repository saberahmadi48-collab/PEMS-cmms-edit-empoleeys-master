using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using PEMS.Application.DTOs;
using PEMS.Web.Controllers;
using PEMS.Web.Helpers;

namespace PEMS.Tests.Web;

/// <summary>
/// SCRUM-529: the CONTRACTOR and SUB-CONTRACTOR number forms moved from VDR details to Admin > Document numbering, keeping their actions.
/// SCRUM-530: VDR details has a Files tab with every submission file and an inline preview.
/// </summary>
public class VdrNumberingAndFilesTests
{
    // ---- SCRUM-529
    [Fact]
    public void VdrDetails_NoLongerHoldsTheNumberForms_ButShowsTheVendorNumberAndTheAdminLink()
    {
        var view = Read("PEMS.Web", "Views", "VDR", "Details.cshtml");
        Assert.DoesNotContain("GenerateContractorNumber", view);
        Assert.DoesNotContain("GenerateVendorNumber", view);
        Assert.Contains("Vd_VendorFormatNo", view);
        Assert.Contains("asp-area=\"Admin\" asp-controller=\"Numbering\" asp-action=\"Index\" asp-route-vdrItemId=\"@Model.VDRItemId\"", view);
    }

    [Theory]
    [InlineData("numbering", "Index", "Numbering")]
    [InlineData(null, "Details", null)]
    [InlineData("elsewhere", "Details", null)]
    public void NumberActions_ReturnToTheAdminPage_OnlyWhenAsked(string? returnTo, string action, string? controller)
    {
        var method = typeof(VDRController).GetMethod("NumberGenerationResult", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var controllerInstance = (VDRController)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(VDRController));
        var answer = method.Invoke(controllerInstance, [7, returnTo]);
        if (returnTo == "numbering")
        {
            Assert.Equal("/p/numbering-vdr", Assert.IsType<RedirectResult>(answer).Url);   // the admin numbering page is a definition page now
            return;
        }
        var result = Assert.IsType<RedirectToActionResult>(answer);
        Assert.Equal(action, result.ActionName);
        Assert.Equal(controller, result.ControllerName);
        if (controller != null) { Assert.Equal("Admin", result.RouteValues!["area"]); Assert.Equal(7, result.RouteValues["vdrItemId"]); }
        else Assert.Equal(7, result.RouteValues!["id"]);
    }

    [Fact]
    public void NumberActions_KeepTheirPermissionCheck_AndUseTheReturnHelperEverywhere()
    {
        var source = Read("PEMS.Web", "Controllers", "VDRController.cs");
        var start = source.IndexOf("public async Task<IActionResult> GenerateContractorNumber(", StringComparison.Ordinal);
        var block = source[start..source.IndexOf("private IActionResult NumberGenerationResult", StringComparison.Ordinal)];
        Assert.Equal(2, CountOf(block, "EnsurePermissionAsync(\"Edit\", vdrItemId"));
        Assert.Equal(4, CountOf(block, "return NumberGenerationResult(vdrItemId, returnTo);"));
        Assert.DoesNotContain("RedirectToAction(nameof(Details)", block);
    }

    // ---- SCRUM-530
    private static VDRSubmissionDto Submission(int rev, int? main, string? mainPath, int? comment = null, string? commentName = null, int? reply = null, string? replyName = null) => new()
    {
        RevisionNo = rev, AttachmentId = main, MainFilePath = mainPath,
        CommentSheetAttachmentId = comment, CommentSheetFileName = commentName, ReplySheetAttachmentId = reply, ReplySheetFileName = replyName
    };

    [Fact]
    public void Files_ListEverySubmissionFile_NewestFirst_AndMarkWhatCanBePreviewed()
    {
        var files = VdrFiles.From([
            Submission(0, 10, "store/a/drawing-r0.pdf"),
            Submission(1, 11, "store/a/drawing-r1.pdf", 12, "comments.xlsx", 13, "reply.pdf")
        ]);
        Assert.Equal([11, 12, 13, 10], files.Select(f => f.AttachmentId));
        Assert.Equal(["Main", "CommentSheet", "ReplySheet", "Main"], files.Select(f => f.Kind));
        Assert.Equal("drawing-r1.pdf", files[0].FileName);
        Assert.False(files[1].CanPreview);   // xlsx is downloaded, never shown inline (SCRUM-202)
        Assert.True(files[2].CanPreview);
    }

    [Fact]
    public void Files_SelectTheRequestedPreviewableFile_ElseTheNewest()
    {
        var files = VdrFiles.From([Submission(1, 11, "a.pdf", 12, "c.xlsx"), Submission(0, 10, "b.pdf")]);
        Assert.Equal(10, VdrFiles.Selected(files, 10)!.AttachmentId);
        Assert.Equal(11, VdrFiles.Selected(files, 12)!.AttachmentId);    // not previewable: the newest previewable instead
        Assert.Equal(11, VdrFiles.Selected(files, 999)!.AttachmentId);   // not a file of this item
        Assert.Null(VdrFiles.Selected(VdrFiles.From([Submission(0, 1, "x.dwg")]), null));
        Assert.Empty(VdrFiles.From([Submission(0, null, null)]));
    }

    [Fact]
    public void VdrDetails_HasTheFilesTab_WithThePreviewComponent()
    {
        var view = Read("PEMS.Web", "Views", "VDR", "Details.cshtml");
        Assert.Contains("<pems-tab target=\"tab-files\"", view);
        Assert.Contains("id=\"tab-files\" role=\"tabpanel\"", view);
        Assert.Contains("<pems-file-preview id=\"vdrFilePreview\" src=\"@vdrPreviewUrl\"", view);
        Assert.Contains("Url.Action(\"Preview\", \"Files\"", view);
        Assert.Contains("asp-controller=\"Files\" asp-action=\"Download\"", view);
        Assert.DoesNotContain("<iframe", view);
    }

    // ---- SCRUM-531: upload in the Files tab
    [Theory]
    [InlineData("Main", "documents")]
    [InlineData("CommentSheet", "comment-sheets")]
    [InlineData("ReplySheet", "reply-sheets")]
    [InlineData("Other", null)]
    [InlineData(null, null)]
    public void UploadKinds_HaveTheirFolder(string? kind, string? folder) => Assert.Equal(folder, VdrFiles.FolderOf(kind));

    [Fact]
    public void UploadAction_KeepsTheUploadRules_AndOnlyTouchesASubmissionOfTheItem()
    {
        var source = Read("PEMS.Web", "Controllers", "VDRController.cs");
        var start = source.IndexOf("public async Task<IActionResult> UploadSubmissionFile(", StringComparison.Ordinal);
        Assert.True(start > 0);
        var head = source[(start - 400)..start];
        Assert.Contains("UploadPolicy(PEMS.Application.Uploads.UploadProfile.Attachment)]", head);
        Assert.Contains("[ValidateAntiForgeryToken]", head);
        var body = source[start..source.IndexOf("private async Task<bool> EnsurePermissionAsync", StringComparison.Ordinal)];
        Assert.Contains("EnsurePermissionAsync(\"Edit\", vdrItemId", body);
        Assert.Contains("x.VDRSubmissionId == submissionId && x.VDRItemId == vdrItemId", body);
        Assert.Contains("VdrFiles.FolderOf(kind)", body);
        Assert.Contains("SaveVdrFileAsync(file, folder)", body);
    }

    [Fact]
    public void FilesTab_HasTheUploadForm()
    {
        var view = Read("PEMS.Web", "Views", "VDR", "Details.cshtml");
        var pane = view[view.IndexOf("id=\"tab-files\"", StringComparison.Ordinal)..];
        Assert.Contains("<pems-form asp-action=\"UploadSubmissionFile\" method=\"post\" enctype=\"multipart/form-data\"", pane);
        foreach (var field in new[] { "name=\"vdrItemId\"", "name=\"submissionId\"", "name=\"kind\"", "<pems-uploader id=\"vdrUploadFile\" name=\"file\" required" })
            Assert.Contains(field, pane);
        Assert.Contains("@foreach (var k in VdrFiles.Kinds)", pane);
    }

    private static int CountOf(string text, string part)
    {
        int n = 0, i = 0;
        while ((i = text.IndexOf(part, i, StringComparison.Ordinal)) >= 0) { n++; i += part.Length; }
        return n;
    }

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PEMS.Web", "PEMS.Web.csproj"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!.FullName, .. parts]));
    }
}
