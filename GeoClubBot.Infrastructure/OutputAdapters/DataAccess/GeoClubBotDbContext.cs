using Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.OutputAdapters.DataAccess;

public class GeoClubBotDbContext : DbContext
{
    public GeoClubBotDbContext(DbContextOptions<GeoClubBotDbContext> options, IMediator mediator) : base(options)
    {
        _mediator = mediator;
    }
    
    public DbSet<Club> Clubs { get; set; }
    
    public DbSet<ClubMember> ClubMembers { get; set; }
    
    public DbSet<ClubMemberExcuse> ClubMemberExcuses { get; set; }
    
    public DbSet<ClubMemberStrike> ClubMemberStrikes { get; set; }
    
    public DbSet<ClubMemberHistoryEntry> ClubMemberHistoryEntries { get; set; }

    public DbSet<ClubChallengeLink> LatestClubChallengeLinks { get; set; }

    public DbSet<GeoGuessrUser> GeoGuessrUsers { get; set; }
    
    public DbSet<GeoGuessrAccountLinkingRequest> GeoGuessrAccountLinkingRequests { get; set; }

    public DbSet<DailyMissionReminder> DailyMissionReminders { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        
#if DEBUG
        optionsBuilder.EnableSensitiveDataLogging();
#endif
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetConverter>();
        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GeoClubBotDbContext).Assembly);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Dispatch domain events before saving
        var entities = ChangeTracker.Entries<BaseEntity>()
            .Where(e => e.Entity.DomainEvents.Any())
            .Select(e => e.Entity)
            .ToList();

        var domainEvents = entities
            .SelectMany(e => e.DomainEvents)
            .ToList();

        var result = await base.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Dispatch events after successful save
        foreach (var domainEvent in domainEvents)
        {
            await _mediator.Publish(domainEvent, cancellationToken).ConfigureAwait(false);
        }

        // Clear events
        entities.ForEach(e => e.ClearDomainEvents());

        return result;
    }
    
    private readonly IMediator _mediator;
}