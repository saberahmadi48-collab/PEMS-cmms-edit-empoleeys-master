using PEMS.UI.Abstractions;

namespace PEMS.UI.Components;

/// <summary>An optional call to action inside a state (a link: "add the first record", "try again"). The label is an input.</summary>
public sealed record StateAction
{
    public required TranslatableText Label { get; init; }
    public required string Href { get; init; }
}

/// <summary>Inputs of <see cref="PemsEmptyState"/>: nothing to show yet. Icon is an icon-font class such as "bi-inbox".</summary>
public sealed record EmptyStateOptions
{
    public required TranslatableText Title { get; init; }
    public TranslatableText? Description { get; init; }
    public string? Icon { get; init; }
    public StateAction? Action { get; init; }
}

/// <summary>Inputs of <see cref="PemsErrorState"/>: something failed. Detail is a safe, user-readable reason (never a stack trace).</summary>
public sealed record ErrorStateOptions
{
    public required TranslatableText Title { get; init; }
    public TranslatableText? Description { get; init; }
    public TranslatableText? Detail { get; init; }
    public StateAction? Action { get; init; }
}

/// <summary>Inputs of <see cref="PemsLoadingState"/>: data is on its way. Lines &gt; 0 adds placeholder lines under the title.</summary>
public sealed record LoadingStateOptions
{
    public required TranslatableText Title { get; init; }
    public int Lines { get; init; }
}
