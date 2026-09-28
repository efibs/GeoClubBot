using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.GeoGuessr;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.CountryChallenges;
using UseCases.UseCases.CountryChallenges.Configuration;
using Xunit;
using static GeoClubBot.Tests.TestBuilders.CountryChallengeFiles;

namespace GeoClubBot.Tests.Application.UseCases.CountryChallenges;

/// <summary>
/// Announcing is the step players see, and the one that must not post a challenge twice, lose a
/// challenge it announced, or keep one it never announced. These tests fail GeoGuessr, the database and
/// Discord in turn and pin what is stored and posted each time.
/// </summary>
public sealed class AnnounceCountryChallengesHandlerTests
{
    private static readonly Guid MainClubId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly IGeoGuessrClientFactory _factory = Substitute.For<IGeoGuessrClientFactory>();
    private readonly IGeoGuessrClient _client = Substitute.For<IGeoGuessrClient>();
    private readonly ICountryChallengeRepository _repository = Substitute.For<ICountryChallengeRepository>();
    private readonly IDiscordMessageAccess _discord = Substitute.For<IDiscordMessageAccess>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly List<CountryChallengePost> _stored = [];
    private readonly List<CountryChallengePost> _removed = [];
    private readonly List<(string Content, ulong ChannelId, MessageMentions Mentions)> _posted = [];
    private readonly List<PostChallengeRequestDto> _created = [];

    public AnnounceCountryChallengesHandlerTests()
    {
        _factory.CreateClient(MainClubId).Returns(_client);

        var tokens = 0;
        _client.CreateChallengeAsync(Arg.Any<PostChallengeRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _created.Add(call.Arg<PostChallengeRequestDto>());
                return new PostChallengeResponseDto { Token = $"token-{++tokens}" };
            });

        _repository.ReadPostsOnAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([]);
        _repository.ReadCountryHistoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
        _repository.When(r => r.AddPosts(Arg.Any<IEnumerable<CountryChallengePost>>()))
            .Do(call => _stored.AddRange(call.Arg<IEnumerable<CountryChallengePost>>()));
        _repository.When(r => r.RemovePosts(Arg.Any<IEnumerable<CountryChallengePost>>()))
            .Do(call => _removed.AddRange(call.Arg<IEnumerable<CountryChallengePost>>()));

        _discord.SendMessageAsync(Arg.Any<string>(), Arg.Any<ulong>(), Arg.Any<MessageMentions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _posted.Add((call.ArgAt<string>(0), call.ArgAt<ulong>(1), call.ArgAt<MessageMentions>(2)));
                return 900UL + (ulong)_posted.Count;
            });
    }

    private static CountryChallengePlan FridayPlan(bool thread = false) => Plan(File(
        Fixed("Argentina Friday", DayOfWeek.Friday, Country("Argentina", "AR")) with
        {
            Settings = new GameSettingsSection { TimeLimit = 60, ForbidMoving = true, ForbidRotating = true, ForbidZooming = true },
            MentionRoleIds = [222]
        },
        Fixed("Indonesia Friday", DayOfWeek.Friday, Country("Indonesia", "ID")) with
        {
            Results = new ChallengeResultsSection { AfterDays = 3 }
        },
        Fixed("Mongolia Monday", DayOfWeek.Monday, Country("Mongolia", "MN"))) with
    {
        Announcement = new AnnouncementSection { Thread = new ThreadSection { Enabled = thread } }
    });

    // ---- Happy path -------------------------------------------------------

    [Fact]
    public async Task Handle_CreatesStoresAndAnnouncesTheDaysChallenges_InOneMessage()
    {
        var outcome = await HandleAsync(FridayPlan(), Friday);

        outcome.Announced.Should().Equal("Argentina Friday: Argentina", "Indonesia Friday: Indonesia");
        _created.Select(c => c.Map).Should().Equal(MapIdOf("Argentina"), MapIdOf("Indonesia"));
        _stored.Select(p => (p.ChallengeName, p.Date, p.Country, p.ChallengeId, p.ResultsDueOn))
            .Should().Equal(
                ("Argentina Friday", Friday, "Argentina", "token-1", Friday.AddDays(1)),
                ("Indonesia Friday", Friday, "Indonesia", "token-2", Friday.AddDays(3)));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        var (content, channelId, mentions) = _posted.Should().ContainSingle().Subject;
        channelId.Should().Be(ChannelId);
        content.Should().Contain("https://www.geoguessr.com/challenge/token-1")
            .And.Contain("https://www.geoguessr.com/challenge/token-2");
        mentions.RoleIds.Should().Equal(222UL);
    }

    [Fact]
    public async Task Handle_CreatesTheChallengeWithTheResolvedSettings()
    {
        await HandleAsync(FridayPlan(), Friday);

        var argentina = _created[0];
        (argentina.TimeLimit, argentina.ForbidMoving, argentina.ForbidRotating, argentina.ForbidZooming)
            .Should().Be((60, true, true, true));
        _stored[0].Should().BeEquivalentTo(new { TimeLimit = 60, ForbidMoving = true, ForbidRotating = true, ForbidZooming = true });
    }

    [Fact]
    public async Task Handle_OpensAThreadOnTheAnnouncement_WhenConfigured()
    {
        await HandleAsync(FridayPlan(thread: true), Friday);

        await _discord.Received(1).CreateThreadAsync(
            ChannelId, 901UL, "Argentina Friday & Indonesia Friday · 2026-10-02", ThreadAutoArchive.OneDay, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_StoresNoResultsDate_ForAChallengeWithoutResults()
    {
        var plan = Plan(Fixed("Mongolia Monday", DayOfWeek.Monday, Country("Mongolia")) with
        {
            Results = new ChallengeResultsSection { Enabled = false }
        });

        await HandleAsync(plan, Monday);

        _stored.Single().ResultsDueOn.Should().BeNull();
    }

    [Fact]
    public async Task Handle_PicksAPoolCountryNotPlayedYetInThisRound()
    {
        var plan = Plan(Pool("Small Country Sunday", DayOfWeek.Sunday, Country("Malta"), Country("Andorra"), Country("Monaco")));
        _repository.ReadCountryHistoryAsync("Small Country Sunday", Arg.Any<CancellationToken>()).Returns(["Malta", "Monaco"]);

        await HandleAsync(plan, Sunday);

        _stored.Single().Country.Should().Be("Andorra");
        _created.Single().Map.Should().Be(MapIdOf("Andorra"));
    }

    [Fact]
    public async Task Handle_PlaysSeveralDifferentCountries_WhenAChallengePicksMoreThanOne()
    {
        var plan = Plan(Pool("Middleweight Saturday", DayOfWeek.Saturday, Country("Peru"), Country("Chile"), Country("Japan")) with
        {
            Picks = 2
        });

        var outcome = await HandleAsync(plan, Friday.AddDays(1));

        _stored.Should().HaveCount(2);
        _stored.Select(p => p.Country).Should().OnlyHaveUniqueItems();
        _stored.Should().OnlyContain(p => p.ChallengeName == "Middleweight Saturday");
        outcome.Announced.Should().HaveCount(2).And.OnlyContain(a => a.StartsWith("Middleweight Saturday: "));

        var content = _posted.Should().ContainSingle("both picks are announced together").Subject.Content;
        content.Should().Contain("token-1").And.Contain("token-2");
    }

    [Fact]
    public async Task Handle_OnlyCreatesTheMissingPicks_NeverRepeatingTheCountryAlreadyPlayedThatDay()
    {
        var plan = Plan(Pool("Middleweight Saturday", DayOfWeek.Saturday, Country("Peru"), Country("Chile"), Country("Japan")) with
        {
            Picks = 2
        });
        var saturday = Friday.AddDays(1);
        _repository.ReadPostsOnAsync(saturday, Arg.Any<CancellationToken>()).Returns([Post("Middleweight Saturday", saturday, "Peru")]);
        _repository.ReadCountryHistoryAsync("Middleweight Saturday", Arg.Any<CancellationToken>()).Returns(["Chile", "Japan", "Peru"]);

        var outcome = await HandleAsync(plan, saturday);

        // The round ended with Peru, so all three are candidates again — but Peru was already played today.
        _stored.Should().ContainSingle().Which.Country.Should().NotBe("Peru");
        outcome.AlreadyPosted.Should().BeEmpty();
    }

    // ---- Nothing to do ----------------------------------------------------

    [Fact]
    public async Task Handle_DoesNothing_OnADayWithoutChallenges()
    {
        var outcome = await HandleAsync(FridayPlan(), Friday.AddDays(1));

        outcome.Should().Be(CountryChallengeAnnouncementOutcome.Nothing);
        _created.Should().BeEmpty();
        _posted.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_SkipsTheChallengesAlreadyPostedThatDay()
    {
        _repository.ReadPostsOnAsync(Friday, Arg.Any<CancellationToken>()).Returns([Post("ARGENTINA FRIDAY", Friday, "Argentina")]);

        var outcome = await HandleAsync(FridayPlan(), Friday);

        outcome.AlreadyPosted.Should().Equal("Argentina Friday");
        outcome.Announced.Should().Equal("Indonesia Friday: Indonesia");
        _created.Should().ContainSingle().Which.Map.Should().Be(MapIdOf("Indonesia"));
    }

    [Fact]
    public async Task Handle_PostsNothing_WhenEveryChallengeWasAlreadyPosted()
    {
        _repository.ReadPostsOnAsync(Friday, Arg.Any<CancellationToken>())
            .Returns([Post("Argentina Friday", Friday, "Argentina"), Post("Indonesia Friday", Friday, "Indonesia")]);

        var outcome = await HandleAsync(FridayPlan(), Friday);

        outcome.AlreadyPosted.Should().HaveCount(2);
        _created.Should().BeEmpty();
        _posted.Should().BeEmpty();
    }

    // ---- Failures ---------------------------------------------------------

    [Fact]
    public async Task Handle_AnnouncesTheOtherChallenges_WhenGeoGuessrRefusesOne()
    {
        _client.CreateChallengeAsync(Arg.Is<PostChallengeRequestDto>(r => r.Map == MapIdOf("Argentina")), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("GeoGuessr is down"));

        var outcome = await HandleAsync(FridayPlan(), Friday);

        outcome.Announced.Should().Equal("Indonesia Friday: Indonesia");
        outcome.Failed.Should().Equal("Argentina Friday: Argentina");
        _stored.Should().ContainSingle().Which.ChallengeName.Should().Be("Indonesia Friday");
        _posted.Single().Content.Should().Contain(":warning: **Argentina Friday** (Argentina) could not be created today.");
    }

    [Fact]
    public async Task Handle_TreatsAChallengeWithoutAToken_AsNotCreated()
    {
        _client.CreateChallengeAsync(Arg.Any<PostChallengeRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(new PostChallengeResponseDto { Token = " " });

        var outcome = await HandleAsync(FridayPlan(), Friday);

        outcome.Failed.Should().HaveCount(2);
        _stored.Should().BeEmpty();
        _posted.Single().Mentions.IsEmpty.Should().BeTrue("a message that only reports failures pings no one");
    }

    [Fact]
    public async Task Handle_AnnouncesNothing_WhenTheChallengesCannotBeStored()
    {
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("database down"));

        var outcome = await HandleAsync(FridayPlan(), Friday);

        outcome.Announced.Should().BeEmpty();
        outcome.Failed.Should().Equal("Argentina Friday: Argentina", "Indonesia Friday: Indonesia");
        _posted.Should().BeEmpty("a challenge nobody can be rewarded for must not be announced");
        _removed.Should().BeEquivalentTo(_stored);
    }

    [Fact]
    public async Task Handle_ForgetsTheChallengesAgain_WhenDiscordRefusesTheAnnouncement()
    {
        _discord.SendMessageAsync(Arg.Any<string>(), Arg.Any<ulong>(), Arg.Any<MessageMentions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Discord is down"));

        var outcome = await HandleAsync(FridayPlan(thread: true), Friday);

        outcome.Announced.Should().BeEmpty();
        outcome.Failed.Should().Equal("Argentina Friday: Argentina", "Indonesia Friday: Indonesia");
        _removed.Should().BeEquivalentTo(_stored, "a later run the same day can then announce them");
        await _unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _discord.DidNotReceiveWithAnyArgs().CreateThreadAsync(default, default, default!, default, default);
    }

    [Fact]
    public async Task Handle_KeepsTheAnnouncement_WhenOnlyTheThreadCannotBeOpened()
    {
        _discord.CreateThreadAsync(Arg.Any<ulong>(), Arg.Any<ulong>(), Arg.Any<string>(), Arg.Any<ThreadAutoArchive>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Missing permissions"));

        var outcome = await HandleAsync(FridayPlan(thread: true), Friday);

        outcome.Announced.Should().HaveCount(2);
        _removed.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_OnlyForgetsTheChannelWhoseAnnouncementFailed()
    {
        var plan = Plan(
            Fixed("Mongolia Monday", DayOfWeek.Monday, Country("Mongolia")),
            Fixed("Chile Monday", DayOfWeek.Monday, Country("Chile")) with { ChannelId = 9 });
        _discord.SendMessageAsync(Arg.Any<string>(), 9UL, Arg.Any<MessageMentions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Missing access"));

        var outcome = await HandleAsync(plan, Monday);

        outcome.Announced.Should().Equal("Mongolia Monday: Mongolia");
        outcome.Failed.Should().Equal("Chile Monday: Chile");
        _removed.Should().ContainSingle().Which.ChallengeName.Should().Be("Chile Monday");
    }

    private Task<CountryChallengeAnnouncementOutcome> HandleAsync(CountryChallengePlan plan, DateOnly date) =>
        new AnnounceCountryChallengesHandler(
                _factory,
                _repository,
                _discord,
                _unitOfWork,
                new GeoGuessrConfigurationBuilder().WithClub(MainClubId).BuildOptions(),
                NullLogger<AnnounceCountryChallengesHandler>.Instance)
            .Handle(new AnnounceCountryChallengesCommand(plan, date), CancellationToken.None);
}
