using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PEMS.Domain.Entities;

namespace PEMS.Persistence.Configurations;

public class MaterialReservationConfiguration : IEntityTypeConfiguration<MaterialReservation>
{
    public void Configure(EntityTypeBuilder<MaterialReservation> builder)
    {
        builder.ToTable("MaterialReservations");
        builder.HasKey(x => x.MaterialReservationId);

        builder.Property(x => x.Status).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Quantity).HasColumnType("decimal(18,3)");
        builder.Property(x => x.IssuedQuantity).HasColumnType("decimal(18,3)");
        builder.HasOne(x => x.StoreRequisitionLine).WithMany().HasForeignKey(x => x.StoreRequisitionLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.StoreRequisitionLineId);

        builder.HasOne(x => x.MaterialItem).WithMany().HasForeignKey(x => x.MaterialItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.MaintenancePlan).WithMany().HasForeignKey(x => x.MaintenancePlanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.StoreRequisition).WithMany().HasForeignKey(x => x.StoreRequisitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ReservedBy).WithMany().HasForeignKey(x => x.ReservedById).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.MaterialItemId, x.Status });
    }
}

public class StoreRequisitionConfiguration : IEntityTypeConfiguration<StoreRequisition>
{
    public void Configure(EntityTypeBuilder<StoreRequisition> builder)
    {
        builder.ToTable("StoreRequisitions");
        builder.HasKey(x => x.StoreRequisitionId);

        builder.Property(x => x.RequisitionNumber).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(30).IsRequired();

        builder.HasIndex(x => x.RequisitionNumber).IsUnique();

        builder.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.RequestedBy).WithMany().HasForeignKey(x => x.RequestedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ApprovedBy).WithMany().HasForeignKey(x => x.ApprovedById).OnDelete(DeleteBehavior.Restrict);
    }
}

public class StoreRequisitionLineConfiguration : IEntityTypeConfiguration<StoreRequisitionLine>
{
    public void Configure(EntityTypeBuilder<StoreRequisitionLine> builder)
    {
        builder.ToTable("StoreRequisitionLines");
        builder.HasKey(x => x.StoreRequisitionLineId);

        builder.Property(x => x.RequestedQuantity).HasColumnType("decimal(18,3)");
        builder.Property(x => x.ApprovedQuantity).HasColumnType("decimal(18,3)");
        builder.Property(x => x.IssuedQuantity).HasColumnType("decimal(18,3)");

        builder.HasOne(x => x.StoreRequisition).WithMany(x => x.Lines).HasForeignKey(x => x.StoreRequisitionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.MaterialItem).WithMany().HasForeignKey(x => x.MaterialItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Uom).WithMany().HasForeignKey(x => x.UomId).OnDelete(DeleteBehavior.Restrict);
        // SCRUM-472: NoAction in the database (a cascade/set-null would give SQL Server two paths from WorkOrders);
        // removing a work-order material line that a requisition already supplies is refused in WarehouseIntakeService.
        builder.HasOne(x => x.WorkOrderMaterial).WithMany().HasForeignKey(x => x.WorkOrderMaterialId).OnDelete(DeleteBehavior.ClientSetNull);
        builder.HasIndex(x => x.WorkOrderMaterialId);
    }
}

public class StoreRequisitionStatusHistoryConfiguration : IEntityTypeConfiguration<StoreRequisitionStatusHistory>
{
    public void Configure(EntityTypeBuilder<StoreRequisitionStatusHistory> builder)
    {
        builder.ToTable("StoreRequisitionStatusHistories");
        builder.HasKey(x => x.StoreRequisitionStatusHistoryId);

        builder.Property(x => x.FromStatus).HasMaxLength(30).IsRequired();
        builder.Property(x => x.ToStatus).HasMaxLength(30).IsRequired();
        builder.Property(x => x.RequestKey).HasMaxLength(64);
        builder.HasIndex(x => new { x.StoreRequisitionId, x.RequestKey }).IsUnique().HasFilter("[RequestKey] IS NOT NULL");

        builder.HasOne(x => x.StoreRequisition).WithMany().HasForeignKey(x => x.StoreRequisitionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.ChangedBy).WithMany().HasForeignKey(x => x.ChangedById).OnDelete(DeleteBehavior.Restrict);
    }
}

public class WorkOrderMaterialConfiguration : IEntityTypeConfiguration<WorkOrderMaterial>
{
    public void Configure(EntityTypeBuilder<WorkOrderMaterial> builder)
    {
        builder.ToTable("WorkOrderMaterials");
        builder.HasKey(x => x.WorkOrderMaterialId);

        builder.Property(x => x.RequiredQuantity).HasColumnType("decimal(18,3)");
        builder.Property(x => x.SupplyCode).HasMaxLength(20).IsRequired().HasDefaultValue(WorkOrderMaterialSupplyCodes.Stock);

        builder.HasOne(x => x.WorkOrder).WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.MaterialItem).WithMany().HasForeignKey(x => x.MaterialItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Uom).WithMany().HasForeignKey(x => x.UomId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.WorkOrderId, x.MaterialItemId }).IsUnique();
    }
}
