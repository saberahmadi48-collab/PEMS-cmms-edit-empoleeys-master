using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PEMS.Domain.Entities;

namespace PEMS.Persistence.Configurations;

public class WorkOrderChangeLogConfiguration : IEntityTypeConfiguration<WorkOrderChangeLog>
{
    public void Configure(EntityTypeBuilder<WorkOrderChangeLog> builder)
    {
        builder.HasIndex(x => new { x.WorkOrderId, x.ChangedDate });
        builder.HasIndex(x => x.IsSecurityEvent);
    }
}
