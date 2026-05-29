using Constants;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UseCases.Abstractions;
using UseCases.OutputPorts;

namespace UseCases.UseCases.Strikes;

public sealed record CheckStrikeDecayCommand : ICommand;

public sealed partial class CheckStrikeDecayHandler(
    IStrikesRepository strikes,
    ILogger<CheckStrikeDecayHandler> logger,
    IConfiguration config) : IRequestHandler<CheckStrikeDecayCommand, Unit>
{
    public async Task<Unit> Handle(CheckStrikeDecayCommand request, CancellationToken cancellationToken)
    {
        LogCheckingStrikeDecay(logger);

        var numDeleted = await strikes
            .DeleteStrikesBeforeAsync(DateTimeOffset.UtcNow - _strikeDecayTimeSpan)
            .ConfigureAwait(false);

        LogDeletedNumStrikes(logger, numDeleted);

        return Unit.Value;
    }

    private readonly TimeSpan _strikeDecayTimeSpan =
        config.GetValue<TimeSpan>(ConfigKeys.ActivityCheckerStrikeDecayTimeSpanConfigurationKey);

    [LoggerMessage(LogLevel.Information, "Deleted {numDeleted} decayed strikes.")]
    static partial void LogDeletedNumStrikes(ILogger<CheckStrikeDecayHandler> logger, int numDeleted);

    [LoggerMessage(LogLevel.Debug, "Checking strike decay...")]
    static partial void LogCheckingStrikeDecay(ILogger<CheckStrikeDecayHandler> logger);
}
