using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PEMS.Domain.Entities;

namespace PEMS.Persistence.Configurations;

/// <summary>SCRUM-793: one view per list, scope and name.</summary>
public class GridSavedViewConfiguration : IEntityTypeConfiguration<GridSavedView>
{
    public void Configure(EntityTypeBuilder<GridSavedView> builder)
    {
        builder.ToTable("GridSavedViews");
        builder.HasIndex(x => new { x.ViewsKey, x.ScopeKind, x.ScopeKey, x.Name }).IsUnique();
        builder.Property(x => x.ViewsKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ScopeKind).HasMaxLength(10).IsRequired();
        builder.Property(x => x.ScopeKey).HasMaxLength(256).IsRequired();
        builder.Property(x => x.StateJson).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(200);
    }
}
