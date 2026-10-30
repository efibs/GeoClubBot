using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using NSubstitute;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.GeoGuessrAccountLinking;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.GeoGuessrAccountLinking;

public sealed class AccountLinkingQueriesHandlerTests
{
    private const string UserId = "user-1";
    private const string Nickname = "Player1";
    private const ulong DiscordUserId = 42;

    private readonly IGeoGuessrUserRepository _users = Substitute.For<IGeoGuessrUserRepository>();
    private readonly IClubMemberRepository _members = Substitute.For<IClubMemberRepository>();
    private readonly IAccountLinkingRequestRepository _requests = Substitute.For<IAccountLinkingRequestRepository>();
    private readonly IGeoGuessrUserProfileReader _profiles = Substitute.For<IGeoGuessrUserProfileReader>();

    private AccountLinkingQueriesHandler CreateHandler() => new(_users, _members, _requests, _profiles);

    private static void ShouldBeNotFound<T>(Result<T> result, string code)
    {
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(code);
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    private void MemberIs(ulong? discordUserId)
    {
        var member = new ClubMemberBuilder().WithUserId(UserId).WithNickname(Nickname);
        if (discordUserId is { } id)
        {
            member.WithDiscordUserId(id);
        }

        _members.ReadClubMemberByNicknameAsync(Nickname, Arg.Any<CancellationToken>()).Returns(member.Build());
    }

    [Fact]
    public async Task LinkedDiscordUser_IsFoundThroughTheGeoGuessrUser()
    {
        _users.ReadUserByUserIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(GeoGuessrUser.Create(UserId, Nickname, DiscordUserId));

        var result = await CreateHandler().Handle(new GetLinkedDiscordUserIdQuery(UserId), CancellationToken.None);

        result.Value.Should().Be(DiscordUserId);
    }

    [Fact]
    public async Task UnlinkedGeoGuessrUser_HasNoDiscordUser()
    {
        _users.ReadUserByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(GeoGuessrUser.Create(UserId, Nickname));

        var result = await CreateHandler().Handle(new GetLinkedDiscordUserIdQuery(UserId), CancellationToken.None);

        ShouldBeNotFound(result, "account_linking.not_linked");
    }

    [Fact]
    public async Task UnknownGeoGuessrUser_HasNoDiscordUser()
    {
        _users.ReadUserByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((GeoGuessrUser?)null);

        var result = await CreateHandler().Handle(new GetLinkedDiscordUserIdQuery(UserId), CancellationToken.None);

        ShouldBeNotFound(result, "account_linking.not_linked");
    }

    [Fact]
    public async Task LinkedGeoGuessrUser_IsFoundThroughTheDiscordUser()
    {
        var user = GeoGuessrUser.Create(UserId, Nickname, DiscordUserId);
        _users.ReadUserByDiscordUserIdAsync(DiscordUserId, Arg.Any<CancellationToken>()).Returns(user);

        var result = await CreateHandler().Handle(new GetLinkedGeoGuessrUserQuery(DiscordUserId), CancellationToken.None);

        result.Value.Should().BeSameAs(user);
    }

    [Fact]
    public async Task UnlinkedDiscordUser_HasNoGeoGuessrUser()
    {
        _users.ReadUserByDiscordUserIdAsync(DiscordUserId, Arg.Any<CancellationToken>()).Returns((GeoGuessrUser?)null);

        var result = await CreateHandler().Handle(new GetLinkedGeoGuessrUserQuery(DiscordUserId), CancellationToken.None);

        ShouldBeNotFound(result, "account_linking.not_linked");
    }

    [Fact]
    public async Task OpenRequest_IsReturned()
    {
        var request = GeoGuessrAccountLinkingRequest.Create(DiscordUserId, UserId, "secret");
        _requests.ReadRequestAsync(DiscordUserId, Arg.Any<CancellationToken>()).Returns(request);

        var result = await CreateHandler().Handle(new GetOpenAccountLinkingRequestQuery(DiscordUserId), CancellationToken.None);

        result.Value.Should().BeSameAs(request);
    }

    [Fact]
    public async Task NoOpenRequest_IsNotFound()
    {
        _requests.ReadRequestAsync(DiscordUserId, Arg.Any<CancellationToken>()).Returns((GeoGuessrAccountLinkingRequest?)null);

        var result = await CreateHandler().Handle(new GetOpenAccountLinkingRequestQuery(DiscordUserId), CancellationToken.None);

        ShouldBeNotFound(result, "account_linking.request_not_found");
    }

    [Fact]
    public async Task DiscordUser_IsFoundByNickname()
    {
        MemberIs(DiscordUserId);

        var result = await CreateHandler().Handle(new GetDiscordUserByNicknameQuery(Nickname), CancellationToken.None);

        result.Value.Should().Be(DiscordUserId);
    }

    [Fact]
    public async Task UnlinkedMember_HasNoDiscordUserByNickname()
    {
        MemberIs(discordUserId: null);

        var result = await CreateHandler().Handle(new GetDiscordUserByNicknameQuery(Nickname), CancellationToken.None);

        ShouldBeNotFound(result, "account_linking.not_linked");
    }

    [Fact]
    public async Task UnknownNickname_HasNoDiscordUser()
    {
        _members.ReadClubMemberByNicknameAsync(Nickname, Arg.Any<CancellationToken>()).Returns((ClubMember?)null);

        var result = await CreateHandler().Handle(new GetDiscordUserByNicknameQuery(Nickname), CancellationToken.None);

        ShouldBeNotFound(result, "account_linking.not_linked");
    }

    [Fact]
    public async Task GeoGuessrUser_IsFoundByNickname()
    {
        MemberIs(DiscordUserId);

        var result = await CreateHandler().Handle(new GetGeoGuessrUserByNicknameQuery(Nickname), CancellationToken.None);

        result.Value.UserId.Should().Be(UserId);
    }

    [Fact]
    public async Task UnknownNickname_HasNoGeoGuessrUser()
    {
        _members.ReadClubMemberByNicknameAsync(Nickname, Arg.Any<CancellationToken>()).Returns((ClubMember?)null);

        var result = await CreateHandler().Handle(new GetGeoGuessrUserByNicknameQuery(Nickname), CancellationToken.None);

        ShouldBeNotFound(result, "club_member.not_found");
    }

    [Fact]
    public async Task Profile_IsReadForTheLinkedAccount()
    {
        var profile = (UserDto)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(UserDto));
        _users.ReadUserByDiscordUserIdAsync(DiscordUserId, Arg.Any<CancellationToken>())
            .Returns(GeoGuessrUser.Create(UserId, Nickname, DiscordUserId));
        _profiles.ReadUserProfileAsync(UserId, Arg.Any<CancellationToken>()).Returns(profile);

        var result = await CreateHandler().Handle(new GetGeoGuessrProfileQuery(DiscordUserId), CancellationToken.None);

        result.Value.Should().BeSameAs(profile);
    }

    [Fact]
    public async Task Profile_NeedsALinkedAccount()
    {
        _users.ReadUserByDiscordUserIdAsync(DiscordUserId, Arg.Any<CancellationToken>()).Returns((GeoGuessrUser?)null);

        var result = await CreateHandler().Handle(new GetGeoGuessrProfileQuery(DiscordUserId), CancellationToken.None);

        ShouldBeNotFound(result, "account_linking.not_linked");
        await _profiles.DidNotReceiveWithAnyArgs().ReadUserProfileAsync(default!, default);
    }

    [Fact]
    public async Task Profile_ThatGeoGuessrDoesNotReturn_IsNotFound()
    {
        _users.ReadUserByDiscordUserIdAsync(DiscordUserId, Arg.Any<CancellationToken>())
            .Returns(GeoGuessrUser.Create(UserId, Nickname, DiscordUserId));
        _profiles.ReadUserProfileAsync(UserId, Arg.Any<CancellationToken>()).Returns((UserDto?)null);

        var result = await CreateHandler().Handle(new GetGeoGuessrProfileQuery(DiscordUserId), CancellationToken.None);

        ShouldBeNotFound(result, "account_linking.profile_not_found");
    }
}
