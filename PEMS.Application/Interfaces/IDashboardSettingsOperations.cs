namespace PEMS.Application.Interfaces;

/// <summary>The answer of an action of these pages: the error is a resource key.</summary>
public sealed record OperationResult(bool Success, string? ErrorKey);

/// <summary>One widget of the personal dashboard layout.</summary>
public sealed record DashboardWidgetRow(string WidgetKey, string Name, int DisplayOrder, string Size, string Visible);

/// <summary>The "Customize dashboard" page as operations (SCRUM-680): the layout of the signed-in user (found by user name), the change of one widget and the reset; the same service as before.</summary>
public interface IDashboardSettingsOperations
{
    [PEMS.Application.Common.PemsOperation("DashboardSettings", "Layout")]
    Task<List<DashboardWidgetRow>> LayoutAsync(string userName);

    [PEMS.Application.Common.PemsOperation("DashboardSettings", "WidgetChoices")]
    Task<List<ReportChoiceRow>> WidgetChoicesAsync();

    [PEMS.Application.Common.PemsOperation("DashboardSettings", "SaveWidget")]
    Task<OperationResult> SaveWidgetAsync(string userName, string key, string? visible = null, string? displayOrder = null, string? size = null);

    [PEMS.Application.Common.PemsOperation("DashboardSettings", "Reset")]
    Task<OperationResult> ResetAsync(string userName);
}

/// <summary>One line of the chat with the engineering assistant.</summary>
public sealed record ChatRow(string Role, string Content, string CreatedDate);

/// <summary>The engineering chat assistant page as operations (SCRUM-672): the history of the signed-in user, a question and clearing the history.</summary>
public interface IChatAssistantOperations
{
    [PEMS.Application.Common.PemsOperation("ChatAssistant", "History")]
    Task<List<ChatRow>> HistoryAsync(string userName);

    [PEMS.Application.Common.PemsOperation("ChatAssistant", "Ask")]
    Task<OperationResult> AskAsync(string userName, string message);

    [PEMS.Application.Common.PemsOperation("ChatAssistant", "Clear")]
    Task<OperationResult> ClearAsync(string userName);
}
