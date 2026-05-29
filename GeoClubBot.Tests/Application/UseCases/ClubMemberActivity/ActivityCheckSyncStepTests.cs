using FluentAssertions;
using MediatR;
using NSubstitute;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.UseCases.ClubMemberActivity.ActivityCheckPhases;
using UseCases.UseCases.ClubMembers;
using UseCases.UseCases.Strikes;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.ClubMemberActivity;

public sealed class ActivityCheckSyncStepTests
{
    private static readonly Guid ClubId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly IGeoGuessrClientFactory _clientFactory = Substitute.For<IGeoGuessrClientFactory>();
    private readonly IGeoGuessrClient _client = Substitute.For<IGeoGuessrClient>();
    private readonly ISender _mediator = Substitute.For<ISender>();

    public ActivityCheckSyncStepTests()
    {
        _clientFactory.CreateClient(Arg.Any<Guid>()).Returns(_client);
    }

    [Fact]
    public async Task Execute_DispatchesStrikeDecayAndSavesMembers()
    {
        _client.ReadClubMembersAsync(ClubId, Arg.Any<CancellationToken>())
            .Returns(new List<ClubMemberDto>
            {
                BuildApiMember("user-1", "Player1", xp: 1000, DateTimeOffset.UtcNow.AddMonths(-6))
            });

        var step = new ActivityCheckSyncStep(_clientFactory, _mediator);

        var members = await step.ExecuteAsync(ClubId, CancellationToken.None);

        members.Should().HaveCount(1);
        members[0].UserId.Should().Be("user-1");
        await _mediator.Received(1).Send(Arg.Any<CheckStrikeDecayCommand>(), Arg.Any<CancellationToken>());
        await _mediator.Received(1).Send(
            Arg.Is<SaveClubMembersCommand>(c => c.Snapshots.Count == 1 && c.Snapshots[0].UserId == "user-1"),
            Arg.Any<CancellationToken>());
    }

    private static ClubMemberDto BuildApiMember(string userId, string nickname, int xp, DateTimeOffset joinedAt) =>
        new()
        {
            User = new ClubMemberUserDto
            {
                UserId = userId,
                Nick = nickname,
                Avatar = "",
                FullBodyAvatar = "",
                BorderUrl = "",
                IsVerified = false,
                Flair = 0,
                CountryCode = "us",
                TierId = 0,
                ClubUserType = 0
            },
            Role = 0,
            JoinedAt = joinedAt,
            Xp = xp,
            WeeklyXp = 0
        };
}
