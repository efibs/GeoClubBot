using Constants;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts.Strikes;
using UseCases.OutputPorts;

namespace UseCases.UseCases.Strikes;

public partial class CheckStrikeDecayUseCase(IUnitOfWork unitOfWork, ILogger<CheckStrikeDecayUseCase> logger, IConfiguration config) : ICheckStrikeDecayUseCase
{
    public async Task CheckStrikeDecayAsync()
    {
        // Log debug
        logger.LogDebug("Checking strike decay...");
        
        // Remove the strikes before the decay threshold
        var numDeleted = await unitOfWork.Strikes
            .DeleteStrikesBeforeAsync(DateTimeOffset.UtcNow - _strikeDecayTimeSpan)
            .ConfigureAwait(false);
        
        // Log info
        LogDeletedNumStrikes(logger, numDeleted);
    }
    
    private readonly TimeSpan _strikeDecayTimeSpan = config.GetValue<TimeSpan>(ConfigKeys.ActivityCheckerStrikeDecayTimeSpanConfigurationKey);
    
    [LoggerMessage(LogLevel.Information, "Deleted {numDeleted} decayed strikes.")]
    static partial void LogDeletedNumStrikes(ILogger<CheckStrikeDecayUseCase> logger, int numDeleted);
}