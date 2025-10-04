using Constants;
using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.OutputAdapters.DataAccess.Configurations;

public class ClubChallengeLinkConfiguration : IEntityTypeConfiguration<ClubChallengeLink>
{
    public void Configure(EntityTypeBuilder<ClubChallengeLink> builder)
    {
        // Configure the primary key to be id and be database generated
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .ValueGeneratedOnAdd();
        
        // Configure the difficulty to be a required string
        builder.Property(x => x.Difficulty)
            .IsRequired();
        
        // Configure the challenge id to have a max length
        builder.Property(x => x.ChallengeId)
            .IsRequired()
            .HasMaxLength(StringLengthConstants.GeoGuessrChallengeIdLength);
    }
}