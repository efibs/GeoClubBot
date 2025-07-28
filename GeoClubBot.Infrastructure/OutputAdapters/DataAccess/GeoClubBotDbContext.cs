using AppAny.Quartz.EntityFrameworkCore.Migrations;
using AppAny.Quartz.EntityFrameworkCore.Migrations.PostgreSQL;
using Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.OutputAdapters.DataAccess;

public class GeoClubBotDbContext : DbContext
{
    public GeoClubBotDbContext(DbContextOptions<GeoClubBotDbContext> options) : base(options)
    {
    }
    
    public DbSet<Club> Clubs { get; set; }
    
    public DbSet<ClubMember> ClubMembers { get; set; }
    
    public DbSet<ClubMemberExcuse> ClubMemberExcuses { get; set; }
    
    public DbSet<ClubMemberStrike> ClubMemberStrikes { get; set; }
    
    public DbSet<ClubMemberHistoryEntry> ClubMemberHistoryEntries { get; set; }

    public DbSet<ClubChallengeLink> LatestClubChallengeLinks { get; set; }
    
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetConverter>();
        base.ConfigureConventions(configurationBuilder);
    }
}