using Entities;
using FluentAssertions;
using NSubstitute;
using UseCases.OutputPorts;
using UseCases.UseCases.Excuses;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.Excuses;

public sealed class ExcuseHandlerTests
{
    private readonly IExcusesRepository _excuses = Substitute.For<IExcusesRepository>();

    private static ClubMemberExcuse SampleExcuse() =>
        ClubMemberExcuse.Create("user-1", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(5));

    [Fact]
    public async Task Update_AppliesNewTimeRange_OnExistingExcuse()
    {
        var excuse = SampleExcuse();
        _excuses.ReadForUpdateByIdAsync(excuse.ExcuseId, Arg.Any<CancellationToken>()).Returns(excuse);
        var newFrom = DateTimeOffset.UtcNow;
        var newTo = newFrom.AddDays(10);

        var result = await new UpdateExcuseHandler(_excuses)
            .Handle(new UpdateExcuseCommand(excuse.ExcuseId, newFrom, newTo), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.From.Should().Be(newFrom);
        result.Value.To.Should().Be(newTo);
    }

    [Fact]
    public async Task Update_ReturnsNotFound_WhenExcuseMissing()
    {
        var id = Guid.NewGuid();
        _excuses.ReadForUpdateByIdAsync(id, Arg.Any<CancellationToken>()).Returns((ClubMemberExcuse?)null);

        var result = await new UpdateExcuseHandler(_excuses)
            .Handle(new UpdateExcuseCommand(id, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1)), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("excuse.not_found");
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Remove_DeletesExcuse_WhenItExists()
    {
        var excuse = SampleExcuse();
        _excuses.ReadForUpdateByIdAsync(excuse.ExcuseId, Arg.Any<CancellationToken>()).Returns(excuse);

        var result = await new RemoveExcuseHandler(_excuses)
            .Handle(new RemoveExcuseCommand(excuse.ExcuseId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _excuses.Received(1).DeleteExcuse(excuse);
    }

    [Fact]
    public async Task Remove_ReturnsNotFound_AndDoesNotDelete_WhenMissing()
    {
        var id = Guid.NewGuid();
        _excuses.ReadForUpdateByIdAsync(id, Arg.Any<CancellationToken>()).Returns((ClubMemberExcuse?)null);

        var result = await new RemoveExcuseHandler(_excuses)
            .Handle(new RemoveExcuseCommand(id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("excuse.not_found");
        _excuses.DidNotReceive().DeleteExcuse(Arg.Any<ClubMemberExcuse>());
    }
}
