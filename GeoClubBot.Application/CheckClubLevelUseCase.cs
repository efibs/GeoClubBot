using Constants;
using Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts;
using UseCases.OutputPorts;
using UseCases.OutputPorts.GeoGuessr;

namespace UseCases;

public class CheckClubLevelUseCase(
    IConfiguration config,
    ILogger<CheckClubLevelUseCase> logger,
    IServiceProvider serviceProvider) : ICheckClubLevelUseCase
{
    public async Task CheckClubLevelAsync()
    {
        // Log debug
        logger.LogDebug("Checking club level...");

        // Create a scope
        using var scope = serviceProvider.CreateScope();

        // Get the GeoGuessrAccess
        var geoGuessrAccess = scope.ServiceProvider.GetRequiredService<IGeoGuessrAccess>();

        // Read the club
        var club = await geoGuessrAccess.ReadClubAsync(_clubId);

        // Get the club level
        var clubLevel = club.Level;

        // If the club level changed
        if (clubLevel != _lastLevel)
        {
            // Log debug
            logger.LogDebug($"Club level changed to {clubLevel}");

            // Get the status updater
            var statusUpdater = scope.ServiceProvider.GetRequiredService<IStatusUpdater>();

            // Build the status message
            var newStatus = $"Level {clubLevel} club!";

            // Update the status
            await statusUpdater.UpdateStatusAsync(newStatus);

            // If the previous level is known
            if (_lastLevel != null)
            {
                // Get the send message service
                var sendMessageService = scope.ServiceProvider.GetRequiredService<IMessageSender>();
                
                // Send the level up message
                await _sendClubLevelUpMessage(club, sendMessageService);
            }
            
            // Set the new last level
            _lastLevel = clubLevel;
        }
    }

    private async Task _sendClubLevelUpMessage(GeoGuessrClubDTO clubDto, IMessageSender messageSender)
    {
        // Build the message
        var message = $"{clubDto.Name} is now level {clubDto.Level} in GeoGuessr! :partying_face: ";
        
        // Send the message
        await messageSender.SendMessageAsync(message, _levelUpMessageChannelId);
    }
    
    private readonly Guid _clubId = config.GetValue<Guid>(ConfigKeys.GeoGuessrClubIdConfigurationKey);
    private readonly string _levelUpMessageChannelId = config.GetValue<string>(ConfigKeys.ClubLevelCheckerLevelUpMessageChannelIdConfigurationKey)!;
    private int? _lastLevel = null;
}