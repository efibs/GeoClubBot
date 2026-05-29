using Constants;
using Entities;
using FluentAssertions;
using NSubstitute;
using UseCases.OutputPorts;
using UseCases.UseCases.GeoGuessrAccountLinking;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.GeoGuessrAccountLinking;

public sealed class StartAndCancelAccountLinkingHandlerTests
{
    private const ulong DiscordUserId = 555UL;
    private const string GeoGuessrUserId = "ggUser-000000000000001";

    private readonly IAccountLinkingRequestRepository _requests = Substitute.For<IAccountLinkingRequestRepository>();

    [Fact]
    public async Task Start_CreatesRequest_AndReturnsOneTimePassword()
    {
        _requests.ReadRequestAsync(DiscordUserId, Arg.Any<CancellationToken>())
            .Returns((GeoGuessrAccountLinkingRequest?)null);

        var result = await new StartAccountLinkingHandler(_requests)
            .Handle(new StartAccountLinkingCommand(DiscordUserId, GeoGuessrUserId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveLength(StringLengthConstants.AccountLinkingRequestOneTimePasswordLength);
        _requests.Received(1).AddRequest(Arg.Is<GeoGuessrAccountLinkingRequest>(r =>
            r.DiscordUserId == DiscordUserId &&
            r.GeoGuessrUserId == GeoGuessrUserId &&
            r.OneTimePassword == result.Value));
    }

    [Fact]
    public async Task Start_ReturnsConflict_WhenRequestAlreadyExists()
    {
        var existing = GeoGuessrAccountLinkingRequest.Create(DiscordUserId, GeoGuessrUserId, "pw");
        _requests.ReadRequestAsync(DiscordUserId, Arg.Any<CancellationToken>()).Returns(existing);

        var result = await new StartAccountLinkingHandler(_requests)
            .Handle(new StartAccountLinkingCommand(DiscordUserId, GeoGuessrUserId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("account_linking.in_progress");
        result.Error.Type.Should().Be(ErrorType.Conflict);
        _requests.DidNotReceive().AddRequest(Arg.Any<GeoGuessrAccountLinkingRequest>());
    }

    [Fact]
    public async Task Cancel_DeletesRequest_WhenItExists()
    {
        var request = GeoGuessrAccountLinkingRequest.Create(DiscordUserId, GeoGuessrUserId, "pw");
        _requests.ReadRequestAsync(DiscordUserId, GeoGuessrUserId, Arg.Any<CancellationToken>()).Returns(request);

        var result = await new CancelAccountLinkingHandler(_requests)
            .Handle(new CancelAccountLinkingCommand(DiscordUserId, GeoGuessrUserId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _requests.Received(1).DeleteRequest(request);
    }

    [Fact]
    public async Task Cancel_ReturnsNotFound_WhenNoRequest()
    {
        _requests.ReadRequestAsync(DiscordUserId, GeoGuessrUserId, Arg.Any<CancellationToken>())
            .Returns((GeoGuessrAccountLinkingRequest?)null);

        var result = await new CancelAccountLinkingHandler(_requests)
            .Handle(new CancelAccountLinkingCommand(DiscordUserId, GeoGuessrUserId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("account_linking.request_not_found");
        _requests.DidNotReceive().DeleteRequest(Arg.Any<GeoGuessrAccountLinkingRequest>());
    }
}
