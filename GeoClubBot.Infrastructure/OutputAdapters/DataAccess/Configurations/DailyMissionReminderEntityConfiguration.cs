using Constants;
using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.OutputAdapters.DataAccess.Configurations;

public class DailyMissionReminderEntityConfiguration : IEntityTypeConfiguration<DailyMissionReminder>
{
    public void Configure(EntityTypeBuilder<DailyMissionReminder> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(x => x.DiscordUserId)
            .IsRequired();

        // A user can have several reminders; index the owner so the per-user reads stay fast.
        builder.HasIndex(x => x.DiscordUserId);

        builder.Property(x => x.ReminderTimeUtc).IsRequired();

        builder.Property(x => x.TimeZoneId)
            .HasMaxLength(StringLengthConstants.TimeZoneIdMaxLength)
            .IsRequired(false);

        builder.Property(x => x.CustomMessage)
            .HasMaxLength(StringLengthConstants.DailyMissionReminderCustomMessageMaxLength)
            .IsRequired(false);

        builder.Property(x => x.LastSentDateUtc).IsRequired(false);

        builder.UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(x => x.DomainEvents);

        builder.HasIndex(x => new { x.ReminderTimeUtc, x.LastSentDateUtc });
    }
}
