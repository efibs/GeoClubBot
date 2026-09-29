using Constants;
using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.OutputAdapters.DataAccess.Configurations;

public class ClubMemberDailyActivityEntityConfiguration : IEntityTypeConfiguration<ClubMemberDailyActivity>
{
    public void Configure(EntityTypeBuilder<ClubMemberDailyActivity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .ValueGeneratedOnAdd();

        builder.Property(x => x.ClubId).IsRequired();

        builder.Property(x => x.UserId)
            .IsRequired()
            .HasMaxLength(StringLengthConstants.GeoGuessrUserIdLength);

        builder.Property(x => x.Date).IsRequired();
        builder.Property(x => x.LegacyDailyMissionCount).IsRequired();

        // Nullable on purpose: rows predating daily-challenge tracking mean "unknown", not "zero".
        builder.Property(x => x.DailyChallengeCount);

        builder.Property(x => x.BoardMissionCount).IsRequired().HasDefaultValue(0);
        builder.Property(x => x.BoardClearBonusCount).IsRequired().HasDefaultValue(0);

        // Nullable on purpose: rows predating XP tracking mean "unknown", not "zero".
        builder.Property(x => x.Xp);

        builder.Ignore(x => x.ExtendsStreak);
        builder.UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(x => x.DomainEvents);

        builder.HasIndex(x => new { x.ClubId, x.Date, x.UserId }).IsUnique();
    }
}
