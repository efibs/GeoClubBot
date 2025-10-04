using Constants;
using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.OutputAdapters.DataAccess.Configurations;

public class ClubMemberHistoryEntryConfiguration : IEntityTypeConfiguration<ClubMemberHistoryEntry>
{
    public void Configure(EntityTypeBuilder<ClubMemberHistoryEntry> builder)
    {
        // Configure the composite primary key
        builder.HasKey(x => new {x.Timestamp, x.UserId});
        
        // Set the timestamp to not be database generated
        builder.Property(x => x.Timestamp)
            .ValueGeneratedNever()
            .IsRequired();
        
        // Set the user id max length and required
        builder.Property(x => x.UserId)
            .IsRequired()
            .HasMaxLength(StringLengthConstants.GeoGuessrUserIdLength);
        
        // Set the xp to be required
        builder.Property(x => x.Xp)
            .IsRequired();
        
        // Configure the club member relationship
        builder.HasOne(x => x.ClubMember)
            .WithMany(x => x.History)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}