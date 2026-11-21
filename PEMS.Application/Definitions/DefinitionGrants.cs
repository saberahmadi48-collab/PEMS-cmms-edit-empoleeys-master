namespace PEMS.Application.Definitions;

/// <summary>SCRUM-591: one grant of a role to a definition.</summary>
public sealed record DefinitionGrantRecord(string Kind, string Key, string RoleName, string Effect);

/// <summary>Reads the grants of a definition (stage 2 of the pipeline asks for them).</summary>
public interface IDefinitionGrantStore
{
    Task<IReadOnlyList<DefinitionGrantRecord>> ForAsync(string kind, string key, CancellationToken ct = default);
}

/// <summary>
/// The decision of stage 2 with grants. The code permission of the definition stays the floor: a role that holds it opens the definition unless a <c>deny</c> grant names one of the user's roles
/// (a deny always wins); a role that does not hold it opens the definition only through an <c>allow</c> grant. With no grants the decision is the permission's, as before.
/// </summary>
public static class DefinitionGrantRules
{
    public const string Allow = "allow";
    public const string Deny = "deny";

    public static bool IsEffect(string? effect) => effect is Allow or Deny;

    public static bool Decide(bool permissionAllows, IReadOnlyCollection<string> roles, IEnumerable<DefinitionGrantRecord> grants)
    {
        var mine = grants.Where(g => roles.Contains(g.RoleName, StringComparer.OrdinalIgnoreCase)).ToList();
        if (mine.Any(g => g.Effect == Deny)) return false;
        return permissionAllows || mine.Any(g => g.Effect == Allow);
    }
}
