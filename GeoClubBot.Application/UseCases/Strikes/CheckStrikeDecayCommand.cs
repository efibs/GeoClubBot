using Configuration;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts;

namespace UseCases.UseCases.Strikes;

public sealed record CheckStrikeDecayCommand : ICommand;

public sealed partial class CheckStrikeDecayHandler(
    IStrikesRepository strikes,
    ILogger<CheckStrikeDecayHandler> logger,
    IOptions<ActivityCheckerConfiguration> activityCheckerConfig) : IRequestHandler<CheckStrikeDecayCommand, Unit>
{
    public async Task<Unit> Handle(CheckStrikeDecayCommand request, CancellationToken cancellationToken)
    {
        LogCheckingStrikeDecay(logger);

        var numDeleted = await strikes
            .DeleteStrikesBeforeAsync(DateTimeOffset.UtcNow - activityCheckerConfig.Value.StrikeDecayTimeSpan)
            .ConfigureAwait(false);

        LogDeletedNumStrikes(logger, numDeleted);

        return Unit.Value;
    }

    [LoggerMessage(LogLevel.Information, "Deleted {numDeleted} decayed strikes.")]
    static partial void LogDeletedNumStrikes(ILogger<CheckStrikeDecayHandler> logger, int numDeleted);

    [LoggerMessage(LogLevel.Debug, "Checking strike decay...")]
    static partial void LogCheckingStrikeDecay(ILogger<CheckStrikeDecayHandler> logger);
}
