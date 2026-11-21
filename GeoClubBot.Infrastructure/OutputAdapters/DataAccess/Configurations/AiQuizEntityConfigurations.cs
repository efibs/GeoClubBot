using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.OutputAdapters.DataAccess.Configurations;

public class AiQuizItemEntityConfiguration : IEntityTypeConfiguration<AiQuizItem>
{
    public void Configure(EntityTypeBuilder<AiQuizItem> builder)
    {
        builder.HasKey(x => x.ChunkId);
        builder.Property(x => x.ChunkId).ValueGeneratedNever();

        builder.Property(x => x.Version).IsRequired();
        builder.Property(x => x.ContentHash).IsRequired().HasMaxLength(64);
        builder.Property(x => x.Country).HasMaxLength(128);
        builder.Property(x => x.SourceType).HasMaxLength(64);
        builder.Property(x => x.ModelId).HasMaxLength(256);
        builder.Property(x => x.RatedAtUtc).IsRequired();

        builder.HasMany(x => x.Questions)
            .WithOne()
            .HasForeignKey(x => x.ChunkId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(x => x.DomainEvents);

        // What a quiz filters by: a country's pictures of the current generation.
        builder.HasIndex(x => new { x.Version, x.Country });
    }
}

public class AiQuizItemQuestionEntityConfiguration : IEntityTypeConfiguration<AiQuizItemQuestion>
{
    public void Configure(EntityTypeBuilder<AiQuizItemQuestion> builder)
    {
        builder.HasKey(x => new { x.ChunkId, x.Kind });

        // Stored as text so the value is readable in the database and survives an enum reordering.
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Difficulty).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Answer).IsRequired().HasMaxLength(128);
        builder.Property(x => x.Decoys).IsRequired();

        builder.UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => new { x.Kind, x.Difficulty });
    }
}

public class AiQuizRoundEntityConfiguration : IEntityTypeConfiguration<AiQuizRound>
{
    public void Configure(EntityTypeBuilder<AiQuizRound> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.ChunkId).IsRequired();
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Difficulty).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.AskedDifficulty).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Options).IsRequired();
        builder.Property(x => x.CorrectIndex).IsRequired();
        builder.Property(x => x.CountryName).HasMaxLength(128);
        builder.Property(x => x.Guide).IsRequired().HasMaxLength(512);
        builder.Property(x => x.Section).IsRequired().HasMaxLength(1024);
        builder.Property(x => x.Explanation).IsRequired().HasMaxLength(2048);
        builder.Property(x => x.SourceUrl).IsRequired().HasMaxLength(2048);
        builder.Property(x => x.ImageUrl).IsRequired().HasMaxLength(2048);
        builder.Property(x => x.AskedCountry).HasMaxLength(100);
        builder.Property(x => x.ChannelId).IsRequired();
        builder.Property(x => x.AskedByUserId).IsRequired();
        builder.Property(x => x.AskedAtUtc).IsRequired();
        builder.Property(x => x.RevealAtUtc).IsRequired();

        builder.HasMany(x => x.Guesses)
            .WithOne()
            .HasForeignKey(x => x.RoundId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(x => x.IsRevealed);
        builder.Ignore(x => x.CorrectOption);
        builder.Ignore(x => x.RightGuesses);

        builder.UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(x => x.DomainEvents);

        // What the reveal job reads every minute, and what puts pictures asked long ago first.
        builder.HasIndex(x => new { x.RevealedAtUtc, x.RevealAtUtc });
        builder.HasIndex(x => x.ChunkId);
    }
}

public class AiQuizGuessEntityConfiguration : IEntityTypeConfiguration<AiQuizGuess>
{
    public void Configure(EntityTypeBuilder<AiQuizGuess> builder)
    {
        // One guess each, held by the key itself.
        builder.HasKey(x => new { x.RoundId, x.UserId });

        builder.Property(x => x.OptionIndex).IsRequired();
        builder.Property(x => x.Correct).IsRequired();
        builder.Property(x => x.Points).IsRequired();
        builder.Property(x => x.AnsweredAtUtc).IsRequired();

        builder.UsePropertyAccessMode(PropertyAccessMode.Field);

        // What the leaderboard groups.
        builder.HasIndex(x => new { x.AnsweredAtUtc, x.UserId });
    }
}
