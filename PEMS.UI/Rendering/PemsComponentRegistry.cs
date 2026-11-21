using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using PEMS.UI.Components;

namespace PEMS.UI.Rendering;

/// <summary>Maps a component key ("field", "table", ...) to its component type. The only place that knows which type renders which key.</summary>
public interface IPemsComponentRegistry
{
    void Register(string key, Type componentType);
    int RegisterAssembly(Assembly assembly);
    Type Resolve(string key);
    /// <summary>Every registered key (for the component manifests and the catalog).</summary>
    IReadOnlyCollection<string> Keys { get; }
}

public sealed class PemsComponentRegistry : IPemsComponentRegistry
{
    private readonly ConcurrentDictionary<string, Type> _types = new(StringComparer.OrdinalIgnoreCase);

    public PemsComponentRegistry()
    {
        Register(UiKeys.Button, typeof(PemsButton));
        Register(UiKeys.Badge, typeof(PemsBadge));
        Register(UiKeys.Skeleton, typeof(PemsSkeleton));
        Register(UiKeys.FilePreview, typeof(PemsFilePreview));
        Register(UiKeys.EmptyState, typeof(PemsEmptyState));
        Register(UiKeys.ErrorState, typeof(PemsErrorState));
        Register(UiKeys.LoadingState, typeof(PemsLoadingState));
        Register(UiKeys.Field, typeof(PemsField));
        Register(UiKeys.Table, typeof(PemsTable));
        Register(UiKeys.Form, typeof(PemsForm));
        Register(UiKeys.FormSection, typeof(PemsFormSection));
        Register(UiKeys.ActionBar, typeof(PemsActionBar));
        Register(UiKeys.Alert, typeof(PemsAlert));
        Register(UiKeys.Modal, typeof(PemsModal));
        Register(UiKeys.Drawer, typeof(PemsDrawer));
        Register(UiKeys.ConfirmDialog, typeof(PemsConfirmDialog));
        Register(UiKeys.Tabs, typeof(PemsTabs));
        Register(UiKeys.Breadcrumb, typeof(PemsBreadcrumb));
        Register(UiKeys.Stepper, typeof(PemsStepper));
        Register(UiKeys.Menu, typeof(PemsMenu));
        Register(UiKeys.SidePanelNav, typeof(PemsSidePanelNav));
        Register(UiKeys.Card, typeof(PemsCard));
        Register(UiKeys.Progress, typeof(PemsProgress));
        Register(UiKeys.Accordion, typeof(PemsAccordion));
        Register(UiKeys.AccordionItem, typeof(PemsAccordionItem));
        Register(UiKeys.ListGroup, typeof(PemsListGroup));
        Register(UiKeys.Spinner, typeof(PemsSpinner));
        Register(UiKeys.InfoGrid, typeof(PemsInfoGrid));
        Register(UiKeys.InfoItem, typeof(PemsInfoItem));
        Register(UiKeys.KpiTile, typeof(PemsKpiTile));
        Register(UiKeys.ListPage, typeof(PemsListPage));
        Register(UiKeys.ListPanel, typeof(PemsListPanel));
        Register(UiKeys.ListToolbar, typeof(PemsListToolbar));
        Register(UiKeys.ListIconTool, typeof(PemsListIconTool));
        Register(UiKeys.ListSearch, typeof(PemsListSearch));
        Register(UiKeys.ListTools, typeof(PemsListTools));
        Register(UiKeys.ListFilters, typeof(PemsListFilters));
        Register(UiKeys.ListFilterSelect, typeof(PemsListFilterSelect));
        Register(UiKeys.ListPopover, typeof(PemsListPopover));
        Register(UiKeys.ListKpiToggle, typeof(PemsListKpiToggle));
        Register(UiKeys.ListExport, typeof(PemsListExport));
        Register(UiKeys.ListResetColumns, typeof(PemsListResetColumns));
        Register(UiKeys.ListNew, typeof(PemsListNew));
        Register(UiKeys.ListKpis, typeof(PemsListKpis));
        Register(UiKeys.ListChips, typeof(PemsListChips));
        Register(UiKeys.ListChip, typeof(PemsListChip));
        Register(UiKeys.ListGrid, typeof(PemsListGrid));
        Register(UiKeys.ListTable, typeof(PemsListTable));
        Register(UiKeys.ListFooter, typeof(PemsListFooter));
        Register(UiKeys.ListBulk, typeof(PemsListBulk));
        Register(UiKeys.ListMenu, typeof(PemsListMenu));
        Register(UiKeys.RelatedList, typeof(PemsRelatedList));
        Register(UiKeys.RelatedTable, typeof(PemsRelatedTable));
        Register(UiKeys.RelatedAdd, typeof(PemsRelatedAdd));
        Register(UiKeys.RelatedRemove, typeof(PemsRelatedRemove));
        Register(UiKeys.RelatedRowForm, typeof(PemsRelatedRowForm));
        Register(UiKeys.RelatedSave, typeof(PemsRelatedSave));
        Register(UiKeys.RelatedAct, typeof(PemsRelatedAct));
        Register(UiKeys.Tree, typeof(PemsTree));
        Register(UiKeys.TreeItem, typeof(PemsTreeItem));
        Register(UiKeys.Chart, typeof(PemsChart));
        Register(UiKeys.Date, typeof(PemsDate));
        Register(UiKeys.ButtonFrame, typeof(PemsButtonFrame));
        Register(UiKeys.DropdownMenu, typeof(PemsDropdownMenu));
        Register(UiKeys.Nav, typeof(PemsNav));
        Register(UiKeys.CrumbTrail, typeof(PemsCrumbTrail));
        Register(UiKeys.TableFrame, typeof(PemsTableFrame));
        Register(UiKeys.ListGroupItem, typeof(PemsListGroupItem));
        Register(UiKeys.Toast, typeof(PemsToast));
        Register(UiKeys.ScriptTemplates, typeof(PemsScriptTemplates));
        Register(UiKeys.Input, typeof(PemsInput));
        Register(UiKeys.Select, typeof(PemsSelect));
        Register(UiKeys.Textarea, typeof(PemsTextarea));
        Register(UiKeys.FormLabel, typeof(PemsFormLabel));
        Register(UiKeys.ValidationSummary, typeof(PemsValidationSummary));
        Register(UiKeys.Icon, typeof(PemsIcon));
        Register(UiKeys.Row, typeof(PemsRow));
        Register(UiKeys.Col, typeof(PemsCol));
        Register(UiKeys.CheckRow, typeof(PemsCheckRow));
        Register(UiKeys.FormGroup, typeof(PemsFormGroup));
        Register(UiKeys.TreeSelect, typeof(PemsTreeSelect));
        Register(UiKeys.DataGrid, typeof(PemsDataGrid));
        Register(UiKeys.DataView, typeof(PemsDataView));
        Register(UiKeys.RecordView, typeof(PemsRecordView));
        Register(UiKeys.Matrix, typeof(PemsMatrix));
        Register(UiKeys.ContentBlocks, typeof(PemsContentBlocks));
        Register(UiKeys.PageFilters, typeof(PemsPageFilters));
        Register(UiKeys.WorkflowHistory, typeof(PemsWorkflowHistory));
        Register(UiKeys.Uploader, typeof(PemsUploader));
        Register(UiKeys.CommentsThread, typeof(PemsCommentsThread));
        Register(UiKeys.AuditTrail, typeof(PemsAuditTrail));
        Register(UiKeys.ActionButton, typeof(PemsActionButton));
        Register(UiKeys.SignaturePad, typeof(PemsSignaturePad));
        Register(UiKeys.Image, typeof(PemsImage));
        Register(UiKeys.ScriptIsland, typeof(PemsScriptIsland));
        Register(UiKeys.WorkflowActionBar, typeof(PemsWorkflowActionBar));
        Register(UiKeys.Pager, typeof(PemsPager));
        Register(UiKeys.ContentCard, typeof(PemsContentCard));
        Register(UiKeys.PageShell, typeof(PemsPageShell));
        Register(UiKeys.Flex, typeof(PemsFlex));
        Register(UiKeys.HelpText, typeof(PemsHelpText));
        Register(UiKeys.TableScroll, typeof(PemsTableScroll));
        Register(UiKeys.TabContent, typeof(PemsTabContent));
        Register(UiKeys.TabPane, typeof(PemsTabPane));
        Register(UiKeys.Tab, typeof(PemsTab));
        Register(UiKeys.NavLink, typeof(PemsNavLink));
        Register(UiKeys.Heading, typeof(PemsHeading));
        Register(UiKeys.Text, typeof(PemsText));
        Register(UiKeys.Link, typeof(PemsLink));
        Register(UiKeys.BulletList, typeof(PemsBulletList));
        Register(UiKeys.Bullet, typeof(PemsBullet));
        Register(UiKeys.Divider, typeof(PemsDivider));
        Register(UiKeys.StageDiagram, typeof(PemsStageDiagram));
        Register(UiKeys.SideLink, typeof(PemsSideLink));
        Register(UiKeys.ModalHeader, typeof(PemsModalHeader));
        Register(UiKeys.ModalBody, typeof(PemsModalBody));
        Register(UiKeys.ModalFooter, typeof(PemsModalFooter));
        Register(UiKeys.Stat, typeof(PemsStat));
        Register(UiKeys.StatGrid, typeof(PemsStatGrid));
        Register(UiKeys.Metric, typeof(PemsMetric));
        Register(UiKeys.PageHero, typeof(PemsPageHero));
        Register(UiKeys.DataTable, typeof(PemsDataTable));
        Register(UiKeys.FormFrame, typeof(PemsFormFrame));
    }

    public void Register(string key, Type componentType)
    {
        if (!typeof(IComponent).IsAssignableFrom(componentType))
            throw new ArgumentException($"{componentType.FullName} is not a component.", nameof(componentType));
        if (!_types.TryAdd(key, componentType) && _types[key] != componentType)
            throw new InvalidOperationException($"Component key '{key}' is already registered for {_types[key].FullName}.");
    }

    /// <summary>Registers every component of <paramref name="assembly"/> that carries <see cref="PemsComponentAttribute"/>.</summary>
    public int RegisterAssembly(Assembly assembly)
    {
        var count = 0;
        foreach (var type in assembly.GetTypes())
        {
            var attr = type.GetCustomAttribute<PemsComponentAttribute>();
            if (attr is null) continue;
            Register(attr.Key, type);
            count++;
        }
        return count;
    }

    public IReadOnlyCollection<string> Keys => _types.Keys.ToList();

    public Type Resolve(string key) => _types.TryGetValue(key, out var t)
        ? t
        : throw new KeyNotFoundException($"No component is registered for key '{key}'.");
}
