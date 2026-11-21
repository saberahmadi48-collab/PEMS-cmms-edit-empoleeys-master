using PEMS.UI.Abstractions;

namespace PEMS.UI.Components;

/// <summary>
/// How a component looks and behaves in the page. <see cref="Bootstrap"/> (the default) renders the Bootstrap markup the pages use today, so replacing a
/// hand-written block changes nothing visible; <see cref="Pems"/> renders the library's own <c>pems-*</c> classes and native dialog.
/// </summary>
public enum ComponentSkin { Bootstrap, Pems }

public enum AlertTone { Info, Success, Warning, Danger, Primary, Secondary, Light, None }

public enum OverlaySize { Small, Medium, Large, ExtraLarge }

public enum DrawerSide { End, Start }

/// <summary>
/// Inputs of <see cref="PemsAlert"/> (toast or inline notice). Danger and warning announce as an alert, the others as a status.
/// With a trace id and a copy label the alert shows the id and a copy button (the error contract of ADR-0009 carries the trace id).
/// </summary>
public sealed record AlertOptions
{
    public ComponentSkin Skin { get; init; } = ComponentSkin.Bootstrap;
    public TranslatableText? Title { get; init; }
    public TranslatableText? Message { get; init; }
    /// <summary>Extra Bootstrap utility classes added after the tone class in the Bootstrap skin, as today's pages write them (<c>py-2 px-3 small</c>).</summary>
    public string? CssClass { get; init; }
    public AlertTone Tone { get; init; } = AlertTone.Info;
    public string? TraceId { get; init; }
    public TranslatableText? TraceLabel { get; init; }
    public TranslatableText? CopyLabel { get; init; }
    public TranslatableText? DismissLabel { get; init; }
    /// <summary>Bootstrap skin only: the other attributes of the element (id, role, hidden, aria-*, data-*), written after the role so a page can set its own role.</summary>
    public IReadOnlyDictionary<string, object>? Attributes { get; init; }
}

/// <summary>Inputs of <see cref="PemsModal"/>: a native dialog opened by any element with <c>data-pems-open="#id"</c>. One modal serves many rows: the opener's <c>data-pems-row-key</c> is copied into the dialog.</summary>
public sealed record ModalOptions
{
    public ComponentSkin Skin { get; init; } = ComponentSkin.Bootstrap;
    public required string Id { get; init; }
    public required TranslatableText Title { get; init; }
    public required TranslatableText CloseLabel { get; init; }
    public TranslatableText? Body { get; init; }
    public OverlaySize Size { get; init; } = OverlaySize.Medium;
    /// <summary>
    /// Bootstrap skin only: write just the frame (<c>modal fade</c>, dialog, content) around the page's own markup (a form with header, body and footer) instead of the
    /// title / body / footer layout. For pages whose modal is a form; <see cref="Title"/> and <see cref="CloseLabel"/> are not used then.
    /// </summary>
    public bool Frame { get; init; }
    /// <summary>Frame only: extra classes on the dialog (<c>modal-dialog-scrollable</c>, <c>modal-dialog-centered</c>).</summary>
    public string? DialogCssClass { get; init; }
    /// <summary>Frame only: extra classes on the modal itself.</summary>
    public string? CssClass { get; init; }
    /// <summary>Frame only: the other attributes of the element (tabindex, aria-*, data-*), written after the class.</summary>
    public IReadOnlyDictionary<string, object>? Attributes { get; init; }
}

/// <summary>Inputs of <see cref="PemsDrawer"/>: a side panel dialog. Same opening rule as the modal.</summary>
public sealed record DrawerOptions
{
    public ComponentSkin Skin { get; init; } = ComponentSkin.Bootstrap;
    public required string Id { get; init; }
    public required TranslatableText Title { get; init; }
    public required TranslatableText CloseLabel { get; init; }
    public TranslatableText? Body { get; init; }
    public DrawerSide Side { get; init; } = DrawerSide.End;
    /// <summary>Bootstrap skin only: write just the <c>offcanvas</c> element around the page's own header and body markup.</summary>
    public bool Frame { get; init; }
    /// <summary>Frame only: extra classes on the panel.</summary>
    public string? CssClass { get; init; }
    /// <summary>Frame only: the other attributes of the element (tabindex, aria-*, data-*), written after the class.</summary>
    public IReadOnlyDictionary<string, object>? Attributes { get; init; }
}

/// <summary>
/// Inputs of <see cref="PemsConfirmDialog"/>: a question with confirm and cancel that posts a form. With a <see cref="ReasonLabel"/> the user
/// must give a reason (a required text posted as <see cref="ReasonName"/>), as the lifecycle actions of work orders, permits and letters need.
/// The opener's row key is posted as <c>key</c>.
/// </summary>
public sealed record ConfirmDialogOptions
{
    public ComponentSkin Skin { get; init; } = ComponentSkin.Bootstrap;
    public required string Id { get; init; }
    public required string Action { get; init; }
    public required TranslatableText Title { get; init; }
    public required TranslatableText Message { get; init; }
    public required TranslatableText ConfirmLabel { get; init; }
    public required TranslatableText CancelLabel { get; init; }
    public Tone Tone { get; init; } = Tone.Danger;
    public TranslatableText? ReasonLabel { get; init; }
    public string ReasonName { get; init; } = "reason";
    public string? RequestToken { get; init; }
    public string RequestTokenName { get; init; } = "__RequestVerificationToken";
}

public sealed record TabItem
{
    public required string Key { get; init; }
    public required TranslatableText Label { get; init; }
    public TranslatableText? Content { get; init; }
}

/// <summary>Inputs of <see cref="PemsTabs"/>: panels in the page, switched without a request; arrow keys move between tabs.</summary>
public sealed record TabsOptions
{
    public ComponentSkin Skin { get; init; } = ComponentSkin.Bootstrap;
    public required string Id { get; init; }
    public required TranslatableText Label { get; init; }
    public required IReadOnlyList<TabItem> Items { get; init; }
    public string? Active { get; init; }
}

public sealed record Crumb
{
    public required TranslatableText Label { get; init; }
    public string? Href { get; init; }
}

/// <summary>Inputs of <see cref="PemsBreadcrumb"/>: the last item is the current page and is not a link.</summary>
public sealed record BreadcrumbOptions
{
    public ComponentSkin Skin { get; init; } = ComponentSkin.Bootstrap;
    public required TranslatableText Label { get; init; }
    public required IReadOnlyList<Crumb> Items { get; init; }
}

public enum StepState { Done, Current, Upcoming }

public sealed record Step
{
    public required TranslatableText Label { get; init; }
    public StepState State { get; init; } = StepState.Upcoming;
}

/// <summary>Inputs of <see cref="PemsStepper"/>: the stages of a record (work order, permit). Replaces the stage diagram partials.</summary>
public sealed record StepperOptions
{
    public required TranslatableText Label { get; init; }
    public required IReadOnlyList<Step> Steps { get; init; }
}

public sealed record MenuItem
{
    public required TranslatableText Label { get; init; }
    public required string Href { get; init; }
}

/// <summary>Inputs of <see cref="PemsMenu"/>: a dropdown menu of links on a native disclosure; it closes on Escape and on an outside click.</summary>
public sealed record MenuOptions
{
    public required TranslatableText Label { get; init; }
    public required IReadOnlyList<MenuItem> Items { get; init; }
}

public sealed record NavItem
{
    public required TranslatableText Label { get; init; }
    public required string Href { get; init; }
    public int? Count { get; init; }
    public bool Active { get; init; }
}

/// <summary>Inputs of <see cref="PemsSidePanelNav"/>: a folder or shortcut rail with counts next to a list (mailbox layout).</summary>
public sealed record SidePanelNavOptions
{
    public required TranslatableText Label { get; init; }
    public required IReadOnlyList<NavItem> Items { get; init; }
}
