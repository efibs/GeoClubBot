using Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.Users;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.UsersTests;

/// <summary>
/// The sync path has to be safe to call repeatedly inside one unit of work: callers such as the
/// daily-challenge role distribution resolve a whole batch of players before anything is committed,
/// and inserting the same user twice corrupts the change tracker for the entire request.
/// </summary>
public sealed class ReadOrSyncGeoGuessrUserByUserIdHandlerTests
{
    private const string UserId = "user-1";

    private readonly IGeoGuessrUserRepository _users = Substitute.For<IGeoGuessrUserRepository>();
    private readonly IGeoGuessrClientFactory _factory = Substitute.For<IGeoGuessrClientFactory>();
    private readonly IGeoGuessrClient _client = Substitute.For<IGeoGuessrClient>();

    /// <summary>Users added but not yet committed, i.e. what the EF change tracker would hold.</summary>
    private readonly Dictionary<string, GeoGuessrUser> _pendingInserts = [];

    public ReadOrSyncGeoGuessrUserByUserIdHandlerTests()
    {
        _factory.CreateUserProfileClient().Returns(_client);
        _client.ReadUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => NewUserDto(callInfo.ArgAt<string>(0)));

        // Mirror EF's semantics: the "for update" read resolves through the change tracker and so
        // sees pending inserts, while adding a second instance with the same key is an error.
        _users.When(u => u.AddUser(Arg.Any<GeoGuessrUser>())).Do(callInfo =>
        {
            var user = callInfo.Arg<GeoGuessrUser>()!;
            if (!_pendingInserts.TryAdd(user.UserId, user))
            {
                throw new InvalidOperationException(
                    $"The instance of entity type 'GeoGuessrUser' cannot be tracked because another instance with the key value '{user.UserId}' is already being tracked.");
            }
        });
        _users.ReadForUpdateByUserIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => _pendingInserts.GetValueOrDefault(callInfo.ArgAt<string>(0)));
        _users.ReadUserByUserIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((GeoGuessrUser?)null);
    }

    private ReadOrSyncGeoGuessrUserByUserIdHandler CreateHandler() =>
        new(_users, _factory, NullLogger<ReadOrSyncGeoGuessrUserByUserIdHandler>.Instance);

    private Task<Result<GeoGuessrUser>> HandleAsync(string userId = UserId) =>
        CreateHandler().Handle(new ReadOrSyncGeoGuessrUserByUserIdQuery(userId), CancellationToken.None);

    [Fact]
    public async Task Handle_ReturnsTheKnownUser_WithoutCallingGeoGuessr()
    {
        var known = GeoGuessrUser.Create(UserId, "Alice", 42);
        _users.ReadForUpdateByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(known);

        var result = await HandleAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(known);
        await _client.DidNotReceive().ReadUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _users.DidNotReceive().AddUser(Arg.Any<GeoGuessrUser>());
    }

    [Fact]
    public async Task Handle_SyncsAnUnknownUser()
    {
        var result = await HandleAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be(UserId);
        result.Value.Nickname.Should().Be($"nick-{UserId}");
        _users.Received(1).AddUser(Arg.Is<GeoGuessrUser>(u => u!.UserId == UserId));
    }

    /// <summary>
    /// Regression test for the daily "Unexpected entry.EntityState: Detached" crash: the second sync
    /// of the same user used to re-read the database only, miss the pending insert and add the user a
    /// second time, which poisoned the unit of work.
    /// </summary>
    [Fact]
    public async Task Handle_AddsAnUnknownUserOnlyOnce_WhenSyncedTwiceInTheSameUnitOfWork()
    {
        var first = await HandleAsync();
        var second = await HandleAsync();

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue("the pending insert from the first sync must be reused");
        second.Value.Should().BeSameAs(first.Value);
        _users.Received(1).AddUser(Arg.Any<GeoGuessrUser>());
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenGeoGuessrDoesNotKnowTheUser()
    {
        _client.ReadUserAsync(UserId, Arg.Any<CancellationToken>())
            .Returns<UserDto>(_ => throw new HttpRequestException("404"));

        var result = await HandleAsync();

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        _users.DidNotReceive().AddUser(Arg.Any<GeoGuessrUser>());
    }

    /// <summary>
    /// Only the GeoGuessr lookup is allowed to degrade into a not-found; a persistence failure has to
    /// surface, otherwise a broken unit of work is reported as "this player does not exist".
    /// </summary>
    [Fact]
    public async Task Handle_DoesNotSwallowPersistenceFailures()
    {
        _users.When(u => u.AddUser(Arg.Any<GeoGuessrUser>())).Do(_ => throw new InvalidOperationException("boom"));

        var act = async () => await HandleAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }

    private static UserDto NewUserDto(string userId) => new()
    {
        Id = userId,
        Nick = $"nick-{userId}",
        Created = DateTimeOffset.UtcNow,
        IsProUser = false,
        Type = "user",
        IsVerified = false,
        CustomImage = "",
        FullBodyPin = "",
        BorderUrl = "",
        Color = 0,
        Url = $"/user/{userId}",
        CountryCode = "us",
        Competitive = new UserCompetitiveDto { Elo = 1000, Rating = 1000, LastRatingChange = 0 },
        IsBanned = false,
        ChatBan = false,
    };
}
