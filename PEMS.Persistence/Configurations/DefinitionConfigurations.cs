using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PEMS.Domain.Entities.Definitions;

namespace PEMS.Persistence.Configurations;

/// <summary>Tables of the definition store (SCRUM-434, ADR-0010). Cascading deletes are avoided on purpose: history is never lost silently.</summary>
public class DefinitionConfiguration : IEntityTypeConfiguration<Definition>
{
    public void Configure(EntityTypeBuilder<Definition> b)
    {
        b.Property(x => x.Kind).HasMaxLength(50).IsRequired();
        b.Property(x => x.Key).HasMaxLength(200).IsRequired();
        b.Property(x => x.AppKey).HasMaxLength(100).IsRequired();
        b.Property(x => x.CreatedBy).HasMaxLength(256);
        b.Property(x => x.Revision).IsConcurrencyToken();
        b.HasIndex(x => new { x.AppKey, x.Kind, x.Key }).IsUnique().HasDatabaseName("UX_Definitions_AppKey_Kind_Key");
        b.HasMany(x => x.Versions).WithOne(x => x.Definition).HasForeignKey(x => x.DefinitionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.PublishedVersion).WithMany().HasForeignKey(x => x.PublishedVersionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class DefinitionVersionConfiguration : IEntityTypeConfiguration<DefinitionVersion>
{
    public void Configure(EntityTypeBuilder<DefinitionVersion> b)
    {
        b.Property(x => x.ContentHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.ChangeSummary).HasMaxLength(500);
        b.Property(x => x.CreatedBy).HasMaxLength(256);
        b.Property(x => x.ValidatedBy).HasMaxLength(256);
        b.Property(x => x.PublishedBy).HasMaxLength(256);
        b.Property(x => x.Revision).IsConcurrencyToken();
        b.HasIndex(x => new { x.DefinitionId, x.VersionNumber }).IsUnique().HasDatabaseName("UX_DefinitionVersions_Definition_Number");
        // one open draft (Draft or Validated) per definition
        b.HasIndex(x => x.DefinitionId).IsUnique().HasFilter("[Status] IN (0, 1)").HasDatabaseName("UX_DefinitionVersions_OneOpenDraft");
        b.HasIndex(x => x.ContentHash).HasDatabaseName("IX_DefinitionVersions_ContentHash");
        b.HasMany(x => x.References).WithOne(x => x.FromVersion).HasForeignKey(x => x.FromVersionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class DefinitionReferenceConfiguration : IEntityTypeConfiguration<DefinitionReference>
{
    public void Configure(EntityTypeBuilder<DefinitionReference> b)
    {
        b.Property(x => x.ToKind).HasMaxLength(50).IsRequired();
        b.Property(x => x.ToKey).HasMaxLength(200).IsRequired();
        b.Property(x => x.Role).HasMaxLength(50).IsRequired();
        b.HasIndex(x => new { x.ToKind, x.ToKey }).HasDatabaseName("IX_DefinitionReferences_Target");
    }
}

public class DefinitionStampConfiguration : IEntityTypeConfiguration<DefinitionStamp>
{
    public void Configure(EntityTypeBuilder<DefinitionStamp> b)
    {
        b.HasKey(x => x.Kind);
        b.Property(x => x.Kind).HasMaxLength(50);
        b.Property(x => x.Value).IsConcurrencyToken();
    }
}
