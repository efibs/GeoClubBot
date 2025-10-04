using Constants;
using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.OutputAdapters.DataAccess.Configurations;

public class GeoGuessrAccountLinkingRequestConfiguration : IEntityTypeConfiguration<GeoGuessrAccountLinkingRequest>
{
    public void Configure(EntityTypeBuilder<GeoGuessrAccountLinkingRequest> builder)
    {
        // Configure the composite primary key
        builder.HasKey(x => new { x.DiscordUserId, x.GeoGuessrUserId });
        
        // Set the key properties to not be database generated
        builder.Property(x => x.DiscordUserId)
            .IsRequired()
            .ValueGeneratedNever();
        builder.Property(x => x.GeoGuessrUserId)
            .IsRequired()
            .HasMaxLength(StringLengthConstants.GeoGuessrUserIdLength)
            .ValueGeneratedNever();
        
        // Configure the one time password property
        builder.Property(x => x.OneTimePassword)
            .IsRequired()
            .HasMaxLength(StringLengthConstants.AccountLinkingRequestOneTimePasswordLength);
    }
}