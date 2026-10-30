using Configuration;
using FluentAssertions;
using GeoClubBot.Discord.InputAdapters.Interactions.AI;
using UseCases.UseCases.AI.Conversations;
using Xunit;

namespace GeoClubBot.Tests.Discord;

/// <summary>
/// Which pictures go up with an answer and which are linked. Uploading is what keeps a relayed picture
/// visible when Discord cannot reach the bot's public URL — the 2026-10-22 outage, where every relayed
/// picture silently failed to show — so each reason a picture falls back to a link is pinned here.
/// </summary>
public sealed class AiAnswerPicturePlannerTests
{
    private const string CurrentBase = "https://bot.example.net";
    private const string OldBase = "https://old-host.tailnet.ts.net";

    [Fact]
    public void Plan_UploadsARelayedPictureFoundOnDisk()
    {
        var name = Name('a', ".png");
        var image = Image(1, $"{OldBase}/api/v1/ai/images/{name}");

        var plan = AiAnswerPicturePlanner.Plan([image], Stored((name, "image/png", 1_000)), Settings());

        var picture = plan.Pictures.Should().ContainSingle().Subject;
        picture.Delivery.Should().Be(AiAnswerPictureDelivery.Uploaded);
        picture.Upload.Should().Be(new AiAnswerUpload("picture-1.png", name, 1_000));
        picture.EmbedImageUrl.Should().Be("attachment://picture-1.png");
        plan.Uploads.Should().ContainSingle();
    }

    [Theory]
    [InlineData("https://i.imgur.com/map.png")]
    [InlineData("https://assets.rmrg.me/guides/brazil/poles.webp")]
    public void Plan_LinksAPictureFromAnotherHostAsItWas(string imageUrl)
    {
        // Those hosts serve their own pictures; copying them would be the bigger imposition.
        var plan = AiAnswerPicturePlanner.Plan([Image(1, imageUrl)], Stored(), Settings());

        var picture = plan.Pictures.Single();
        picture.Delivery.Should().Be(AiAnswerPictureDelivery.Linked);
        picture.EmbedImageUrl.Should().Be(imageUrl);
        plan.Uploads.Should().BeEmpty();
    }

    [Fact]
    public void Plan_LinksAMissingFileUnderTheCurrentPublicBase()
    {
        // The host a URL was indexed under may no longer resolve; the configured one is the only
        // address this bot can still be expected to answer on.
        var name = Name('b', ".jpg");

        var plan = AiAnswerPicturePlanner.Plan([Image(1, $"{OldBase}/api/v1/ai/images/{name}")], Stored(), Settings());

        var picture = plan.Pictures.Single();
        picture.Delivery.Should().Be(AiAnswerPictureDelivery.FileMissing);
        picture.EmbedImageUrl.Should().Be($"{CurrentBase}/api/v1/ai/images/{name}");
    }

    [Fact]
    public void Plan_KeepsTheStoredLink_WhenNoPublicBaseIsConfigured()
    {
        var url = $"{OldBase}/api/v1/ai/images/{Name('b', ".jpg")}";

        var plan = AiAnswerPicturePlanner.Plan([Image(1, url)], Stored(), Settings(publicBaseUrl: null));

        plan.Pictures.Single().EmbedImageUrl.Should().Be(url);
    }

    [Fact]
    public void Plan_StillUploads_WhenNoPublicBaseIsConfigured()
    {
        // The bytes are on disk either way; only the link depends on the public address.
        var name = Name('a', ".png");

        var plan = AiAnswerPicturePlanner.Plan(
            [Image(1, $"{OldBase}/api/v1/ai/images/{name}")], Stored((name, "image/png", 10)), Settings(publicBaseUrl: null));

        plan.Pictures.Single().Delivery.Should().Be(AiAnswerPictureDelivery.Uploaded);
    }

    [Fact]
    public void Plan_LinksAPictureOverThePerPictureCap()
    {
        var name = Name('a', ".png");

        var plan = AiAnswerPicturePlanner.Plan(
            [Image(1, $"{CurrentBase}/api/v1/ai/images/{name}")],
            Stored((name, "image/png", 501)),
            Settings(perPicture: 500));

        plan.Pictures.Single().Delivery.Should().Be(AiAnswerPictureDelivery.TooLarge);
        plan.Uploads.Should().BeEmpty();
    }

    [Fact]
    public void Plan_LinksAPictureOfUnknownSize()
    {
        // Sending a file of unknown size could take the whole message past Discord's limit.
        var name = Name('a', ".png");

        var plan = AiAnswerPicturePlanner.Plan(
            [Image(1, $"{CurrentBase}/api/v1/ai/images/{name}")],
            new Dictionary<string, StoredPicture> { [name] = new("image/png", null) },
            Settings());

        plan.Pictures.Single().Delivery.Should().Be(AiAnswerPictureDelivery.TooLarge);
    }

    [Fact]
    public void Plan_StopsAtTheAnswerAllowance_ButStillFitsASmallerLaterPicture()
    {
        var (first, second, third) = (Name('1', ".png"), Name('2', ".png"), Name('3', ".png"));

        var plan = AiAnswerPicturePlanner.Plan(
            [
                Image(1, $"{CurrentBase}/api/v1/ai/images/{first}"),
                Image(2, $"{CurrentBase}/api/v1/ai/images/{second}"),
                Image(3, $"{CurrentBase}/api/v1/ai/images/{third}")
            ],
            Stored((first, "image/png", 60), (second, "image/png", 60), (third, "image/png", 30)),
            Settings(perAnswer: 100));

        plan.Pictures.Select(picture => picture.Delivery).Should().Equal(
            AiAnswerPictureDelivery.Uploaded, AiAnswerPictureDelivery.OverAllowance, AiAnswerPictureDelivery.Uploaded);
        plan.Uploads.Sum(upload => upload.Length).Should().Be(90);
        plan.Pictures[1].EmbedImageUrl.Should().Be($"{CurrentBase}/api/v1/ai/images/{second}");
    }

    [Fact]
    public void Plan_UploadsNothing_WhenTheAllowanceIsZero()
    {
        var name = Name('a', ".png");

        var plan = AiAnswerPicturePlanner.Plan(
            [Image(1, $"{CurrentBase}/api/v1/ai/images/{name}")], Stored((name, "image/png", 10)), Settings(perAnswer: 0));

        plan.Pictures.Single().Delivery.Should().Be(AiAnswerPictureDelivery.OverAllowance);
    }

    [Fact]
    public void Plan_ConsidersAtMostTenPictures()
    {
        // Discord refuses a message with an eleventh embed or attachment, and the picture with it.
        var names = Enumerable.Range(1, 12).Select(marker => $"{marker:x64}.png").ToList();
        var images = names.Select((name, index) => Image(index + 1, $"{CurrentBase}/api/v1/ai/images/{name}")).ToList();

        var plan = AiAnswerPicturePlanner.Plan(
            images, Stored([.. names.Select(name => (name, "image/png", 10L))]), Settings());

        plan.Pictures.Should().HaveCount(AiAnswerPicturePlanner.MaxPicturesPerMessage);
        plan.Uploads.Should().HaveCount(AiAnswerPicturePlanner.MaxPicturesPerMessage);
        AiAnswerPicturePlanner.RelayedNames(images).Should().HaveCount(AiAnswerPicturePlanner.MaxPicturesPerMessage);
    }

    [Theory]
    [InlineData("image/png", ".png", "picture-4.png")]
    [InlineData("image/jpeg", ".jpg", "picture-4.jpg")]
    [InlineData("image/webp", ".webp", "picture-4.webp")]
    [InlineData("image/gif", ".gif", "picture-4.gif")]
    // A type the relay never writes keeps the stored name's extension rather than losing it.
    [InlineData("application/octet-stream", ".png", "picture-4.png")]
    public void Plan_NamesTheFileByItsNumber_WithTheExtensionOfItsType(string contentType, string storedExtension, string expected)
    {
        // Discord goes by the name's extension when deciding whether an attachment is a picture.
        var name = Name('c', storedExtension);

        var plan = AiAnswerPicturePlanner.Plan(
            [Image(4, $"{CurrentBase}/api/v1/ai/images/{name}")], Stored((name, contentType, 10)), Settings());

        plan.Uploads.Single().FileName.Should().Be(expected);
    }

    [Fact]
    public void Plan_GivesEachUploadItsOwnFileName_WhenNumbersRepeat()
    {
        var (first, second) = (Name('1', ".png"), Name('2', ".png"));

        var plan = AiAnswerPicturePlanner.Plan(
            [Image(1, $"{CurrentBase}/api/v1/ai/images/{first}"), Image(1, $"{CurrentBase}/api/v1/ai/images/{second}")],
            Stored((first, "image/png", 10), (second, "image/png", 10)),
            Settings());

        plan.Uploads.Select(upload => upload.FileName).Should().Equal("picture-1.png", "picture-1-2.png");
    }

    [Fact]
    public void Plan_NamesEachOfAnExcerptsPictures_ByItsPlaceAmongThem()
    {
        // One cited slide caption brings three pictures, all numbered [2]; the files say which is which,
        // as the embeds' "(1/3)", "(2/3)" do, even when one of them is linked rather than uploaded.
        var (first, missing, third) = (Name('1', ".png"), Name('2', ".png"), Name('3', ".jpg"));

        var plan = AiAnswerPicturePlanner.Plan(
            [
                Image(2, $"{CurrentBase}/api/v1/ai/images/{first}") with { Position = 1, Count = 3 },
                Image(2, $"{CurrentBase}/api/v1/ai/images/{missing}") with { Position = 2, Count = 3 },
                Image(2, $"{CurrentBase}/api/v1/ai/images/{third}") with { Position = 3, Count = 3 }
            ],
            Stored((first, "image/png", 10), (third, "image/jpeg", 10)),
            Settings());

        plan.Uploads.Select(upload => upload.FileName).Should().Equal("picture-2-1.png", "picture-2-3.jpg");
        plan.Pictures.Select(picture => picture.Delivery).Should().Equal(
            AiAnswerPictureDelivery.Uploaded, AiAnswerPictureDelivery.FileMissing, AiAnswerPictureDelivery.Uploaded);
    }

    [Fact]
    public void Plan_SendsTheSameBytesOnce_WhenTheyAreCitedUnderTwoAddresses()
    {
        // An image indexed under an old host and again under the current one is one file on disk.
        var name = Name('a', ".png");

        var plan = AiAnswerPicturePlanner.Plan(
            [Image(1, $"{OldBase}/api/v1/ai/images/{name}"), Image(2, $"{CurrentBase}/api/v1/ai/images/{name}")],
            Stored((name, "image/png", 60)),
            Settings(perAnswer: 100));

        plan.Uploads.Should().ContainSingle();
        plan.Pictures.Select(picture => picture.EmbedImageUrl).Should().Equal(
            "attachment://picture-1.png", "attachment://picture-1.png");
    }

    [Fact]
    public void WithoutUploads_LinksEveryUploadedPictureUnderTheCurrentBase()
    {
        // What is posted when Discord refuses the files: the answer must still arrive.
        var name = Name('a', ".png");
        var plan = AiAnswerPicturePlanner.Plan(
            [Image(1, $"{OldBase}/api/v1/ai/images/{name}"), Image(2, "https://i.imgur.com/map.png")],
            Stored((name, "image/png", 10)),
            Settings());

        var linked = plan.WithoutUploads();

        linked.Uploads.Should().BeEmpty();
        linked.Pictures.Select(picture => picture.Delivery).Should().Equal(
            AiAnswerPictureDelivery.UploadRefused, AiAnswerPictureDelivery.Linked);
        linked.Pictures.Select(picture => picture.EmbedImageUrl).Should().Equal(
            $"{CurrentBase}/api/v1/ai/images/{name}", "https://i.imgur.com/map.png");
    }

    [Fact]
    public void RelayedNames_ListsEachStoredNameOnce_AndNothingElse()
    {
        var name = Name('a', ".png");

        var names = AiAnswerPicturePlanner.RelayedNames(
        [
            Image(1, $"{OldBase}/api/v1/ai/images/{name}"),
            Image(2, $"{CurrentBase}/api/v1/ai/images/{name}"),
            Image(3, "https://i.imgur.com/map.png"),
            Image(4, $"{CurrentBase}/api/v1/ai/images/../../etc/passwd")
        ]);

        names.Should().Equal(name);
    }

    private static string Name(char digit, string extension) => new string(digit, 64) + extension;

    private static AiAnswerImage Image(int marker, string url) =>
        new(marker, url, $"https://guide.example/{marker}", Title: null);

    private static Dictionary<string, StoredPicture> Stored(params (string Name, string ContentType, long Length)[] files) =>
        files.ToDictionary(file => file.Name, file => new StoredPicture(file.ContentType, file.Length));

    private static AiImageRelayConfiguration Settings(
        string? publicBaseUrl = CurrentBase,
        long perAnswer = 8 * 1024 * 1024,
        long perPicture = 8 * 1024 * 1024) =>
        new()
        {
            PublicBaseUrl = publicBaseUrl,
            MaxUploadBytesPerAnswer = perAnswer,
            MaxUploadBytesPerPicture = perPicture
        };
}
