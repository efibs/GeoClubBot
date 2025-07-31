using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Constants;

namespace Entities;

public class ClubChallengeLink
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public string Difficulty { get; set; } = string.Empty;

    public int RolePriority { get; set; }
    
    [MaxLength(StringLengthConstants.GeoGuessrChallengeIdLength)]
    public string ChallengeId { get; set; } = string.Empty;
}