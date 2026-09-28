using Constants;
using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.OutputAdapters.DataAccess.Configurations;

public class CountryChallengePointAwardConfiguration : IEntityTypeConfiguration<CountryChallengePointAward>
{
    public void Configure(EntityTypeBuilder<CountryChallengePointAward> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .ValueGeneratedOnAdd();

        builder.Property(x => x.Season)
            .IsRequired()
            .HasMaxLength(StringLengthConstants.CountryChallengeSeasonMaxLength);

        builder.Property(x => x.UserId)
            .IsRequired()
            .HasMaxLength(StringLengthConstants.GeoGuessrUserIdLength);

        builder.Property(x => x.Nickname)
            .IsRequired()
            .HasMaxLength(StringLengthConstants.GeoGuessrPlayerNicknameMaxLength);

        builder.Property(x => x.Points).IsRequired();
        builder.Property(x => x.Source).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.AwardedAt).IsRequired();

        builder.HasOne<CountryChallengePost>()
            .WithMany()
            .HasForeignKey(x => x.PostId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(x => x.DomainEvents);

        builder.HasIndex(x => x.Season);
    }
}
