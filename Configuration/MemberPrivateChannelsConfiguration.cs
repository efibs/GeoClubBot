using System.ComponentModel.DataAnnotations;

namespace Configuration;

public class MemberPrivateChannelsConfiguration
{
    public const string SectionName = "MemberPrivateChannels";

    [Required]
    public required ulong CategoryId { get; set; }

    [Required(AllowEmptyStrings = false)]
    public required string Description { get; set; }

    /// <summary>
    /// Category the channel of a member who left every club is moved to. The channel keeps its
    /// permission overwrites, so admins can still write to the member and the member can reply.
    /// </summary>
    [Required]
    public required ulong ArchiveCategoryId { get; set; }

    /// <summary>
    /// How long a channel stays archived before it is deleted for good. Measured from the moment
    /// the leave was detected; rejoining a club restores the channel and stops the clock.
    /// </summary>
    [Required]
    public required TimeSpan ArchiveKeepTimeSpan { get; set; }

    /// <summary>Cron schedule of the sweep that deletes expired archived channels.</summary>
    [Required(AllowEmptyStrings = false)]
    public required string ArchiveCleanupSchedule { get; set; }
}
