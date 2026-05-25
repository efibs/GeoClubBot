using Constants;
using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.OutputAdapters.DataAccess.Configurations;

public class ClubMemberHistoryEntryConfiguration : IEntityTypeConfiguration<ClubMemberHistoryEntry>
{
    public void Configure(EntityTypeBuilder<ClubMemberHistoryEntry> builder)
    {
        builder.HasKey(x => new { x.Timestamp, x.UserId });

        builder.Property(x => x.Timestamp)
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(x => x.UserId)
            .IsRequired()
            .HasMaxLength(StringLengthConstants.GeoGuessrUserIdLength);

        builder.Property(x => x.Xp).IsRequired();

        builder.UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(x => x.DomainEvents);

        builder.HasOne(x => x.ClubMember)
            .WithMany(x => x.History)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Club)
            .WithMany()
            .HasForeignKey(x => x.ClubId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
