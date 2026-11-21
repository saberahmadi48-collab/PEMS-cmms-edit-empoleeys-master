using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PEMS.Domain.Entities;

namespace PEMS.Persistence.Configurations;

public class SecurityEventConfiguration : IEntityTypeConfiguration<SecurityEvent>
{
    public void Configure(EntityTypeBuilder<SecurityEvent> builder)
    {
        builder.Property(x => x.EventType).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Method).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Result).HasMaxLength(16).IsRequired();
        builder.Property(x => x.FailureReason).HasMaxLength(64);
        builder.Property(x => x.ActorUserName).HasMaxLength(256);
        builder.Property(x => x.TargetUserName).HasMaxLength(256);
        builder.Property(x => x.IpAddress).HasMaxLength(64);

        builder.HasIndex(x => x.OccurredAtUtc);
        builder.HasIndex(x => new { x.Method, x.Result });
        // صف ارسال به SIEM (SCRUM-212): رویدادهای هنوز ارسال‌نشده
        builder.HasIndex(x => x.ForwardedToSiemAt);
    }
}
