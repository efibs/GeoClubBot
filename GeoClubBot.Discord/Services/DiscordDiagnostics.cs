using System.Diagnostics;

namespace GeoClubBot.Discord.Services;

/// <summary>
/// Shared <see cref="ActivitySource"/> for Discord interactions. Discord commands arrive over
/// the gateway WebSocket (not ASP.NET Core), so without this they would produce no traces.
/// <see cref="InteractionHandler"/> opens one root span per interaction; the MediatR use-case
/// spans nest underneath. Tracer providers subscribe via <c>AddSource(<see cref="ActivitySourceName"/>)</c>.
/// </summary>
public static class DiscordDiagnostics
{
    public const string ActivitySourceName = "GeoClubBot.Discord";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, version: "1.0.0");
}
