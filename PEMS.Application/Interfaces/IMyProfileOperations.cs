namespace PEMS.Application.Interfaces;

/// <summary>The signed-in user's own profile (one row): the employee behind the account and the stored photo and signature (paths of this site, empty when there are none); <c>Message</c> says when no employee is linked to the account.</summary>
public sealed record MyProfileRow(string FullName, string Position, string PersonnelCode, string PhotoUrl, string SignatureUrl, string Message);

/// <summary>The answer of a profile write: the error is the refusal, or the message to show on success.</summary>
public sealed record MyProfileResult(bool Success, string? Error);

/// <summary>The My Profile page as operations (SCRUM-729): every user manages only the employee linked to their own account (found by the signed-in user name, never by an id from the input).</summary>
public interface IMyProfileOperations
{
    [PEMS.Application.Common.PemsOperation("MyProfile", "Profile")]
    Task<List<MyProfileRow>> ProfileAsync(string userName);

    [PEMS.Application.Common.PemsOperation("MyProfile", "SavePhoto")]
    Task<MyProfileResult> SavePhotoAsync(string userName, byte[] file);

    [PEMS.Application.Common.PemsOperation("MyProfile", "SaveSignature")]
    Task<MyProfileResult> SaveSignatureAsync(string userName, string signatureDataUrl);
}
