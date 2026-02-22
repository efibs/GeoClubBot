using System.ComponentModel.DataAnnotations;

namespace Configuration;

public class GeoGuessrConfiguration : IValidatableObject
{
    public const string SectionName = "GeoGuessr";

    [Required(AllowEmptyStrings = false)]
    public required string SyncSchedule { get; set; }

    [Required]
    [MinLength(1)]
    public required List<GeoGuessrClubEntry> Clubs { get; set; }

    public GeoGuessrClubEntry MainClub => Clubs.Single(c => c.IsMain);

    public GeoGuessrClubEntry GetClub(Guid clubId) => Clubs.Single(c => c.ClubId == clubId);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var mainClubs = Clubs.Where(c => c.IsMain).ToList();

        if (mainClubs.Count == 0)
        {
            yield return new ValidationResult("Exactly one club must have IsMain set to true. None found.");
        }
        else if (mainClubs.Count > 1)
        {
            yield return new ValidationResult(
                $"Exactly one club must have IsMain set to true. Found {mainClubs.Count}.");
        }

        var duplicateClubIds = Clubs.GroupBy(c => c.ClubId).Where(g => g.Count() > 1).ToList();
        if (duplicateClubIds.Count != 0)
        {
            yield return new ValidationResult(
                $"Duplicate ClubId(s) found: {string.Join(", ", duplicateClubIds.Select(g => g.Key))}");
        }

        foreach (var club in Clubs)
        {
            if (string.IsNullOrWhiteSpace(club.NcfaToken))
            {
                yield return new ValidationResult($"Club {club.ClubId} has an empty NcfaToken.");
            }
        }
    }
}
