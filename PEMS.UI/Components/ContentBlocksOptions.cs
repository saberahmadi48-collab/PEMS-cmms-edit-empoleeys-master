using PEMS.UI.Abstractions;

namespace PEMS.UI.Components;

// SCRUM-666..757 (batch 5): the pages that only show text (the k6 performance notes, the quick-setup guide, the knowledge-base introduction) are a definition, not a view: one generic component renders a list of
// text blocks the definition writes in both languages. Nothing here reads data; the text is escaped, never markup.

/// <summary>A card of a <c>cards</c> block: a Bootstrap icon name (bi-...), a title, a short text and its links (buttons to paths of this site).</summary>
public sealed record ContentCard
{
    public string? Icon { get; init; }
    public required TranslatableText Title { get; init; }
    public TranslatableText? Text { get; init; }
    public IReadOnlyList<ContentLink> Links { get; init; } = [];
}

/// <summary>A button-link of a card: a path of this site and its label (default Open).</summary>
public sealed record ContentLink
{
    public string? Href { get; init; }
    public TranslatableText Label { get; init; } = TranslatableText.Of(("en", "Open"), ("fa", "باز کردن"));
}

/// <summary>One block: <c>heading</c> (Level 1 to 6), <c>text</c>, <c>alert</c> (Tone info, success, warning or danger, optional Title), <c>list</c> (Items, Ordered), <c>code</c>, <c>link</c> (Href is a path of this site), <c>cards</c> (Cards in Columns per row: 1, 2, 3, 4 or 6) or <c>divider</c>.</summary>
public sealed record ContentBlock
{
    public required string Type { get; init; }
    public TranslatableText? Text { get; init; }
    public TranslatableText? Title { get; init; }
    public int Level { get; init; } = 3;
    public string Tone { get; init; } = "info";
    public bool Ordered { get; init; }
    public IReadOnlyList<TranslatableText> Items { get; init; } = [];
    public string? Href { get; init; }
    public IReadOnlyList<ContentCard> Cards { get; init; } = [];
    public int Columns { get; init; } = 3;
    public string? Css { get; init; }
}

/// <summary>Inputs of <see cref="PemsContentBlocks"/>.</summary>
public sealed record ContentBlocksOptions : FrameOptions
{
    public string? Id { get; init; }
    public string? Css { get; init; }
    public IReadOnlyList<ContentBlock> Blocks { get; init; } = [];
    public TranslatableText Empty { get; init; } = TranslatableText.Of(("en", "Nothing to show"), ("fa", "موردی برای نمایش نیست"));
}
