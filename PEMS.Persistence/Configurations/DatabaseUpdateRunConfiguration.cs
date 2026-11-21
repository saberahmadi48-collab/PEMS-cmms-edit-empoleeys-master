using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PEMS.Domain.Entities;

namespace PEMS.Persistence.Configurations;

public class DatabaseUpdateRunConfiguration : IEntityTypeConfiguration<DatabaseUpdateRun>
{
    public void Configure(EntityTypeBuilder<DatabaseUpdateRun> builder)
    {
        builder.HasIndex(x => x.StartedAtUtc)
            .HasDatabaseName("IX_DatabaseUpdateRuns_StartedAtUtc");
    }
}
