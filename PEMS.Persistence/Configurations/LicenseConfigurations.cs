using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PEMS.Domain.Entities;

namespace PEMS.Persistence.Configurations;

public class LicenseSealRecordConfiguration : IEntityTypeConfiguration<LicenseSealRecord>
{
    public void Configure(EntityTypeBuilder<LicenseSealRecord> builder)
    {
        builder.ToTable("LicenseSeal");
        builder.Property(x => x.LicenseSealRecordId).ValueGeneratedNever();
        builder.Property(x => x.Bundle).IsRequired();
    }
}

public class LicenseEventRecordConfiguration : IEntityTypeConfiguration<LicenseEventRecord>
{
    public void Configure(EntityTypeBuilder<LicenseEventRecord> builder)
    {
        builder.ToTable("LicenseEvent");
        builder.Property(x => x.Seq).ValueGeneratedNever();   // the sequence belongs to the hash chain, not to the database
    }
}
