using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PEMS.Domain.Entities;

namespace PEMS.Persistence.Configurations;

/// <summary>
/// پیکربندی RCM کلاس‌محور: الگوی استراتژی (کلاس/حالت خرابی/نسخه) + تخصیص به تگ + تاریخچه.
/// همه‌ی FK ها Restrict (قرارداد کل کدبیس) و رکوردهای تاریخچه هرگز حذف نمی‌شوند.
/// </summary>
public class MaintenanceStrategyRcmConfiguration : IEntityTypeConfiguration<MaintenanceStrategy>
{
    public void Configure(EntityTypeBuilder<MaintenanceStrategy> builder)
    {
        builder.Property(x => x.Status).HasMaxLength(20).HasDefaultValue("Draft");
        builder.Property(x => x.Title).HasMaxLength(200);
        builder.Property(x => x.FunctionalFailure).HasMaxLength(500);
        builder.Property(x => x.FailureEffect).HasMaxLength(500);
        builder.Property(x => x.TaskType).HasMaxLength(30);
        builder.Property(x => x.Source).HasMaxLength(30);
        builder.Property(x => x.CbmParameter).HasMaxLength(100);
        builder.Property(x => x.ThresholdUnit).HasMaxLength(30);
        builder.Property(x => x.RequiredSkills).HasMaxLength(500);
        builder.Property(x => x.ApprovedBy).HasMaxLength(200);
        builder.Property(x => x.EstimatedDurationHours).HasPrecision(9, 2);
        builder.Property(x => x.AlarmThreshold).HasPrecision(18, 4);
        builder.Property(x => x.TripThreshold).HasPrecision(18, 4);

        builder.HasOne(x => x.EquipmentClass).WithMany()
            .HasForeignKey(x => x.EquipmentClassId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.FailureMode).WithMany()
            .HasForeignKey(x => x.FailureModeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.FailureCause).WithMany()
            .HasForeignKey(x => x.FailureCauseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.OwnerDiscipline).WithMany()
            .HasForeignKey(x => x.OwnerDisciplineId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.EquipmentClassId).HasDatabaseName("IX_MaintenanceStrategies_EquipmentClassId");
        builder.HasIndex(x => x.FamilyId).HasDatabaseName("IX_MaintenanceStrategies_FamilyId");
        builder.HasIndex(x => new { x.Status, x.IsActive }).HasDatabaseName("IX_MaintenanceStrategies_Status_IsActive");
    }
}

public class MaintenanceStrategyAssignmentConfiguration : IEntityTypeConfiguration<MaintenanceStrategyAssignment>
{
    public void Configure(EntityTypeBuilder<MaintenanceStrategyAssignment> builder)
    {
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.OverrideStatus).HasMaxLength(20).IsRequired();

        builder.HasOne(x => x.MaintenanceStrategy).WithMany(x => x.Assignments)
            .HasForeignKey(x => x.MaintenanceStrategyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Equipment).WithMany()
            .HasForeignKey(x => x.EquipmentId).OnDelete(DeleteBehavior.Restrict);

        // یک تگ برای یک نسخه‌ی استراتژی فقط یک تخصیص فعال دارد
        builder.HasIndex(x => new { x.MaintenanceStrategyId, x.EquipmentId })
            .IsUnique()
            .HasFilter("[IsActive] = 1")
            .HasDatabaseName("UX_MaintenanceStrategyAssignments_Strategy_Equipment_Active");
        builder.HasIndex(x => x.EquipmentId).HasDatabaseName("IX_MaintenanceStrategyAssignments_EquipmentId");
    }
}

public class MaintenanceStrategyHistoryConfiguration : IEntityTypeConfiguration<MaintenanceStrategyHistory>
{
    public void Configure(EntityTypeBuilder<MaintenanceStrategyHistory> builder)
    {
        builder.HasOne(x => x.MaintenanceStrategy).WithMany()
            .HasForeignKey(x => x.MaintenanceStrategyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.MaintenanceStrategyId, x.Timestamp })
            .HasDatabaseName("IX_MaintenanceStrategyHistories_Strategy_Timestamp");
    }
}

public class MaintenancePlanRcmConfiguration : IEntityTypeConfiguration<MaintenancePlan>
{
    public void Configure(EntityTypeBuilder<MaintenancePlan> builder)
    {
        builder.Property(x => x.ScheduleMode).HasMaxLength(10).HasDefaultValue("Fixed");

        builder.HasOne(x => x.MaintenanceStrategyAssignment).WithMany()
            .HasForeignKey(x => x.MaintenanceStrategyAssignmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.MaintenanceStrategyAssignmentId)
            .HasDatabaseName("IX_MaintenancePlans_MaintenanceStrategyAssignmentId");
    }
}
