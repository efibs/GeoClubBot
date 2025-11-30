using System.ComponentModel.DataAnnotations;

namespace Configuration;

public class DiscordConfiguration
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
}