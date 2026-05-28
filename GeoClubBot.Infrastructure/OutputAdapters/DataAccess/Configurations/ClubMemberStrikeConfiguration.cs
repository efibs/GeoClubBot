using Constants;
using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.OutputAdapters.DataAccess.Configurations;

public class ClubMemberStrikeConfiguration : IEntityTypeConfiguration<ClubMemberStrike>
{
    public void Configure(EntityTypeBuilder<ClubMemberStrike> builder)
    {
        builder.HasKey(x => x.StrikeId);
        builder.Property(x => x.StrikeId)
            .ValueGeneratedNever();

        builder.Property(x => x.UserId)
            .IsRequired()
            .HasMaxLength(StringLengthConstants.GeoGuessrUserIdLength);

        builder.Property(x => x.Timestamp).IsRequired();
        builder.Property(x => x.Revoked).IsRequired();

        // Bypass private setters so EF cannot accidentally trigger domain behaviour during materialisation.
        builder.UsePropertyAccessMode(PropertyAccessMode.Field);

        // Domain events are tracked via BaseEntity but not persisted.
        builder.Ignore(x => x.DomainEvents);

        builder.HasOne(x => x.ClubMember)
            .WithMany(x => x.Strikes)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.Timestamp);
        builder.HasIndex(x => x.UserId);
    }
}