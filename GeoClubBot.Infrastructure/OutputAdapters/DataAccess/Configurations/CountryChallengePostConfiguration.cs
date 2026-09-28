using Constants;
using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.OutputAdapters.DataAccess.Configurations;

public class CountryChallengePostConfiguration : IEntityTypeConfiguration<CountryChallengePost>
{
    public void Configure(EntityTypeBuilder<CountryChallengePost> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .ValueGeneratedOnAdd();

        builder.Property(x => x.ChallengeName)
            .IsRequired()
            .HasMaxLength(StringLengthConstants.CountryChallengeNameMaxLength);

        builder.Property(x => x.Date).IsRequired();

        builder.Property(x => x.Country)
            .IsRequired()
            .HasMaxLength(StringLengthConstants.CountryChallengeCountryNameMaxLength);

        builder.Property(x => x.CountryCode)
            .HasMaxLength(StringLengthConstants.CountryChallengeCountryCodeLength);

        builder.Property(x => x.MapId)
            .IsRequired()
            .HasMaxLength(StringLengthConstants.GeoGuessrMapIdMaxLength);

        builder.Property(x => x.MapName)
            .HasMaxLength(StringLengthConstants.GeoGuessrMapNameMaxLength);

        builder.Property(x => x.ChallengeId)
            .IsRequired()
            .HasMaxLength(StringLengthConstants.GeoGuessrChallengeIdLength);

        builder.Property(x => x.ChannelId).IsRequired();
        builder.Property(x => x.PostedAt).IsRequired();

        builder.UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(x => x.DomainEvents);

        // A challenge is posted at most once a day, even when a manual run races the scheduled one.
        builder.HasIndex(x => new { x.ChallengeName, x.Date }).IsUnique();
    }
}
