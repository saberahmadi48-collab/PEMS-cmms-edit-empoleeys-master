namespace PEMS.UI.Components;

/// <summary>
/// Inputs of <see cref="PemsTreeSelect"/> (SCRUM-513): a field that picks one node of a large tree (organization unit, functional location, PBS, equipment) without loading the tree.
/// The value is posted as a hidden input; the nodes come from <see cref="Url"/> (<c>?parentId=&amp;q=&amp;page=</c> answers <c>{ items: [{ value, text, sub?, hasChildren }], hasMore }</c>) one level at a time,
/// and a search box asks the same endpoint over the whole tree. Every text is the page's own (ADR-0008).
/// </summary>
public sealed record TreeSelectOptions : FrameOptions
{
    public required string Name { get; init; }
    public string? Id { get; init; }
    /// <summary>The selected node's id (the value that is posted); null or empty for none.</summary>
    public string? Value { get; init; }
    /// <summary>The selected node's text shown on the button; the page knows it (it loaded the record).</summary>
    public string? SelectedText { get; init; }
    public required string Url { get; init; }
    public string? Placeholder { get; init; }
    public string? SearchLabel { get; init; }
    public string? LoadingText { get; init; }
    public string? NoResultsText { get; init; }
    public string? MoreText { get; init; }
    public string? ClearText { get; init; }
    public bool Disabled { get; init; }
    public bool Required { get; init; }
    public string? Css { get; init; }
}
