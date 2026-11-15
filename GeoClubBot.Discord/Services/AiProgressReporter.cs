using System.Threading.Channels;
using Configuration;
using Discord;
using GeoClubBot.Discord.InputAdapters.Interactions.AI;
using Microsoft.Extensions.Logging;
using UseCases.UseCases.AI.Progress;

namespace GeoClubBot.Discord.Services;

/// <summary>Where an answer's steps are shown: a message edited as they change, removed once it is done.</summary>
public interface IAiProgressDisplay
{
    /// <summary>Shows <paramref name="text"/>, replacing what was shown before.</summary>
    Task ShowAsync(string text);

    /// <summary>Takes away what was shown, when anything was.</summary>
    Task RemoveAsync();
}

/// <summary>
/// A small reply under the question, without a ping or a notification, edited as the steps change and
/// deleted when the answer is posted. Discord has no ephemeral reply to a message, so this is the
/// nearest thing: visible to the channel only while the answer is made.
/// </summary>
public sealed class ReplyProgressDisplay(IUserMessage question) : IAiProgressDisplay
{
    private IUserMessage? _status;

    public async Task ShowAsync(string text)
    {
        if (_status is null)
        {
            _status = await question.ReplyAsync(
                text,
                allowedMentions: AllowedMentions.None,
                flags: MessageFlags.SuppressNotification).ConfigureAwait(false);
            return;
        }

        await _status.ModifyAsync(message => message.Content = text).ConfigureAwait(false);
    }

    public Task RemoveAsync() => _status?.DeleteAsync() ?? Task.CompletedTask;
}

/// <summary>
/// A slash command's own deferred, ephemeral reply, edited as the steps change and deleted once the
/// command has answered in a follow-up of its own.
/// </summary>
public sealed class InteractionProgressDisplay(IDiscordInteraction interaction) : IAiProgressDisplay
{
    private bool _shown;

    public async Task ShowAsync(string text)
    {
        await interaction.ModifyOriginalResponseAsync(message => message.Content = text).ConfigureAwait(false);
        _shown = true;
    }

    public Task RemoveAsync() => _shown ? interaction.DeleteOriginalResponseAsync() : Task.CompletedTask;
}

/// <summary>
/// Shows an <see cref="AiProgressLog"/> on an <see cref="IAiProgressDisplay"/> while an answer is made
/// (#481): first after <see cref="AiProgressConfiguration.ShowAfterSeconds"/>, then at most once per
/// <see cref="AiProgressConfiguration.MinEditIntervalSeconds"/>, and only when the text changed.
///
/// Nothing here may cost the answer: a display that fails is logged and, after a few failures, given
/// up. Discord's requests are never cancelled half-way, because a reply cancelled after Discord stored
/// it would leave a status message nobody knows the id of, and so nobody can delete.
/// </summary>
public sealed partial class AiProgressReporter : IAsyncDisposable
{
    /// <summary>Failed edits in a row after which the display is left alone — no permission, the question deleted.</summary>
    private const int MaxFailures = 3;

    private readonly AiProgressLog _log;
    private readonly IAiProgressDisplay _display;
    private readonly TimeSpan _showAfter;
    private readonly TimeSpan _minInterval;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly Channel<bool> _changes = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private int _disposed;

    private AiProgressReporter(
        AiProgressLog log,
        IAiProgressDisplay display,
        AiProgressConfiguration settings,
        TimeProvider timeProvider,
        ILogger logger)
    {
        _log = log;
        _display = display;
        _showAfter = TimeSpan.FromSeconds(Math.Max(0, settings.ShowAfterSeconds));
        _minInterval = TimeSpan.FromSeconds(Math.Max(0, settings.MinEditIntervalSeconds));
        _timeProvider = timeProvider;
        _logger = logger;

        _log.Changed += OnChanged;
        _changes.Writer.TryWrite(true);
        _loop = Task.Run(() => RunAsync(_stop.Token));
    }

    /// <summary>Starts showing <paramref name="log"/>, or returns null when progress is turned off.</summary>
    public static AiProgressReporter? Start(
        AiProgressLog log,
        IAiProgressDisplay display,
        AiProgressConfiguration settings,
        TimeProvider timeProvider,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(settings);

        return settings.Enabled ? new AiProgressReporter(log, display, settings, timeProvider, logger) : null;
    }

    /// <summary>Stops showing the steps and removes what was shown. Safe to call more than once.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _log.Changed -= OnChanged;
        await _stop.CancelAsync().ConfigureAwait(false);

        // Lets an edit in flight finish, so what it posted is what gets removed.
        await _loop.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        try
        {
            await _display.RemoveAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LogRemoveFailed(_logger, exception);
        }

        _stop.Dispose();
    }

    private void OnChanged() => _changes.Writer.TryWrite(true);

    private async Task RunAsync(CancellationToken stop)
    {
        try
        {
            if (_showAfter > TimeSpan.Zero)
            {
                await Task.Delay(_showAfter, _timeProvider, stop).ConfigureAwait(false);
            }

            string? shown = null;
            var failures = 0;

            while (true)
            {
                await _changes.Reader.ReadAsync(stop).ConfigureAwait(false);

                var text = AiProgressFormatter.Render(_log.Snapshot());
                if (text is not null && text != shown)
                {
                    try
                    {
                        await _display.ShowAsync(text).ConfigureAwait(false);
                        shown = text;
                        failures = 0;
                    }
                    catch (Exception exception)
                    {
                        LogShowFailed(_logger, exception);
                        if (++failures >= MaxFailures)
                        {
                            return;
                        }
                    }
                }

                if (_minInterval > TimeSpan.Zero)
                {
                    await Task.Delay(_minInterval, _timeProvider, stop).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // The answer is done.
        }
    }

    [LoggerMessage(LogLevel.Debug, "Could not show the AI answer's progress.")]
    static partial void LogShowFailed(ILogger logger, Exception exception);

    [LoggerMessage(LogLevel.Warning, "Could not remove the AI answer's progress message.")]
    static partial void LogRemoveFailed(ILogger logger, Exception exception);
}
