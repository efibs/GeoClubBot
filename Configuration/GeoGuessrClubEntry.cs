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

    public int? AverageXpTopN { get; set; }

    public int? AverageXpBottomN { get; set; }

    public int? AverageXpHistoryDepth { get; set; }

    public int GetMinXP(ActivityCheckerConfiguration defaults) => MinXP ?? defaults.MinXP;

    public int GetGracePeriodDays(ActivityCheckerConfiguration defaults) => GracePeriodDays ?? defaults.GracePeriodDays;

    public int GetMaxNumStrikes(ActivityCheckerConfiguration defaults) => MaxNumStrikes ?? defaults.MaxNumStrikes;

    public int? GetAverageXpTopN(ActivityCheckerConfiguration defaults) => AverageXpTopN ?? defaults.AverageXpTopN;

    public int? GetAverageXpBottomN(ActivityCheckerConfiguration defaults) => AverageXpBottomN ?? defaults.AverageXpBottomN;

    public int GetAverageXpHistoryDepth(ActivityCheckerConfiguration defaults) => AverageXpHistoryDepth ?? defaults.AverageXpHistoryDepth;
}
