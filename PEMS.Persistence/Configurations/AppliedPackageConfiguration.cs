using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PEMS.Domain.Entities;

namespace PEMS.Persistence.Configurations;

/// <summary>SCRUM-807, 814: the packages run on this installation; looked up by hash.</summary>
public class AppliedPackageConfiguration : IEntityTypeConfiguration<AppliedPackage>
{
    public void Configure(EntityTypeBuilder<AppliedPackage> builder)
    {
        builder.ToTable("AppliedPackages");
        builder.HasIndex(x => x.PackageHash);
        builder.Property(x => x.PackageHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Kind).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Version).HasMaxLength(40);
        builder.Property(x => x.Summary).HasMaxLength(1000);
        builder.Property(x => x.BackupPoint).HasMaxLength(260);
        builder.Property(x => x.AppliedBy).HasMaxLength(200);
    }
}
