using Constants;
using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.OutputAdapters.DataAccess.Configurations;

public class DailyMissionReminderEntityConfiguration : IEntityTypeConfiguration<DailyMissionReminder>
{
    public void Configure(EntityTypeBuilder<DailyMissionReminder> builder)
    {
        builder.HasKey(x => x.DiscordUserId);
        builder.Property(x => x.DiscordUserId)
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(x => x.ReminderTimeUtc)
            .IsRequired();

        builder.Property(x => x.TimeZoneId)
            .HasMaxLength(StringLengthConstants.TimeZoneIdMaxLength)
            .IsRequired(false);

        builder.Property(x => x.CustomMessage)
            .HasMaxLength(StringLengthConstants.DailyMissionReminderCustomMessageMaxLength)
            .IsRequired(false);

        builder.Property(x => x.LastSentDateUtc)
            .IsRequired(false);

        builder.HasIndex(x => x.ReminderTimeUtc);
    }
}
