namespace PEMS.UI.Abstractions;

/// <summary>
/// Inputs of the page title row: the page title and an optional muted module label. The label is shown only when it differs from
/// the title. An empty title renders nothing. Whether a page wants a header at all (for example HidePageTitle) is the host's decision.
/// </summary>
public sealed record PageHeaderModel
{
    public required TranslatableText Title { get; init; }
    public TranslatableText? Section { get; init; }
}
