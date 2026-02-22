using System.ComponentModel.DataAnnotations;

namespace Configuration;

public class GeoGuessrClubEntry
{
    [Required]
    public Guid ClubId { get; set; }

    [Required(AllowEmptyStrings = false)]
    public required string NcfaToken { get; set; }

    public bool IsMain { get; set; }

    public int? MinXP { get; set; }

    public int? GracePeriodDays { get; set; }

    public int? MaxNumStrikes { get; set; }

    public int GetMinXP(ActivityCheckerConfiguration defaults) => MinXP ?? defaults.MinXP;

    public int GetGracePeriodDays(ActivityCheckerConfiguration defaults) => GracePeriodDays ?? defaults.GracePeriodDays;

    public int GetMaxNumStrikes(ActivityCheckerConfiguration defaults) => MaxNumStrikes ?? defaults.MaxNumStrikes;
}
