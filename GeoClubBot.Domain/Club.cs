using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Constants;

namespace Entities;

public class Club
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid ClubId { get; set; }
    
    [MaxLength(StringLengthConstants.GeoGuessrClubNameMaxLength)]
    public string Name { get; set; } = string.Empty;
    
    public int Level { get; set; }
    
    public DateTimeOffset? LatestActivityCheckTime { get; set; }
    
    public List<ClubMember>? Members { get; set; }

    public override string ToString()
    {
        return Name;
    }

    public Club DeepCopy()
    {
        return new Club
        {
            ClubId = ClubId,
            Name = Name,
            Level = Level,
            LatestActivityCheckTime = LatestActivityCheckTime,
            Members = Members?.Select(m => m.DeepCopy()).ToList()
        };
    }
}