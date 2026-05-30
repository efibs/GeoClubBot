using System.ComponentModel.DataAnnotations;

namespace Configuration;

public class GeoGuessrAccountLinkingConfiguration
{
    public const string SectionName = "GeoGuessrAccountLinking";

    [Required]
    public required ulong AdminChannelId { get; set; }

    [Required]
    public required ulong HasLinkedRoleId { get; set; }
}
