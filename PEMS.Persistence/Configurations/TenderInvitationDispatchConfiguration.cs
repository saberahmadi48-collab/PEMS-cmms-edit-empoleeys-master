using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PEMS.Domain.Entities;

namespace PEMS.Persistence.Configurations;

/// <summary>SCRUM-429: the tender invitation send log.</summary>
public class TenderInvitationDispatchConfiguration : IEntityTypeConfiguration<TenderInvitationDispatch>
{
    public void Configure(EntityTypeBuilder<TenderInvitationDispatch> builder)
    {
        builder.ToTable("TenderInvitationDispatches");
        builder.HasKey(x => x.TenderInvitationDispatchId);

        builder.Property(x => x.ToAddress).HasMaxLength(256).IsRequired();
        builder.Property(x => x.ToName).HasMaxLength(256);
        builder.Property(x => x.Subject).HasMaxLength(400).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.LastError).HasMaxLength(1000);
        builder.Property(x => x.QueuedByUserName).HasMaxLength(256);
        builder.Property(x => x.CreatedBy).HasMaxLength(256);
        builder.Property(x => x.ModifiedBy).HasMaxLength(256);

        builder.HasOne(x => x.Tender).WithMany().HasForeignKey(x => x.TenderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Vendor).WithMany().HasForeignKey(x => x.VendorId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.TenderId, x.VendorId });
    }
}
