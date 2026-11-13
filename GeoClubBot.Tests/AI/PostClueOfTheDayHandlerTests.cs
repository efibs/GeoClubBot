using Configuration;
using Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using UseCases.OutputPorts.AI;
using UseCases.OutputPorts.Discord;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.AI.ClueOfTheDay;
using UseCases.UseCases.AI.Conversations;
using UseCases.UseCases.AI.Retrieval;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// The clue of the day: a clue drawn as for "a clue I don't know yet", not one posted recently, posted
/// like an answer and recorded whether or not the post went out.
/// </summary>
public sealed class PostClueOfTheDayHandlerTests
{
    private const ulong Channel = 4242;

    private readonly IKnowledgeIndex _index = Substitute.For<IKnowledgeIndex>();
    private readonly IAiCluePostRepository _posts = Substitute.For<IAiCluePostRepository>();
    private readonly IAiChannelPublisher _publisher = Substitute.For<IAiChannelPublisher>();
    private readonly AiClueOfTheDayConfiguration _settings = new() { Enabled = true, TextChannelId = Channel, Draws = 3 };
    private readonly AiConfiguration _ai = new() { Active = true };
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 13, 17, 0, 0, TimeSpan.Zero));
    private readonly List<AiCluePost> _recorded = [];

    public PostClueOfTheDayHandlerTests()
    {
        _posts.ReadChunkIdsPostedSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(new HashSet<Guid>()));
        _posts.When(posts => posts.Add(Arg.Any<AiCluePost>())).Do(call => _recorded.Add(call.Arg<AiCluePost>()));
        _publisher.PostAsync(Arg.Any<ulong>(), Arg.Any<AiAnswer>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<ulong?>(777));
    }

    [Fact]
    public async Task PostsADrawnClue_ToTheConfiguredChannel_AndRecordsIt()
    {
        var clue = Clue("guide-a", "In Lạng Sơn the poles carry a white band at eye height, seen nowhere else.");
        Draws(clue);

        var result = await Handler().Handle(new PostClueOfTheDayCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
        result.Value.MessageId.Should().Be(777);
        result.Value.SourceUrl.Should().Be(clue.SourceUrl);

        await _publisher.Received(1).PostAsync(Channel,
            Arg.Is<AiAnswer>(answer => answer.Text.Contains("Clue of the day") && answer.Images.Count == 1 && answer.Images[0].ImageUrl == clue.ImageUrl),
            Arg.Any<CancellationToken>());

        var recorded = _recorded.Should().ContainSingle().Subject;
        recorded.ChunkId.Should().Be(clue.Id);
        recorded.ChannelId.Should().Be(Channel);
        recorded.MessageId.Should().Be(777);
        recorded.PostedAtUtc.Should().Be(_clock.GetUtcNow());
    }

    [Fact]
    public async Task SkipsAClue_PostedWithinTheNoRepeatWindow()
    {
        var recent = Clue("guide-a", "A clue posted last month, long enough to count as a clue of its own in the index.");
        var fresh = Clue("guide-b", "A clue never posted before, long enough to count as a clue of its own in the index.");
        Draws(recent, fresh);
        _posts.ReadChunkIdsPostedSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HashSet<Guid> { recent.Id }));

        var result = await Handler().Handle(new PostClueOfTheDayCommand(), CancellationToken.None);

        result.Value.SourceUrl.Should().Be(fresh.SourceUrl);
        await _posts.Received().ReadChunkIdsPostedSinceAsync(_clock.GetUtcNow() - TimeSpan.FromDays(180), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaysSo_WhenEveryDrawWasPostedRecently()
    {
        var clue = Clue("guide-a", "A clue posted last month, long enough to count as a clue of its own in the index.");
        Draws(clue);
        _posts.ReadChunkIdsPostedSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HashSet<Guid> { clue.Id }));

        var result = await Handler().Handle(new PostClueOfTheDayCommand(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ClueOfTheDayErrorCodes.NothingToPost);
        await _publisher.DidNotReceiveWithAnyArgs().PostAsync(default, default!, default);
        _recorded.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordsTheClue_EvenWhenThePostFails_SoItIsNotDrawnAgainSoon()
    {
        Draws(Clue("guide-a", "A clue the channel refused, long enough to count as a clue of its own in the index."));
        _publisher.PostAsync(Arg.Any<ulong>(), Arg.Any<AiAnswer>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<ulong?>(null));

        var result = await Handler().Handle(new PostClueOfTheDayCommand(), CancellationToken.None);

        result.Error.Code.Should().Be(ClueOfTheDayErrorCodes.NotPosted);
        _recorded.Should().ContainSingle().Which.MessageId.Should().BeNull();
    }

    [Fact]
    public async Task PostsToTheAdminsChannel_WhenAsked_EvenWhileTheScheduleIsOff()
    {
        _settings.Enabled = false;
        _settings.TextChannelId = null;
        Draws(Clue("guide-a", "A clue for the admin trying it out, long enough to count as a clue of its own."));

        var result = await Handler().Handle(new PostClueOfTheDayCommand(ChannelId: 99), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _publisher.Received(1).PostAsync(99, Arg.Any<AiAnswer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefusesTheSchedule_WhileOff_OrWithoutAChannel()
    {
        _settings.Enabled = false;
        (await Handler().Handle(new PostClueOfTheDayCommand(), CancellationToken.None)).Error.Code.Should().Be(ClueOfTheDayErrorCodes.Disabled);

        _settings.Enabled = true;
        _settings.TextChannelId = null;
        (await Handler().Handle(new PostClueOfTheDayCommand(), CancellationToken.None)).Error.Code.Should().Be(ClueOfTheDayErrorCodes.NoChannel);

        _ai.Active = false;
        _settings.TextChannelId = Channel;
        (await Handler().Handle(new PostClueOfTheDayCommand(), CancellationToken.None)).Error.Code.Should().Be(ClueOfTheDayErrorCodes.Disabled);

        await _index.DidNotReceiveWithAnyArgs().SampleAsync(default!, default);
    }

    [Fact]
    public async Task DrawsAsManyAsConfigured_LeavingPlonkItOut()
    {
        Draws(Clue("guide-a", "A clue long enough to count as a clue of its own, about a short white bollard."));

        await Handler().Handle(new PostClueOfTheDayCommand(), CancellationToken.None);

        await _index.Received(1).SampleAsync(
            Arg.Is<KnowledgeSample>(sample => sample.Limit == 3 * 8 && sample.Kind == KnowledgeChunkKind.Image && sample.ExcludedSourceType == MainGuideOverlap.MainGuide),
            Arg.Any<CancellationToken>());
    }

    private PostClueOfTheDayHandler Handler()
    {
        var retrieval = Options.Create(new AiRetrievalConfiguration());
        var countries = new KnowledgeCountryDirectory(_index, retrieval, TimeProvider.System, NullLogger<KnowledgeCountryDirectory>.Instance);
        var sampler = new ClueSampler(
            _index,
            new MainGuideOverlap(_index, countries, retrieval, NullLogger<MainGuideOverlap>.Instance),
            retrieval,
            NullLogger<ClueSampler>.Instance);

        return new PostClueOfTheDayHandler(
            sampler, _posts, _publisher, Options.Create(_settings), Options.Create(_ai), _clock, NullLogger<PostClueOfTheDayHandler>.Instance);
    }

    private void Draws(params KnowledgeHit[] clues) =>
        _index.SampleAsync(Arg.Any<KnowledgeSample>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<KnowledgeHit>>(clues));

    private static KnowledgeHit Clue(string guide, string text) =>
        new(Guid.NewGuid(), 0f, KnowledgeChunkKind.Image, text, $"https://docs.google.com/presentation/d/{guide}/edit",
            $"https://bot.example/api/v1/ai/images/{guide}.png", "A guide", null, "A guide > Slide 3", null, 0, "gslides", guide, "3-img-1");
}
