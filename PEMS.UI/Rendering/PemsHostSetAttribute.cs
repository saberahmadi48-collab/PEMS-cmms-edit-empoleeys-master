namespace PEMS.UI.Rendering;

/// <summary>
/// A component parameter that the host sets from the page (for example <c>Empty</c> of a tile whose tag helper saw no child content), never an option a stored definition writes.
/// The manifest keeps it in the parameter list but leaves it out of the options schema, so a component with one <c>Options</c> record and such a flag still takes that record as its options JSON.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PemsHostSetAttribute : Attribute;
