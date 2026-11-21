using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PEMS.Domain.Entities;

namespace PEMS.Persistence.Configurations;

/// <summary>SCRUM-591: one grant per definition and role.</summary>
public class DefinitionGrantConfiguration : IEntityTypeConfiguration<DefinitionGrant>
{
    public void Configure(EntityTypeBuilder<DefinitionGrant> builder)
    {
        builder.ToTable("DefinitionGrants");
        builder.HasIndex(x => new { x.Kind, x.DefinitionKey, x.RoleName }).IsUnique();
        builder.Property(x => x.Kind).HasMaxLength(40).IsRequired();
        builder.Property(x => x.DefinitionKey).HasMaxLength(120).IsRequired();
        builder.Property(x => x.RoleName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Effect).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500);
        builder.Property(x => x.CreatedBy).HasMaxLength(200);
    }
}
