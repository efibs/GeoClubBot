using Constants;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts.Club;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;

namespace UseCases.UseCases.Club;

public partial class CheckClubLevelUseCase : ICheckClubLevelUseCase
{
    public CheckClubLevelUseCase(IConfiguration config,
        ILogger<CheckClubLevelUseCase> logger,
        IServiceProvider serviceProvider)
    {
        _initClubLevelTask = Task.Run(_initClubLevelAsync);
        _clubId = config.GetValue<Guid>(ConfigKeys.GeoGuessrClubIdConfigurationKey);
        _logger = logger;
        _serviceProvider = serviceProvider;
    }
    
    public async Task CheckClubLevelAsync()
    {
        // Await the init of the club level
        await _initClubLevelTask.ConfigureAwait(false);
        
        // Log debug
        _logger.LogDebug("Checking club level...");

        // Create a scope
        using var scope = _serviceProvider.CreateScope();

        // Get the GeoGuessrAccess
        var geoGuessrAccess = scope.ServiceProvider.GetRequiredService<IGeoGuessrClient>();

        // Read the club
        var clubDto = await geoGuessrAccess.ReadClubAsync(_clubId).ConfigureAwait(false);

        // Get the club level
        var clubLevel = clubDto.Level;

        // If the club level changed
        if (clubLevel != _lastLevel)
        {
            // Log debug
            LogClubLevelChangedToClubLevel(clubLevel);

            // Get the status updater
            var statusUpdater = scope.ServiceProvider.GetRequiredService<ISetClubLevelStatusUseCase>();

            // Update the status
            await statusUpdater.SetClubLevelStatusAsync(clubLevel).ConfigureAwait(false);

            // If the previous level is known
            if (_lastLevel != null)
            {
                // Update the club level on the database
                var club = await _updateClubLevelAsync(clubDto.Level).ConfigureAwait(false);
                
                // Get the notifier services
                var notifiers = scope.ServiceProvider.GetRequiredService<IEnumerable<IClubEventNotifier>>();
                
                // For every notifier
                foreach (var notifier in notifiers)
                {
                    // Send the notification
                    await notifier.SendClubLevelUpEvent(club).ConfigureAwait(false);
                }
            }
            
            // Set the new last level
            _lastLevel = clubLevel;
        }
    }

    private async Task _initClubLevelAsync()
    {
        // Create a scope
        using var scope = _serviceProvider.CreateScope();
        
        // Get the unit of work
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        
        // Read the club
        var club = await unitOfWork.Clubs.ReadClubByIdAsync(_clubId).ConfigureAwait(false);
        
        // If the club was not found
        if (club == null)
        {
            // Log warning
            LogFailedToInitClubLevelClubDoesNotExits(_clubId);
            
            return;
        }
        
        // Init the club level
        _lastLevel = club.Level;
    }

    private async Task<Entities.Club> _updateClubLevelAsync(int newClubLevel)
    {
        // Create a scope
        using var scope = _serviceProvider.CreateScope();
        
        // Get the unit of work
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        
        // Read the existing club
        var club =  await unitOfWork.Clubs.ReadClubByIdAsync(_clubId).ConfigureAwait(false);
        
        // If the club was not found
        if (club == null)
        {
            // Log warning
            LogFailedToUpdateClubLevelClubDoesNotExits(_clubId);
            
            throw new InvalidOperationException($"Failed to update club level. Club {_clubId} does not exits.");
        }
        
        // Set the new club level
        club.Level = newClubLevel;
        
        // Save the club to the database
        await unitOfWork.Clubs.CreateOrUpdateClubAsync(club).ConfigureAwait(false);

        // Save the changes
        await unitOfWork.SaveChangesAsync().ConfigureAwait(false);
        
        return club;
    }
    
    private readonly Task _initClubLevelTask;
    private readonly Guid _clubId;
    private int? _lastLevel;
    private readonly ILogger<CheckClubLevelUseCase> _logger;
    private readonly IServiceProvider _serviceProvider;
    
    [LoggerMessage(LogLevel.Debug, "Club level changed to {clubLevel}")]
    partial void LogClubLevelChangedToClubLevel(int clubLevel);

    [LoggerMessage(LogLevel.Warning, "Failed to init club level. Club {clubId} does not exits.")]
    partial void LogFailedToInitClubLevelClubDoesNotExits(Guid clubId);

    [LoggerMessage(LogLevel.Warning, "Failed to update club level. Club {clubId} does not exits.")]
    partial void LogFailedToUpdateClubLevelClubDoesNotExits(Guid clubId);
}