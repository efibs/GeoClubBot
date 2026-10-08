using System.ComponentModel.DataAnnotations;

namespace Configuration;

public class DiscordConfiguration : IValidatableObject
{
    public const string SectionName = "Discord";

    [Required(AllowEmptyStrings = false)]
    public required string BotToken { get; set; }

    [Required]
    public required ulong ServerId { get; set; }

    [Required(AllowEmptyStrings = false)]
    public required string WelcomeMessage { get; set; }

    [Required]
    public required ulong WelcomeTextChannelId { get; set; }

    [Required(AllowEmptyStrings = false)]
    public required string LeftMessage { get; set; }

    [Required]
    public required ulong LeftTextChannelId { get; set; }

    /// <summary>How long the server's online member count is cached. A failed read is not cached.</summary>
    public TimeSpan OnlineCountCacheTimeToLive { get; set; } = TimeSpan.FromMinutes(1);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (OnlineCountCacheTimeToLive <= TimeSpan.Zero)
        {
            yield return new ValidationResult(
                $"{nameof(OnlineCountCacheTimeToLive)} must be greater than zero.");
        }
    }
}
