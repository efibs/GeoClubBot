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
}