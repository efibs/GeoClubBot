using Entities;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.RankedSystem;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.RankedSystem;

public sealed class RankedSystemQueriesHandlerTests
{
    private const ulong DiscordUserId = 42UL;
    private const string GeoGuessrUserId = "gg-user-1";

    private readonly IGeoGuessrUserRepository _users = Substitute.For<IGeoGuessrUserRepository>();
    private readonly IGeoGuessrUserRankedSystemReader _reader = Substitute.For<IGeoGuessrUserRankedSystemReader>();

    private RankedSystemQueriesHandler CreateHandler() => new(_users, _reader);

    private static RankedProgressResponseDto MakeProgressDto(int? rating = null) => new()
    {
        Rating = rating,
        GuessedFirstRate = 0f,
        WinStreak = 0,
        LatestGames = [],
        BestCountries = [],
        WorstCountries = []
    };

    // --- GetUserRankedProgressQuery ---

    [Fact]
    public async Task GetProgress_ReturnsNotLinked_WhenUserNotFound()
    {
        _users.ReadUserByDiscordUserIdAsync(DiscordUserId, Arg.Any<CancellationToken>())
            .Returns((GeoGuessrUser?)null);

        var result = await CreateHandler().Handle(
            new GetUserRankedProgressQuery(DiscordUserId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ranked_system.not_linked");
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task GetProgress_ReturnsProgressNotFound_WhenReaderReturnsNull()
    {
        _users.ReadUserByDiscordUserIdAsync(DiscordUserId, Arg.Any<CancellationToken>())
            .Returns(GeoGuessrUser.Create(GeoGuessrUserId, "Player"));
        _reader.ReadRankedProgressOfUserAsync(GeoGuessrUserId, Arg.Any<CancellationToken>())
            .Returns((RankedProgressResponseDto?)null);

        var result = await CreateHandler().Handle(
            new GetUserRankedProgressQuery(DiscordUserId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ranked_system.progress_not_found");
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task GetProgress_ReturnsDto_WhenFound()
    {
        var user = GeoGuessrUser.Create(GeoGuessrUserId, "Player");
        var dto = MakeProgressDto(rating: 1500);
        _users.ReadUserByDiscordUserIdAsync(DiscordUserId, Arg.Any<CancellationToken>()).Returns(user);
        _reader.ReadRankedProgressOfUserAsync(GeoGuessrUserId, Arg.Any<CancellationToken>()).Returns(dto);

        var result = await CreateHandler().Handle(
            new GetUserRankedProgressQuery(DiscordUserId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(dto);
    }

    // --- GetUserRankedPeakRatingQuery ---

    [Fact]
    public async Task GetPeakRating_ReturnsNotLinked_WhenUserNotFound()
    {
        _users.ReadUserByDiscordUserIdAsync(DiscordUserId, Arg.Any<CancellationToken>())
            .Returns((GeoGuessrUser?)null);

        var result = await CreateHandler().Handle(
            new GetUserRankedPeakRatingQuery(DiscordUserId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ranked_system.not_linked");
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task GetPeakRating_ReturnsPeakRatingNotFound_WhenReaderReturnsNull()
    {
        _users.ReadUserByDiscordUserIdAsync(DiscordUserId, Arg.Any<CancellationToken>())
            .Returns(GeoGuessrUser.Create(GeoGuessrUserId, "Player"));
        _reader.ReadRankedPeakRatingOfUserAsync(GeoGuessrUserId, Arg.Any<CancellationToken>())
            .Returns((RankedPeakRatingResponseDto?)null);

        var result = await CreateHandler().Handle(
            new GetUserRankedPeakRatingQuery(DiscordUserId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ranked_system.peak_rating_not_found");
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task GetPeakRating_ReturnsPeakRatingNotFound_WhenReaderThrows()
    {
        _users.ReadUserByDiscordUserIdAsync(DiscordUserId, Arg.Any<CancellationToken>())
            .Returns(GeoGuessrUser.Create(GeoGuessrUserId, "Player"));
        _reader.ReadRankedPeakRatingOfUserAsync(GeoGuessrUserId, Arg.Any<CancellationToken>())
            .Throws(new HttpRequestException("API unavailable"));

        var result = await CreateHandler().Handle(
            new GetUserRankedPeakRatingQuery(DiscordUserId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ranked_system.peak_rating_not_found");
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task GetPeakRating_ReturnsDto_WhenFound()
    {
        var user = GeoGuessrUser.Create(GeoGuessrUserId, "Player");
        var dto = new RankedPeakRatingResponseDto { PeakOverallRating = 2000 };
        _users.ReadUserByDiscordUserIdAsync(DiscordUserId, Arg.Any<CancellationToken>()).Returns(user);
        _reader.ReadRankedPeakRatingOfUserAsync(GeoGuessrUserId, Arg.Any<CancellationToken>()).Returns(dto);

        var result = await CreateHandler().Handle(
            new GetUserRankedPeakRatingQuery(DiscordUserId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(dto);
    }
}
