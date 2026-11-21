namespace PEMS.Web.Configuration;

/// <summary>The upload limit the uploader component shows (SCRUM-580): the same setting the upload guard enforces.</summary>
public sealed record UploadLimits(int MaxFileSizeMb) : PEMS.UI.Components.IUploadLimits;
