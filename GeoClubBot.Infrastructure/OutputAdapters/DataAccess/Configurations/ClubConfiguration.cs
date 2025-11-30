using Constants;
using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.OutputAdapters.DataAccess.Configurations;

public class ClubConfiguration : IEntityTypeConfiguration<Club>
{
    public void Configure(EntityTypeBuilder<Club> builder)
    {
        // Configure the primary key to be the club id
        builder.HasKey(c => c.ClubId);
        builder.Property(c => c.ClubId)
            .ValueGeneratedNever();
        
        // Configure the name to be required and set max length
        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(StringLengthConstants.GeoGuessrClubNameMaxLength);
        
        
    }
}