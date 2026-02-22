using System.ComponentModel.DataAnnotations;

namespace Configuration;

public class GeoGuessrClubEntry
{
    [Required]
    public Guid ClubId { get; set; }

    [Required(AllowEmptyStrings = false)]
    public required string NcfaToken { get; set; }

    public bool IsMain { get; set; }
}
