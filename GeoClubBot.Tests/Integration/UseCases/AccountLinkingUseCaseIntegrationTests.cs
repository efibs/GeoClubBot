using Configuration;
using Entities;
using FluentAssertions;
using Infrastructure.OutputAdapters.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.UseCases.GeoGuessrAccountLinking;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.Integration.UseCases;

/// <summary>
/// Exercises the GeoGuessr account-linking use cases (start / cancel / complete-failures / unlink-
/// failures / queries) through the real MediatR pipeline against the shared Postgres container.
/// Success paths that emit account-linked/-unlinked domain events are covered separately once the
/// event handlers' side effects are wired into the host.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AccountLinkingUseCaseIntegrationTests(PostgresFixture fixture)
{
    private static ulong NewDiscordId() => (ulong)Random.Shared.NextInt64(1_000_000_000_000_000L, long.MaxValue);
    private static string NewGeoGuessrId() => Guid.NewGuid().ToString("N")[..24];
    private static string NewNickname() => $"nick-{Guid.NewGuid():N}"[..30];

    private MediatorTestHost CreateHost() => new(fixture.ConnectionString);

    /// <summary>Host with an empty club list so UnlinkAccounts' role-removal fan-out has nothing to iterate.</summary>
    private MediatorTestHost CreateHostWithoutClubs() =>
        new(fixture.ConnectionString, services =>
            services.AddSingleton(Options.Create(new GeoGuessrConfiguration
            {
                SyncSchedule = "0 0 0 * * ?",
                ActivityNcfaToken = "x",
                MissionsNcfaToken = "x",
                UserProfileNcfaToken = "x",
                Clubs = [],
            })));

    private async Task SeedRequestAsync(ulong discordId, string geoGuessrId, string otp)
    {
        await using var seed = fixture.CreateDbContext();
        seed.Add(GeoGuessrAccountLinkingRequest.Create(discordId, geoGuessrId, otp));
        await seed.SaveChangesAsync();
    }

    private async Task SeedUserAsync(string geoGuessrId, string nickname, ulong? discordId = null)
    {
        await using var seed = fixture.CreateDbContext();
        seed.Add(GeoGuessrUser.Create(geoGuessrId, nickname, discordId));
        await seed.SaveChangesAsync();
    }

    private async Task SeedLinkedMemberAsync(string geoGuessrId, string nickname, ulong discordId)
    {
        var clubId = Guid.NewGuid();
        await using var seed = fixture.CreateDbContext();
        seed.Add(Club.Create(clubId, $"club-{clubId:N}", 1));
        var user = GeoGuessrUser.Create(geoGuessrId, nickname, discordId);
        seed.Add(user);
        seed.Add(ClubMember.Create(user, clubId, xp: 0, joinedAt: DateTimeOffset.UtcNow.AddMonths(-1)));
        await seed.SaveChangesAsync();
    }

    // ---- Start ------------------------------------------------------------

    [Fact]
    public async Task StartLinking_CreatesARequest_AndReturnsTheOneTimePassword()
    {
        var discordId = NewDiscordId();
        var geoGuessrId = NewGeoGuessrId();

        using var host = CreateHost();
        var result = await host.SendAsync(new StartAccountLinkingCommand(discordId, geoGuessrId));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNullOrWhiteSpace();

        await using var read = fixture.CreateDbContext();
        var persisted = await new EfAccountLinkingRequestRepository(read).ReadRequestAsync(discordId);
        persisted.Should().NotBeNull();
        persisted!.OneTimePassword.Should().Be(result.Value);
    }

    [Fact]
    public async Task StartLinking_ReturnsConflict_WhenARequestAlreadyExists()
    {
        var discordId = NewDiscordId();
        await SeedRequestAsync(discordId, NewGeoGuessrId(), "otp");

        using var host = CreateHost();
        var result = await host.SendAsync(new StartAccountLinkingCommand(discordId, NewGeoGuessrId()));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }

    // ---- Cancel -----------------------------------------------------------

    [Fact]
    public async Task CancelLinking_DeletesTheRequest()
    {
        var discordId = NewDiscordId();
        var geoGuessrId = NewGeoGuessrId();
        await SeedRequestAsync(discordId, geoGuessrId, "otp");

        using var host = CreateHost();
        var result = await host.SendAsync(new CancelAccountLinkingCommand(discordId, geoGuessrId));

        result.IsSuccess.Should().BeTrue();
        await using var read = fixture.CreateDbContext();
        (await new EfAccountLinkingRequestRepository(read).ReadRequestAsync(discordId)).Should().BeNull();
    }

    [Fact]
    public async Task CancelLinking_ReturnsNotFound_WhenNoRequestExists()
    {
        using var host = CreateHost();

        var result = await host.SendAsync(new CancelAccountLinkingCommand(NewDiscordId(), NewGeoGuessrId()));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    // ---- Complete (failure paths) -----------------------------------------

    // The CompleteAccountLinkingCommandValidator requires an 18-character one-time password, so
    // these failure-path tests must supply correctly-shaped (but wrong / unmatched) passwords to
    // reach the handler rather than tripping validation.
    private const string ValidLengthOtp = "AAAAAAAAAAAAAAAAAA"; // 18 chars
    private const string OtherValidLengthOtp = "BBBBBBBBBBBBBBBBBB"; // 18 chars

    [Fact]
    public async Task CompleteLinking_ReturnsValidationError_ForWrongOneTimePassword()
    {
        var discordId = NewDiscordId();
        var geoGuessrId = NewGeoGuessrId();
        await SeedUserAsync(geoGuessrId, NewNickname());
        await SeedRequestAsync(discordId, geoGuessrId, ValidLengthOtp);

        using var host = CreateHost();
        var result = await host.SendAsync(new CompleteAccountLinkingCommand(discordId, geoGuessrId, OtherValidLengthOtp));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be("account_linking.otp_mismatch");
    }

    [Fact]
    public async Task CompleteLinking_ReturnsNotFound_WhenNoRequestExists()
    {
        using var host = CreateHost();

        var result = await host.SendAsync(new CompleteAccountLinkingCommand(NewDiscordId(), NewGeoGuessrId(), ValidLengthOtp));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be("account_linking.request_not_found");
    }

    [Fact]
    public async Task CompleteLinking_LinksTheAccount_AndDeletesTheRequest()
    {
        var discordId = NewDiscordId();
        var geoGuessrId = NewGeoGuessrId();
        await SeedUserAsync(geoGuessrId, NewNickname());
        await SeedRequestAsync(discordId, geoGuessrId, ValidLengthOtp);

        using var host = CreateHost();
        var result = await host.SendAsync(new CompleteAccountLinkingCommand(discordId, geoGuessrId, ValidLengthOtp));

        result.IsSuccess.Should().BeTrue();
        result.Value.DiscordUserId.Should().Be(discordId);

        await using var read = fixture.CreateDbContext();
        var user = await new EfGeoGuessrUserRepository(read).ReadUserByUserIdAsync(geoGuessrId);
        user!.DiscordUserId.Should().Be(discordId, "the account should be linked and committed");
        (await new EfAccountLinkingRequestRepository(read).ReadRequestAsync(discordId))
            .Should().BeNull("the linking request should be consumed");
    }

    // Regression test for the "GeoGuessr user with id xxxx was not found after syncing." error that
    // occurred on the FIRST linking attempt for someone not yet in the database. CompleteAccountLinking
    // syncs the new user into the DbContext (Added, uncommitted) and then re-reads it "for update".
    // The re-read must see that pending, freshly-Added entity within the same unit of work — otherwise
    // the first attempt fails and only a retry (after the failed attempt persisted the user) succeeds.
    [Fact]
    public async Task CompleteLinking_SyncsAndLinksTheAccount_OnFirstAttempt_WhenUserIsNotYetInTheDatabase()
    {
        var discordId = NewDiscordId();
        var geoGuessrId = NewGeoGuessrId();
        var nickname = NewNickname();

        // Deliberately do NOT seed the user — this is the "new to the bot's database" scenario.
        await SeedRequestAsync(discordId, geoGuessrId, ValidLengthOtp);

        using var host = CreateHost();
        ArrangeGeoGuessrUserProfileSync(host, geoGuessrId, nickname);

        var result = await host.SendAsync(new CompleteAccountLinkingCommand(discordId, geoGuessrId, ValidLengthOtp));

        result.IsSuccess.Should().BeTrue("a brand-new user must be synced and linked on the first attempt");
        result.Value.DiscordUserId.Should().Be(discordId);

        await using var read = fixture.CreateDbContext();
        var user = await new EfGeoGuessrUserRepository(read).ReadUserByUserIdAsync(geoGuessrId);
        user.Should().NotBeNull("the synced user should be persisted");
        user!.DiscordUserId.Should().Be(discordId, "the freshly-synced account should be linked and committed");
        user.Nickname.Should().Be(nickname);
        (await new EfAccountLinkingRequestRepository(read).ReadRequestAsync(discordId))
            .Should().BeNull("the linking request should be consumed");
    }

    /// <summary>
    /// Arranges the substituted GeoGuessr user-profile client so that syncing an as-yet-unknown user
    /// during account linking returns a profile, mirroring "first contact" with the API.
    /// </summary>
    private static void ArrangeGeoGuessrUserProfileSync(MediatorTestHost host, string geoGuessrId, string nickname)
    {
        var client = Substitute.For<IGeoGuessrClient>();
        client.ReadUserAsync(geoGuessrId, Arg.Any<CancellationToken>())
            .Returns(NewUserDto(geoGuessrId, nickname));
        host.Mock<IGeoGuessrClientFactory>().CreateUserProfileClient().Returns(client);
    }

    private static UserDto NewUserDto(string userId, string nickname) => new()
    {
        Id = userId,
        Nick = nickname,
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

    // ---- Unlink -----------------------------------------------------------

    [Fact]
    public async Task UnlinkAccounts_ReturnsNotFound_WhenAccountsAreNotLinked()
    {
        using var host = CreateHost();

        var result = await host.SendAsync(new UnlinkAccountsCommand(NewDiscordId(), NewGeoGuessrId()));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task UnlinkAccounts_ClearsTheDiscordLink()
    {
        var discordId = NewDiscordId();
        var geoGuessrId = NewGeoGuessrId();
        await SeedUserAsync(geoGuessrId, NewNickname(), discordId);

        using var host = CreateHostWithoutClubs();
        var result = await host.SendAsync(new UnlinkAccountsCommand(discordId, geoGuessrId));

        result.IsSuccess.Should().BeTrue();

        await using var read = fixture.CreateDbContext();
        var user = await new EfGeoGuessrUserRepository(read).ReadUserByUserIdAsync(geoGuessrId);
        user!.DiscordUserId.Should().BeNull("unlinking should clear and commit the Discord id");
    }

    // ---- Queries ----------------------------------------------------------

    [Fact]
    public async Task GetLinkedGeoGuessrUser_ReturnsTheUser_WhenLinked()
    {
        var discordId = NewDiscordId();
        var geoGuessrId = NewGeoGuessrId();
        await SeedUserAsync(geoGuessrId, NewNickname(), discordId);

        using var host = CreateHost();
        var result = await host.SendAsync(new GetLinkedGeoGuessrUserQuery(discordId));

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be(geoGuessrId);
    }

    [Fact]
    public async Task GetLinkedGeoGuessrUser_ReturnsNotFound_WhenNotLinked()
    {
        using var host = CreateHost();

        var result = await host.SendAsync(new GetLinkedGeoGuessrUserQuery(NewDiscordId()));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task GetLinkedDiscordUserId_ReturnsTheId_WhenLinked()
    {
        var discordId = NewDiscordId();
        var geoGuessrId = NewGeoGuessrId();
        await SeedUserAsync(geoGuessrId, NewNickname(), discordId);

        using var host = CreateHost();
        var result = await host.SendAsync(new GetLinkedDiscordUserIdQuery(geoGuessrId));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(discordId);
    }

    [Fact]
    public async Task GetOpenAccountLinkingRequest_ReturnsTheRequest_WhenPresent()
    {
        var discordId = NewDiscordId();
        await SeedRequestAsync(discordId, NewGeoGuessrId(), "otp");

        using var host = CreateHost();
        var result = await host.SendAsync(new GetOpenAccountLinkingRequestQuery(discordId));

        result.IsSuccess.Should().BeTrue();
        result.Value.DiscordUserId.Should().Be(discordId);
    }

    [Fact]
    public async Task GetOpenAccountLinkingRequest_ReturnsNotFound_WhenAbsent()
    {
        using var host = CreateHost();

        var result = await host.SendAsync(new GetOpenAccountLinkingRequestQuery(NewDiscordId()));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task GetGeoGuessrUserByNickname_ReturnsTheUser_ForAKnownMember()
    {
        var discordId = NewDiscordId();
        var geoGuessrId = NewGeoGuessrId();
        var nickname = NewNickname();
        await SeedLinkedMemberAsync(geoGuessrId, nickname, discordId);

        using var host = CreateHost();
        var result = await host.SendAsync(new GetGeoGuessrUserByNicknameQuery(nickname));

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be(geoGuessrId);
    }

    [Fact]
    public async Task GetDiscordUserByNickname_ReturnsTheDiscordId_ForALinkedMember()
    {
        var discordId = NewDiscordId();
        var nickname = NewNickname();
        await SeedLinkedMemberAsync(NewGeoGuessrId(), nickname, discordId);

        using var host = CreateHost();
        var result = await host.SendAsync(new GetDiscordUserByNicknameQuery(nickname));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(discordId);
    }
}
