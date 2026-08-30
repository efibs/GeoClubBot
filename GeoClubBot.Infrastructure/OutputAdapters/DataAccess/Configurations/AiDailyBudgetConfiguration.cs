using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.OutputAdapters.DataAccess.Configurations;

public class AiDailyBudgetConfiguration : IEntityTypeConfiguration<AiDailyBudget>
{
    public void Configure(EntityTypeBuilder<AiDailyBudget> builder)
    {
        // The UTC date is the natural key, and the atomic reservation statement relies on it being
        // the conflict target of an ON CONFLICT upsert.
        builder.HasKey(x => x.DateUtc);
        builder.Property(x => x.DateUtc)
            .ValueGeneratedNever();

        builder.Property(x => x.RequestCount).IsRequired();
        builder.Property(x => x.PromptTokens).IsRequired();
        builder.Property(x => x.CompletionTokens).IsRequired();

        // Bypass private setters so EF can hydrate from the database without going through
        // factory/behaviour methods.
        builder.UsePropertyAccessMode(PropertyAccessMode.Field);

        // Domain events live on BaseEntity but are not persisted.
        builder.Ignore(x => x.DomainEvents);
    }
}
