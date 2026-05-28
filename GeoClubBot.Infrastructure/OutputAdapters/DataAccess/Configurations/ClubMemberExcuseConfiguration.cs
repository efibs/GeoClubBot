using Constants;
using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.OutputAdapters.DataAccess.Configurations;

public class ClubMemberExcuseConfiguration : IEntityTypeConfiguration<ClubMemberExcuse>
{
    public void Configure(EntityTypeBuilder<ClubMemberExcuse> builder)
    {
        builder.HasKey(x => x.ExcuseId);
        builder.Property(x => x.ExcuseId)
            .ValueGeneratedOnAdd();

        builder.Property(x => x.UserId)
            .IsRequired()
            .HasMaxLength(StringLengthConstants.GeoGuessrUserIdLength);

        builder.Property(x => x.From).IsRequired();
        builder.Property(x => x.To).IsRequired();

        // Bypass private setters so EF can hydrate from the database without going through
        // factory/behaviour methods.
        builder.UsePropertyAccessMode(PropertyAccessMode.Field);

        // Domain events live on BaseEntity but are not persisted.
        builder.Ignore(x => x.DomainEvents);

        builder.HasOne(x => x.ClubMember)
            .WithMany(x => x.Excuses)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.To);
        builder.HasIndex(x => x.UserId);
    }
}