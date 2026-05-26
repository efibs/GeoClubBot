using Constants;
using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.OutputAdapters.DataAccess.Configurations;

public class ClubChallengeLinkConfiguration : IEntityTypeConfiguration<ClubChallengeLink>
{
    public void Configure(EntityTypeBuilder<ClubChallengeLink> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .ValueGeneratedOnAdd();

        builder.Property(x => x.Difficulty)
            .IsRequired();

        builder.Property(x => x.ChallengeId)
            .IsRequired()
            .HasMaxLength(StringLengthConstants.GeoGuessrChallengeIdLength);

        builder.UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(x => x.DomainEvents);
    }
}
