using Constants;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts;

namespace Infrastructure.InputAdapters;

public class CheckClubLevelService(
    DiscordBotReadyService botReadyService,
    ICheckClubLevelUseCase useCase,
    IConfiguration config,
    ILogger<CheckClubLevelService> logger) : IHostedService, IDisposable
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Await the bot to be ready
        await botReadyService.DiscordSocketClientReady;

        // Create the new timer
        _timer = new Timer(_checkClubLevel, null, TimeSpan.Zero, _checkFrequency);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Stop the timer
        _timer?.Change(Timeout.Infinite, 0);

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }

    private void _checkClubLevel(object? sender)
    {
        // Run the check in the background
        Task.Run(_checkClubLevelAsync);
    }

    private async Task _checkClubLevelAsync()
    {
        try
        {
            await useCase.CheckClubLevelAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking club level.");
        }
    }

    private readonly TimeSpan _checkFrequency =
        config.GetValue<TimeSpan>(ConfigKeys.ClubLevelCheckerFrequencyConfigurationKey);

    private Timer? _timer;
}