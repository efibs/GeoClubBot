using Entities;

namespace UseCases.OutputPorts.GeoGuessr.Assemblers;

public static class ClubMissionBoardAssembler
{
    public static ClubMissionBoardWeek AssembleEntity(ClubMissionBoardSnapshotDto dto)
    {
        var boards = dto.Boards
            .OrderBy(b => b.Number)
            .Select(b => new ClubMissionBoard(
                b.Number,
                b.Size,
                b.ClearRewardXp,
                b.ClearedAt,
                b.Tiles
                    .OrderBy(t => t.Index)
                    .Select(t => new ClubMissionTile(
                        t.MissionId,
                        b.Number,
                        t.Index,
                        t.TemplateId,
                        ClubMissionTitleRenderer.Render(t, dto.Templates.GetValueOrDefault(t.TemplateId)),
                        t.TargetProgress,
                        t.CurrentProgress,
                        t.RewardXp,
                        t.ClaimedBy,
                        t.ClaimedAt,
                        t.Helpers,
                        t.HelpRequestedAt,
                        t.Completed,
                        t.CompletedAt))
                    .ToList()))
            .ToList();

        return new ClubMissionBoardWeek(
            dto.PeriodStart,
            dto.PeriodEnd,
            dto.CurrentBoardNumber,
            dto.AllBoardsCleared,
            boards,
            dto.You?.NextDayAt);
    }
}
