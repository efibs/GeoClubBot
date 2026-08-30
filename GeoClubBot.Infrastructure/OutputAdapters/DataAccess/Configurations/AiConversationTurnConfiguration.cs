using Constants;
using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.OutputAdapters.DataAccess.Configurations;

public class AiConversationTurnConfiguration : IEntityTypeConfiguration<AiConversationTurn>
{
    public void Configure(EntityTypeBuilder<AiConversationTurn> builder)
    {
        builder.HasKey(x => x.TurnId);
        builder.Property(x => x.TurnId).ValueGeneratedNever();

        // Unique because a Discord message maps to exactly one turn; this also makes recognising a
        // reply as a continuation a single indexed lookup.
        builder.HasIndex(x => x.DiscordMessageId).IsUnique();

        // Loading a whole conversation is the hot path for building context.
        builder.HasIndex(x => x.ConversationId);

        // Retention sweep.
        builder.HasIndex(x => x.CreatedAtUtc);

        // Per-user throttling counts recent turns by author.
        builder.HasIndex(x => new { x.AuthorDiscordUserId, x.CreatedAtUtc });

        builder.Property(x => x.DiscordMessageId).IsRequired();
        builder.Property(x => x.ConversationId).IsRequired();
        builder.Property(x => x.ChannelId).IsRequired();
        builder.Property(x => x.AuthorDiscordUserId).IsRequired();
        builder.Property(x => x.Depth).IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();

        builder.Property(x => x.Content)
            .IsRequired()
            .HasMaxLength(StringLengthConstants.AiConversationContentMaxLength);

        builder.Property(x => x.ModelId)
            .HasMaxLength(StringLengthConstants.AiModelIdMaxLength);

        // Stored as text so the value is readable in the database and survives an enum reordering.
        builder.Property(x => x.Role)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        // Bypass private setters so EF can hydrate from the database without going through
        // factory/behaviour methods.
        builder.UsePropertyAccessMode(PropertyAccessMode.Field);

        // Domain events live on BaseEntity but are not persisted.
        builder.Ignore(x => x.DomainEvents);
    }
}
