using System.ComponentModel.DataAnnotations;

namespace Configuration;

public class ActivityRewardConfiguration
{
    public const string SectionName = "ActivityReward";
    
    [Required]
    public ulong TextChannelId { get; set; }
    
    [Required]
    public ulong MvpRoleId { get; set; }
}