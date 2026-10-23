using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PEMS.Domain.Entities;

namespace PEMS.Persistence.Configurations;

// پیکربندی EF «درخواست کاتالوگ قطعه» (Warehouse). همه‌ی FK ها به Employee/Equipment/MaterialUOM «Restrict» هستند
// (تاریخچه‌ی درخواست و تأییدها هرگز با حذف رکوردهای دیگر ناپدید نشود)؛ فقط فرزندانِ مالکیتیِ خودِ درخواست Cascade دارند.

public class PartCatalogueTechnicalClassConfiguration : IEntityTypeConfiguration<PartCatalogueTechnicalClass>
{
    public void Configure(EntityTypeBuilder<PartCatalogueTechnicalClass> b)
    {
        b.ToTable("PartCatalogueTechnicalClasses");
        b.HasKey(x => x.TechnicalClassId);
        b.Property(x => x.Code).HasMaxLength(30).IsRequired();
        b.Property(x => x.Description).HasMaxLength(200).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        b.HasIndex(x => x.Description);
    }
}

public class PartCatalogueCommodityGroupConfiguration : IEntityTypeConfiguration<PartCatalogueCommodityGroup>
{
    public void Configure(EntityTypeBuilder<PartCatalogueCommodityGroup> b)
    {
        b.ToTable("PartCatalogueCommodityGroups");
        b.HasKey(x => x.CommodityGroupId);
        b.Property(x => x.Code).HasMaxLength(30).IsRequired();
        b.Property(x => x.Description).HasMaxLength(200).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
    }
}

public class PartCatalogueProductFamilyConfiguration : IEntityTypeConfiguration<PartCatalogueProductFamily>
{
    public void Configure(EntityTypeBuilder<PartCatalogueProductFamily> b)
    {
        b.ToTable("PartCatalogueProductFamilies");
        b.HasKey(x => x.ProductFamilyId);
        b.Property(x => x.Code).HasMaxLength(30).IsRequired();
        b.Property(x => x.Description).HasMaxLength(200).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
    }
}

public class PartCatalogueSectionConfiguration : IEntityTypeConfiguration<PartCatalogueSection>
{
    public void Configure(EntityTypeBuilder<PartCatalogueSection> b)
    {
        b.ToTable("PartCatalogueSections");
        b.HasKey(x => x.SectionId);
        b.Property(x => x.Code).HasMaxLength(60).IsRequired();
        b.Property(x => x.NameFa).HasMaxLength(100);
        b.HasIndex(x => x.Code).IsUnique();
    }
}

public class PartCatalogueManufacturerConfiguration : IEntityTypeConfiguration<PartCatalogueManufacturer>
{
    public void Configure(EntityTypeBuilder<PartCatalogueManufacturer> b)
    {
        b.ToTable("PartCatalogueManufacturers");
        b.HasKey(x => x.ManufacturerId);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.NormalizedName).HasMaxLength(200).IsRequired();
        b.HasIndex(x => x.NormalizedName);
        b.HasIndex(x => x.Name);
    }
}

public class PartCatalogueSupplierConfiguration : IEntityTypeConfiguration<PartCatalogueSupplier>
{
    public void Configure(EntityTypeBuilder<PartCatalogueSupplier> b)
    {
        b.ToTable("PartCatalogueSuppliers");
        b.HasKey(x => x.SupplierId);
        b.Property(x => x.Name).HasMaxLength(300).IsRequired();
        b.Property(x => x.NormalizedName).HasMaxLength(300).IsRequired();
        b.HasIndex(x => x.NormalizedName);
        b.HasIndex(x => x.Name);
    }
}

public class PartCatalogueCurrencyConfiguration : IEntityTypeConfiguration<PartCatalogueCurrency>
{
    public void Configure(EntityTypeBuilder<PartCatalogueCurrency> b)
    {
        b.ToTable("PartCatalogueCurrencies");
        b.HasKey(x => x.CurrencyId);
        b.Property(x => x.Code).HasMaxLength(10).IsRequired();
        b.Property(x => x.Name).HasMaxLength(60).IsRequired();
        b.Property(x => x.NameFa).HasMaxLength(60);
        b.HasIndex(x => x.Code).IsUnique();
    }
}

public class PartCatalogueStockGradeConfiguration : IEntityTypeConfiguration<PartCatalogueStockGrade>
{
    public void Configure(EntityTypeBuilder<PartCatalogueStockGrade> b)
    {
        b.ToTable("PartCatalogueStockGrades");
        b.HasKey(x => x.StockGradeId);
        b.Property(x => x.Description).HasMaxLength(400).IsRequired();
        b.Property(x => x.DescriptionFa).HasMaxLength(400);
        b.HasIndex(x => x.Grade).IsUnique();
    }
}

public class PartCataloguePlanningMethodConfiguration : IEntityTypeConfiguration<PartCataloguePlanningMethod>
{
    public void Configure(EntityTypeBuilder<PartCataloguePlanningMethod> b)
    {
        b.ToTable("PartCataloguePlanningMethods");
        b.HasKey(x => x.PlanningMethodId);
        b.Property(x => x.Code).HasMaxLength(5).IsRequired();
        b.Property(x => x.Description).HasMaxLength(200);
        b.HasIndex(x => x.Code).IsUnique();
    }
}

public class PartCatalogueTypeDesignationConfiguration : IEntityTypeConfiguration<PartCatalogueTypeDesignation>
{
    public void Configure(EntityTypeBuilder<PartCatalogueTypeDesignation> b)
    {
        b.ToTable("PartCatalogueTypeDesignations");
        b.HasKey(x => x.TypeDesignationId);
        b.Property(x => x.Code).HasMaxLength(60).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
    }
}

public class PartCatalogueSectionCommodityGroupConfiguration : IEntityTypeConfiguration<PartCatalogueSectionCommodityGroup>
{
    public void Configure(EntityTypeBuilder<PartCatalogueSectionCommodityGroup> b)
    {
        b.ToTable("PartCatalogueSectionCommodityGroups");
        b.HasKey(x => x.SectionCommodityGroupId);
        b.HasOne(x => x.Section).WithMany().HasForeignKey(x => x.SectionId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.CommodityGroup).WithMany().HasForeignKey(x => x.CommodityGroupId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.SectionId, x.CommodityGroupId }).IsUnique();
    }
}

public class PartCatalogueCommodityGroupChildConfiguration : IEntityTypeConfiguration<PartCatalogueCommodityGroupChild>
{
    public void Configure(EntityTypeBuilder<PartCatalogueCommodityGroupChild> b)
    {
        b.ToTable("PartCatalogueCommodityGroupChildren");
        b.HasKey(x => x.CommodityGroupChildId);
        b.HasOne(x => x.ParentGroup).WithMany().HasForeignKey(x => x.ParentGroupId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.ChildGroup).WithMany().HasForeignKey(x => x.ChildGroupId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.ParentGroupId, x.ChildGroupId }).IsUnique();
    }
}

public class PartCatalogueWorkflowStepConfiguration : IEntityTypeConfiguration<PartCatalogueWorkflowStep>
{
    public void Configure(EntityTypeBuilder<PartCatalogueWorkflowStep> b)
    {
        b.ToTable("PartCatalogueWorkflowSteps");
        b.HasKey(x => x.WorkflowStepId);
        b.Property(x => x.StepKey).HasMaxLength(50).IsRequired();
        b.Property(x => x.NameEn).HasMaxLength(150).IsRequired();
        b.Property(x => x.NameFa).HasMaxLength(150).IsRequired();
        b.Property(x => x.Kind).HasMaxLength(20).IsRequired();
        b.Property(x => x.ApproverSource).HasMaxLength(30).IsRequired();
        b.Property(x => x.ConditionKey).HasMaxLength(40).IsRequired();
        b.HasIndex(x => x.StepKey).IsUnique();
        b.HasIndex(x => x.StepOrder);
    }
}

public class PartCatalogueApproverAssignmentConfiguration : IEntityTypeConfiguration<PartCatalogueApproverAssignment>
{
    public void Configure(EntityTypeBuilder<PartCatalogueApproverAssignment> b)
    {
        b.ToTable("PartCatalogueApproverAssignments");
        b.HasKey(x => x.ApproverAssignmentId);
        b.Property(x => x.StepKey).HasMaxLength(50).IsRequired();
        b.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.StepKey, x.EmployeeId }).IsUnique();
    }
}

public class PartCatalogueOrgLinkConfiguration : IEntityTypeConfiguration<PartCatalogueOrgLink>
{
    public void Configure(EntityTypeBuilder<PartCatalogueOrgLink> b)
    {
        b.ToTable("PartCatalogueOrgLinks");
        b.HasKey(x => x.OrgLinkId);
        b.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Manager).WithMany().HasForeignKey(x => x.ManagerEmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.EmployeeId).IsUnique();
    }
}

public class PartCatalogueRequestConfiguration : IEntityTypeConfiguration<PartCatalogueRequest>
{
    public void Configure(EntityTypeBuilder<PartCatalogueRequest> b)
    {
        b.ToTable("PartCatalogueRequests");
        b.HasKey(x => x.PartCatalogueRequestId);

        b.Property(x => x.RequestNumber).HasMaxLength(40);
        b.Property(x => x.RequestType).HasMaxLength(10).IsRequired();
        b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.Property(x => x.CurrentStepKey).HasMaxLength(50);
        b.HasIndex(x => x.WorkflowInstanceId);
        b.Property(x => x.RoutingBlockReason).HasMaxLength(100);
        b.Property(x => x.AssetType).HasMaxLength(20).IsRequired();
        b.Property(x => x.NewManufacturerName).HasMaxLength(200);
        b.Property(x => x.ManufacturerPartNumber).HasMaxLength(100);
        b.Property(x => x.SupplierPartNumber).HasMaxLength(100);
        b.Property(x => x.SparePartSerialNo).HasMaxLength(100);
        b.Property(x => x.EquipmentSerialNo).HasMaxLength(100);
        b.Property(x => x.ManufacturerDrawingNo).HasMaxLength(100);
        b.Property(x => x.DocumentNo).HasMaxLength(100);
        b.Property(x => x.StorageVolume).HasMaxLength(100);
        b.Property(x => x.ExistingPartNumber).HasMaxLength(50);
        b.Property(x => x.OldManufacturerName).HasMaxLength(200);
        b.Property(x => x.OldManufacturerPartNumber).HasMaxLength(100);
        b.Property(x => x.AssignedPartNumber).HasMaxLength(50);

        b.Property(x => x.PartPrice).HasColumnType("decimal(18,2)");
        b.Property(x => x.MinimumStock).HasColumnType("decimal(18,4)");
        b.Property(x => x.MaxConsumptionRate).HasColumnType("decimal(18,4)");
        b.Property(x => x.FirstPurchaseQuantity).HasColumnType("decimal(18,4)");
        b.Property(x => x.IssuedQty).HasColumnType("decimal(18,4)");

        // شماره‌ی درخواست تا قبل از کلیک «شماره‌دهی» Null است → ایندکس یکتای فیلترشده
        b.HasIndex(x => x.RequestNumber).IsUnique().HasFilter("[RequestNumber] IS NOT NULL");
        b.HasIndex(x => new { x.Status, x.Urgent, x.CreatedDate });
        b.HasIndex(x => x.RequesterEmployeeId);
        b.HasIndex(x => x.CurrentStepKey);
        b.HasIndex(x => x.SectionId);
        b.HasIndex(x => x.ExistingPartNumber);

        b.HasOne(x => x.Requester).WithMany().HasForeignKey(x => x.RequesterEmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Section).WithMany().HasForeignKey(x => x.SectionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.TechnicalClass).WithMany().HasForeignKey(x => x.TechnicalClassId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CommodityGroup1).WithMany().HasForeignKey(x => x.CommodityGroup1Id).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CommodityGroup2).WithMany().HasForeignKey(x => x.CommodityGroup2Id).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ProductFamily).WithMany().HasForeignKey(x => x.ProductFamilyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.UnitOfMeasure).WithMany().HasForeignKey(x => x.UnitOfMeasureId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Manufacturer).WithMany().HasForeignKey(x => x.ManufacturerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Currency).WithMany().HasForeignKey(x => x.CurrencyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.TypeDesignation).WithMany().HasForeignKey(x => x.TypeDesignationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.PlanningMethod).WithMany().HasForeignKey(x => x.PlanningMethodId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.StockGrade).WithMany().HasForeignKey(x => x.StockGradeId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PartCatalogueRequestEquipmentConfiguration : IEntityTypeConfiguration<PartCatalogueRequestEquipment>
{
    public void Configure(EntityTypeBuilder<PartCatalogueRequestEquipment> b)
    {
        b.ToTable("PartCatalogueRequestEquipment");
        b.HasKey(x => x.RequestEquipmentId);
        b.Property(x => x.EquipmentText).HasMaxLength(300);
        b.Property(x => x.InstalledQuantity).HasColumnType("decimal(18,4)");
        b.HasOne(x => x.Request).WithMany(r => r.Equipment).HasForeignKey(x => x.PartCatalogueRequestId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Equipment).WithMany().HasForeignKey(x => x.EquipmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.PartCatalogueRequestId);
    }
}

public class PartCatalogueRequestAttributeConfiguration : IEntityTypeConfiguration<PartCatalogueRequestAttribute>
{
    public void Configure(EntityTypeBuilder<PartCatalogueRequestAttribute> b)
    {
        b.ToTable("PartCatalogueRequestAttributes");
        b.HasKey(x => x.RequestAttributeId);
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.HasOne(x => x.Request).WithMany(r => r.Attributes).HasForeignKey(x => x.PartCatalogueRequestId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.PartCatalogueRequestId, x.Name });
    }
}

public class PartCatalogueRequestNoteConfiguration : IEntityTypeConfiguration<PartCatalogueRequestNote>
{
    public void Configure(EntityTypeBuilder<PartCatalogueRequestNote> b)
    {
        b.ToTable("PartCatalogueRequestNotes");
        b.HasKey(x => x.RequestNoteId);
        b.Property(x => x.Text).HasMaxLength(2000).IsRequired();
        b.Property(x => x.StepKey).HasMaxLength(50);
        b.HasOne(x => x.Request).WithMany(r => r.Notes).HasForeignKey(x => x.PartCatalogueRequestId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorEmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.PartCatalogueRequestId);
    }
}

public class PartCatalogueRequestAttachmentConfiguration : IEntityTypeConfiguration<PartCatalogueRequestAttachment>
{
    public void Configure(EntityTypeBuilder<PartCatalogueRequestAttachment> b)
    {
        b.ToTable("PartCatalogueRequestAttachments");
        b.HasKey(x => x.RequestAttachmentId);
        b.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        b.Property(x => x.StorageKey).HasMaxLength(300).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(150);
        b.Property(x => x.FileHash).HasMaxLength(100);
        b.Property(x => x.Kind).HasMaxLength(20).IsRequired();
        b.HasOne(x => x.Request).WithMany(r => r.Attachments).HasForeignKey(x => x.PartCatalogueRequestId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.PartCatalogueRequestId);
    }
}

public class PartCatalogueRequestStepConfiguration : IEntityTypeConfiguration<PartCatalogueRequestStep>
{
    public void Configure(EntityTypeBuilder<PartCatalogueRequestStep> b)
    {
        b.ToTable("PartCatalogueRequestSteps");
        b.HasKey(x => x.RequestStepId);
        b.Property(x => x.StepKey).HasMaxLength(50).IsRequired();
        b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.Property(x => x.SkipReason).HasMaxLength(200);
        b.HasOne(x => x.Request).WithMany(r => r.Steps).HasForeignKey(x => x.PartCatalogueRequestId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.PartCatalogueRequestId, x.StepOrder });
        // صف «در انتظار اقدام من»: گام Active با گیرنده‌ی مشخص
        b.HasIndex(x => new { x.Status, x.StepKey, x.AssignedEmployeeId });
    }
}

public class PartCatalogueRequestApprovalConfiguration : IEntityTypeConfiguration<PartCatalogueRequestApproval>
{
    public void Configure(EntityTypeBuilder<PartCatalogueRequestApproval> b)
    {
        b.ToTable("PartCatalogueRequestApprovals");
        b.HasKey(x => x.RequestApprovalId);
        b.Property(x => x.StepKey).HasMaxLength(50);
        b.Property(x => x.Action).HasMaxLength(30).IsRequired();
        b.Property(x => x.Comment).HasMaxLength(2000);
        b.Property(x => x.FromStatus).HasMaxLength(20);
        b.Property(x => x.ToStatus).HasMaxLength(20);
        b.HasOne(x => x.Request).WithMany(r => r.Approvals).HasForeignKey(x => x.PartCatalogueRequestId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Actor).WithMany().HasForeignKey(x => x.ActorEmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.PartCatalogueRequestId);
    }
}

public class WorkflowActorResponsibilityConfiguration : IEntityTypeConfiguration<WorkflowActorResponsibility>
{
    public void Configure(EntityTypeBuilder<WorkflowActorResponsibility> b)
    {
        b.ToTable("WorkflowActorResponsibilities");
        b.HasKey(x => x.WorkflowActorResponsibilityId);
        b.Property(x => x.ResponsibilityKey).HasMaxLength(100).IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        b.Property(x => x.DisplayNameFa).HasMaxLength(200);
        b.Property(x => x.Description).HasMaxLength(500);
        b.Property(x => x.Kind).HasMaxLength(30).IsRequired();
        b.Property(x => x.RequiredModule).HasMaxLength(60);
        b.Property(x => x.RequiredAction).HasMaxLength(60);
        b.Property(x => x.RoleName).HasMaxLength(100);
        b.Property(x => x.ScopeType).HasMaxLength(30).IsRequired();
        b.Property(x => x.ScopeValue).HasMaxLength(200);
        b.Property(x => x.SelectionStrategy).HasMaxLength(20).IsRequired();
        b.HasIndex(x => x.ResponsibilityKey).IsUnique();
    }
}

public class WorkflowActorResponsibilityPositionConfiguration : IEntityTypeConfiguration<WorkflowActorResponsibilityPosition>
{
    public void Configure(EntityTypeBuilder<WorkflowActorResponsibilityPosition> b)
    {
        b.ToTable("WorkflowActorResponsibilityPositions");
        b.HasKey(x => x.WorkflowActorResponsibilityPositionId);
        b.HasOne(x => x.Responsibility).WithMany(x => x.Positions).HasForeignKey(x => x.WorkflowActorResponsibilityId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Position).WithMany().HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.WorkflowActorResponsibilityId, x.PositionId }).IsUnique();
    }
}

public class CommodityGroupResponsibleConfiguration : IEntityTypeConfiguration<CommodityGroupResponsible>
{
    public void Configure(EntityTypeBuilder<CommodityGroupResponsible> b)
    {
        b.ToTable("CommodityGroupResponsibles");
        b.HasKey(x => x.CommodityGroupResponsibleId);
        b.Property(x => x.ResponsibilityKey).HasMaxLength(100).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(500);
        b.HasOne(x => x.CommodityGroup).WithMany().HasForeignKey(x => x.CommodityGroupId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Position).WithMany().HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.ResponsibilityKey, x.CommodityGroupId });
    }
}

/// <summary>تنظیمات سراسری ماژول (یک ردیف): روش تولید شماره‌ی قطعه و قالب آن.</summary>
public class PartCatalogueSettingsConfiguration : IEntityTypeConfiguration<PartCatalogueSettings>
{
    public void Configure(EntityTypeBuilder<PartCatalogueSettings> b)
    {
        b.ToTable("PartCatalogueSettings", t => t.HasCheckConstraint("CK_PartCatalogueSettings_Method", "[PartNoGenerationMethod] IN ('MANUAL','API')"));
        b.HasKey(x => x.PartCatalogueSettingsId);
        b.Property(x => x.SingletonKey).HasDefaultValue(1);
        b.HasIndex(x => x.SingletonKey).IsUnique();   // حداکثر یک ردیف تنظیمات
        b.Property(x => x.PartNoGenerationMethod).HasMaxLength(20).IsRequired().HasDefaultValue(PartNoGenerationMethods.Manual);
        b.Property(x => x.PartNoFormat).HasMaxLength(50);
    }
}

/// <summary>هدف قابل‌ویرایش شاخص‌های داشبورد مدیریتی (یک ردیف به‌ازای هر کلید KpiCatalog).</summary>
public class KpiTargetConfiguration : IEntityTypeConfiguration<KpiTarget>
{
    public void Configure(EntityTypeBuilder<KpiTarget> b)
    {
        b.ToTable("KpiTargets");
        b.HasKey(x => x.KpiTargetId);
        b.Property(x => x.KpiKey).HasMaxLength(60).IsRequired();
        b.HasIndex(x => x.KpiKey).IsUnique();
        b.Property(x => x.TargetValue).HasPrecision(18, 4);
        b.Property(x => x.WarnValue).HasPrecision(18, 4);
    }
}
