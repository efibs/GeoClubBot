using Constants;
using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.OutputAdapters.DataAccess.Configurations;

public class AiFeedbackTurnConfiguration : IEntityTypeConfiguration<AiFeedbackTurn>
{
    public void Configure(EntityTypeBuilder<AiFeedbackTurn> builder)
    {
        builder.HasKey(x => x.FeedbackTurnId);
        builder.Property(x => x.FeedbackTurnId).ValueGeneratedNever();

        // Declared from the child side, as ClubMemberStrike does. Cascade because a transcript has no
        // meaning without the verdict it was archived for: withdrawing feedback must take the stored
        // conversation with it.
        builder.HasOne<AiAnswerFeedback>()
            .WithMany(x => x.Turns)
            .HasForeignKey(x => x.FeedbackId)
            .OnDelete(DeleteBehavior.Cascade);

        // Loading a transcript in order is the only way these rows are ever read.
        builder.HasIndex(x => new { x.FeedbackId, x.Ordinal }).IsUnique();

        builder.Property(x => x.Ordinal).IsRequired();
        builder.Property(x => x.AuthorDiscordUserId).IsRequired();
        builder.Property(x => x.DiscordMessageId).IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();

        builder.Property(x => x.Content)
            .IsRequired()
            .HasMaxLength(StringLengthConstants.AiConversationContentMaxLength);

        builder.Property(x => x.ModelId).HasMaxLength(StringLengthConstants.AiModelIdMaxLength);

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
