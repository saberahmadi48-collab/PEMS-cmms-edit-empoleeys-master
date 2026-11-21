using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PEMS.Domain.Entities;

namespace PEMS.Persistence.Configurations;

/// <summary>SCRUM-40: one scheme per document type.</summary>
public class NumberingSchemeConfiguration : IEntityTypeConfiguration<NumberingScheme>
{
    public void Configure(EntityTypeBuilder<NumberingScheme> builder)
    {
        builder.ToTable("NumberingSchemes");
        builder.HasKey(x => x.NumberingSchemeId);

        builder.Property(x => x.DocumentType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Pattern).HasMaxLength(60).IsRequired();
        builder.Property(x => x.Calendar).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ResetPeriod).HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(256);
        builder.Property(x => x.ModifiedBy).HasMaxLength(256);

        builder.HasIndex(x => x.DocumentType).IsUnique();
    }
}
