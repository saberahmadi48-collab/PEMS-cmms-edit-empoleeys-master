using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PEMS.Domain.Entities;

namespace PEMS.Persistence.Configurations;

/// <summary>سایدبار داده‌محور: Key یکتا؛ والد با Key (نه FK) تا جابه‌جایی بین بخش‌ها ساده و بدون Cascade بماند.</summary>
public class NavigationMenuItemConfiguration : IEntityTypeConfiguration<NavigationMenuItem>
{
    public void Configure(EntityTypeBuilder<NavigationMenuItem> builder)
    {
        builder.ToTable("NavigationMenuItems");

        builder.HasIndex(x => x.Key).IsUnique();
        builder.HasIndex(x => new { x.ParentKey, x.SortOrder });

        builder.Property(x => x.Key).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ParentKey).HasMaxLength(100);
        builder.Property(x => x.Kind).HasMaxLength(20).IsRequired();
        builder.Property(x => x.TitleKey).HasMaxLength(150).IsRequired();
        builder.Property(x => x.TitleEn).HasMaxLength(200);
        builder.Property(x => x.TitleFa).HasMaxLength(200);
        builder.Property(x => x.Icon).HasMaxLength(60);
        builder.Property(x => x.Area).HasMaxLength(100);
        builder.Property(x => x.Controller).HasMaxLength(100);
        builder.Property(x => x.Action).HasMaxLength(100);
        builder.Property(x => x.ExternalPath).HasMaxLength(300);
        builder.Property(x => x.VisibilityRule).HasMaxLength(400).IsRequired();
        builder.Property(x => x.ExtraActiveControllers).HasMaxLength(1000);
        builder.Property(x => x.ActiveArea).HasMaxLength(100);
        builder.Property(x => x.BadgeKey).HasMaxLength(60);
        builder.Property(x => x.CreatedBy).HasMaxLength(200);
        builder.Property(x => x.ModifiedBy).HasMaxLength(200);
    }
}
