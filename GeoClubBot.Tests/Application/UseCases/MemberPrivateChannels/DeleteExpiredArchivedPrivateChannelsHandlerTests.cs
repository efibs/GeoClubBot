using Configuration;
using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.MemberPrivateChannels;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.MemberPrivateChannels;

public sealed class DeleteExpiredArchivedPrivateChannelsHandlerTests
{
    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly IClubMemberRepository _clubMembers = Substitute.For<IClubMemberRepository>();

    private readonly ILogger<DeleteExpiredArchivedPrivateChannelsHandler> _logger =
        Substitute.For<ILogger<DeleteExpiredArchivedPrivateChannelsHandler>>();

    [Fact]
    public async Task Handle_QueriesWithTheConfiguredRetentionSpanAsThreshold()
    {
        var keep = TimeSpan.FromDays(30);
        var handler = CreateHandler(keep);

        _clubMembers
            .ReadMembersWithExpiredArchivedPrivateChannelsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var before = DateTimeOffset.UtcNow;

        await handler.Handle(new DeleteExpiredArchivedPrivateChannelsCommand(), CancellationToken.None);

        var after = DateTimeOffset.UtcNow;

        await _clubMembers.Received(1).ReadMembersWithExpiredArchivedPrivateChannelsAsync(
            Arg.Is<DateTimeOffset>(ts => ts >= before - keep && ts <= after - keep),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DeletesEveryExpiredChannel_AndCountsTheSuccesses()
    {
        var handler = CreateHandler(TimeSpan.FromDays(30));

        var expired = new List<ClubMember>
        {
            new ClubMemberBuilder().WithUserId("user-a").InClub(null)
                .WithPrivateChannel(111UL, DateTimeOffset.UtcNow.AddDays(-40)).Build(),
            new ClubMemberBuilder().WithUserId("user-b").InClub(null)
                .WithPrivateChannel(222UL, DateTimeOffset.UtcNow.AddDays(-50)).Build(),
        };

        _clubMembers
            .ReadMembersWithExpiredArchivedPrivateChannelsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(expired);

        _mediator
            .Send(Arg.Any<DeleteMemberPrivateChannelCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var result = await handler.Handle(new DeleteExpiredArchivedPrivateChannelsCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(2);

        await _mediator.Received(1).Send(
            Arg.Is<DeleteMemberPrivateChannelCommand>(c => c.ClubMember!.UserId == "user-a"),
            Arg.Any<CancellationToken>());
        await _mediator.Received(1).Send(
            Arg.Is<DeleteMemberPrivateChannelCommand>(c => c.ClubMember!.UserId == "user-b"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DoesNotCountAFailedDeletion()
    {
        var handler = CreateHandler(TimeSpan.FromDays(30));

        _clubMembers
            .ReadMembersWithExpiredArchivedPrivateChannelsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([
                new ClubMemberBuilder().InClub(null)
                    .WithPrivateChannel(111UL, DateTimeOffset.UtcNow.AddDays(-40)).Build()
            ]);

        _mediator
            .Send(Arg.Any<DeleteMemberPrivateChannelCommand>(), Arg.Any<CancellationToken>())
            .Returns(Error.Unexpected("member_private_channel.delete_failed", "nope"));

        var result = await handler.Handle(new DeleteExpiredArchivedPrivateChannelsCommand(), CancellationToken.None);

        result.Value.Should().Be(0);
    }

    private DeleteExpiredArchivedPrivateChannelsHandler CreateHandler(TimeSpan keep)
    {
        var options = Options.Create(new MemberPrivateChannelsConfiguration
        {
            CategoryId = 1UL,
            Description = "Private channel",
            ArchiveCategoryId = 2UL,
            ArchiveKeepTimeSpan = keep,
            ArchiveCleanupSchedule = "0 0 3 * * ?"
        });

        return new DeleteExpiredArchivedPrivateChannelsHandler(_mediator, _clubMembers, options, _logger);
    }
}
