using Entities;
using FluentAssertions;
using NSubstitute;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.Excuses;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.Excuses;

public sealed class ReadRelevantExcusesHandlerTests
{
    private readonly IExcusesRepository _excuses = Substitute.For<IExcusesRepository>();

    private ReadRelevantExcusesHandler CreateHandler() => new(_excuses);

    [Fact]
    public async Task Handle_ReturnsSuccess_WithListFromRepository()
    {
        var now = DateTimeOffset.UtcNow;
        var expected = new List<ClubMemberRelevantExcuse>
        {
            new("Player1", new TimeRange(now.AddDays(-1), now.AddDays(3)), false),
            new("Player2", new TimeRange(now.AddDays(1), now.AddDays(5)), true)
        };
        _excuses.ReadAllRelevantExcusesAsync(7, Arg.Any<CancellationToken>()).Returns(expected);

        var result = await CreateHandler().Handle(new ReadRelevantExcusesQuery(7), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task Handle_PassesUpcomingExcusesNumDays_ToRepository()
    {
        _excuses.ReadAllRelevantExcusesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);

        await CreateHandler().Handle(new ReadRelevantExcusesQuery(14), CancellationToken.None);

        await _excuses.Received(1).ReadAllRelevantExcusesAsync(14, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReturnsEmptySuccess_WhenRepositoryReturnsNoExcuses()
    {
        _excuses.ReadAllRelevantExcusesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);

        var result = await CreateHandler().Handle(new ReadRelevantExcusesQuery(7), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }
}
