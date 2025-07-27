using Constants;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts;
using UseCases.OutputPorts;

namespace UseCases;

public class CheckStrikeDecayUseCase(IStrikesRepository strikesRepository, ILogger<CheckStrikeDecayUseCase> logger, IConfiguration config) : ICheckStrikeDecayUseCase
{
    public async Task CheckStrikeDecayAsync()
    {
        // Log debug
        logger.LogDebug("Checking strike decay...");
        
        // Remove the strikes before the decay threshold
        var numDeleted = await strikesRepository
            .DeleteStrikesBeforeAsync(DateTimeOffset.UtcNow - _strikeDecayTimeSpan);
        
        // Log info
        logger.LogInformation($"Deleted {numDeleted} decayed strikes.");
    }
    
    private readonly TimeSpan _strikeDecayTimeSpan = config.GetValue<TimeSpan>(ConfigKeys.ActivityCheckerStrikeDecayTimeSpanConfigurationKey);
}