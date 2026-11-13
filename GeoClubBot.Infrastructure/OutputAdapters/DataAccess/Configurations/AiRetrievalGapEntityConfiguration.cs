using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.OutputAdapters.DataAccess.Configurations;

public class AiRetrievalGapEntityConfiguration : IEntityTypeConfiguration<AiRetrievalGap>
{
    public void Configure(EntityTypeBuilder<AiRetrievalGap> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();

        builder.Property(x => x.AskedAtUtc).IsRequired();
        builder.Property(x => x.Question).IsRequired().HasMaxLength(AiRetrievalGap.MaxQuestionLength);
        builder.Property(x => x.Country).HasMaxLength(128);
        builder.Property(x => x.Topic).HasMaxLength(64);
        builder.Property(x => x.Reason).IsRequired();

        builder.UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(x => x.DomainEvents);

        builder.HasIndex(x => x.AskedAtUtc);
    }
}
