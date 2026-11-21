using PEMS.Application.Definitions;
using PEMS.UI.Manifest;
using PEMS.UI.Rendering;
using PEMS.Web.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>The definition kinds as the product registers them, with the generic entity providers and handlers (no scope: only their keys and parameter checks are used), so the shipped package validates the way it does at start-up.</summary>
internal static class ShippedKinds
{
    public static IDefinitionKind[] Build(IPemsComponentRegistry registry, ComponentManifestCatalog catalog, EntityCatalog? entities = null)
    {
        var cat = entities ?? EntityCatalog.Default();
        var reader = new OperationReader(null!, OperationCatalog.Scan(typeof(PEMS.Application.Common.PemsOperationAttribute).Assembly));
        return
        [
            new FormDefinitionKind(), new PageDefinitionKind(registry, catalog), new LayoutDefinitionKind(), new ViewDefinitionKind(), new ThemeDefinitionKind(), new RouteDefinitionKind(), new ComponentDefinitionKind(registry),
            new DataSourceDefinitionKind([new EntityOptionsProvider(null!, cat), new OperationOptionsProvider(reader)], [new EntityGridProvider(null!, cat), new ComponentCatalogGridProvider(null!), new OperationGridProvider(reader), new DefinitionsGridProvider(null!), new DefinitionKindsGridProvider(null!, [], new DefinitionOwnership(null)), new DefinitionUsagesGridProvider(null!), new DefinitionLargestGridProvider(null!), new DefinitionGrowthGridProvider(null!), new DefinitionHealthGridProvider(null!), new OwnershipPreflightGridProvider(null!), new DefinitionVersionsGridProvider(null!), new DefinitionDiffGridProvider(null!), new GridViewsProvider(null!), new MigratorHistoryGridProvider(null!), new OperationArgumentsGridProvider(OperationCatalog.Scan(typeof(PEMS.Application.Common.PemsOperationAttribute).Assembly)), new MenuTreeGridProvider(null!), new DefinitionSodGridProvider(null!), new DefinitionTranslationsGridProvider(null!), new DefinitionPayloadGridProvider(null!), new OperationCatalogGridProvider(OperationCatalog.Scan(typeof(PEMS.Application.Common.PemsOperationAttribute).Assembly))]),
            new ActionDefinitionKind([new EntityCreateAction(null!, cat), new EntityUpdateAction(null!, cat), new EntityDeleteAction(null!, cat), new EntityToggleAction(null!, cat), new WorkflowExecuteAction(null!), new StepsAction(null!, null!), new FileUploadAction(null!, null!), new FileDownloadAction(null!), new FileExportAction(null!), new DefinitionExportAction(null!), new DefinitionImportAction(null!), new GridViewSaveAction(null!), new GridViewDeleteAction(null!), new EntityImportAction(null!, cat, null!), new OperationInvokeAction(null!, OperationCatalog.Scan(typeof(PEMS.Application.Common.PemsOperationAttribute).Assembly))])
        ];
    }
}
