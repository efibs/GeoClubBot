using System.ComponentModel.DataAnnotations;

namespace Configuration;

/// <summary>
/// GeoGuessr's WebSocket, held once per configured club with that club's token: GeoGuessr pushes
/// member joins and leaves, club updates and mission board changes on the club's topic, so the bot
/// reacts within seconds instead of at the next scheduled sync. The scheduled jobs stay as the
/// safety net, and a club whose socket is down or off is refreshed every
/// <see cref="PollWhileDisconnected"/>.
/// </summary>
/// <remarks>
/// None of these keys may contain "Schedule": start-up and the E2E host treat such keys as crons.
/// </remarks>
public class GeoGuessrSocketConfiguration : IValidatableObject
{
    public const string SectionName = "GeoGuessrSocket";

    /// <summary>Holds the sockets. Off, every club is only refreshed every <see cref="PollWhileDisconnected"/>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Applies joins and leaves from the pushed data. Off, every member event triggers a full roster
    /// sync instead — the fallback if GeoGuessr's payloads ever stop matching what the bot expects.
    /// </summary>
    public bool UsePayloads { get; set; } = true;

    /// <summary>Acts on <c>ClubMemberJoined</c> / <c>ClubMemberLeft</c>.</summary>
    public bool MemberEvents { get; set; } = true;

    /// <summary>Acts on <c>ClubMissionBoardUpdated</c> (records that club's claims).</summary>
    public bool MissionBoardEvents { get; set; } = true;

    /// <summary>Acts on <c>ClubUpdated</c> (level and name).</summary>
    public bool ClubUpdatedEvents { get; set; } = true;

    [Required(AllowEmptyStrings = false)]
    public string Url { get; set; } = "wss://api.geoguessr.com/ws";

    [Required(AllowEmptyStrings = false)]
    public string Origin { get; set; } = "https://www.geoguessr.com";

    [Required(AllowEmptyStrings = false)]
    public string UserAgent { get; set; } = "GeoClubBot (+https://github.com/efibs/GeoClubBot)";

    /// <summary>
    /// The site's build id, sent as the socket's <c>c</c> parameter. GeoGuessr closes with 4200 once
    /// it considers it outdated; take the current one from the site's page source then.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string ClientVersion { get; set; } = "web-1.8700-39ce1da";

    /// <summary>How often the site's <c>{"code":"HeartBeat"}</c> is sent. The server doesn't answer it.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>WebSocket ping interval, and how long an unanswered ping may take before the connection is dropped.</summary>
    public TimeSpan KeepAlive { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>Largest frame accepted; a bigger one closes the connection.</summary>
    [Range(1024, int.MaxValue)]
    public int MaxFrameBytes { get; set; } = 4 * 1024 * 1024;

    /// <summary>Longest wait between reconnects after ordinary failures.</summary>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>A connection that lasted this long counts as stable: the backoff starts over.</summary>
    public TimeSpan StableAfter { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>First wait after GeoGuessr closed the connection as superseded (4100), doubling up to <see cref="SupersededYieldMax"/>.</summary>
    public TimeSpan SupersededYieldInitial { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan SupersededYieldMax { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Wait after GeoGuessr called the client outdated (4200).</summary>
    public TimeSpan ClientOutdatedRetry { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>First wait after the handshake was refused with 403 (presumably Cloudflare), doubling up to <see cref="BlockedRetryMax"/>.</summary>
    public TimeSpan BlockedRetry { get; set; } = TimeSpan.FromHours(1);

    public TimeSpan BlockedRetryMax { get; set; } = TimeSpan.FromHours(6);

    /// <summary>Wait after the club's token was rejected (401, close 1001 "Unauthorized", or the club topic denied).</summary>
    public TimeSpan AuthRetry { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Events are collected for this long from the first one, then applied together: a burst becomes
    /// one batch, and a club switch (a leave on one club's socket, a join on the other's) one switch.
    /// </summary>
    public TimeSpan BatchWindow { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How often a club whose socket is down — or off — is refreshed from the API (roster, board and
    /// level). Per club: <see cref="GeoGuessrClubEntry.SocketPollWhileDisconnected"/>.
    /// </summary>
    public TimeSpan PollWhileDisconnected { get; set; } = TimeSpan.FromHours(1);

    /// <summary>A club's socket down for this long is reported (Warning, so the log channel sees it).</summary>
    public TimeSpan AlertAfter { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Least time between two reports for the same club while it stays down.</summary>
    public TimeSpan RepeatAlertAfter { get; set; } = TimeSpan.FromHours(6);

    /// <summary>Least time between two reports of a rejected token for the same club.</summary>
    public TimeSpan RepeatAuthAlertAfter { get; set; } = TimeSpan.FromDays(1);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Uri.TryCreate(Url, UriKind.Absolute, out var url) || (url.Scheme != "wss" && url.Scheme != "ws"))
        {
            yield return new ValidationResult($"{nameof(Url)} must be an absolute ws:// or wss:// address.", [nameof(Url)]);
        }

        if (!Uri.TryCreate(Origin, UriKind.Absolute, out _))
        {
            yield return new ValidationResult($"{nameof(Origin)} must be an absolute address.", [nameof(Origin)]);
        }

        foreach (var (name, value) in Durations())
        {
            if (value <= TimeSpan.Zero)
            {
                yield return new ValidationResult($"{name} must be positive.", [name]);
            }
        }

        if (BatchWindow < TimeSpan.Zero)
        {
            yield return new ValidationResult($"{nameof(BatchWindow)} must not be negative.", [nameof(BatchWindow)]);
        }

        if (SupersededYieldMax < SupersededYieldInitial)
        {
            yield return new ValidationResult(
                $"{nameof(SupersededYieldMax)} must not be shorter than {nameof(SupersededYieldInitial)}.",
                [nameof(SupersededYieldMax)]);
        }

        if (BlockedRetryMax < BlockedRetry)
        {
            yield return new ValidationResult(
                $"{nameof(BlockedRetryMax)} must not be shorter than {nameof(BlockedRetry)}.", [nameof(BlockedRetryMax)]);
        }
    }

    private IEnumerable<(string Name, TimeSpan Value)> Durations()
    {
        yield return (nameof(HeartbeatInterval), HeartbeatInterval);
        yield return (nameof(KeepAlive), KeepAlive);
        yield return (nameof(MaxBackoff), MaxBackoff);
        yield return (nameof(StableAfter), StableAfter);
        yield return (nameof(SupersededYieldInitial), SupersededYieldInitial);
        yield return (nameof(SupersededYieldMax), SupersededYieldMax);
        yield return (nameof(ClientOutdatedRetry), ClientOutdatedRetry);
        yield return (nameof(BlockedRetry), BlockedRetry);
        yield return (nameof(BlockedRetryMax), BlockedRetryMax);
        yield return (nameof(AuthRetry), AuthRetry);
        yield return (nameof(PollWhileDisconnected), PollWhileDisconnected);
        yield return (nameof(AlertAfter), AlertAfter);
        yield return (nameof(RepeatAlertAfter), RepeatAlertAfter);
        yield return (nameof(RepeatAuthAlertAfter), RepeatAuthAlertAfter);
    }
}
