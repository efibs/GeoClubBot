using Constants;
using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.OutputAdapters.DataAccess.Configurations;

public class KnowledgeSourceConfiguration : IEntityTypeConfiguration<KnowledgeSource>
{
    public void Configure(EntityTypeBuilder<KnowledgeSource> builder)
    {
        builder.HasKey(x => x.SourceId);
        builder.Property(x => x.SourceId).ValueGeneratedNever();

        // A source is identified by its family plus its key within that family; the pair must be
        // unique or a sync would insert a second copy of the same document on every run.
        builder.HasIndex(x => new { x.SourceType, x.NaturalKey }).IsUnique();

        // Drives the "what should I ingest next" query.
        builder.HasIndex(x => new { x.Status, x.LastAttemptedAtUtc });

        builder.Property(x => x.SourceType)
            .IsRequired()
            .HasMaxLength(StringLengthConstants.KnowledgeSourceTypeMaxLength);

        builder.Property(x => x.NaturalKey)
            .IsRequired()
            .HasMaxLength(StringLengthConstants.KnowledgeSourceNaturalKeyMaxLength);

        builder.Property(x => x.Url)
            .IsRequired()
            .HasMaxLength(StringLengthConstants.KnowledgeSourceUrlMaxLength);

        builder.Property(x => x.Title).HasMaxLength(StringLengthConstants.KnowledgeSourceTitleMaxLength);
        builder.Property(x => x.Country).HasMaxLength(StringLengthConstants.KnowledgeSourceTitleMaxLength);
        builder.Property(x => x.Continent).HasMaxLength(StringLengthConstants.KnowledgeSourceTitleMaxLength);
        builder.Property(x => x.Author).HasMaxLength(StringLengthConstants.KnowledgeSourceTitleMaxLength);

        // Matches the clamp inside the entity, which cannot reference the Constants project.
        builder.Property(x => x.StatusReason)
            .HasMaxLength(StringLengthConstants.KnowledgeSourceStatusReasonMaxLength);

        builder.Property(x => x.ContentHash).HasMaxLength(64);

        // Stored as text so values stay readable and survive an enum reordering.
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Origin).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.Property(x => x.FirstSeenAtUtc).IsRequired();

        // Bypass private setters so EF can hydrate from the database without going through
        // factory/behaviour methods.
        builder.UsePropertyAccessMode(PropertyAccessMode.Field);

        // Domain events live on BaseEntity but are not persisted.
        builder.Ignore(x => x.DomainEvents);
    }
}
