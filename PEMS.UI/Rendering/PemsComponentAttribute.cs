namespace PEMS.UI.Rendering;

/// <summary>
/// Marks a component as renderable by key from a descriptor and describes it for designers. Extension packs use it to add their own components.
/// Category and Icon group the component in a designer palette; a small valid sample options JSON per component lives in <c>ComponentSamples</c>.
/// No visible text lives here: the designer resolves the palette name through the message key <c>component.&lt;key&gt;</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class PemsComponentAttribute(string key) : Attribute
{
    public string Key { get; } = key;
    public string Category { get; init; } = "general";
    public string Icon { get; init; } = "";
}
