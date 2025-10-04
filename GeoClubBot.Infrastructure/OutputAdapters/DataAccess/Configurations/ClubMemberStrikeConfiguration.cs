using Constants;
using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.OutputAdapters.DataAccess.Configurations;

public class ClubMemberStrikeConfiguration : IEntityTypeConfiguration<ClubMemberStrike>
{
    public void Configure(EntityTypeBuilder<ClubMemberStrike> builder)
    {
        // Configure the primary key
        builder.HasKey(x => x.StrikeId);
        builder.Property(x => x.StrikeId)
            .ValueGeneratedNever();
        
        // Configure the user id property
        builder.Property(x => x.UserId)
            .IsRequired()
            .HasMaxLength(StringLengthConstants.GeoGuessrUserIdLength);
        
        // Configure the other properties to be required
        builder.Property(x => x.Timestamp)
            .IsRequired();
        builder.Property(x => x.Revoked)
            .IsRequired();
        
        // Configure the club member foreign key
        builder.HasOne(x => x.ClubMember)
            .WithMany(x => x.Strikes)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        
        // Configure the index
        builder.HasIndex(x => x.Timestamp);
    }
}