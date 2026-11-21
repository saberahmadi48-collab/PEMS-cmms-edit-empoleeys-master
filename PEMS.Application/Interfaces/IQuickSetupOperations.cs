namespace PEMS.Application.Interfaces;

/// <summary>One step of the quick setup page; the first row (Key "progress") is the overall progress. <c>Url</c> is empty when the user may not open the step.</summary>
public sealed record QuickSetupRow(string Key, string Title, string Description, string Status, string Missing, string ActionLabel, string Url);

/// <summary>The quick setup page as an operation (SCRUM-743): the real status of the setup steps (nothing estimated), with the links to the owning modules; admin-only steps are not linked for others.</summary>
public interface IQuickSetupOperations
{
    [PEMS.Application.Common.PemsOperation("QuickSetup", "Steps", "Setup.View")]
    Task<List<QuickSetupRow>> StepsAsync(List<string> userRoles);
}
