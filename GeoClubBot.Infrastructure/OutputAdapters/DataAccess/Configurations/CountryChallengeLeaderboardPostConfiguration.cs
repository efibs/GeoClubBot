using Constants;
using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.OutputAdapters.DataAccess.Configurations;

public class CountryChallengeLeaderboardPostConfiguration : IEntityTypeConfiguration<CountryChallengeLeaderboardPost>
{
    public void Configure(EntityTypeBuilder<CountryChallengeLeaderboardPost> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .ValueGeneratedOnAdd();

        builder.Property(x => x.Date).IsRequired();

        builder.Property(x => x.Season)
            .IsRequired()
            .HasMaxLength(StringLengthConstants.CountryChallengeSeasonMaxLength);

        builder.Property(x => x.PostedAt).IsRequired();

        builder.UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(x => x.DomainEvents);

        builder.HasIndex(x => x.Date).IsUnique();
    }
}
