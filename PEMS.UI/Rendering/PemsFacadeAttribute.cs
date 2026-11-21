namespace PEMS.UI.Rendering;

/// <summary>How a <c>pems-*</c> tag helper relates to the component registry (ADR-0013).</summary>
public enum FacadeKind
{
    /// <summary>Renders through the registry: the component of <see cref="PemsFacadeAttribute.Key"/>.</summary>
    Component,
    /// <summary>A child tag of another facade (a segment, a header); it has no component of its own.</summary>
    Part,
}

/// <summary>Links a tag helper to the registry key it renders, so the code graph and the drift tests see which component each tag stands for.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class PemsFacadeAttribute : Attribute
{
    public PemsFacadeAttribute(string key) { Key = key; Kind = FacadeKind.Component; }
    public PemsFacadeAttribute(FacadeKind kind) { Kind = kind; }
    public string? Key { get; }
    public FacadeKind Kind { get; }
}
