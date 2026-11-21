using PEMS.Application.DTOs;

namespace PEMS.Web.Navigation;

/// <summary>One action of the command palette: a page that starts a task, shown only when the user passes its rule.</summary>
/// <param name="Key">Stable id; the title text key is <c>Palette_Action_{Key}</c>.</param>
/// <param name="Controller">MVC controller of the target page.</param>
/// <param name="Action">MVC action of the target page.</param>
/// <param name="Icon">Bootstrap icon class.</param>
/// <param name="Access">The same check the target page (or the save it leads to) makes.</param>
public sealed record CommandPaletteAction(string Key, string Controller, string Action, string Icon, SearchAccessRule Access, string? Url = null);

/// <summary>
/// SCRUM-64: the actions the command palette offers. Each one carries the access rule of its page, checked through
/// <c>IGlobalSearchService.IsAllowedAsync</c> (the same contract as the record search), so the palette never offers a
/// page that would answer "access denied". Menus are not listed here: the palette reads them from the sidebar, which
/// the server has already filtered. Theme and language switches are added by the controller (they need the return URL).
/// </summary>
public sealed class CommandPaletteActions
{
    public IReadOnlyList<CommandPaletteAction> All { get; } = new[]
    {
        new CommandPaletteAction("NewLetter", "Letter", "Create", "bi-envelope-plus", SearchAccessRule.AnyUser()),
        new CommandPaletteAction("NewDocument", "EngineeringDocuments", "Create", "bi-file-earmark-plus", SearchAccessRule.Permission("EngineeringDocuments", "Create")),
        new CommandPaletteAction("NewVdr", "VDR", "Create", "bi-journal-plus", SearchAccessRule.Permission("VDR", "Create")),
        new CommandPaletteAction("NewWorkRequest", "WorkRequest", "Create", "bi-wrench", SearchAccessRule.Permission("WorkRequest", "Create")),
        new CommandPaletteAction("NewWorkOrder", "WorkOrder", "Create", "bi-tools", SearchAccessRule.Permission("WorkOrder", "Create")),
        new CommandPaletteAction("NewEquipment", "Equipment", "Create", "bi-gear", SearchAccessRule.Permission("Equipment", "Create")),
        new CommandPaletteAction("NewMaterialRequisition", "MaterialRequisition", "Create", "bi-cart-plus", SearchAccessRule.Permission("MaterialRequisition", "Create")),
        new CommandPaletteAction("NewContract", "Contract", "Create", "bi-file-earmark-text", SearchAccessRule.Permission("Contracts", "Create")),
        new CommandPaletteAction("MyWork", "MyWork", "Index", "bi-inbox", SearchAccessRule.AnyUser(), Url: "/p/my-work"),   // SCRUM-730: a definition page
        new CommandPaletteAction("Notifications", "Notification", "Index", "bi-bell", SearchAccessRule.AnyUser(), Url: "/p/notifications"),   // SCRUM-731
        new CommandPaletteAction("Help", "Help", "Index", "bi-life-preserver", SearchAccessRule.AnyUser(), Url: "/p/help"),   // SCRUM-704
        new CommandPaletteAction("ChangePassword", "Account", "ChangePassword", "bi-key", SearchAccessRule.AnyUser()),
    };
}
