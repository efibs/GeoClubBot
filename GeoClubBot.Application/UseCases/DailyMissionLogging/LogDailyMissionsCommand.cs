using System.Globalization;
using System.Text.RegularExpressions;
using Configuration;
using Entities;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Rendering;

namespace UseCases.UseCases.DailyMissionLogging;

public sealed record LogDailyMissionsCommand : ICommand;

public sealed partial class LogDailyMissionsHandler(
    IGeoGuessrClientFactory geoGuessrClientFactory,
    IDailyMissionRepository missions,
    IDiscordMessageAccess discordMessageAccess,
    IDailyMissionRenderer renderer,
    IOptions<DailyMissionLoggingConfiguration> config,
    ILogger<LogDailyMissionsHandler> logger) : IRequestHandler<LogDailyMissionsCommand, Unit>
{
    private static readonly Regex DatePlaceholderRegex =
        new(@"\{\{date:([^}]+)\}\}", RegexOptions.Compiled);

    public async Task<Unit> Handle(LogDailyMissionsCommand request, CancellationToken cancellationToken)
    {
        var missionsClient = geoGuessrClientFactory.CreateMissionsClient();

        DailyMissionsResponseDto response;
        try
        {
            response = await missionsClient.ReadDailyMissionsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogFetchFailed(ex);
            return Unit.Value;
        }

        var fetchedAt = DateTimeOffset.UtcNow;

        var entities = response.Missions.Select(m => DailyMission.Create(
            m.Id,
            m.Type,
            m.GameMode,
            m.CurrentProgress,
            m.TargetProgress,
            m.Completed,
            m.EndDate,
            m.RewardAmount,
            m.RewardType,
            fetchedAt,
            m.MapSlug,
            m.MapName)).ToList();

        if (entities.Count > 0)
        {
            missions.AddRange(entities);
        }

        LogFetched(response.Missions.Count);

        var renderedLines = response.Missions.Select(renderer.RenderMission).ToList();
        var missionText = string.Join("\n", renderedLines);

        var readableMessage = RenderTemplate(config.Value.ReadableFormat, missionText);
        var lookupMessage = RenderTemplate(config.Value.LookupFormat, missionText);

        await TrySendAsync(readableMessage, config.Value.ReadableChannelId, "readable", cancellationToken).ConfigureAwait(false);
        await TrySendAsync(lookupMessage, config.Value.LookupChannelId, "lookup", cancellationToken).ConfigureAwait(false);

        return Unit.Value;
    }

    private async Task TrySendAsync(string message, ulong channelId, string channelLabel, CancellationToken cancellationToken)
    {
        try
        {
            await discordMessageAccess.SendMessageAsync(message, channelId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogChannelSendFailed(ex, channelLabel, channelId);
        }
    }

    private static string RenderTemplate(string template, string missionText)
    {
        var withDate = DatePlaceholderRegex.Replace(template, match =>
            DateTime.UtcNow.ToString(match.Groups[1].Value, CultureInfo.InvariantCulture));

        return withDate.Replace("{{missionText}}", missionText);
    }

    [LoggerMessage(LogLevel.Information, "Fetched {Count} daily missions from GeoGuessr.")]
    partial void LogFetched(int count);

    [LoggerMessage(LogLevel.Error, "Failed to fetch daily missions from GeoGuessr.")]
    partial void LogFetchFailed(Exception exception);

    [LoggerMessage(LogLevel.Error, "Failed to send daily missions to {Channel} channel {ChannelId}.")]
    partial void LogChannelSendFailed(Exception exception, string channel, ulong channelId);
}
