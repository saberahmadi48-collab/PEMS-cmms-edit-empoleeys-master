namespace PEMS.Application.Interfaces;

/// <summary>
/// The designers as operations (SCRUM-439 page, 604 form, 606 action, 607 data source, 608 menu): one small set of edits of the open draft of any definition, addressed by <c>reference</c> (<c>appKey:kind:key</c>) and by
/// JSON Pointer, plus typed helpers that build the right item for a form field, a page instance, a menu item and an action. The draft is opened when there is none; every edit goes through the definition service
/// (If-Match, kind validation at validate and publish), so a designer is a stored page and form over these operations, never a compiled screen. The result is <c>(Success, Error)</c> with the first problem.
/// </summary>
public interface IDefinitionDesigner
{
    [PEMS.Application.Common.PemsOperation("Designer", "SetValue", "Definitions.Edit")]
    Task<(bool Success, string? Error)> SetValueAsync(string reference, string pointer, string valueJson, string actor);

    [PEMS.Application.Common.PemsOperation("Designer", "AddItem", "Definitions.Edit")]
    Task<(bool Success, string? Error)> AddItemAsync(string reference, string arrayPointer, string itemJson, string actor);

    [PEMS.Application.Common.PemsOperation("Designer", "RemoveItem", "Definitions.Edit")]
    Task<(bool Success, string? Error)> RemoveItemAsync(string reference, string pointer, string actor);

    [PEMS.Application.Common.PemsOperation("Designer", "MoveItem", "Definitions.Edit")]
    Task<(bool Success, string? Error)> MoveItemAsync(string reference, string arrayPointer, int from, int to, string actor);

    /// <summary>Removes the row a designer grid shows: <paramref name="rowId"/> is the <c>rowId</c> of the row (reference|pointer|index).</summary>
    [PEMS.Application.Common.PemsOperation("Designer", "RemoveRow", "Definitions.Edit")]
    Task<(bool Success, string? Error)> RemoveRowAsync(string rowId, string actor);

    /// <summary>Moves the row a designer grid shows one place <c>up</c> or <c>down</c>.</summary>
    [PEMS.Application.Common.PemsOperation("Designer", "MoveRow", "Definitions.Edit")]
    Task<(bool Success, string? Error)> MoveRowAsync(string rowId, string direction, string actor);

    /// <summary>A field of a form (SCRUM-604): <c>{ name, kind, label: { en, fa }, required }</c> at the end of <c>/fields</c>.</summary>
    [PEMS.Application.Common.PemsOperation("Designer", "AddFormField", "Definitions.Edit")]
    Task<(bool Success, string? Error)> AddFormFieldAsync(string reference, string name, string kind, string titleEn, string titleFa, string required, string actor);

    /// <summary>A component instance in a region of a page (SCRUM-439): <c>{ id, component, options }</c> at the end of <c>/regions/{region}</c>.</summary>
    [PEMS.Application.Common.PemsOperation("Designer", "AddPageInstance", "Definitions.Edit")]
    Task<(bool Success, string? Error)> AddPageInstanceAsync(string reference, string region, string id, string component, string? optionsJson, string actor);

    /// <summary>An item of a menu (SCRUM-608): <c>{ key, parent, kind: link, order, title: { en, fa }, page }</c> at the end of <c>/items</c>.</summary>
    [PEMS.Application.Common.PemsOperation("Designer", "AddMenuItem", "Definitions.Edit")]
    Task<(bool Success, string? Error)> AddMenuItemAsync(string reference, string itemKey, string? parent, string titleEn, string titleFa, string page, int order, string actor);

    /// <summary>A new action definition that runs an operation (SCRUM-606): handler operation.invoke with model, action and the arguments; created as a definition, published through the workbench.</summary>
    [PEMS.Application.Common.PemsOperation("Designer", "CreateAction", "Definitions.Create")]
    Task<(bool Success, string? Error)> CreateActionAsync(string key, string? appKey, string model, string action, string? argsJson, string successEn, string successFa, string? redirect, string actor);

    // ---- SCRUM-810: the form designer edits one field in place and the layout and rules of the form, with no JSON

    /// <summary>Edits the field <paramref name="name"/> of a form draft: every argument that is not empty replaces that property of the field, <c>-</c> removes it. The kind, conditions (visible, requiredWhen, readOnlyWhen), length limits, pattern, span and the data source of the options are the arguments.</summary>
    [PEMS.Application.Common.PemsOperation("Designer", "EditFormField", "Definitions.Edit")]
    Task<(bool Success, string? Error)> EditFormFieldAsync(string reference, string name, string? titleEn, string? titleFa, string? kind, string? required, string? visible, string? requiredWhen, string? readOnlyWhen,
        string? minLength, string? maxLength, string? pattern, string? span, string? optionsFrom, string? helpEn, string? helpFa, string actor);

    /// <summary>The layout of a form draft: columns (1 or 2), warn on leave, and the label of the submit button.</summary>
    [PEMS.Application.Common.PemsOperation("Designer", "SetFormLayout", "Definitions.Edit")]
    Task<(bool Success, string? Error)> SetFormLayoutAsync(string reference, string? columns, string? warnOnLeave, string? submitEn, string? submitFa, string actor);

    /// <summary>A rule across the fields of a form draft: the expression must be true, otherwise the message is shown on the field (or on the form).</summary>
    [PEMS.Application.Common.PemsOperation("Designer", "AddFormRule", "Definitions.Edit")]
    Task<(bool Success, string? Error)> AddFormRuleAsync(string reference, string expression, string? field, string messageEn, string messageFa, string actor);

    // ---- SCRUM-811: the action designer sets the access decision of an operation action

    /// <summary>The access-control decision of an operation action (setting <c>guard</c>): resource, action, the source of the record id and the mode (sod or strict).</summary>
    [PEMS.Application.Common.PemsOperation("Designer", "SetActionGuard", "Definitions.Edit")]
    Task<(bool Success, string? Error)> SetActionGuardAsync(string reference, string resource, string action, string? resourceId, string? mode, string actor);

    /// <summary>The section guard of an operation action (setting <c>sectionGuard</c>): section, action and the sources of the work order, the reason and the record reference.</summary>
    [PEMS.Application.Common.PemsOperation("Designer", "SetSectionGuard", "Definitions.Edit")]
    Task<(bool Success, string? Error)> SetSectionGuardAsync(string reference, string section, string action, string? workOrderId, string? reason, string? recordRef, string actor);

    // ---- SCRUM-812: the data-source designer edits the settings of an entity list

    /// <summary>The settings of an entity grid data source draft (provider entity): the entity, the fields shown, the fields the search reads, the default sort, the fixed row scope (a JSON object property to value) and the page size.</summary>
    [PEMS.Application.Common.PemsOperation("Designer", "SetEntityGrid", "Definitions.Edit")]
    Task<(bool Success, string? Error)> SetEntityGridAsync(string reference, string entity, string? fields, string? searchFields, string? sort, string? whereJson, string? pageSize, string actor);

    // ---- SCRUM-813: the menu designer moves an item under another parent

    /// <summary>Moves the item <paramref name="itemKey"/> of a menu draft under <paramref name="parent"/> (empty = the top level) at the order <paramref name="order"/>.</summary>
    [PEMS.Application.Common.PemsOperation("Designer", "MoveMenuItem", "Definitions.Edit")]
    Task<(bool Success, string? Error)> MoveMenuItemAsync(string reference, string itemKey, string? parent, int order, string actor);

    // ---- SCRUM-809: the page designer edits a component instance by its id, wherever it is

    /// <summary>Sets one option of the component instance <paramref name="instanceId"/> of a page draft (any region): the value is JSON (<c>"text"</c>, <c>12</c>, <c>true</c>, an object); an empty value removes the option. The options are checked against the manifest of the component when the draft is validated.</summary>
    [PEMS.Application.Common.PemsOperation("Designer", "SetInstanceOption", "Definitions.Edit")]
    Task<(bool Success, string? Error)> SetInstanceOptionAsync(string reference, string instanceId, string option, string? valueJson, string actor);

    /// <summary>Moves the component instance <paramref name="instanceId"/> of a page draft into <paramref name="region"/> at <paramref name="position"/> (0 = first; beyond the end = last): the same move a drag and drop on a canvas makes.</summary>
    [PEMS.Application.Common.PemsOperation("Designer", "MoveInstance", "Definitions.Edit")]
    Task<(bool Success, string? Error)> MoveInstanceAsync(string reference, string instanceId, string region, int position, string actor);
}
