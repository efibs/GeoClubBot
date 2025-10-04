using Constants;
using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.OutputAdapters.DataAccess.Configurations;

public class ClubMemberExcuseConfiguration : IEntityTypeConfiguration<ClubMemberExcuse>
{
    public void Configure(EntityTypeBuilder<ClubMemberExcuse> builder)
    {
        // Configure the primary key
        builder.HasKey(x => x.ExcuseId);
        builder.Property(x => x.ExcuseId)
            .ValueGeneratedOnAdd();
        
        // Configure user id
        builder.Property(x => x.UserId)
            .IsRequired()
            .HasMaxLength(StringLengthConstants.GeoGuessrUserIdLength);
        
        // Configure the date range to be required
        builder.Property(x => x.From)
            .IsRequired();
        builder.Property(x => x.To)
            .IsRequired();
        
        // Configure the club member relationship
        builder.HasOne(x => x.ClubMember)
            .WithMany(x => x.Excuses)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        
        // Add an index to the To property
        builder.HasIndex(x => x.To);
    }
}