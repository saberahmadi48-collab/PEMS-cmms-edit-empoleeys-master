using PEMS.Application.Definitions;

namespace PEMS.Tests.Definitions;

/// <summary>Tests that read definition files (the development owner, or the shipped system package) say so: since SCRUM-579 the default owner of the UI kinds is the database.</summary>
internal static class FilesOwnership
{
    public static DefinitionOwnership For(IEnumerable<IDefinitionKind> kinds) => new(kinds.ToDictionary(k => k.Key, _ => "files", StringComparer.Ordinal));
}
