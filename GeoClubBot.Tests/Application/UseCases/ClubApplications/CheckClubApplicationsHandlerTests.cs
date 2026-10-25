using Configuration;
using Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.ClubApplications;
using Xunit;

namespace GeoClubBot.Tests.Application.UseCases.ClubApplications;

public sealed class CheckClubApplicationsHandlerTests
{
    private const ulong ChannelId = 42;
    private const ulong RoleId = 7;
    private static readonly Guid ClubA = Guid.NewGuid();
    private static readonly Guid ClubB = Guid.NewGuid();

    private readonly IGeoGuessrClientFactory _clientFactory = Substitute.For<IGeoGuessrClientFactory>();
    private readonly IGeoGuessrClient _clientA = Substitute.For<IGeoGuessrClient>();
    private readonly IGeoGuessrClient _clientB = Substitute.For<IGeoGuessrClient>();
    private readonly IClubRepository _clubs = Substitute.For<IClubRepository>();
    private readonly IGeoGuessrUserRepository _users = Substitute.For<IGeoGuessrUserRepository>();
    private readonly IClubApplicationNotificationRepository _notifications = Substitute.For<IClubApplicationNotificationRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IDiscordMessageAccess _discord = Substitute.For<IDiscordMessageAccess>();

    private readonly ClubApplicationNotificationsConfiguration _options = new()
    {
        Enabled = true,
        Schedule = "x",
        TextChannelId = ChannelId,
        RoleIdToPing = RoleId
    };

    private readonly List<GeoGuessrClubEntry> _clubEntries =
    [
        new() { ClubId = ClubA, NcfaToken = "a", IsMain = true },
        new() { ClubId = ClubB, NcfaToken = "b" }
    ];

    public CheckClubApplicationsHandlerTests()
    {
        _clientFactory.CreateClient(ClubA).Returns(_clientA);
        _clientFactory.CreateClient(ClubB).Returns(_clientB);
        _clubs.ReadClubByIdAsync(ClubA, Arg.Any<CancellationToken>()).Returns(global::Entities.Club.Create(ClubA, "Dragon", 10));
        _clubs.ReadClubByIdAsync(ClubB, Arg.Any<CancellationToken>()).Returns(global::Entities.Club.Create(ClubB, "Den", 5));
        _notifications.ReadNotifiedUserIdsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
        Pending(_clientA);
        Pending(_clientB);
    }

    [Fact]
    public async Task NewApplicant_IsAnnouncedOnce_AndRecorded()
    {
        Pending(_clientA, Applicant("u1", "Alice"));

        await Handle();

        await _discord.Received(1).SendMessageAsync(
            Arg.Is<string>(m => m.Contains("**[Dragon]**") && m.Contains("**Alice**") && m.Contains("https://www.geoguessr.com/user/u1")),
            ChannelId,
            Arg.Any<MessageMentions>(),
            Arg.Any<CancellationToken>());
        _notifications.Received(1).Add(Arg.Is<ClubApplicationNotification>(n => n.ClubId == ClubA && n.UserId == "u1"));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AlreadyAnnouncedApplicant_IsNotAnnouncedAgain()
    {
        Pending(_clientA, Applicant("u1", "Alice"));
        _notifications.ReadNotifiedUserIdsAsync(ClubA, Arg.Any<CancellationToken>()).Returns(["u1"]);

        await Handle();

        await _discord.DidNotReceiveWithAnyArgs().SendMessageAsync(default!, default, default!, default);
        _notifications.DidNotReceiveWithAnyArgs().Add(default!);
        await _notifications.DidNotReceiveWithAnyArgs().DeleteAsync(default, default!, default);
    }

    [Fact]
    public async Task ApplicationNoLongerPending_IsForgotten_SoAReapplicationIsAnnouncedAgain()
    {
        Pending(_clientA, Applicant("still", "Still"));
        _notifications.ReadNotifiedUserIdsAsync(ClubA, Arg.Any<CancellationToken>()).Returns(["still", "accepted"]);

        await Handle();

        await _notifications.Received(1).DeleteAsync(
            ClubA,
            Arg.Is<IReadOnlyCollection<string>>(ids => ids.SequenceEqual(new[] { "accepted" })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FailedRead_ForgetsNothing_AndOtherClubsAreStillChecked()
    {
        _clientA.ReadClubApplicationsAsync(ClubA, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("down"));
        Pending(_clientB, Applicant("u2", "Bob"));

        await Handle();

        await _notifications.DidNotReceive().ReadNotifiedUserIdsAsync(ClubA, Arg.Any<CancellationToken>());
        await _notifications.DidNotReceive().DeleteAsync(ClubA, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
        _notifications.Received(1).Add(Arg.Is<ClubApplicationNotification>(n => n.ClubId == ClubB && n.UserId == "u2"));
    }

    [Fact]
    public async Task LinkedApplicant_IsShownButOnlyTheRoleIsPinged()
    {
        Pending(_clientA, Applicant("u1", "Alice"));
        _users.ReadUserByUserIdAsync("u1", Arg.Any<CancellationToken>()).Returns(GeoGuessrUser.Create("u1", "Alice", 1234));

        await Handle();

        await _discord.Received(1).SendMessageAsync(
            Arg.Is<string>(m => m.Contains("**Alice** (<@1234>)") && m.Contains($"<@&{RoleId}>")),
            ChannelId,
            Arg.Is<MessageMentions>(x => x.RoleIds.SequenceEqual(new[] { RoleId }) && x.UserIds.Count == 0 && !x.Everyone),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Disabled_ReadsNothing()
    {
        _options.Enabled = false;

        await Handle();

        _clientFactory.DidNotReceiveWithAnyArgs().CreateClient(default);
    }

    [Fact]
    public async Task ClubThatOptedOut_IsNeverRead()
    {
        _clubEntries[1].NotifyApplications = false;

        await Handle();

        _clientFactory.Received(1).CreateClient(ClubA);
        _clientFactory.DidNotReceive().CreateClient(ClubB);
    }

    [Fact]
    public async Task EveryPage_IsRead()
    {
        _clientA.ReadClubApplicationsAsync(ClubA, null, Arg.Any<CancellationToken>())
            .Returns(new ReadClubApplicationsResponseDto { Items = [Applicant("u1", "Alice")], PaginationToken = "next" });
        _clientA.ReadClubApplicationsAsync(ClubA, "next", Arg.Any<CancellationToken>())
            .Returns(new ReadClubApplicationsResponseDto { Items = [Applicant("u2", "Bob")], PaginationToken = null });

        await Handle();

        _notifications.Received(1).Add(Arg.Is<ClubApplicationNotification>(n => n.UserId == "u1"));
        _notifications.Received(1).Add(Arg.Is<ClubApplicationNotification>(n => n.UserId == "u2"));
    }

    [Fact]
    public void Render_EscapesTheNickname_AndDropsEmptyPlaceholders()
    {
        var text = CheckClubApplicationsHandler.Render(
            "{{club}}|{{applicant}}|{{nick}}|{{discord}}|{{profile}}|{{role}}", Applicant("u1", "*bold*_"), null, "Dragon", null);

        text.Should().Be(@"Dragon|**\*bold\*\_**|\*bold\*\_||https://www.geoguessr.com/user/u1|");
    }

    private Task Handle() =>
        new CheckClubApplicationsHandler(
                _clientFactory,
                _clubs,
                _users,
                _notifications,
                _unitOfWork,
                _discord,
                Options.Create(_options),
                Options.Create(new GeoGuessrConfiguration
                {
                    SyncSchedule = "x",
                    ActivityNcfaToken = "x",
                    UserProfileNcfaToken = "x",
                    Clubs = _clubEntries
                }),
                NullLogger<CheckClubApplicationsHandler>.Instance)
            .Handle(new CheckClubApplicationsCommand(), CancellationToken.None);

    private void Pending(IGeoGuessrClient client, params ClubMemberUserDto[] applicants)
    {
        var clubId = client == _clientA ? ClubA : ClubB;
        client.ReadClubApplicationsAsync(clubId, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new ReadClubApplicationsResponseDto { Items = [.. applicants], PaginationToken = null });
    }

    private static ClubMemberUserDto Applicant(string userId, string nick) => new()
    {
        UserId = userId,
        Nick = nick,
        Avatar = "",
        FullBodyAvatar = "",
        BorderUrl = "",
        IsVerified = false,
        Flair = 0,
        CountryCode = "de",
        TierId = 0,
        ClubUserType = 3
    };
}

public sealed class ClubApplicationNotificationsConfigurationTests
{
    [Fact]
    public void Enabled_WithoutAChannel_IsInvalid()
    {
        var options = new ClubApplicationNotificationsConfiguration { Enabled = true, Schedule = "x" };

        var errors = options.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(options)).ToList();

        errors.Should().ContainSingle().Which.ErrorMessage.Should().Contain(nameof(options.TextChannelId));
    }

    [Fact]
    public void Disabled_WithoutAChannel_IsValid()
    {
        var options = new ClubApplicationNotificationsConfiguration { Schedule = "x" };

        options.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(options)).Should().BeEmpty();
    }
}
