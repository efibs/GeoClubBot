using Constants;
using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.OutputAdapters.DataAccess.Configurations;

public class DailyMissionEntityConfiguration : IEntityTypeConfiguration<DailyMission>
{
    public void Configure(EntityTypeBuilder<DailyMission> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .ValueGeneratedOnAdd();

        builder.Property(x => x.MissionId)
            .IsRequired();

        builder.Property(x => x.Type)
            .IsRequired()
            .HasMaxLength(StringLengthConstants.DailyMissionTypeMaxLength);

        builder.Property(x => x.GameMode)
            .IsRequired()
            .HasMaxLength(StringLengthConstants.DailyMissionGameModeMaxLength);

        builder.Property(x => x.RewardType)
            .IsRequired()
            .HasMaxLength(StringLengthConstants.DailyMissionRewardTypeMaxLength);

        builder.Property(x => x.CurrentProgress).IsRequired();
        builder.Property(x => x.TargetProgress).IsRequired();
        builder.Property(x => x.Completed).IsRequired();
        builder.Property(x => x.EndDate).IsRequired();
        builder.Property(x => x.RewardAmount).IsRequired();
        builder.Property(x => x.FetchedAtUtc).IsRequired();

        builder.HasIndex(x => x.MissionId);
        builder.HasIndex(x => x.FetchedAtUtc);
    }
}
