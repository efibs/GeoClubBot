namespace Entities;

public class DailyMissionReminder : BaseEntity
{
    // A user can have several reminders, so the identity is a surrogate key rather than the
    // Discord user id (which is now just an indexed foreign-key-style column).
    public Guid Id { get; private set; }

    public ulong DiscordUserId { get; private set; }

    public TimeOnly ReminderTimeUtc { get; private set; }

    public string? TimeZoneId { get; private set; }

    public string? CustomMessage { get; private set; }

    public DateOnly? LastSentDateUtc { get; private set; }

    public static DailyMissionReminder Create(
        ulong discordUserId,
        TimeOnly reminderTimeUtc,
        string? timeZoneId,
        string? customMessage)
    {
        return new DailyMissionReminder
        {
            Id = Guid.NewGuid(),
            DiscordUserId = discordUserId,
            ReminderTimeUtc = reminderTimeUtc,
            TimeZoneId = timeZoneId,
            CustomMessage = customMessage
        };
    }

    public void UpdateSchedule(TimeOnly reminderTimeUtc, string? timeZoneId, string? customMessage)
    {
        ReminderTimeUtc = reminderTimeUtc;
        TimeZoneId = timeZoneId;
        CustomMessage = customMessage;
        LastSentDateUtc = null;
    }

    public void MarkSent(DateOnly today) => LastSentDateUtc = today;

    private DailyMissionReminder()
    {
    }
}
