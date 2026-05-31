using FluentAssertions;
using GeoClubBot.Discord.InputAdapters.Interactions.Users;
using UseCases.OutputPorts.GeoGuessr;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.Discord;

public sealed class UserInfoEmbedTests
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
        CountryCode = string.Empty,
        Competitive = new UserCompetitiveDto { Elo = 0, Rating = 0, LastRatingChange = 0 },
        IsBanned = false,
        ChatBan = false,
    };

    private static RankedProgressResponseDto MakeProgress(int? rating = null) => new()
    {
        Rating = rating,
        GuessedFirstRate = 0f,
        WinStreak = 0,
        LatestGames = [],
        BestCountries = [],
        WorstCountries = []
    };

    private static readonly Error SomeError = Error.NotFound("ranked_system.not_linked", "No linked account.");

    [Fact]
    public void BuildProfileEmbed_ShowsRating_WhenRankedProgressHasRating()
    {
        var rankedProgress = Result<RankedProgressResponseDto>.Success(MakeProgress(rating: 1234));
        var peakRating = Result<RankedPeakRatingResponseDto>.Failure(SomeError);

        var embed = UserInfoModule.BuildProfileEmbed(CreateProfile(), rankedProgress, peakRating);

        var ratingField = embed.Fields.Single(f => f.Name is "Rating" or "Peak rating");
        ratingField.Name.Should().Be("Rating");
        ratingField.Value.Should().Be("1234");
    }

    [Fact]
    public void BuildProfileEmbed_ShowsPeakRating_WhenRankedProgressHasNullRating()
    {
        var rankedProgress = Result<RankedProgressResponseDto>.Success(MakeProgress(rating: null));
        var peakRating = Result<RankedPeakRatingResponseDto>.Success(new RankedPeakRatingResponseDto { PeakOverallRating = 5678 });

        var embed = UserInfoModule.BuildProfileEmbed(CreateProfile(), rankedProgress, peakRating);

        var ratingField = embed.Fields.Single(f => f.Name is "Rating" or "Peak rating");
        ratingField.Name.Should().Be("Peak rating");
        ratingField.Value.Should().Be("5678");
    }

    [Fact]
    public void BuildProfileEmbed_ShowsPeakRating_WhenRankedProgressFailed()
    {
        var rankedProgress = Result<RankedProgressResponseDto>.Failure(SomeError);
        var peakRating = Result<RankedPeakRatingResponseDto>.Success(new RankedPeakRatingResponseDto { PeakOverallRating = 5678 });

        var embed = UserInfoModule.BuildProfileEmbed(CreateProfile(), rankedProgress, peakRating);

        var ratingField = embed.Fields.Single(f => f.Name is "Rating" or "Peak rating");
        ratingField.Name.Should().Be("Peak rating");
        ratingField.Value.Should().Be("5678");
    }

    [Fact]
    public void BuildProfileEmbed_ShowsNaRating_WhenNeitherRatingIsAvailable()
    {
        var rankedProgress = Result<RankedProgressResponseDto>.Failure(SomeError);
        var peakRating = Result<RankedPeakRatingResponseDto>.Failure(SomeError);

        var embed = UserInfoModule.BuildProfileEmbed(CreateProfile(), rankedProgress, peakRating);

        var ratingField = embed.Fields.Single(f => f.Name is "Rating" or "Peak rating");
        ratingField.Name.Should().Be("Peak rating");
        ratingField.Value.Should().Be("N/A");
    }
}
