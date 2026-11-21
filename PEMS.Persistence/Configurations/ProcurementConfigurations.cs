using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PEMS.Domain.Entities;

namespace PEMS.Persistence.Configurations;

/// <summary>
/// پیکربندی خانواده‌ی Procurement (MR, MRQ, ProcurementPackage و اعضای آن) -
/// طبق استاندارد جدید (Volume V): یک کلاس به‌ازای هر Entity، همه در یک فایل چون به‌شدت
/// به‌هم مرتبط‌اند.
/// </summary>
public class MaterialRequisitionConfiguration : IEntityTypeConfiguration<MaterialRequisition>
{
    public void Configure(EntityTypeBuilder<MaterialRequisition> builder)
    {
        // طبق ممیزی مستقل فاز ۶B Wave A (P1): یکتایی MRNumber قبلاً فقط سطح Application بود
        // (AnyAsync قبل از Insert) که در برابر Race Condition امن نیست - همون الگوی
        // WorkOrderAssignmentConfiguration.WorkOrderNumber. طول ستون توسط Convention پیش‌فرض
        // SQL Server برای ستون‌های ایندکس‌شده (nvarchar(450)) تعیین می‌شود.
        builder.HasIndex(x => x.MRNumber).IsUnique();

        builder.HasOne(x => x.Project)
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Discipline)
            .WithMany()
            .HasForeignKey(x => x.DisciplineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ResponsibleEngineer)
            .WithMany()
            .HasForeignKey(x => x.ResponsibleEngineerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.RelatedDocument)
            .WithMany()
            .HasForeignKey(x => x.RelatedDocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ProcurementPackage)
            .WithMany(x => x.MaterialRequisitions)
            .HasForeignKey(x => x.ProcurementPackageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>SCRUM-428: optional links of an MR item to the registered part it was resolved to (never cascades).</summary>
public class MaterialRequisitionItemConfiguration : IEntityTypeConfiguration<MaterialRequisitionItem>
{
    public void Configure(EntityTypeBuilder<MaterialRequisitionItem> builder)
    {
        builder.Ignore(x => x.IsCatalogued);

        builder.HasOne(x => x.MaterialItem)
            .WithMany()
            .HasForeignKey(x => x.MaterialItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.PartCatalogueRequest)
            .WithMany()
            .HasForeignKey(x => x.PartCatalogueRequestId)
            .OnDelete(DeleteBehavior.Restrict);

        // SCRUM-471: origin link to the pre-procurement item
        builder.HasOne(x => x.ProcurementDemand)
            .WithMany()
            .HasForeignKey(x => x.ProcurementDemandId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class MaterialRequisitionQuotationConfiguration : IEntityTypeConfiguration<MaterialRequisitionQuotation>
{
    public void Configure(EntityTypeBuilder<MaterialRequisitionQuotation> builder)
    {
        builder.HasOne(x => x.MR)
            .WithMany(x => x.Quotations)
            .HasForeignKey(x => x.MRId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.ProcurementPackage)
            .WithMany(x => x.Quotations)
            .HasForeignKey(x => x.ProcurementPackageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.QuotedAmount)
            .HasPrecision(18, 2);
    }
}

public class ProcurementPackageConfiguration : IEntityTypeConfiguration<ProcurementPackage>
{
    public void Configure(EntityTypeBuilder<ProcurementPackage> builder)
    {
        builder.HasOne(x => x.Project)
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Discipline)
            .WithMany()
            .HasForeignKey(x => x.DisciplineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ProcurementCommunicationConfiguration : IEntityTypeConfiguration<ProcurementCommunication>
{
    public void Configure(EntityTypeBuilder<ProcurementCommunication> builder)
    {
        builder.HasOne(x => x.ProcurementPackage)
            .WithMany(x => x.Communications)
            .HasForeignKey(x => x.ProcurementPackageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Vendor)
            .WithMany()
            .HasForeignKey(x => x.VendorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Attachment)
            .WithMany()
            .HasForeignKey(x => x.AttachmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Tender)
            .WithMany()
            .HasForeignKey(x => x.TenderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ProcurementCommunicationResponseConfiguration : IEntityTypeConfiguration<ProcurementCommunicationResponse>
{
    public void Configure(EntityTypeBuilder<ProcurementCommunicationResponse> builder)
    {
        builder.HasOne(x => x.ProcurementCommunication)
            .WithMany(x => x.Responses)
            .HasForeignKey(x => x.ProcurementCommunicationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ProcurementApprovalConfiguration : IEntityTypeConfiguration<ProcurementApproval>
{
    public void Configure(EntityTypeBuilder<ProcurementApproval> builder)
    {
        builder.HasOne(x => x.ProcurementPackage)
            .WithMany(x => x.Approvals)
            .HasForeignKey(x => x.ProcurementPackageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>SCRUM-471: pre-procurement items. One live item per source (filtered unique index): no double raising, even under concurrency.</summary>
public class ProcurementDemandConfiguration : IEntityTypeConfiguration<ProcurementDemand>
{
    public void Configure(EntityTypeBuilder<ProcurementDemand> builder)
    {
        builder.ToTable("ProcurementDemands");
        builder.HasKey(x => x.ProcurementDemandId);

        builder.Property(x => x.SourceType).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Quantity).HasColumnType("decimal(18,3)");
        builder.Property(x => x.DecisionReason).HasMaxLength(1000);
        builder.Property(x => x.RaisedByUserName).HasMaxLength(256);
        builder.Property(x => x.DecidedByUserName).HasMaxLength(256);

        builder.HasIndex(x => new { x.SourceType, x.SourceId }).IsUnique()
            .HasFilter("[Status] IN ('Open','Converted','Accepted')")
            .HasDatabaseName("UX_ProcurementDemands_LiveSource");
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => new { x.MaterialItemId, x.WarehouseId });

        builder.HasOne(x => x.MaterialItem).WithMany().HasForeignKey(x => x.MaterialItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Uom).WithMany().HasForeignKey(x => x.UomId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.MaterialRequisition).WithMany().HasForeignKey(x => x.MaterialRequisitionId).OnDelete(DeleteBehavior.Restrict);
    }
}
