using Constants;
using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.OutputAdapters.DataAccess.Configurations;

public class GeoGuesrUserConfiguration : IEntityTypeConfiguration<GeoGuessrUser>
{
    public void Configure(EntityTypeBuilder<GeoGuessrUser> builder)
    {
        builder.HasKey(u => u.UserId);
        builder.Property(u => u.UserId)
            .HasMaxLength(StringLengthConstants.GeoGuessrUserIdLength)
            .ValueGeneratedNever();

        builder.Property(u => u.Nickname)
            .HasMaxLength(StringLengthConstants.GeoGuessrPlayerNicknameMaxLength)
            .IsRequired();
        builder.HasIndex(u => u.Nickname);

        builder.HasIndex(u => u.DiscordUserId).IsUnique();

        builder.UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(u => u.DomainEvents);
    }
}
