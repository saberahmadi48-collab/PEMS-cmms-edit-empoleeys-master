namespace PEMS.UI.Components;

// SCRUM-507: the child collection of a detail page (SCRUM-337 RelatedList) and the tree view as registered components. Texts and the
// request's antiforgery field arrive resolved from the tag helper, so a component never reads the view context.

/// <summary>The antiforgery field of the current request (name and token); null outside a request.</summary>
public sealed record AntiforgeryField(string Name, string Token);

/// <summary>Inputs of <see cref="PemsRelatedList"/>: a titled section with a count and an empty text; the page's table is the child content.</summary>
public sealed record RelatedListOptions
{
    public string? Id { get; init; }
    /// <summary>The heading's id (also the section's aria-labelledby), already resolved.</summary>
    public required string HeadingId { get; init; }
    public string? Icon { get; init; }
    public string Title { get; init; } = "";
    public int? Count { get; init; }
    public bool Empty { get; init; }
    public string? EmptyText { get; init; }
    public string? CssClass { get; init; }
    public IReadOnlyDictionary<string, object>? Attributes { get; init; }
}

/// <summary>Inputs of <see cref="PemsRelatedTable"/>: the child table (thead and tbody are the child content), scrolling sideways on narrow screens.</summary>
public sealed record RelatedTableOptions
{
    public string? Id { get; init; }
    /// <summary>The page's own data-* hooks, kept on the table.</summary>
    public IReadOnlyDictionary<string, string>? DataAttributes { get; init; }
}

/// <summary>Inputs of <see cref="PemsRelatedAdd"/>: the add form under the table (parent key as a hidden field, the page's fields, a submit button).</summary>
public sealed record RelatedAddOptions
{
    public string Action { get; init; } = "";
    public string? ParentName { get; init; }
    public string? ParentValue { get; init; }
    public string SubmitLabel { get; init; } = "";
    public AntiforgeryField? Antiforgery { get; init; }
}

/// <summary>Inputs of <see cref="PemsRelatedRemove"/>: the delete form of one row; <see cref="Confirm"/> asks first.</summary>
public sealed record RelatedRemoveOptions
{
    public string Action { get; init; } = "";
    public string KeyName { get; init; } = "";
    public string? KeyValue { get; init; }
    public string? ParentName { get; init; }
    public string? ParentValue { get; init; }
    public string Label { get; init; } = "";
    public string? Confirm { get; init; }
    public AntiforgeryField? Antiforgery { get; init; }
}

/// <summary>Inputs of <see cref="PemsRelatedRowForm"/>: the hidden form of a row edited in place; its inputs point at it with <c>form="id"</c>.</summary>
public sealed record RelatedRowFormOptions
{
    public string Id { get; init; } = "";
    public string Action { get; init; } = "";
    public AntiforgeryField? Antiforgery { get; init; }
}

/// <summary>Inputs of <see cref="PemsRelatedSave"/>: the save button of a row edited in place; submits the form named <see cref="FormId"/>.</summary>
public sealed record RelatedSaveOptions
{
    public string FormId { get; init; } = "";
    public string Label { get; init; } = "";
}

/// <summary>Inputs of <see cref="PemsRelatedAct"/>: an icon-only row action (submit button) with its label as tool tip and for screen readers.</summary>
public sealed record RelatedActOptions
{
    public required string Icon { get; init; }
    public required string Label { get; init; }
    /// <summary>The colour class (text-danger, text-primary).</summary>
    public string Tone { get; init; } = "";
    /// <summary>The id of the form the button submits when it sits outside it.</summary>
    public string? Form { get; init; }
}

/// <summary>Inputs of <see cref="PemsTree"/>: the tree as a WAI-ARIA tree; <see cref="PemsTreeItem"/>s are the child content.</summary>
public sealed record TreeOptions
{
    /// <summary>The accessible name of the tree.</summary>
    public string Label { get; init; } = "";
    /// <summary>The browser storage key of the collapsed branches; none: not remembered.</summary>
    public string? RememberKey { get; init; }
    public string? CssClass { get; init; }
    public IReadOnlyDictionary<string, object>? Attributes { get; init; }
}

/// <summary>Inputs of <see cref="PemsTreeItem"/>: one node; nested items are the child content, a label fragment replaces the label text.</summary>
public sealed record TreeItemOptions
{
    public string Label { get; init; } = "";
    public string? Key { get; init; }
    public string? Href { get; init; }
    public bool Current { get; init; }
    /// <summary>The count after the label, formatted for the current culture by the tag helper.</summary>
    public string? CountText { get; init; }
    public bool Expanded { get; init; } = true;
    public bool HasChildren { get; init; }
    public string? LinkClass { get; init; }
    /// <summary>The expand / collapse button's name, already combined with the label.</summary>
    public string ToggleName { get; init; } = "";
    public string? CssClass { get; init; }
    public IReadOnlyDictionary<string, object>? Attributes { get; init; }
}
