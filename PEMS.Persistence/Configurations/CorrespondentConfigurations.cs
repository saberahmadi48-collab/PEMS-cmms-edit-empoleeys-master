using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PEMS.Domain.Entities;

namespace PEMS.Persistence.Configurations;

public class CorrespondentPartyConfiguration : IEntityTypeConfiguration<CorrespondentParty>
{
    public void Configure(EntityTypeBuilder<CorrespondentParty> builder)
    {
        builder.HasOne(x => x.Vendor).WithMany().HasForeignKey(x => x.VendorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.Name);
        builder.HasIndex(x => x.VendorId);
    }
}

public class LetterLinkConfiguration : IEntityTypeConfiguration<LetterLink>
{
    public void Configure(EntityTypeBuilder<LetterLink> builder)
    {
        builder.HasOne(x => x.Letter).WithMany(x => x.Links).HasForeignKey(x => x.LetterId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.LetterId, x.LinkType, x.TargetId }).IsUnique();
        builder.HasIndex(x => new { x.LinkType, x.TargetId });
    }
}
