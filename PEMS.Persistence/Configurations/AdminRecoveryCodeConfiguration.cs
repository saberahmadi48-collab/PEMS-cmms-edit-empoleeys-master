using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PEMS.Domain.Entities;

namespace PEMS.Persistence.Configurations;

public class AdminRecoveryCodeConfiguration : IEntityTypeConfiguration<AdminRecoveryCode>
{
    public void Configure(EntityTypeBuilder<AdminRecoveryCode> builder)
    {
        builder.Property(x => x.CodeHash).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Salt).HasMaxLength(16).IsRequired();

        // مصرف/ابطال فقط وقتی ذخیره می‌شود که مقدار قبلی هنوز null باشد (دو مصرف هم‌زمان یک کد => یکی DbUpdateConcurrencyException)
        builder.Property(x => x.UsedAtUtc).IsConcurrencyToken();
        builder.Property(x => x.RevokedAtUtc).IsConcurrencyToken();

        builder.HasIndex(x => new { x.UserId, x.UsedAtUtc, x.RevokedAtUtc });
    }
}
