using Constants;
using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.OutputAdapters.DataAccess.Configurations;

public class ClubMemberConfiguration : IEntityTypeConfiguration<ClubMember>
{
    public void Configure(EntityTypeBuilder<ClubMember> builder)
    {
        builder.HasKey(x => x.UserId);
        builder.Property(x => x.UserId)
            .HasMaxLength(StringLengthConstants.GeoGuessrUserIdLength)
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(x => x.ClubId);
        builder.Property(x => x.Xp).IsRequired();
        builder.Property(x => x.JoinedAt).IsRequired();
        builder.Property(x => x.PrivateTextChannelId).IsRequired(false);

        // Bypass private setters so EF hydrates straight into the backing fields.
        builder.UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<Club>()
            .WithMany()
            .HasForeignKey(x => x.ClubId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.User)
            .WithOne()
            .HasForeignKey<ClubMember>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(x => x.DomainEvents);
    }
}
