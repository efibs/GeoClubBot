using System.ComponentModel.DataAnnotations;

namespace Configuration;

public class MemberPrivateChannelsConfiguration
{
    public const string SectionName = "MemberPrivateChannels";

    [Required]
    public required ulong CategoryId { get; set; }

    [Required(AllowEmptyStrings = false)]
    public required string Description { get; set; }
}
