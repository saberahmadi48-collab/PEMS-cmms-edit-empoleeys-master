using System.ComponentModel.DataAnnotations;

namespace PEMS.Domain.Entities;

/// <summary>
/// A password hash a user had before the latest change (SCRUM-412 / #382). A new password must differ from the current one and
/// from the last N entries (setting Authentication:Password:HistoryCount). Only the Identity hash is stored, never the password.
/// Written by PemsUserManager when a password is replaced; trimmed to N entries per user.
/// </summary>
public class PasswordHistoryEntry
{
    [Key]
    public int PasswordHistoryEntryId { get; set; }

    /// <summary>AspNetUsers.Id of the owner.</summary>
    public int UserId { get; set; }

    /// <summary>The replaced Identity password hash (salted, one-way).</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>When the hash was replaced by a newer password.</summary>
    public DateTime ReplacedAtUtc { get; set; }
}
