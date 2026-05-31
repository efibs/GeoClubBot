using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using MediatR;
using NSubstitute;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.ClubMembers;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.ClubMembers;

public sealed class ReadOrSyncClubMemberHandlerTests
{
    private static readonly Guid ClubId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly IClubMemberRepository _members = Substitute.For<IClubMemberRepository>();
    private readonly IGeoGuessrClientFactory _clientFactory = Substitute.For<IGeoGuessrClientFactory>();
    private readonly IGeoGuessrClient _client = Substitute.For<IGeoGuessrClient>();
    private readonly ISender _mediator = Substitute.For<ISender>();

    public ReadOrSyncClubMemberHandlerTests()
    {
        _clientFactory.CreateClient(Arg.Any<Guid>()).Returns(_client);
    }

    private ReadOrSyncClubMemberHandler CreateHandler()
    {
        var config = new GeoGuessrConfigurationBuilder().WithClub(ClubId).BuildOptions();
        return new ReadOrSyncClubMemberHandler(_members, _clientFactory, _mediator, config);
    }

    [Fact]
    public async Task ByUserId_ReturnsLocalMember_AndSkipsTheApi_WhenAlreadyPersisted()
    {
        var existing = new ClubMemberBuilder()
            .WithUserId("user-1").WithNickname("Player1").InClub(ClubId).Build();
        _members.ReadClubMemberByUserIdAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(existing);

        var result = await CreateHandler().Handle(
            new ReadOrSyncClubMemberByUserIdQuery("user-1"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(existing);
        await _client.DidNotReceive().ReadClubMembersAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ByUserId_FallsBackToApi_AndPersistsViaSaveClubMembersCommand_WhenLocalLookupMisses()
    {
        _members.ReadClubMemberByUserIdAsync("user-1", Arg.Any<CancellationToken>())
            .Returns((ClubMember?)null);
        _client.ReadClubMembersAsync(ClubId, Arg.Any<CancellationToken>()).Returns(new List<ClubMemberDto>
        {
            BuildApiMember("user-1", "Player1", 500, DateTimeOffset.UtcNow.AddDays(-30))
        });

        var result = await CreateHandler().Handle(
            new ReadOrSyncClubMemberByUserIdQuery("user-1"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be("user-1");
        await _mediator.Received(1).Send(
            Arg.Is<SaveClubMembersCommand>(c => c.Snapshots.Count == 1 && c.Snapshots[0].UserId == "user-1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ByUserId_ReturnsNotFound_WhenApiAlsoHasNoMatch()
    {
        _members.ReadClubMemberByUserIdAsync("user-1", Arg.Any<CancellationToken>())
            .Returns((ClubMember?)null);
        _client.ReadClubMembersAsync(ClubId, Arg.Any<CancellationToken>()).Returns([]);

        var result = await CreateHandler().Handle(
            new ReadOrSyncClubMemberByUserIdQuery("user-1"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be("club_member.not_found");
    }

    [Fact]
    public async Task ByNickname_ReturnsLocalMember_WhenAlreadyPersisted()
    {
        var existing = new ClubMemberBuilder()
            .WithUserId("user-1").WithNickname("Player1").InClub(ClubId).Build();
        _members.ReadClubMemberByNicknameAsync("Player1", Arg.Any<CancellationToken>())
            .Returns(existing);

        var result = await CreateHandler().Handle(
            new ReadOrSyncClubMemberByNicknameQuery("Player1"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.User.Nickname.Should().Be("Player1");
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
