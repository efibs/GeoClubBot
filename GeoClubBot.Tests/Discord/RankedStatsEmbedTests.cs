using Discord;
using FluentAssertions;
using GeoClubBot.Discord.InputAdapters.Interactions.Users;
using UseCases.OutputPorts.GeoGuessr;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.Discord;

public sealed class RankedStatsEmbedTests
{
    private static UserDto CreateProfile() => new()
    {
        Nick = "TestPlayer",
        Created = DateTimeOffset.UnixEpoch,
        IsProUser = false,
        Type = "Standard",
        IsVerified = false,
        CustomImage = string.Empty,
        FullBodyPin = string.Empty,
        BorderUrl = string.Empty,
        Color = 0,
        Url = string.Empty,
        Id = "user-123",
        CountryCode = "DE",
        Competitive = new UserCompetitiveDto { Elo = 0, Rating = 0, LastRatingChange = 0 },
        IsBanned = false,
        ChatBan = false,
    };

    private static readonly Error SomeError = Error.NotFound("ranked_system.not_linked", "No linked account.");

    private static Result<RankedProgressResponseDto> Progress(RankedProgressResponseDto value) =>
        Result<RankedProgressResponseDto>.Success(value);

    private static Result<RankedPeakRatingResponseDto> Peak(RankedPeakRatingResponseDto value) =>
        Result<RankedPeakRatingResponseDto>.Success(value);

    [Fact]
    public void BuildRankedStatsEmbed_FullyPopulated_RendersAllFields()
    {
        var progress = Progress(new RankedProgressResponseDto
        {
            DivisionName = "Gold III",
            Rating = 1200,
            GameModeRatings = new GameModeRatingsDto { MoveDuels = 1100, NoMoveDuels = 1050, NmpzDuels = 950 },
            GuessedFirstRate = 0.5f,
            WinStreak = 7,
            LatestGames = [true, false, true],
            BestCountries = ["DE", "FR"],
            WorstCountries = ["US"],
        });
        var peak = Peak(new RankedPeakRatingResponseDto
        {
            PeakOverallRating = 1500,
            PeakGameModeRatings = new GameModeRatingsDto { MoveDuels = 1400, NoMoveDuels = 1350, NmpzDuels = 1250 },
        });

        var embed = UserInfoModule.BuildRankedStatsEmbed(CreateProfile(), progress, peak);

        embed.Title.Should().Be("🇩🇪 TestPlayer");
        Field(embed, "Division").Should().Be("Gold III");
        Field(embed, "Win streak").Should().Be("7");
        Field(embed, "Guessed first").Should().Be("50%");
        Field(embed, "Current rating").Should().Be("Overall: 1200\nMove: 1100\nNo Move: 1050\nNMPZ: 950");
        Field(embed, "Peak rating").Should().Be("Overall: 1500\nMove: 1400\nNo Move: 1350\nNMPZ: 1250");
        Field(embed, "Recent games").Should().Be("🟩🟥🟩");
        Field(embed, "Best countries").Should().Be("🇩🇪 🇫🇷");
        Field(embed, "Worst countries").Should().Be("🇺🇸");
    }

    [Fact]
    public void BuildRankedStatsEmbed_PartialProgressData_FillsMissingFieldsWithNa()
    {
        var progress = Progress(new RankedProgressResponseDto
        {
            DivisionName = null,
            Rating = null,
            GameModeRatings = null,
            GuessedFirstRate = 0f,
            WinStreak = 0,
            LatestGames = [],
            BestCountries = [],
            WorstCountries = [],
        });
        var peak = Peak(new RankedPeakRatingResponseDto { PeakOverallRating = 1500 });

        var embed = UserInfoModule.BuildRankedStatsEmbed(CreateProfile(), progress, peak);

        Field(embed, "Division").Should().Be("N/A");
        Field(embed, "Current rating").Should().Be("Overall: N/A\nMove: N/A\nNo Move: N/A\nNMPZ: N/A");
        Field(embed, "Recent games").Should().Be("N/A");
        Field(embed, "Best countries").Should().Be("N/A");
        Field(embed, "Worst countries").Should().Be("N/A");
    }

    [Fact]
    public void BuildRankedStatsEmbed_PeakRatingFailed_ShowsNaPeakButStillBuildsEmbed()
    {
        var progress = Progress(new RankedProgressResponseDto
        {
            DivisionName = "Silver I",
            Rating = 900,
            GameModeRatings = new GameModeRatingsDto { MoveDuels = 880, NoMoveDuels = 870, NmpzDuels = 820 },
            GuessedFirstRate = 0.33f,
            WinStreak = 2,
            LatestGames = [true],
            BestCountries = ["DE"],
            WorstCountries = ["US"],
        });
        var peak = Result<RankedPeakRatingResponseDto>.Failure(SomeError);

        var embed = UserInfoModule.BuildRankedStatsEmbed(CreateProfile(), progress, peak);

        Field(embed, "Division").Should().Be("Silver I");
        Field(embed, "Current rating").Should().Be("Overall: 900\nMove: 880\nNo Move: 870\nNMPZ: 820");
        Field(embed, "Peak rating").Should().Be("Overall: N/A\nMove: N/A\nNo Move: N/A\nNMPZ: N/A");
    }

    private static string Field(Embed embed, string name) =>
        embed.Fields.Single(f => f.Name == name).Value;
}
