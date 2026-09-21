using Constants;
using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.OutputAdapters.DataAccess.Configurations;

public class AiAnswerFeedbackConfiguration : IEntityTypeConfiguration<AiAnswerFeedback>
{
    public void Configure(EntityTypeBuilder<AiAnswerFeedback> builder)
    {
        builder.HasKey(x => x.FeedbackId);
        builder.Property(x => x.FeedbackId).ValueGeneratedNever();

        // One verdict per person per answer. Several people rating the same answer is more signal;
        // the same person rating twice is a correction, and this is what turns the second reaction
        // into an update instead of a duplicate row. It is also the lookup both handlers do.
        builder.HasIndex(x => new { x.RatedDiscordMessageId, x.ReviewerDiscordUserId }).IsUnique();

        // Export and the optional retention sweep both window on time.
        builder.HasIndex(x => x.CreatedAtUtc);

        // The summary splits by verdict.
        builder.HasIndex(x => x.Rating);

        builder.HasIndex(x => x.ConversationId);

        builder.Property(x => x.RatedDiscordMessageId).IsRequired();
        builder.Property(x => x.ConversationId).IsRequired();
        builder.Property(x => x.ChannelId).IsRequired();
        builder.Property(x => x.ReviewerDiscordUserId).IsRequired();
        builder.Property(x => x.AnswerDepth).IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        // Matches the clamp inside the entity, which cannot reference the Constants project.
        builder.Property(x => x.Comment).HasMaxLength(StringLengthConstants.AiFeedbackCommentMaxLength);

        builder.Property(x => x.ModelId).HasMaxLength(StringLengthConstants.AiModelIdMaxLength);

        // Stored as text so the value is readable in the database and survives an enum reordering.
        builder.Property(x => x.Rating)
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
