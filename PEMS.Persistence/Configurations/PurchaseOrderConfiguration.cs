using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PEMS.Domain.Entities;

namespace PEMS.Persistence.Configurations;

public class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.HasOne(x => x.Project)
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Vendor)
            .WithMany()
            .HasForeignKey(x => x.VendorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Contract)
            .WithMany(x => x.PurchaseOrders)
            .HasForeignKey(x => x.ContractId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.Value)
            .HasPrecision(18, 2);

        // SCRUM-470
        builder.HasOne(x => x.MaterialRequisition).WithMany().HasForeignKey(x => x.MaterialRequisitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AuthorityRule).WithMany().HasForeignKey(x => x.AuthorityRuleId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.SubmittedByUserName).HasMaxLength(256);
        builder.Property(x => x.ApprovedByUserName).HasMaxLength(256);
        builder.Property(x => x.ApproverRole).HasMaxLength(100);
        builder.Property(x => x.RejectionReason).HasMaxLength(1000);
    }
}

/// <summary>SCRUM-470: purchase-order lines. An MR item sits on at most one line of a live PO (checked in the service).</summary>
public class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> builder)
    {
        builder.ToTable("PurchaseOrderLines");
        builder.HasKey(x => x.PurchaseOrderLineId);
        builder.Ignore(x => x.IsCatalogued);
        builder.Ignore(x => x.LineAmount);

        builder.Property(x => x.Description).HasMaxLength(500).IsRequired();
        builder.Property(x => x.PartNumber).HasMaxLength(100);
        builder.Property(x => x.Unit).HasMaxLength(50);
        builder.Property(x => x.FreeTextReason).HasMaxLength(500);
        builder.Property(x => x.Quantity).HasColumnType("decimal(18,3)");
        builder.Property(x => x.UnitPrice).HasPrecision(18, 2);

        builder.HasOne(x => x.PurchaseOrder).WithMany(x => x.Lines).HasForeignKey(x => x.PoId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.MaterialRequisitionItem).WithMany().HasForeignKey(x => x.MaterialRequisitionItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.MaterialItem).WithMany().HasForeignKey(x => x.MaterialItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CommodityGroup).WithMany().HasForeignKey(x => x.CommodityGroupId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.PoId, x.LineNo }).IsUnique();
        builder.HasIndex(x => x.MaterialRequisitionItemId);

        // SCRUM-473: delivery warehouse and inspection release
        builder.Property(x => x.InspectionReleasedByUserName).HasMaxLength(256);
        builder.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.InspectionTestPlan).WithMany().HasForeignKey(x => x.InspectionTestPlanId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>SCRUM-470: the PO authority table (separate from the contract rules).</summary>
public class PurchaseOrderAuthorityRuleConfiguration : IEntityTypeConfiguration<PurchaseOrderAuthorityRule>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderAuthorityRule> builder)
    {
        builder.ToTable("PurchaseOrderAuthorityRules");
        builder.HasKey(x => x.PurchaseOrderAuthorityRuleId);
        builder.Property(x => x.MinAmount).HasPrecision(18, 2);
        builder.Property(x => x.MaxAmount).HasPrecision(18, 2);
        builder.Property(x => x.Currency).HasMaxLength(10).IsRequired();
        builder.Property(x => x.ApproverRole).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.HasIndex(x => new { x.Currency, x.Level });
    }
}
