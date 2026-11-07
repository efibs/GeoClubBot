using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.OutputAdapters.DataAccess.Configurations;

public class ClubChatForwardEntityConfiguration : IEntityTypeConfiguration<ClubChatForward>
{
    public void Configure(EntityTypeBuilder<ClubChatForward> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .ValueGeneratedOnAdd();

        builder.Property(x => x.DiscordMessageId).IsRequired();
        builder.Property(x => x.ClubId).IsRequired();
        builder.Property(x => x.ForwardedByDiscordUserId).IsRequired();
        builder.Property(x => x.ForwardedAt).IsRequired();

        builder.UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(x => x.DomainEvents);

        // Not unique: forwarding a message again is allowed, it is only warned about.
        builder.HasIndex(x => new { x.DiscordMessageId, x.ClubId });
    }
}
