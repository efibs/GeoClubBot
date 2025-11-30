using Constants;
using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.OutputAdapters.DataAccess.Configurations;

public class GeoGuesrUserConfiguration : IEntityTypeConfiguration<GeoGuessrUser>
{
    public void Configure(EntityTypeBuilder<GeoGuessrUser> builder)
    {
        // Configure the primary key to be the user id
        builder.HasKey(u => u.UserId);
        builder.Property(u => u.UserId)
            .HasMaxLength(StringLengthConstants.GeoGuessrUserIdLength)
            .ValueGeneratedNever();
        
        // Nickname with index
        builder.Property(u => u.Nickname)
            .HasMaxLength(StringLengthConstants.GeoGuessrPlayerNicknameMaxLength)
            .IsRequired();
        builder.HasIndex(u => u.Nickname);
        
        // Discord user id with unique index and filter
        builder.HasIndex(u => u.DiscordUserId)
            .IsUnique();
        
        // Ignore the domain events
        builder.Ignore(u => u.DomainEvents);
    }
}