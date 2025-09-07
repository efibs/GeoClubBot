using Constants;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts.Club;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;

namespace UseCases.UseCases.Club;

public class CheckClubLevelUseCase : ICheckClubLevelUseCase
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
        await _initClubLevelTask;
        
        // Log debug
        _logger.LogDebug("Checking club level...");

        // Create a scope
        using var scope = _serviceProvider.CreateScope();

        // Get the GeoGuessrAccess
        var geoGuessrAccess = scope.ServiceProvider.GetRequiredService<IGeoGuessrAccess>();

        // Read the club
        var clubDto = await geoGuessrAccess.ReadClubAsync(_clubId);

        // Get the club level
        var clubLevel = clubDto.Level;

        // If the club level changed
        if (clubLevel != _lastLevel)
        {
            // Log debug
            _logger.LogDebug($"Club level changed to {clubLevel}");

            // Get the status updater
            var statusUpdater = scope.ServiceProvider.GetRequiredService<ISetClubLevelStatusUseCase>();

            // Update the status
            await statusUpdater.SetClubLevelStatusAsync(clubLevel);

            // If the previous level is known
            if (_lastLevel != null)
            {
                // Update the club level on the database
                var club = await _updateClubLevelAsync(clubDto.Level);
                
                // Get the notifier services
                var notifiers = scope.ServiceProvider.GetRequiredService<IEnumerable<IClubEventNotifier>>();
                
                // For every notifier
                foreach (var notifier in notifiers)
                {
                    // Send the notification
                    await notifier.SendClubLevelUpEvent(club);
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
        
        // Get the club repository
        var clubRepository = scope.ServiceProvider.GetRequiredService<IClubRepository>();
        
        // Read the club
        var club = await clubRepository.ReadClubByIdAsync(_clubId);
        
        // If the club was not found
        if (club == null)
        {
            // Log warning
            _logger.LogWarning($"{nameof(CheckClubLevelUseCase)}.{nameof(_initClubLevelAsync)}: Failed to init club level. Club {_clubId} does not exits.");
            
            return;
        }
        
        // Init the club level
        _lastLevel = club.Level;
    }

    private async Task<Entities.Club> _updateClubLevelAsync(int newClubLevel)
    {
        // Create a scope
        using var scope = _serviceProvider.CreateScope();
        
        // Get the club repository
        var clubRepository = scope.ServiceProvider.GetRequiredService<IClubRepository>();
        
        // Read the existing club
        var club =  await clubRepository.ReadClubByIdAsync(_clubId);
        
        // If the club was not found
        if (club == null)
        {
            // Log warning
            _logger.LogWarning($"{nameof(CheckClubLevelUseCase)}.{nameof(_updateClubLevelAsync)}: Failed to update club level. Club {_clubId} does not exits.");
            
            throw new InvalidOperationException($"Failed to update club level. Club {_clubId} does not exits.");
        }
        
        // Set the new club level
        club.Level = newClubLevel;
        
        // Save the club to the database
        await clubRepository.CreateOrUpdateClubAsync(club);

        return club;
    }
    
    private readonly Task _initClubLevelTask;
    private readonly Guid _clubId;
    private int? _lastLevel;
    private readonly ILogger<CheckClubLevelUseCase> _logger;
    private readonly IServiceProvider _serviceProvider;
}