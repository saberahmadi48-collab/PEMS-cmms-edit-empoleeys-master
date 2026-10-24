using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PEMS.Domain.Entities;

namespace PEMS.Persistence.Configurations;

/// <summary>
/// Task Manager: افزایشی روی TaskItem + جدول‌های فضا/وضعیت/نوع/Sprint/نظر/پیوند/ناظر. همه‌ی کلیدهای خارجی Restrict
/// (هیچ حذف آبشاری تاریخچه)؛ حذف در این ماژول نرم است.
/// </summary>
public class TaskItemTaskManagerConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        builder.Property(x => x.ItemKey).HasMaxLength(30);
        builder.HasIndex(x => x.ItemKey).IsUnique().HasFilter("[ItemKey] IS NOT NULL");
        builder.HasIndex(x => new { x.WorkSpaceId, x.IsActive, x.Rank });
        builder.HasIndex(x => x.SprintId);
        builder.HasIndex(x => x.ParentTaskItemId);
        builder.Property(x => x.StoryPoints).HasPrecision(9, 2);
        // ستون‌های موجود (Status/Priority/...) عمداً دست‌نخورده‌اند - Migration فقط افزایشی است
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        builder.HasOne(x => x.WorkSpace).WithMany().HasForeignKey(x => x.WorkSpaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.WorkItemType).WithMany().HasForeignKey(x => x.WorkItemTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.WorkItemStatus).WithMany().HasForeignKey(x => x.WorkItemStatusId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ReporterEmployee).WithMany().HasForeignKey(x => x.ReporterEmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ParentTaskItem).WithMany().HasForeignKey(x => x.ParentTaskItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Sprint).WithMany().HasForeignKey(x => x.SprintId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class WorkSpaceConfiguration : IEntityTypeConfiguration<WorkSpace>
{
    public void Configure(EntityTypeBuilder<WorkSpace> builder)
    {
        builder.ToTable("WorkSpaces");
        builder.HasIndex(x => x.Key).IsUnique();
        builder.Property(x => x.Key).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(150).IsRequired();
        builder.Property(x => x.NameFa).HasMaxLength(150);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.CreatedBy).HasMaxLength(200);
        builder.Property(x => x.ModifiedBy).HasMaxLength(200);
        builder.HasOne(x => x.OwnerDepartment).WithMany().HasForeignKey(x => x.OwnerDepartmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class WorkSpaceMemberConfiguration : IEntityTypeConfiguration<WorkSpaceMember>
{
    public void Configure(EntityTypeBuilder<WorkSpaceMember> builder)
    {
        builder.ToTable("WorkSpaceMembers");
        builder.HasIndex(x => new { x.WorkSpaceId, x.EmployeeId }).IsUnique();
        builder.Property(x => x.Role).HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(200);
        builder.Property(x => x.ModifiedBy).HasMaxLength(200);
        builder.HasOne(x => x.WorkSpace).WithMany(x => x.Members).HasForeignKey(x => x.WorkSpaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class WorkItemTypeConfiguration : IEntityTypeConfiguration<WorkItemType>
{
    public void Configure(EntityTypeBuilder<WorkItemType> builder)
    {
        builder.ToTable("WorkItemTypes");
        builder.HasIndex(x => new { x.WorkSpaceId, x.Name }).IsUnique();
        builder.Property(x => x.Name).HasMaxLength(60).IsRequired();
        builder.Property(x => x.NameFa).HasMaxLength(60);
        builder.Property(x => x.Icon).HasMaxLength(60).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(200);
        builder.Property(x => x.ModifiedBy).HasMaxLength(200);
        builder.HasOne(x => x.WorkSpace).WithMany(x => x.Types).HasForeignKey(x => x.WorkSpaceId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class WorkItemStatusConfiguration : IEntityTypeConfiguration<WorkItemStatus>
{
    public void Configure(EntityTypeBuilder<WorkItemStatus> builder)
    {
        builder.ToTable("WorkItemStatuses");
        builder.HasIndex(x => new { x.WorkSpaceId, x.Name }).IsUnique();
        builder.Property(x => x.Name).HasMaxLength(60).IsRequired();
        builder.Property(x => x.NameFa).HasMaxLength(60);
        builder.Property(x => x.Category).HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(200);
        builder.Property(x => x.ModifiedBy).HasMaxLength(200);
        builder.HasOne(x => x.WorkSpace).WithMany(x => x.Statuses).HasForeignKey(x => x.WorkSpaceId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class WorkItemStatusTransitionConfiguration : IEntityTypeConfiguration<WorkItemStatusTransition>
{
    public void Configure(EntityTypeBuilder<WorkItemStatusTransition> builder)
    {
        builder.ToTable("WorkItemStatusTransitions");
        builder.HasIndex(x => new { x.WorkSpaceId, x.FromStatusId, x.ToStatusId }).IsUnique();
        builder.HasOne(x => x.FromStatus).WithMany().HasForeignKey(x => x.FromStatusId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ToStatus).WithMany().HasForeignKey(x => x.ToStatusId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WorkSpace>().WithMany().HasForeignKey(x => x.WorkSpaceId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class SprintConfiguration : IEntityTypeConfiguration<Sprint>
{
    public void Configure(EntityTypeBuilder<Sprint> builder)
    {
        builder.ToTable("Sprints");
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Goal).HasMaxLength(500);
        builder.Property(x => x.State).HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(200);
        builder.Property(x => x.ModifiedBy).HasMaxLength(200);
        // حداکثر یک Sprint فعال در هر فضا - در خود دیتابیس
        builder.HasIndex(x => x.WorkSpaceId).IsUnique().HasFilter("[State] = N'Active'").HasDatabaseName("UX_Sprints_OneActivePerSpace");
        builder.HasIndex(x => new { x.WorkSpaceId, x.State });
        builder.HasOne(x => x.WorkSpace).WithMany().HasForeignKey(x => x.WorkSpaceId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class WorkItemCommentConfiguration : IEntityTypeConfiguration<WorkItemComment>
{
    public void Configure(EntityTypeBuilder<WorkItemComment> builder)
    {
        builder.ToTable("WorkItemComments");
        builder.HasIndex(x => new { x.TaskItemId, x.CreatedDate });
        builder.Property(x => x.Body).IsRequired();
        builder.Property(x => x.AuthorName).HasMaxLength(200);
        builder.Property(x => x.CreatedBy).HasMaxLength(200);
        builder.Property(x => x.ModifiedBy).HasMaxLength(200);
        builder.HasOne(x => x.TaskItem).WithMany().HasForeignKey(x => x.TaskItemId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class WorkItemLinkConfiguration : IEntityTypeConfiguration<WorkItemLink>
{
    public void Configure(EntityTypeBuilder<WorkItemLink> builder)
    {
        builder.ToTable("WorkItemLinks");
        builder.HasIndex(x => new { x.SourceTaskItemId, x.LinkType, x.TargetTaskItemId }).IsUnique();
        builder.HasIndex(x => x.TargetTaskItemId);
        builder.Property(x => x.LinkType).HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(200);
        builder.Property(x => x.ModifiedBy).HasMaxLength(200);
        builder.HasOne(x => x.SourceTaskItem).WithMany().HasForeignKey(x => x.SourceTaskItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.TargetTaskItem).WithMany().HasForeignKey(x => x.TargetTaskItemId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class WorkItemWatcherConfiguration : IEntityTypeConfiguration<WorkItemWatcher>
{
    public void Configure(EntityTypeBuilder<WorkItemWatcher> builder)
    {
        builder.ToTable("WorkItemWatchers");
        builder.HasIndex(x => new { x.TaskItemId, x.EmployeeId }).IsUnique();
        builder.HasIndex(x => x.EmployeeId);
        builder.HasOne(x => x.TaskItem).WithMany().HasForeignKey(x => x.TaskItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}
