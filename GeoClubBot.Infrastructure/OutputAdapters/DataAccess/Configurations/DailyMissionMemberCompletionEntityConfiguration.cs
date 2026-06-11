using Constants;
using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.OutputAdapters.DataAccess.Configurations;

public class DailyMissionMemberCompletionEntityConfiguration : IEntityTypeConfiguration<DailyMissionMemberCompletion>
{
    public void Configure(EntityTypeBuilder<DailyMissionMemberCompletion> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .ValueGeneratedOnAdd();

        builder.Property(x => x.ClubId).IsRequired();

        builder.Property(x => x.UserId)
            .IsRequired()
            .HasMaxLength(StringLengthConstants.GeoGuessrUserIdLength);

        builder.Property(x => x.Date).IsRequired();
        builder.Property(x => x.CompletedCount).IsRequired();

        builder.UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(x => x.DomainEvents);

        builder.HasIndex(x => new { x.ClubId, x.Date, x.UserId }).IsUnique();
    }
}
