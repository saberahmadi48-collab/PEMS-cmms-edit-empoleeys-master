using System.ComponentModel.DataAnnotations;

namespace PEMS.Domain.Entities;

/// <summary>
/// SCRUM-591: a grant of a role to a definition (page, form, action or data source key), kept in the database so an administrator can open or close a definition for a role without a deploy.
/// <c>Effect</c> is <c>allow</c> (the role may open it although the definition's own permission would not let it in) or <c>deny</c> (the role may not, whatever it holds). A deny always wins.
/// Every change is in the audit log through the context's SaveChanges. The grant decides only the opening of the definition; the permission, separation-of-duties and scope checks of its actions still apply.
/// </summary>
public class DefinitionGrant
{
    [Key]
    public int DefinitionGrantId { get; set; }

    /// <summary>page, form, action or data-source.</summary>
    [MaxLength(40)]
    public string Kind { get; set; } = string.Empty;

    [MaxLength(120)]
    public string DefinitionKey { get; set; } = string.Empty;

    [MaxLength(256)]
    public string RoleName { get; set; } = string.Empty;

    /// <summary>allow or deny.</summary>
    [MaxLength(10)]
    public string Effect { get; set; } = "allow";

    [MaxLength(500)]
    public string? Reason { get; set; }

    [MaxLength(200)]
    public string? CreatedBy { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.Now;
}
