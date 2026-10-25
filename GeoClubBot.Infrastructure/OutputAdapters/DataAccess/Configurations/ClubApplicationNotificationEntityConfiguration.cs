using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.OutputAdapters.DataAccess.Configurations;

public class ClubApplicationNotificationEntityConfiguration : IEntityTypeConfiguration<ClubApplicationNotification>
{
    public void Configure(EntityTypeBuilder<ClubApplicationNotification> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .ValueGeneratedOnAdd();

        builder.Property(x => x.ClubId).IsRequired();
        builder.Property(x => x.UserId).IsRequired();
        builder.Property(x => x.NotifiedAt).IsRequired();

        builder.UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(x => x.DomainEvents);

        // What makes the announcement idempotent: a second one for the same pending application fails.
        builder.HasIndex(x => new { x.ClubId, x.UserId }).IsUnique();
    }
}
