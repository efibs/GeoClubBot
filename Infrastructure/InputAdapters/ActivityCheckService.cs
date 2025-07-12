using Extensions;
using GeoClubBot;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UseCases.InputPorts;

namespace Infrastructure.InputAdapters;

public class ActivityCheckService : IHostedService, IDisposable
{
    public ActivityCheckService(ICheckGeoGuessrPlayerActivityUseCase useCase, IConfiguration config, ILogger<ActivityCheckService> logger)
    {
        _logger = logger;
        
        // Get the configured frequency
        var frequency = config.GetValue<string>(ConfigKeys.ActivityCheckerFrequencyConfigurationKey);

        // Convert the frequency string to time span
        _checkFrequency = frequency switch
        {
            FrequencyValues.Minutely => TimeSpan.FromMinutes(1),
            FrequencyValues.Hourly => TimeSpan.FromHours(1),
            FrequencyValues.Daily => TimeSpan.FromDays(1),
            FrequencyValues.Weekly => TimeSpan.FromDays(7),
            FrequencyValues.Monthly => TimeSpan.FromDays(30),
            FrequencyValues.Yearly => TimeSpan.FromDays(365),
            _ => throw new InvalidOperationException($"Unknown frequency {frequency}")
        };
        
        _useCase = useCase;
        
        // Log debug message
        _logger.LogDebug($"Scheduling activity check for frequency: {_checkFrequency}");
    }
    
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Get the next activity check time
        var nextCheckTime = DateTimeOffset.UtcNow.RoundUp(_checkFrequency);
        
        // Create the new timer
        _timer = new Timer(_checkActivity, null, nextCheckTime - DateTimeOffset.UtcNow, _checkFrequency);
        
        // Log information
        _logger.LogInformation("Player activity checking scheduled. Next check time: {DateTimeOffset:R}", nextCheckTime);
        
        return Task.CompletedTask;
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
    
    private void _checkActivity(object? sender)
    {
        // Run the check in the background
        Task.Run(_checkActivityAsync);
    }

    private async Task _checkActivityAsync()
    {
        try
        {
            await _useCase.CheckPlayerActivityAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking player activity.");
        }
    }
    
    private readonly ICheckGeoGuessrPlayerActivityUseCase _useCase;
    private readonly TimeSpan _checkFrequency;
    private readonly ILogger<ActivityCheckService> _logger;
    private Timer? _timer;
}