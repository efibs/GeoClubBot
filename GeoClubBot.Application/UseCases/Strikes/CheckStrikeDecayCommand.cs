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
        logger.LogDebug("Checking strike decay...");

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
}
