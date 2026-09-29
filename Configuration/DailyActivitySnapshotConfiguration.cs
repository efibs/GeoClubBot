using System.ComponentModel.DataAnnotations;

namespace Configuration;

/// <summary>
/// The nightly snapshot of each member's club activity per UTC day, which keeps history (streaks)
/// longer than GeoGuessr's activity feed reaches back.
/// </summary>
public class DailyActivitySnapshotConfiguration
{
    public const string SectionName = "DailyActivitySnapshot";

    /// <summary>Snapshots the previous UTC day; keep it shortly after midnight UTC.</summary>
    [Required(AllowEmptyStrings = false)]
    public required string Schedule { get; set; }
}
