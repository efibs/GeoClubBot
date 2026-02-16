namespace Entities;

public class DailyMissionReminder
{
    public required ulong DiscordUserId { get; set; }

    public required TimeOnly ReminderTimeUtc { get; set; }

    public string? TimeZoneId { get; set; }

    public string? CustomMessage { get; set; }

    public DateOnly? LastSentDateUtc { get; set; }
}
