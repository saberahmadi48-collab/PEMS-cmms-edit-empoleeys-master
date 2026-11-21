using PEMS.UI.Abstractions;

namespace PEMS.UI.Components;

// SCRUM-697: a front-end island (React or any bundle the product ships under /dist/): a root element, the boot data the bundle reads as JSON, its stylesheets and its module script. The Bklit
// dashboards bundle (frontend/dashboards) is the first one. Only files of this site under /dist/ can be named, so a definition cannot load a script from anywhere else.

/// <summary>Inputs of <see cref="PemsScriptIsland"/>.</summary>
public sealed record ScriptIslandOptions : FrameOptions
{
    /// <summary>The id of the root element the bundle mounts into.</summary>
    public required string Id { get; init; }
    /// <summary>The id of the JSON element the bundle reads its boot data from.</summary>
    public required string BootId { get; init; }
    /// <summary>The module script, a path under /dist/ ending in .js.</summary>
    public required string Module { get; init; }
    /// <summary>Stylesheets, paths under /dist/ ending in .css.</summary>
    public IReadOnlyList<string> Styles { get; init; } = [];
    /// <summary>Scalar values written at the top of the boot data (the data url).</summary>
    public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>();
    /// <summary>Named links the bundle shows (written as <c>links</c>).</summary>
    public IReadOnlyDictionary<string, string> Links { get; init; } = new Dictionary<string, string>();
    /// <summary>The texts of the bundle in the language of the request (written as <c>i18n</c>).</summary>
    public IReadOnlyDictionary<string, TranslatableText> I18n { get; init; } = new Dictionary<string, TranslatableText>();
    /// <summary>The note shown until the bundle has mounted.</summary>
    public TranslatableText? Note { get; init; }
    /// <summary>Extra classes of the root (the bundle's scope class).</summary>
    public string? Css { get; init; }
}
