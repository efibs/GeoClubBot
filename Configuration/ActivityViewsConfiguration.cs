using System.ComponentModel.DataAnnotations;

namespace Configuration;

/// <summary>The members' own activity views: the <c>last-days</c> commands and the dashboard.</summary>
public class ActivityViewsConfiguration
{
    public const string SectionName = "ActivityViews";

    /// <summary>
    /// Longest window the <c>last-days</c> views accept. GeoGuessr's activity feed is read live and
    /// reaches back a little over two weeks, so going far beyond 14 shows empty days.
    /// </summary>
    [Range(1, 60)]
    public int MaxDaysBack { get; set; } = 14;

    /// <summary>Show how many missions of others a member pressed "help out" on (unverified).</summary>
    public bool ShowHelps { get; set; } = true;
}
