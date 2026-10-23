using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.OutputAdapters.DataAccess.Configurations;

public class MissionClaimEntityConfiguration : IEntityTypeConfiguration<MissionClaim>
{
    public void Configure(EntityTypeBuilder<MissionClaim> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .ValueGeneratedOnAdd();

        builder.Property(x => x.ClubId).IsRequired();
        builder.Property(x => x.PeriodStart).IsRequired();
        builder.Property(x => x.PeriodEnd).IsRequired();
        builder.Property(x => x.MissionId).IsRequired();
        builder.Property(x => x.UserId).IsRequired();
        builder.Property(x => x.ClaimedAt).IsRequired();

        builder.UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(x => x.DomainEvents);
        builder.Ignore(x => x.CompletionTime);

        // Every club gets the same mission ids for a week, so a mission is only unique per club and week.
        builder.HasIndex(x => new { x.ClubId, x.PeriodStart, x.MissionId }).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.ClaimedAt });
        builder.HasIndex(x => new { x.ClubId, x.ClaimedAt });
    }
}
