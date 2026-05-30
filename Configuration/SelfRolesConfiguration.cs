using System.ComponentModel.DataAnnotations;
using Entities;

namespace Configuration;

public class SelfRolesConfiguration
{
    public const string SectionName = "SelfRoles";

    [Required]
    public required ulong TextChannelId { get; set; }

    public List<SelfRoleSetting> Roles { get; set; } = [];
}
