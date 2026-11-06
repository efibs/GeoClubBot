using System.ComponentModel.DataAnnotations;

namespace Configuration;

public class MemberPrivateChannelsConfiguration : IValidatableObject
{
    public const string SectionName = "MemberPrivateChannels";

    /// <summary>The placeholder in <see cref="CategoryNameTemplate"/> replaced by the club's name.</summary>
    public const string ClubPlaceholder = "{club}";

    /// <summary>Discord's own cap on the channels of one category.</summary>
    public const int DiscordMaxChannelsPerCategory = 50;

    [Required(AllowEmptyStrings = false)]
    public required string Description { get; set; }

    /// <summary>
    /// Name of the categories the bot creates per club for its members' channels. A club's second
    /// category gets " 2" appended, the third " 3", and so on.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string CategoryNameTemplate { get; set; } = "{club} – Private";

    /// <summary>
    /// How many channels the bot puts into one category (club or archive) before it opens the next one
    /// or, for the archive, deletes the longest-archived channel. Channels admins add by hand count too.
    /// </summary>
    [Range(1, DiscordMaxChannelsPerCategory)]
    public int MaxChannelsPerCategory { get; set; } = 45;

    /// <summary>
    /// Roles that see every member's private channel and every club's category, besides the member and
    /// anyone with the Administrator permission.
    /// </summary>
    public List<ulong> AdminRoleIds { get; set; } = [];

    /// <summary>
    /// Whether the start-up sync is followed by a reconcile that moves every private channel where it
    /// belongs, recreates missing ones and fixes their permissions.
    /// </summary>
    public bool ReconcileOnStartup { get; set; } = true;

    /// <summary>
    /// Category the channel of a member who left every club is moved to. The channel keeps its
    /// permission overwrites, so admins can still write to the member and the member can reply.
    /// When it is full, the channel archived the longest is deleted to make room.
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

    /// <summary>The name of a club's <paramref name="index"/>-th category (1-based).</summary>
    public string CategoryName(string clubName, int index)
    {
        var name = CategoryNameTemplate.Replace(ClubPlaceholder, clubName, StringComparison.Ordinal);
        return index <= 1 ? name : $"{name} {index}";
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!CategoryNameTemplate.Contains(ClubPlaceholder, StringComparison.Ordinal))
        {
            yield return new ValidationResult(
                $"{nameof(CategoryNameTemplate)} must contain {ClubPlaceholder}, or every club's category would share one name.",
                [nameof(CategoryNameTemplate)]);
        }
    }
}
