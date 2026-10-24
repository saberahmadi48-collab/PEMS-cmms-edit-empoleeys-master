using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PEMS.Domain.Entities;

namespace PEMS.Persistence.Configurations;

public class LetterApprovalStepConfiguration : IEntityTypeConfiguration<LetterApprovalStep>
{
    public void Configure(EntityTypeBuilder<LetterApprovalStep> builder)
    {
        builder.HasOne(x => x.Letter).WithMany().HasForeignKey(x => x.LetterId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.LetterId, x.StepOrder }).IsUnique();
        builder.HasIndex(x => new { x.ApproverUserId, x.Status });
    }
}

public class LetterVersionConfiguration : IEntityTypeConfiguration<LetterVersion>
{
    public void Configure(EntityTypeBuilder<LetterVersion> builder)
    {
        builder.HasOne(x => x.Letter).WithMany().HasForeignKey(x => x.LetterId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.LetterId, x.VersionNo }).IsUnique();
    }
}

public class LetterViewLogConfiguration : IEntityTypeConfiguration<LetterViewLog>
{
    public void Configure(EntityTypeBuilder<LetterViewLog> builder)
    {
        builder.HasOne(x => x.Letter).WithMany().HasForeignKey(x => x.LetterId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.LetterId, x.UserId }).IsUnique();
    }
}

public class LetterNumberSequenceConfiguration : IEntityTypeConfiguration<LetterNumberSequence>
{
    public void Configure(EntityTypeBuilder<LetterNumberSequence> builder)
    {
        builder.HasIndex(x => new { x.Scope, x.Year }).IsUnique();
    }
}
