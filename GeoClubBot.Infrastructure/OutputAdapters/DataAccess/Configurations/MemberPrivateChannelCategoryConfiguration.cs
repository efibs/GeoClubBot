using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.OutputAdapters.DataAccess.Configurations;

public class MemberPrivateChannelCategoryConfiguration : IEntityTypeConfiguration<MemberPrivateChannelCategory>
{
    public void Configure(EntityTypeBuilder<MemberPrivateChannelCategory> builder)
    {
        builder.HasKey(x => x.CategoryId);
        builder.Property(x => x.CategoryId).ValueGeneratedNever();

        builder.Property(x => x.ClubId).IsRequired();
        builder.Property(x => x.Index).IsRequired();

        builder.UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(x => x.DomainEvents);

        builder.HasOne<Club>()
            .WithMany()
            .HasForeignKey(x => x.ClubId)
            .OnDelete(DeleteBehavior.Cascade);

        // Two categories of one club never share a number.
        builder.HasIndex(x => new { x.ClubId, x.Index }).IsUnique();
    }
}
