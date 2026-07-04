using Entities;

namespace GeoClubBot.DTOs.Assemblers;

public static class ActivityAdminDtoAssembler
{
    public static AdminStrikeDto AssembleStrike(ClubMemberStrike strike, TimeSpan strikeDecay) => new(
        strike.StrikeId,
        strike.ClubMember?.User?.Nickname ?? "N/A",
        strike.Timestamp,
        strike.Revoked,
        strike.Timestamp + strikeDecay);

    /// <summary>For strikes read via a member lookup, where the nickname is already known.</summary>
    public static AdminStrikeDto AssembleStrike(ClubMemberStrike strike, string nickname, TimeSpan strikeDecay) => new(
        strike.StrikeId,
        nickname,
        strike.Timestamp,
        strike.Revoked,
        strike.Timestamp + strikeDecay);

    public static AdminExcuseDto AssembleExcuse(ClubMemberExcuse excuse) => new(
        excuse.ExcuseId,
        excuse.ClubMember?.User?.Nickname ?? "N/A",
        excuse.From,
        excuse.To);

    public static AdminLinkRequestDto AssembleLinkRequest(GeoGuessrAccountLinkingRequest request) => new(
        request.DiscordUserId.ToString(),
        request.GeoGuessrUserId);

    public static PlayerStatisticsDto AssemblePlayerStatistics(PlayerStatistics statistics) => new(
        statistics.Nickname,
        statistics.HistorySince,
        statistics.NumHistoryEntries,
        statistics.AveragePoints,
        statistics.MinPoints,
        statistics.FirstQuartilePoints,
        statistics.MedianPoints,
        statistics.ThirdQuartilePoints,
        statistics.MaxPoints);

    public static ClubStatisticsDto AssembleClubStatistics(ClubStatistics statistics) => new(
        statistics.ClubName,
        statistics.AverageAveragePoints,
        statistics.MinAveragePoints,
        statistics.FirstQuartileAveragePoints,
        statistics.MedianAveragePoints,
        statistics.ThirdQuartileAveragePoints,
        statistics.MaxAveragePoints);
}
