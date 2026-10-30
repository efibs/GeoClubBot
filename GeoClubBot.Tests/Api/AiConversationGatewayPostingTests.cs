using System.Net;
using Configuration;
using Discord;
using Discord.Net;
using FluentAssertions;
using GeoClubBot.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Conversations;
using Xunit;

namespace GeoClubBot.Tests.Api;

/// <summary>
/// How an answer reaches Discord. Relayed pictures are uploaded with it rather than linked, because on
/// 2026-10-22 Discord could not resolve the bot's public URL and every linked relayed picture failed
/// to show while its bytes sat on the bot's own disk.
/// </summary>
public sealed class AiConversationGatewayPostingTests
{
    private const string CurrentBase = "https://bot.example.net";
    private const ulong QuestionId = 1000;

    private static readonly string StoredName = new string('a', 64) + ".png";

    private readonly IMessageChannel _channel = Substitute.For<IMessageChannel>();
    private readonly IImageRelay _relay = Substitute.For<IImageRelay>();
    private readonly List<SentMessage> _sent = [];
    private ulong _nextMessageId = 2000;

    public AiConversationGatewayPostingTests()
    {
        _channel
            .SendMessageAsync(
                Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<Embed>(), Arg.Any<RequestOptions>(),
                Arg.Any<AllowedMentions>(), Arg.Any<MessageReference>(), Arg.Any<MessageComponent>(),
                Arg.Any<ISticker[]>(), Arg.Any<Embed[]>(), Arg.Any<MessageFlags>(), Arg.Any<PollProperties>())
            .Returns(call => Record(
                call.ArgAt<string>(0), call.ArgAt<AllowedMentions>(4), call.ArgAt<MessageReference>(5),
                call.ArgAt<Embed[]?>(8), attachments: []));

        _channel
            .SendFilesAsync(
                Arg.Any<IEnumerable<FileAttachment>>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<Embed>(),
                Arg.Any<RequestOptions>(), Arg.Any<AllowedMentions>(), Arg.Any<MessageReference>(),
                Arg.Any<MessageComponent>(), Arg.Any<ISticker[]>(), Arg.Any<Embed[]>(), Arg.Any<MessageFlags>(),
                Arg.Any<PollProperties>())
            .Returns(call => Record(
                call.ArgAt<string>(1), call.ArgAt<AllowedMentions>(5), call.ArgAt<MessageReference>(6),
                call.ArgAt<Embed[]?>(9), [.. call.ArgAt<IEnumerable<FileAttachment>>(0)]));
    }

    [Fact]
    public async Task Post_UploadsARelayedPictureOnDisk_AsAReplyThatMentionsNobody()
    {
        var file = new MemoryStream([1, 2, 3]);
        _relay.ReadAsync(StoredName, Arg.Any<CancellationToken>())
            .Returns(new RelayedImageContent(file, "image/png"));

        var posted = await PostAsync(Answer("See [1].", $"https://old-host.tailnet.ts.net/api/v1/ai/images/{StoredName}"));

        var sent = _sent.Should().ContainSingle().Subject;
        sent.Attachments.Should().ContainSingle().Which.Should().Be(new SentAttachment("picture-1.png", WasReadable: true));
        sent.EmbedImageUrls.Should().Equal("attachment://picture-1.png");
        sent.ReplyTo.Should().Be(QuestionId);
        sent.AllowedMentions.Should().BeEquivalentTo(AllowedMentions.None, "nothing the model writes may ping anyone");
        posted.LastMessageId.Should().Be(sent.Id);
        file.CanRead.Should().BeFalse("every file opened for the upload is closed afterwards");
    }

    [Fact]
    public async Task Post_LinksAMissingPictureUnderTheCurrentBase()
    {
        // ReadAsync answers null: the file is not on disk, so the link is all there is — and the host
        // it was indexed under may be the one that stopped resolving.
        await PostAsync(Answer("See [1].", $"https://old-host.tailnet.ts.net/api/v1/ai/images/{StoredName}"));

        var sent = _sent.Should().ContainSingle().Subject;
        sent.Attachments.Should().BeEmpty();
        sent.EmbedImageUrls.Should().Equal($"{CurrentBase}/api/v1/ai/images/{StoredName}");
        sent.ReplyTo.Should().Be(QuestionId);
    }

    [Fact]
    public async Task Post_LinksAPictureFromAnotherHost_WithoutLookingOnDisk()
    {
        await PostAsync(Answer("See [1].", "https://i.imgur.com/map.png"));

        _sent.Single().EmbedImageUrls.Should().Equal("https://i.imgur.com/map.png");
        await _relay.DidNotReceiveWithAnyArgs().ReadAsync(default!, default);
    }

    [Fact]
    public async Task Post_PutsThePicturesOnTheLastChunkOnly_AndRepliesEachChunkToThePrevious()
    {
        _relay.ReadAsync(StoredName, Arg.Any<CancellationToken>())
            .Returns(new RelayedImageContent(new MemoryStream([1, 2, 3]), "image/png"));
        var longText = string.Join("\n", Enumerable.Repeat("A fairly long line about bollards.", 120));

        var posted = await PostAsync(Answer(longText, $"{CurrentBase}/api/v1/ai/images/{StoredName}"));

        _sent.Should().HaveCountGreaterThan(1);
        _sent.SkipLast(1).Should().OnlyContain(message => message.Attachments.Count == 0 && message.EmbedImageUrls.Count == 0);
        _sent[^1].Attachments.Should().ContainSingle();
        _sent[0].ReplyTo.Should().Be(QuestionId);
        _sent.Skip(1).Select(message => message.ReplyTo).Should().Equal(_sent.SkipLast(1).Select(message => (ulong?)message.Id));
        posted.EarlierMessageIds.Should().Equal(_sent.SkipLast(1).Select(message => message.Id));
    }

    [Fact]
    public async Task Post_FallsBackToLinks_WhenDiscordRefusesTheUpload()
    {
        // Over a server's upload limit, or without Attach Files: the answer must still arrive.
        var file = new MemoryStream([1, 2, 3]);
        _relay.ReadAsync(StoredName, Arg.Any<CancellationToken>())
            .Returns(new RelayedImageContent(file, "image/png"));
        UploadsThrow(new HttpException(HttpStatusCode.RequestEntityTooLarge, Substitute.For<IRequest>()));

        var posted = await PostAsync(Answer("See [1].", $"https://old-host.tailnet.ts.net/api/v1/ai/images/{StoredName}"));

        var sent = _sent.Should().ContainSingle().Subject;
        sent.Attachments.Should().BeEmpty();
        sent.EmbedImageUrls.Should().Equal($"{CurrentBase}/api/v1/ai/images/{StoredName}");
        sent.ReplyTo.Should().Be(QuestionId);
        posted.LastMessageId.Should().Be(sent.Id);
        file.CanRead.Should().BeFalse();
    }

    [Theory]
    [InlineData(nameof(HttpRequestException))]
    [InlineData(nameof(TaskCanceledException))]
    [InlineData(nameof(IOException))]
    [InlineData(nameof(ArgumentOutOfRangeException))]
    public async Task Post_FallsBackToLinks_WhenTheUploadFailsInTransit(string failure)
    {
        // Discord.Net throws HttpException only for an error status. A connection reset during the
        // multipart POST, its HttpClient's 100 s timeout, a file failing mid-read, and its own pre-flight
        // check against the guild's upload limit all escape its request queue as these instead — and the
        // small linked reply would very likely still have gone through.
        var file = new MemoryStream([1, 2, 3]);
        _relay.ReadAsync(StoredName, Arg.Any<CancellationToken>())
            .Returns(new RelayedImageContent(file, "image/png"));
        UploadsThrow(failure switch
        {
            nameof(HttpRequestException) => new HttpRequestException("The SSL connection could not be established."),
            nameof(TaskCanceledException) => new TaskCanceledException(
                "The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.",
                new TimeoutException()),
            nameof(IOException) => new IOException("Unable to read data from the transport connection."),
            nameof(ArgumentOutOfRangeException) => new ArgumentOutOfRangeException(
                "attachments", "File size exceeds the max file size of 10485760 bytes in that guild!"),
            _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, null),
        });

        var posted = await PostAsync(Answer("See [1].", $"https://old-host.tailnet.ts.net/api/v1/ai/images/{StoredName}"));

        var sent = _sent.Should().ContainSingle().Subject;
        sent.Attachments.Should().BeEmpty();
        sent.EmbedImageUrls.Should().Equal($"{CurrentBase}/api/v1/ai/images/{StoredName}");
        sent.ReplyTo.Should().Be(QuestionId);
        sent.AllowedMentions.Should().BeEquivalentTo(AllowedMentions.None);
        posted.LastMessageId.Should().Be(sent.Id);
        file.CanRead.Should().BeFalse();
    }

    [Fact]
    public async Task Post_DoesNotHideAFailureThatIsNotTheUpload()
    {
        // The fallback is for a failed upload, not a catch-all: a bug must still surface.
        var file = new MemoryStream([1, 2, 3]);
        _relay.ReadAsync(StoredName, Arg.Any<CancellationToken>())
            .Returns(new RelayedImageContent(file, "image/png"));
        UploadsThrow(new InvalidOperationException("bug"));

        var post = () => PostAsync(Answer("See [1].", $"{CurrentBase}/api/v1/ai/images/{StoredName}"));

        await post.Should().ThrowAsync<InvalidOperationException>();
        _sent.Should().BeEmpty();
        file.CanRead.Should().BeFalse("the opened files are closed however the post ends");
    }

    [Fact]
    public async Task Post_LinksAPicture_WhenItsFileCannotBeRead()
    {
        _relay.ReadAsync(StoredName, Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("disk gone"));

        await PostAsync(Answer("See [1].", $"{CurrentBase}/api/v1/ai/images/{StoredName}"));

        _sent.Single().EmbedImageUrls.Should().Equal($"{CurrentBase}/api/v1/ai/images/{StoredName}");
    }

    private Task<AiConversationGateway.PostedAnswer> PostAsync(AiAnswer answer)
    {
        var question = Substitute.For<IUserMessage>();
        question.Id.Returns(QuestionId);
        question.Channel.Returns(_channel);

        return AiConversationGateway.PostAsync(
            question,
            answer,
            _relay,
            new AiImageRelayConfiguration { PublicBaseUrl = CurrentBase },
            NullLogger.Instance);
    }

    private void UploadsThrow(Exception exception) =>
        _channel
            .SendFilesAsync(
                Arg.Any<IEnumerable<FileAttachment>>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<Embed>(),
                Arg.Any<RequestOptions>(), Arg.Any<AllowedMentions>(), Arg.Any<MessageReference>(),
                Arg.Any<MessageComponent>(), Arg.Any<ISticker[]>(), Arg.Any<Embed[]>(), Arg.Any<MessageFlags>(),
                Arg.Any<PollProperties>())
            .ThrowsAsync(exception);

    private static AiAnswer Answer(string text, string imageUrl) =>
        new(text,
            [new AiAnswerImage(1, imageUrl, "https://www.plonkit.net/philippines#GaHP", "Philippines > Agriculture")],
            [new AiAnswerSource(1, "Philippines > Agriculture", "https://www.plonkit.net/philippines#GaHP", IsImage: true)],
            "test/model",
            ConversationId: QuestionId,
            Depth: 1,
            IsLongThread: false,
            RetrievedSourceUrls: [],
            CitedSourceUrls: []);

    private Task<IUserMessage> Record(
        string text,
        AllowedMentions? allowedMentions,
        MessageReference? reference,
        Embed[]? embeds,
        IReadOnlyList<FileAttachment> attachments)
    {
        var id = _nextMessageId++;
        _sent.Add(new SentMessage(
            id,
            text,
            reference?.MessageId.IsSpecified == true ? reference.MessageId.Value : null,
            allowedMentions,
            [.. (embeds ?? []).Select(embed => embed.Image?.Url ?? string.Empty)],
            [.. attachments.Select(attachment => new SentAttachment(attachment.FileName, attachment.Stream.CanRead))]));

        var message = Substitute.For<IUserMessage>();
        message.Id.Returns(id);
        message.Channel.Returns(_channel);
        return Task.FromResult(message);
    }

    private sealed record SentAttachment(string FileName, bool WasReadable);

    private sealed record SentMessage(
        ulong Id,
        string Text,
        ulong? ReplyTo,
        AllowedMentions? AllowedMentions,
        IReadOnlyList<string> EmbedImageUrls,
        IReadOnlyList<SentAttachment> Attachments);
}
