using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.OutputAdapters.DataAccess.Configurations;

public class MissionBoardAlertEntityConfiguration : IEntityTypeConfiguration<MissionBoardAlert>
{
    public void Configure(EntityTypeBuilder<MissionBoardAlert> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .ValueGeneratedOnAdd();

        builder.Property(x => x.ClubId).IsRequired();
        builder.Property(x => x.MissionId).IsRequired();
        builder.Property(x => x.Kind).IsRequired();
        builder.Property(x => x.SentAt).IsRequired();

        builder.UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(x => x.DomainEvents);

        // What makes the alert idempotent: a second attempt for the same mission and kind fails.
        builder.HasIndex(x => new { x.ClubId, x.MissionId, x.Kind }).IsUnique();
        builder.HasIndex(x => x.SentAt);
    }
}
