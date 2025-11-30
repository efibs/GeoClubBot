using Constants;
using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.OutputAdapters.DataAccess.Configurations;

public class ClubMemberConfiguration : IEntityTypeConfiguration<ClubMember>
{
    public void Configure(EntityTypeBuilder<ClubMember> builder)
    {
        // Configure the user id to be the primary key
        builder.HasKey(x => x.UserId);
        builder.Property(x => x.UserId)
            .HasMaxLength(StringLengthConstants.GeoGuessrUserIdLength)
            .ValueGeneratedNever()
            .IsRequired();
        
        // Configure the properties to be required
        builder.Property(x => x.ClubId)
            .IsRequired();
        builder.Property(x => x.IsCurrentlyMember)
            .IsRequired();
        builder.Property(x => x.Xp)
            .IsRequired();
        builder.Property(x => x.JoinedAt)
            .IsRequired();
        
        // Set the private text channel id to be not required
        builder.Property(x => x.PrivateTextChannelId)
            .IsRequired(false);
        
        // Configure the club foreign key
        builder.HasOne<Club>()
            .WithMany()
            .HasForeignKey(x => x.ClubId)
            .OnDelete(DeleteBehavior.Cascade);
        
        // Configure the user foreign key
        builder.HasOne(x => x.User)
            .WithOne()
            .HasForeignKey<ClubMember>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        
        // Ignore the domain events
        builder.Ignore(x => x.DomainEvents);
    }
}