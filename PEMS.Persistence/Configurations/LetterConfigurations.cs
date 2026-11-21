using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PEMS.Domain.Entities;

namespace PEMS.Persistence.Configurations;

/// <summary>
/// پیکربندی ماژول مکاتبات رسمی (Letter, LetterReferral, Notification، و FK جدید روی
/// DocumentAttachment) - طبق استاندارد جدید (Volume V): یک کلاس به‌ازای هر Entity.
/// </summary>
public class LetterConfiguration : IEntityTypeConfiguration<Letter>
{
    public void Configure(EntityTypeBuilder<Letter> builder)
    {
        builder.HasOne(x => x.Project)
            .WithMany()
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Discipline)
            .WithMany()
            .HasForeignKey(x => x.DisciplineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ReceiverDepartment)
            .WithMany()
            .HasForeignKey(x => x.ReceiverDepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ReceiverEmployee)
            .WithMany()
            .HasForeignKey(x => x.ReceiverEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.RelatedDocument)
            .WithMany()
            .HasForeignKey(x => x.RelatedDocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.RelatedMR)
            .WithMany()
            .HasForeignKey(x => x.RelatedMRId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ParentLetter)
            .WithMany()
            .HasForeignKey(x => x.ParentLetterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.CorrespondentParty).WithMany().HasForeignKey(x => x.CorrespondentPartyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Contract).WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);

        // جست‌وجوی ثبت تکراری (طرف مکاتبه + شماره‌ی نامه‌ی فرستنده) و رشته‌ی مکاتبه‌ی یک طرف؛ عمداً یکتا نیست (داده‌ی قدیمی ممکن است تکرار داشته باشد)
        builder.HasIndex(x => new { x.CorrespondentPartyId, x.SenderReference });
        builder.HasIndex(x => x.LetterNumber);
        builder.HasIndex(x => new { x.Status, x.IsActive });
        builder.HasIndex(x => x.RegisteredById);
        builder.HasIndex(x => x.SenderEmployeeId);

        // طبق بهینه‌سازی عملکرد: این‌ها دقیقاً همان فیلدهایی هستند که SearchAsync همیشه
        // با آن‌ها فیلتر/مرتب‌سازی می‌کند
        builder.HasIndex(x => new { x.IsActive, x.LetterDate });
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.LetterType);
    }
}

public class LetterReferralConfiguration : IEntityTypeConfiguration<LetterReferral>
{
    public void Configure(EntityTypeBuilder<LetterReferral> builder)
    {
        builder.HasOne(x => x.Letter)
            .WithMany(x => x.Referrals)
            .HasForeignKey(x => x.LetterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.ReferredToEmployee)
            .WithMany()
            .HasForeignKey(x => x.ReferredToEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ReferredToDepartment)
            .WithMany()
            .HasForeignKey(x => x.ReferredToDepartmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// طبق «PEMS Letter Details — Controlled Lifecycle» بند ۱۳: Restrict (نه Cascade) طبق قرارداد
/// این نشست - حذف یک نامه نباید تاریخچه‌ی تصمیمات بازبینی‌اش را خودکار پاک کند؛ IsActive برای
/// حذف نرم به‌کار می‌رود.
/// </summary>
public class LetterReviewConfiguration : IEntityTypeConfiguration<LetterReview>
{
    public void Configure(EntityTypeBuilder<LetterReview> builder)
    {
        builder.HasOne(x => x.Letter)
            .WithMany(x => x.Reviews)
            .HasForeignKey(x => x.LetterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.LetterId, x.CreatedDate });
    }
}

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.HasIndex(x => new { x.UserId, x.IsRead });

        // صندوق فعال (DismissedDate IS NULL) مرتب بر اساس تاریخ؛ (UserId,IsRead) برای شمارنده کافی است
        builder.HasIndex(x => new { x.UserId, x.DismissedDate, x.CreatedDate });
    }
}

public class DocumentAttachmentLetterConfiguration : IEntityTypeConfiguration<DocumentAttachment>
{
    public void Configure(EntityTypeBuilder<DocumentAttachment> builder)
    {
        builder.HasOne(x => x.Letter)
            .WithMany(x => x.Attachments)
            .HasForeignKey(x => x.LetterId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class DocumentAttachmentTechnicalQueryConfiguration : IEntityTypeConfiguration<DocumentAttachment>
{
    public void Configure(EntityTypeBuilder<DocumentAttachment> builder)
    {
        builder.HasOne(x => x.TechnicalQuery)
            .WithMany(x => x.Attachments)
            .HasForeignKey(x => x.TechnicalQueryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class DocumentAttachmentMOCConfiguration : IEntityTypeConfiguration<DocumentAttachment>
{
    public void Configure(EntityTypeBuilder<DocumentAttachment> builder)
    {
        builder.HasOne(x => x.MOC)
            .WithMany(x => x.Attachments)
            .HasForeignKey(x => x.MOCId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
