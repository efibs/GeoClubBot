using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.OutputAdapters.DataAccess.Configurations;

public class ClubMissionBoardObservationEntityConfiguration : IEntityTypeConfiguration<ClubMissionBoardObservation>
{
    public void Configure(EntityTypeBuilder<ClubMissionBoardObservation> builder)
    {
        builder.HasKey(x => x.ClubId);
        builder.Property(x => x.ClubId).ValueGeneratedNever();
        builder.Property(x => x.PeriodStart).IsRequired();
        builder.Property(x => x.CurrentBoardNumber).IsRequired();
        builder.Property(x => x.AllBoardsCleared).IsRequired();
        builder.Property(x => x.ObservedAt).IsRequired();

        builder.UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(x => x.DomainEvents);
    }
}
