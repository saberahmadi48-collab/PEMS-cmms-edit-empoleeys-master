using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PEMS.Domain.Entities;

namespace PEMS.Persistence.Configurations;

public class LetterTemplateConfiguration : IEntityTypeConfiguration<LetterTemplate>
{
    public void Configure(EntityTypeBuilder<LetterTemplate> builder)
    {
        builder.HasIndex(x => new { x.IsActive, x.LetterType });
        builder.HasIndex(x => x.Name);
    }
}
