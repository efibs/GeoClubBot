namespace Entities;

/// <summary>
/// The last state of a club's mission board the change notifications (#504) saw: which week, and
/// which board was the current one. Stored so a restart neither repeats a DM nor misses a board
/// unlocked while the bot was down.
/// </summary>
public class ClubMissionBoardObservation : BaseEntity
{
    public Guid ClubId { get; private set; }

    public DateTimeOffset PeriodStart { get; private set; }

    public int CurrentBoardNumber { get; private set; }

    public bool AllBoardsCleared { get; private set; }

    public DateTimeOffset ObservedAt { get; private set; }

    public static ClubMissionBoardObservation Create(Guid clubId, ClubMissionBoardWeek week, DateTimeOffset now)
    {
        var observation = new ClubMissionBoardObservation { ClubId = clubId };
        observation.Update(week, now);
        return observation;
    }

    public void Update(ClubMissionBoardWeek week, DateTimeOffset now)
    {
        PeriodStart = week.PeriodStart;
        CurrentBoardNumber = week.CurrentBoardNumber;
        AllBoardsCleared = week.AllBoardsCleared;
        ObservedAt = now;
    }

    private ClubMissionBoardObservation()
    {
    }
}
