using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Persistence.Context;
using PEMS.Web.Resources;

namespace PEMS.Web.Services;

/// <summary>SCRUM-680: the dashboard layout page for the signed-in user (by user name), over IUserDashboardService; one widget is changed at a time, the rest of the layout is kept.</summary>
public sealed class DashboardSettingsOperations(IUserDashboardService service, PEMSDbContext context, IStringLocalizer<SharedResource> loc) : IDashboardSettingsOperations
{
    private static readonly string[] Sizes = ["Small", "Medium", "Large"];

    public async Task<List<DashboardWidgetRow>> LayoutAsync(string userName)
    {
        if (await UserIds.FindAsync(context, userName) is not { } id) return [];
        return (await service.GetLayoutAsync(id)).OrderBy(w => w.DisplayOrder)
            .Select(w => new DashboardWidgetRow(w.WidgetKey, w.DisplayName, w.DisplayOrder, w.Size, w.IsVisible ? loc["Rpt_Yes"].Value : loc["Rpt_No"].Value)).ToList();
    }

    public Task<List<ReportChoiceRow>> WidgetChoicesAsync() => Task.FromResult(service.GetCatalog().Select(w => new ReportChoiceRow(w.WidgetKey, w.DisplayName)).ToList());

    public async Task<OperationResult> SaveWidgetAsync(string userName, string key, string? visible = null, string? displayOrder = null, string? size = null)
    {
        if (await UserIds.FindAsync(context, userName) is not { } id) return new(false, "Dashboard_Err_NoUser");
        var layout = await service.GetLayoutAsync(id);
        var widget = layout.FirstOrDefault(w => w.WidgetKey == key);
        if (widget is null) return new(false, "Dashboard_Err_UnknownWidget");
        if (!string.IsNullOrWhiteSpace(visible)) widget.IsVisible = visible.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(displayOrder))
        {
            if (!int.TryParse(displayOrder.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var order)) return new(false, "Kpi_Err_NotANumber");
            widget.DisplayOrder = order;
        }
        if (!string.IsNullOrWhiteSpace(size)) widget.Size = Sizes.Contains(size) ? size : widget.Size;
        await service.SaveLayoutAsync(id, layout);
        return new(true, null);
    }

    public async Task<OperationResult> ResetAsync(string userName)
    {
        if (await UserIds.FindAsync(context, userName) is not { } id) return new(false, "Dashboard_Err_NoUser");
        await service.ResetToDefaultAsync(id);
        return new(true, null);
    }
}

/// <summary>SCRUM-672: the chat assistant for the signed-in user (by user name), over IChatAssistantService.</summary>
public sealed class ChatAssistantOperations(IChatAssistantService service, PEMSDbContext context) : IChatAssistantOperations
{
    public async Task<List<ChatRow>> HistoryAsync(string userName)
        => (await service.GetHistoryAsync(await UserIds.FindAsync(context, userName) ?? 0)).Select(m => new ChatRow(m.Role, m.Content, m.CreatedDate.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))).ToList();

    public async Task<OperationResult> AskAsync(string userName, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return new(false, "Chat_Err_Empty");
        await service.AskAsync(await UserIds.FindAsync(context, userName) ?? 0, message);
        return new(true, null);
    }

    public async Task<OperationResult> ClearAsync(string userName)
    {
        await service.ClearHistoryAsync(await UserIds.FindAsync(context, userName) ?? 0);
        return new(true, null);
    }
}

internal static class UserIds
{
    public static async Task<int?> FindAsync(PEMSDbContext context, string userName)
        => string.IsNullOrEmpty(userName) ? null : await context.Users.AsNoTracking().Where(u => u.UserName == userName).Select(u => (int?)u.Id).FirstOrDefaultAsync();
}
