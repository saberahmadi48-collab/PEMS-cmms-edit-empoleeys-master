using PEMS.UI.Abstractions;

namespace PEMS.UI.Components;

// SCRUM-729: the drawn signature. A canvas the user draws on and the two buttons to clear and save it; the save sends the PNG (a data URL) to the action definition named by Action as the value
// of Field, so no controller and no view hold a script of their own. pems-signature.js draws and posts (the antiforgery token of the page, the answer as JSON).

/// <summary>Inputs of <see cref="PemsSignaturePad"/>.</summary>
public sealed record SignaturePadOptions : FrameOptions
{
    public required string Id { get; init; }
    /// <summary>The key of the action definition that stores the signature.</summary>
    public required string Action { get; init; }
    /// <summary>The name of the value the action receives (the PNG as a data URL).</summary>
    public string Field { get; init; } = "signatureDataUrl";
    public required TranslatableText ClearLabel { get; init; }
    public required TranslatableText SaveLabel { get; init; }
    /// <summary>The text above the canvas.</summary>
    public TranslatableText? Hint { get; init; }
    /// <summary>The text shown when the canvas is empty at save time.</summary>
    public required TranslatableText EmptyText { get; init; }
    /// <summary>The text shown when the save worked.</summary>
    public required TranslatableText SavedText { get; init; }
    /// <summary>The text shown when the save failed.</summary>
    public required TranslatableText ErrorText { get; init; }
    /// <summary>A path of this site of the current signature image; none = no current signature.</summary>
    public string? CurrentUrl { get; init; }
    public TranslatableText? CurrentLabel { get; init; }
    public int Width { get; init; } = 600;
    public int Height { get; init; } = 200;
}
