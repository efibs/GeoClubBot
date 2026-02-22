using Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.InputPorts.Club;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;

namespace UseCases.UseCases.Club;

public partial class CheckClubLevelUseCase : ICheckClubLevelUseCase
{
    public CheckClubLevelUseCase(IOptions<GeoGuessrConfiguration> geoGuessrConfig,
        ILogger<CheckClubLevelUseCase> logger,
        IServiceProvider serviceProvider)
    {
        _clubs = geoGuessrConfig.Value.Clubs;
        _mainClubId = geoGuessrConfig.Value.MainClub.ClubId;
        _initClubLevelTask = Task.Run(_initAllClubLevelsAsync);
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public async Task CheckClubLevelAsync()
    {
        // Await the init of all club levels
        await _initClubLevelTask.ConfigureAwait(false);

        // Log debug
        _logger.LogDebug("Checking club levels...");

        // Create a scope
        using var scope = _serviceProvider.CreateScope();

        // Get the client factory
        var clientFactory = scope.ServiceProvider.GetRequiredService<IGeoGuessrClientFactory>();

        // For every club
        foreach (var clubEntry in _clubs)
        {
            // Get the client for this club
            var client = clientFactory.CreateClient(clubEntry.ClubId);

            // Read the club
            var clubDto = await client.ReadClubAsync(clubEntry.ClubId).ConfigureAwait(false);

            // Get the club level
            var clubLevel = clubDto.Level;

            // Get the last known level for this club
            _lastLevels.TryGetValue(clubEntry.ClubId, out var lastLevel);

            // If the club level changed
            if (clubLevel != lastLevel)
            {
                // Log debug
                LogClubLevelChangedToClubLevel(clubLevel);

                // Only update bot status for the main club
                if (clubEntry.ClubId == _mainClubId)
                {
                    var statusUpdater = scope.ServiceProvider.GetRequiredService<ISetClubLevelStatusUseCase>();
                    await statusUpdater.SetClubLevelStatusAsync(clubLevel).ConfigureAwait(false);
                }

                // If the previous level is known
                if (lastLevel != null)
                {
                    // Update the club level on the database
                    var club = await _updateClubLevelAsync(clubEntry.ClubId, clubDto.Level).ConfigureAwait(false);

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
                _lastLevels[clubEntry.ClubId] = clubLevel;
            }
        }
    }

    private async Task _initAllClubLevelsAsync()
    {
        // Create a scope
        using var scope = _serviceProvider.CreateScope();

        // Get the unit of work
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // For every club
        foreach (var clubEntry in _clubs)
        {
            // Read the club
            var club = await unitOfWork.Clubs.ReadClubByIdAsync(clubEntry.ClubId).ConfigureAwait(false);

            // If the club was not found
            if (club == null)
            {
                // Log warning
                LogFailedToInitClubLevelClubDoesNotExits(clubEntry.ClubId);
                continue;
            }

            // Init the club level
            _lastLevels[clubEntry.ClubId] = club.Level;
        }
    }

    private async Task<Entities.Club> _updateClubLevelAsync(Guid clubId, int newClubLevel)
    {
        // Create a scope
        using var scope = _serviceProvider.CreateScope();

        // Get the unit of work
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // Read the existing club
        var club = await unitOfWork.Clubs.ReadClubByIdAsync(clubId).ConfigureAwait(false);

        // If the club was not found
        if (club == null)
        {
            // Log warning
            LogFailedToUpdateClubLevelClubDoesNotExits(clubId);

            throw new InvalidOperationException($"Failed to update club level. Club {clubId} does not exits.");
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
    private readonly List<GeoGuessrClubEntry> _clubs;
    private readonly Guid _mainClubId;
    private readonly Dictionary<Guid, int?> _lastLevels = new();
    private readonly ILogger<CheckClubLevelUseCase> _logger;
    private readonly IServiceProvider _serviceProvider;

    [LoggerMessage(LogLevel.Debug, "Club level changed to {clubLevel}")]
    partial void LogClubLevelChangedToClubLevel(int clubLevel);

    [LoggerMessage(LogLevel.Warning, "Failed to init club level. Club {clubId} does not exits.")]
    partial void LogFailedToInitClubLevelClubDoesNotExits(Guid clubId);

    [LoggerMessage(LogLevel.Warning, "Failed to update club level. Club {clubId} does not exits.")]
    partial void LogFailedToUpdateClubLevelClubDoesNotExits(Guid clubId);
}
