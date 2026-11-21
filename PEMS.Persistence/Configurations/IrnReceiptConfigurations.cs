using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PEMS.Domain.Entities;

namespace PEMS.Persistence.Configurations;

public class IrnReceiptConfiguration : IEntityTypeConfiguration<IrnReceipt>
{
    public void Configure(EntityTypeBuilder<IrnReceipt> builder)
    {
        builder.ToTable("IrnReceipts");
        builder.HasKey(x => x.IrnReceiptId);

        builder.Property(x => x.IrnNumber).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(30).IsRequired();

        builder.HasIndex(x => x.IrnNumber).IsUnique();

        builder.HasOne(x => x.Vendor).WithMany().HasForeignKey(x => x.VendorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.PurchaseOrder).WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.MrsImportBatch).WithMany().HasForeignKey(x => x.MrsImportBatchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ReceivedBy).WithMany().HasForeignKey(x => x.ReceivedById).OnDelete(DeleteBehavior.Restrict);
    }
}

public class IrnReceiptLineConfiguration : IEntityTypeConfiguration<IrnReceiptLine>
{
    public void Configure(EntityTypeBuilder<IrnReceiptLine> builder)
    {
        builder.ToTable("IrnReceiptLines");
        builder.HasKey(x => x.IrnReceiptLineId);

        builder.Property(x => x.ReceivedQuantity).HasColumnType("decimal(18,3)");
        builder.Property(x => x.AcceptedQuantity).HasColumnType("decimal(18,3)");
        builder.Property(x => x.RejectedQuantity).HasColumnType("decimal(18,3)");
        builder.Property(x => x.HoldQuantity).HasColumnType("decimal(18,3)");
        builder.Ignore(x => x.HasOpenHold);

        // SCRUM-473: receiving against the PO line, the buyer's rejection decision (D6b) and the Hold resolution (D7)
        builder.Property(x => x.RejectionAction).HasMaxLength(20);
        builder.Property(x => x.RejectionDecidedByUserName).HasMaxLength(256);
        builder.Property(x => x.HoldAcceptedQuantity).HasColumnType("decimal(18,3)");
        builder.Property(x => x.HoldRejectedQuantity).HasColumnType("decimal(18,3)");
        builder.Property(x => x.HoldResolvedByUserName).HasMaxLength(256);
        builder.Property(x => x.HoldResolutionNotes).HasMaxLength(1000);
        builder.HasOne(x => x.PurchaseOrderLine).WithMany(x => x.ReceiptLines).HasForeignKey(x => x.PurchaseOrderLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.PurchaseOrderLineId);

        builder.HasOne(x => x.IrnReceipt).WithMany(x => x.Lines).HasForeignKey(x => x.IrnReceiptId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.MaterialItem).WithMany().HasForeignKey(x => x.MaterialItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Uom).WithMany().HasForeignKey(x => x.UomId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.StorageLocation).WithMany().HasForeignKey(x => x.StorageLocationId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class IrnReceiptStatusHistoryConfiguration : IEntityTypeConfiguration<IrnReceiptStatusHistory>
{
    public void Configure(EntityTypeBuilder<IrnReceiptStatusHistory> builder)
    {
        builder.ToTable("IrnReceiptStatusHistories");
        builder.HasKey(x => x.IrnReceiptStatusHistoryId);

        builder.Property(x => x.FromStatus).HasMaxLength(30).IsRequired();
        builder.Property(x => x.ToStatus).HasMaxLength(30).IsRequired();

        builder.HasOne(x => x.IrnReceipt).WithMany().HasForeignKey(x => x.IrnReceiptId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.ChangedBy).WithMany().HasForeignKey(x => x.ChangedById).OnDelete(DeleteBehavior.Restrict);
    }
}

public class IrnDispositionRecordConfiguration : IEntityTypeConfiguration<IrnDispositionRecord>
{
    public void Configure(EntityTypeBuilder<IrnDispositionRecord> builder)
    {
        builder.ToTable("IrnDispositionRecords");
        builder.HasKey(x => x.IrnDispositionRecordId);

        builder.Property(x => x.OverallResult).HasMaxLength(30).IsRequired();
        builder.Property(x => x.WorkflowOutcome).HasMaxLength(30).IsRequired();

        builder.HasOne(x => x.IrnReceipt).WithMany().HasForeignKey(x => x.IrnReceiptId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.DispositionedBy).WithMany().HasForeignKey(x => x.DispositionedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.NonConformanceReport).WithMany().HasForeignKey(x => x.NonConformanceReportId).OnDelete(DeleteBehavior.Restrict);
    }
}
