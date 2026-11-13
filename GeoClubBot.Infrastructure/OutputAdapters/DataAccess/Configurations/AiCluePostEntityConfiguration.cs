using Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.OutputAdapters.DataAccess.Configurations;

public class AiCluePostEntityConfiguration : IEntityTypeConfiguration<AiCluePost>
{
    public void Configure(EntityTypeBuilder<AiCluePost> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .ValueGeneratedOnAdd();

        builder.Property(x => x.ChunkId).IsRequired();
        builder.Property(x => x.SourceUrl).IsRequired().HasMaxLength(2048);
        builder.Property(x => x.ChannelId).IsRequired();
        builder.Property(x => x.PostedAtUtc).IsRequired();

        builder.UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(x => x.DomainEvents);

        // What the no-repeat rule reads: the chunks posted since a date.
        builder.HasIndex(x => x.PostedAtUtc);
        builder.HasIndex(x => x.ChunkId);
    }
}
