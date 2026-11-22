using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.OutputAdapters.DataAccess.Configurations;

public class MissionBoardSubscriptionEntityConfiguration : IEntityTypeConfiguration<MissionBoardSubscription>
{
    public void Configure(EntityTypeBuilder<MissionBoardSubscription> builder)
    {
        // One subscription per member: the club follows their linked account.
        builder.HasKey(x => x.DiscordUserId);
        builder.Property(x => x.DiscordUserId).ValueGeneratedNever();
        builder.Property(x => x.Mode).IsRequired();
        builder.Property(x => x.SubscribedAt).IsRequired();

        builder.UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(x => x.DomainEvents);
    }
}
