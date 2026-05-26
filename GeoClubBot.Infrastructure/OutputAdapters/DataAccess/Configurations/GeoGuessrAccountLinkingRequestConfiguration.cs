using Constants;
using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.OutputAdapters.DataAccess.Configurations;

public class GeoGuessrAccountLinkingRequestConfiguration : IEntityTypeConfiguration<GeoGuessrAccountLinkingRequest>
{
    public void Configure(EntityTypeBuilder<GeoGuessrAccountLinkingRequest> builder)
    {
        builder.HasKey(x => new { x.DiscordUserId, x.GeoGuessrUserId });

        builder.Property(x => x.DiscordUserId)
            .IsRequired()
            .ValueGeneratedNever();
        builder.Property(x => x.GeoGuessrUserId)
            .IsRequired()
            .HasMaxLength(StringLengthConstants.GeoGuessrUserIdLength)
            .ValueGeneratedNever();

        builder.Property(x => x.OneTimePassword)
            .IsRequired()
            .HasMaxLength(StringLengthConstants.AccountLinkingRequestOneTimePasswordLength);

        builder.UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(x => x.DomainEvents);
    }
}
