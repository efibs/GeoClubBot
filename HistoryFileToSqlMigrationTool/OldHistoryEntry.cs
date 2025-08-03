namespace HistoryFileToSqlMigrationTool;

public class OldHistoryEntry
{
    public string Nickname { get; set; } = string.Empty;

    public int Xp { get; set; }

    public DateTimeOffset Timestamp { get; set; }
}