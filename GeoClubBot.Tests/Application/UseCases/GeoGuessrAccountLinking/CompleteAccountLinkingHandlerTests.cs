using Configuration;
using Entities;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts;
using UseCases.OutputPorts.Discord;
using UseCases.UseCases.GeoGuessrAccountLinking;
using UseCases.UseCases.Users;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.GeoGuessrAccountLinking;

public sealed class CompleteAccountLinkingHandlerTests
{
    private const ulong DiscordUserId = 555UL;
    private const string GeoGuessrUserId = "ggUser-1";
    private const string Otp = "abcdefghij";
    private const ulong HasLinkedRoleId = 9999UL;

    private readonly IAccountLinkingRequestRepository _requests = Substitute.For<IAccountLinkingRequestRepository>();
    private readonly IGeoGuessrUserRepository _users = Substitute.For<IGeoGuessrUserRepository>();
    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly IDiscordServerRolesAccess _roles = Substitute.For<IDiscordServerRolesAccess>();
    private readonly ILogger<CompleteAccountLinkingHandler> _logger = Substitute.For<ILogger<CompleteAccountLinkingHandler>>();

    private CompleteAccountLinkingHandler CreateHandler()
    {
        var accountLinkingConfig = Options.Create(new GeoGuessrAccountLinkingConfiguration
        {
            AdminChannelId = 0UL,
            HasLinkedRoleId = HasLinkedRoleId
        });
        return new CompleteAccountLinkingHandler(_requests, _users, _mediator, _roles, accountLinkingConfig, _logger);
    }

    [Fact]
    public async Task Handle_ReturnsValidationFailure_WhenOtpDoesNotMatch()
    {
        var request = GeoGuessrAccountLinkingRequest.Create(DiscordUserId, GeoGuessrUserId, Otp);
        _requests.ReadRequestAsync(DiscordUserId, GeoGuessrUserId, Arg.Any<CancellationToken>())
            .Returns(request);

        var result = await CreateHandler().Handle(
            new CompleteAccountLinkingCommand(DiscordUserId, GeoGuessrUserId, "WRONG_OTP_"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("account_linking.otp_mismatch");
        result.Error.Type.Should().Be(ErrorType.Validation);
        await _roles.DidNotReceive().AddRoleToMembersByUserIdsAsync(
            Arg.Any<IEnumerable<ulong>>(), Arg.Any<ulong>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_LinksDiscordIdAndAssignsRole_OnSuccess()
    {
        var request = GeoGuessrAccountLinkingRequest.Create(DiscordUserId, GeoGuessrUserId, Otp);
        _requests.ReadRequestAsync(DiscordUserId, GeoGuessrUserId, Arg.Any<CancellationToken>())
            .Returns(request);

        var trackedUser = GeoGuessrUser.Create(GeoGuessrUserId, "Player1");
        _mediator
            .Send(Arg.Is<ReadOrSyncGeoGuessrUserByUserIdQuery>(q => q.UserId == GeoGuessrUserId),
                Arg.Any<CancellationToken>())
            .Returns(Result<GeoGuessrUser>.Success(trackedUser));
        _users.ReadForUpdateByUserIdAsync(GeoGuessrUserId, Arg.Any<CancellationToken>())
            .Returns(trackedUser);

        var result = await CreateHandler().Handle(
            new CompleteAccountLinkingCommand(DiscordUserId, GeoGuessrUserId, Otp),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(trackedUser);
        trackedUser.DiscordUserId.Should().Be(DiscordUserId);
        _requests.Received(1).DeleteRequest(request);
        await _roles.Received(1).AddRoleToMembersByUserIdsAsync(
            Arg.Is<IEnumerable<ulong>>(ids => ids.Single() == DiscordUserId),
            HasLinkedRoleId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReturnsRequestNotFound_WhenNoLinkingRequestExists()
    {
        _requests.ReadRequestAsync(DiscordUserId, GeoGuessrUserId, Arg.Any<CancellationToken>())
            .Returns((GeoGuessrAccountLinkingRequest?)null);

        var result = await CreateHandler().Handle(
            new CompleteAccountLinkingCommand(DiscordUserId, GeoGuessrUserId, Otp),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("account_linking.request_not_found");
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Handle_ReturnsUserSyncFailed_WhenUserSyncFails()
    {
        var request = GeoGuessrAccountLinkingRequest.Create(DiscordUserId, GeoGuessrUserId, Otp);
        _requests.ReadRequestAsync(DiscordUserId, GeoGuessrUserId, Arg.Any<CancellationToken>())
            .Returns(request);
        _mediator
            .Send(Arg.Is<ReadOrSyncGeoGuessrUserByUserIdQuery>(q => q.UserId == GeoGuessrUserId),
                Arg.Any<CancellationToken>())
            .Returns(Result<GeoGuessrUser>.Failure(Error.NotFound("user.not_found", "missing")));

        var result = await CreateHandler().Handle(
            new CompleteAccountLinkingCommand(DiscordUserId, GeoGuessrUserId, Otp),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("account_linking.user_sync_failed");
    }
}
