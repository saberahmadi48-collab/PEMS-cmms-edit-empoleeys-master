using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PEMS.Domain.Entities;

namespace PEMS.Persistence.Configurations;

public class LetterUserStateConfiguration : IEntityTypeConfiguration<LetterUserState>
{
    public void Configure(EntityTypeBuilder<LetterUserState> builder)
    {
        builder.HasIndex(x => new { x.LetterId, x.ApplicationUserId }).IsUnique();
        builder.HasIndex(x => new { x.ApplicationUserId, x.IsTrashed });
        builder.HasOne(x => x.Letter).WithMany().HasForeignKey(x => x.LetterId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class LetterDelegationConfiguration : IEntityTypeConfiguration<LetterDelegation>
{
    public void Configure(EntityTypeBuilder<LetterDelegation> builder)
    {
        builder.HasIndex(x => new { x.DelegatorUserId, x.IsActive });
        builder.HasIndex(x => new { x.DelegateUserId, x.IsActive });
    }
}

public class LetterRoutingRuleConfiguration : IEntityTypeConfiguration<LetterRoutingRule>
{
    public void Configure(EntityTypeBuilder<LetterRoutingRule> builder)
    {
        builder.HasIndex(x => new { x.OwnerUserId, x.IsActive, x.SortOrder });
    }
}

public class LetterContactGroupConfiguration : IEntityTypeConfiguration<LetterContactGroup>
{
    public void Configure(EntityTypeBuilder<LetterContactGroup> builder)
    {
        builder.HasIndex(x => new { x.OwnerUserId, x.IsActive });
        builder.HasMany(x => x.Members).WithOne(x => x.Group).HasForeignKey(x => x.LetterContactGroupId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class LetterContactGroupMemberConfiguration : IEntityTypeConfiguration<LetterContactGroupMember>
{
    public void Configure(EntityTypeBuilder<LetterContactGroupMember> builder)
    {
        builder.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CorrespondentParty).WithMany().HasForeignKey(x => x.CorrespondentPartyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.LetterContactGroupId, x.EmployeeId, x.CorrespondentPartyId }).IsUnique();
    }
}

public class LetterReminderConfiguration : IEntityTypeConfiguration<LetterReminder>
{
    public void Configure(EntityTypeBuilder<LetterReminder> builder)
    {
        builder.HasIndex(x => new { x.OwnerUserId, x.IsDone, x.RemindAt });
        builder.HasOne(x => x.Letter).WithMany().HasForeignKey(x => x.LetterId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class LetterUserSettingConfiguration : IEntityTypeConfiguration<LetterUserSetting>
{
    public void Configure(EntityTypeBuilder<LetterUserSetting> builder)
    {
        builder.HasIndex(x => x.ApplicationUserId).IsUnique();
    }
}

public class TagTreeConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.HasOne(x => x.ParentTag).WithMany().HasForeignKey(x => x.ParentTagId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.OwnerUserId, x.ParentTagId });
    }
}
