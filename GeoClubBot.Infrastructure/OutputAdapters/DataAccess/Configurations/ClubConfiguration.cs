using Constants;
using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.OutputAdapters.DataAccess.Configurations;

public class ClubConfiguration : IEntityTypeConfiguration<Club>
{
    public void Configure(EntityTypeBuilder<Club> builder)
    {
        builder.HasKey(c => c.ClubId);
        builder.Property(c => c.ClubId)
            .ValueGeneratedNever();

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(StringLengthConstants.GeoGuessrClubNameMaxLength);

        builder.UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(c => c.DomainEvents);
    }
}
