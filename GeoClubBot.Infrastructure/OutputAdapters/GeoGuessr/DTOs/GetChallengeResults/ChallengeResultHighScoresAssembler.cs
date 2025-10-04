using Entities;

namespace Infrastructure.OutputAdapters.GeoGuessr.DTOs.GetChallengeResults;

public static class ChallengeResultHighScoresAssembler
{
    public static List<ClubChallengeResultPlayer> AssembleEntities(ChallengeResultHighscoresDto dtos)
    {
        return dtos.Items.Select(AssembleEntity).ToList();
    }
    
    public static ClubChallengeResultPlayer AssembleEntity(ChallengeResultItemDto dto)
    {
        return new ClubChallengeResultPlayer(
            dto.Game.Player.Id,
            dto.Game.Player.Nick,
            $"{dto.Game.Player.TotalScore.Amount} {dto.Game.Player.TotalScore.Unit}",
            $"{dto.Game.Player.TotalDistance.Meters.Amount}{dto.Game.Player.TotalDistance.Meters.Unit}");
    }
}