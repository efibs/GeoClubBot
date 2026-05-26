namespace Entities;

public class Club : BaseEntity
{
    public Guid ClubId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public int Level { get; private set; }

    public DateTimeOffset? LatestActivityCheckTime { get; private set; }

    public static Club Create(Guid clubId, string name, int level, DateTimeOffset? latestActivityCheckTime = null)
    {
        return new Club
        {
            ClubId = clubId,
            Name = name,
            Level = level,
            LatestActivityCheckTime = latestActivityCheckTime
        };
    }

    public void UpdateLevel(int newLevel) => Level = newLevel;

    public void Rename(string newName) => Name = newName;

    public void RecordActivityCheck(DateTimeOffset checkedAt) => LatestActivityCheckTime = checkedAt;

    public override string ToString() => Name;

    private Club()
    {
    }
}
