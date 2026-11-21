using System.ComponentModel.DataAnnotations;

namespace PEMS.Domain.Entities;

/// <summary>
/// SCRUM-793: a saved view of a generic list (the grid state: columns, sort, filters, search, density) kept on the server, so it follows its owner to another device. <c>ScopeKind</c> is <c>user</c> (visible to the user in
/// <c>ScopeKey</c>) or <c>role</c> (visible to every member of the role in <c>ScopeKey</c>). <c>ViewsKey</c> is the key the list declares for its views. Every change is in the audit log.
/// </summary>
public class GridSavedView
{
    [Key]
    public int GridSavedViewId { get; set; }

    [MaxLength(100)]
    public string ViewsKey { get; set; } = string.Empty;

    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>user or role.</summary>
    [MaxLength(10)]
    public string ScopeKind { get; set; } = "user";

    /// <summary>The user name or the role name.</summary>
    [MaxLength(256)]
    public string ScopeKey { get; set; } = string.Empty;

    /// <summary>The grid state as JSON.</summary>
    public string StateJson { get; set; } = "{}";

    [MaxLength(200)]
    public string? CreatedBy { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}
