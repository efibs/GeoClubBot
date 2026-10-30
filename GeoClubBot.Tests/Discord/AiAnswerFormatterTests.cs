using System.Text;
using Configuration;
using Discord;
using FluentAssertions;
using GeoClubBot.Discord.InputAdapters.Interactions.AI;
using UseCases.UseCases.AI.Conversations;
using Xunit;
using static VerifyXunit.Verifier;

namespace GeoClubBot.Tests.Discord;

public sealed class AiAnswerFormatterTests
{
    [Fact]
    public Task Render_ShowsTheAnswerWithItsModelAttribution()
    {
        var answer = new AiAnswer(
            "Ghanaian bollards are short, white, and squared off at the top.",
            [],
            [],
            "google/gemma-4-31b-it:free",
            ConversationId: 100,
            Depth: 1,
            IsLongThread: false,
            RetrievedSourceUrls: [],
            CitedSourceUrls: []);

        return Verify(Render(RenderLinked(answer)));
    }

    [Fact]
    public Task Render_AttachesCitedGuideImagesAsLinkedEmbeds()
    {
        // Pictures are numbered like every other citation — in the prose, in the list and on the
        // embed — so "[2]" in a sentence leads to the picture that shows it.
        var answer = new AiAnswer(
            "The area codes are laid out by governorate [1], and signs show them in blue [2][3].",
            [
                new AiAnswerImage(1, "https://i.imgur.com/map.png", "https://docs.google.com/document/d/abc", "Tunisia area codes"),
                new AiAnswerImage(3, "https://i.imgur.com/signs.png", "https://www.plonkit.net/tunisia", null)
            ],
            [
                new AiAnswerSource(1, "Tunisia · Area codes", "https://docs.google.com/document/d/abc", IsImage: true),
                new AiAnswerSource(2, "Tunisia > Signs", "https://www.plonkit.net/tunisia#s1gn"),
                new AiAnswerSource(3, "Tunisia > Signs", "https://www.plonkit.net/tunisia", IsImage: true)
            ],
            "dots-studio/dots-3-note-preview:free",
            ConversationId: 100,
            Depth: 3,
            IsLongThread: false,
            RetrievedSourceUrls: [],
            CitedSourceUrls: []);

        return Verify(Render(RenderLinked(answer)));
    }

    [Fact]
    public Task Render_ResolvesCitationMarkersIntoNamedClickableSources()
    {
        // A bare "[1]" tells the reader nothing and leads nowhere. Each marker the answer uses is
        // listed below it with the heading the model saw and a link to the guide it came from.
        var answer = new AiAnswer(
            "Roads starting with MR are exclusive to Eswatini [1], and the MR9 runs through dark, "
            + "wooded highlands [2].",
            [],
            [
                new AiAnswerSource(1, "Eswatini > Identifying", "https://www.plonkit.net/eswatini#m1jr"),
                new AiAnswerSource(2, "Eswatini > Regional clues", "https://www.plonkit.net/eswatini#1chu")
            ],
            "minimax/minimax-m3:free",
            ConversationId: 100,
            Depth: 1,
            IsLongThread: false,
            RetrievedSourceUrls: [],
            CitedSourceUrls: []);

        return Verify(Render(RenderLinked(answer)));
    }

    [Fact]
    public Task Render_SaysTheAnswerCitesNoGuide_WhenTheModelCitedNothing()
    {
        // Unnumbered, because there is no marker in the prose for a number to point at, and headed so
        // the list does not read as backing the answer never claimed: "I can't confirm if it is
        // actually correct since there is no source to back those claims."
        var answer = new AiAnswer(
            "Roads starting with MR are exclusive to Eswatini.",
            [],
            [
                new AiAnswerSource(null, "Eswatini > Identifying", "https://www.plonkit.net/eswatini#m1jr"),
                new AiAnswerSource(null, "Eswatini > Regional clues", "https://www.plonkit.net/eswatini#1chu")
            ],
            "minimax/minimax-m3:free",
            ConversationId: 100,
            Depth: 1,
            IsLongThread: false,
            RetrievedSourceUrls: [],
            CitedSourceUrls: []);

        return Verify(Render(RenderLinked(answer)));
    }

    [Fact]
    public Task Render_SuggestsAFreshThread_WhenTheBranchIsLong()
    {
        var answer = new AiAnswer("Still here.", [], [], "test/model", ConversationId: 100, Depth: 21, IsLongThread: true, RetrievedSourceUrls: [], CitedSourceUrls: []);

        return Verify(Render(RenderLinked(answer)));
    }

    [Fact]
    public void Render_KeepsALongPictureTitleWithinDiscordsLimit()
    {
        // Discord rejects the whole message when an embed title runs past 256 characters.
        var answer = new AiAnswer(
            "See [1].",
            [new AiAnswerImage(1, "https://i.imgur.com/a.png", "https://guide/a", new string('x', 400))],
            [new AiAnswerSource(1, "A", "https://guide/a", IsImage: true)],
            "test/model", ConversationId: 100, Depth: 1, IsLongThread: false, RetrievedSourceUrls: [], CitedSourceUrls: []);

        var rendering = RenderLinked(answer);

        rendering.Embeds.Single().Title.Should().StartWith("[1] ").And.HaveLength(256);
    }

    [Fact]
    public void Render_SplitsALongAnswerAcrossMessages()
    {
        // Discord rejects anything over 2000 characters, so an answer that exceeds it must arrive as
        // several messages rather than being truncated or dropped.
        var answer = new AiAnswer(
            string.Join("\n", Enumerable.Repeat("A fairly long line about bollards.", 120)),
            [], [], "test/model", ConversationId: 100, Depth: 1, IsLongThread: false, RetrievedSourceUrls: [], CitedSourceUrls: []);

        var rendering = RenderLinked(answer);

        rendering.MessageChunks.Should().HaveCountGreaterThan(1);
        rendering.MessageChunks.Should().OnlyContain(chunk => chunk.Length <= 2000);
        rendering.MessageChunks[^1].Should().Contain("via test/model", "attribution belongs on the final message");
    }

    [Fact]
    public void Render_SubstitutesPlaceholderText_WhenTheModelReturnsNothing()
    {
        // Discord rejects an empty message outright, which would surface as an exception rather than
        // a visible failure.
        var answer = new AiAnswer("   ", [], [], "test/model", ConversationId: 100, Depth: 1, IsLongThread: false, RetrievedSourceUrls: [], CitedSourceUrls: []);

        var rendering = RenderLinked(answer);

        rendering.MessageChunks[0].Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public Task Render_UploadsRelayedPicturesAndLinksTheRest()
    {
        // A relayed picture's link only works while Discord can reach the bot's public URL, which it
        // could not on 2026-10-22. On disk, the picture goes up with the answer and its embed points at
        // the attachment; a relayed picture whose file is gone is linked under today's address, not
        // the one it was indexed under; any other host's picture is linked as it was.
        var onDisk = new string('a', 64) + ".png";
        var gone = new string('b', 64) + ".jpg";
        var answer = new AiAnswer(
            "Philippine sugarcane grows on Negros [1]; the map shows the rest [2], and Luzon has some [3].",
            [
                new AiAnswerImage(1, $"https://old-host.tailnet.ts.net/api/v1/ai/images/{onDisk}", "https://www.plonkit.net/philippines#GaHP", "Philippines > Agriculture"),
                new AiAnswerImage(2, "https://i.imgur.com/sugarcane.png", "https://docs.google.com/document/d/abc", "Sugarcane map"),
                new AiAnswerImage(3, $"https://old-host.tailnet.ts.net/api/v1/ai/images/{gone}", "https://docs.google.com/presentation/d/def", null)
            ],
            [
                new AiAnswerSource(1, "Philippines > Agriculture", "https://www.plonkit.net/philippines#GaHP", IsImage: true),
                new AiAnswerSource(2, "Sugarcane map", "https://docs.google.com/document/d/abc", IsImage: true),
                new AiAnswerSource(3, "Luzon > Slide 4", "https://docs.google.com/presentation/d/def", IsImage: true)
            ],
            "dots-studio/dots-3-note-preview:free",
            ConversationId: 100,
            Depth: 1,
            IsLongThread: false,
            RetrievedSourceUrls: [],
            CitedSourceUrls: []);

        var plan = AiAnswerPicturePlanner.Plan(
            answer.Images,
            new Dictionary<string, StoredPicture> { [onDisk] = new("image/png", 120_000) },
            new AiImageRelayConfiguration { PublicBaseUrl = "https://bot.example.net" });

        return Verify(Render(AiAnswerFormatter.Render(answer, plan)));
    }

    [Fact]
    public Task Render_CountsTheSeveralPicturesOfOneCitedExcerpt()
    {
        // One slide caption sits beside each picture of its slide, so citing it once brings them all.
        // Discord shows them as one grid under the first embed's title, so that title counts them all;
        // only the uploaded files are told apart by their place.
        var first = new string('c', 64) + ".png";
        var second = new string('d', 64) + ".png";
        var answer = new AiAnswer(
            "Gen 4 with an antenna is common around Boa Vista [1], and in Rondônia [2].",
            [
                new AiAnswerImage(1, $"https://bot.example.net/api/v1/ai/images/{first}",
                    "https://docs.google.com/presentation/d/brazil/edit#slide=id.p7", "Brazilian Poles Lite", Position: 1, Count: 3),
                new AiAnswerImage(1, $"https://bot.example.net/api/v1/ai/images/{second}",
                    "https://docs.google.com/presentation/d/brazil/edit#slide=id.p7", "Brazilian Poles Lite", Position: 2, Count: 3),
                new AiAnswerImage(1, "https://i.imgur.com/roraima.png",
                    "https://docs.google.com/presentation/d/brazil/edit#slide=id.p7", "Brazilian Poles Lite", Position: 3, Count: 3),
                new AiAnswerImage(2, "https://i.imgur.com/rondonia.png", "https://www.plonkit.net/brazil#l5MR", "Brazil")
            ],
            [
                new AiAnswerSource(1, "Brazil · Brazilian Poles Lite > Slide 7", "https://docs.google.com/presentation/d/brazil/edit#slide=id.p7", IsImage: true),
                new AiAnswerSource(2, "Brazil > Car metas", "https://www.plonkit.net/brazil#l5MR", IsImage: true)
            ],
            "nvidia/nemotron-3-ultra-550b-a55b:free",
            ConversationId: 100,
            Depth: 1,
            IsLongThread: false,
            RetrievedSourceUrls: [],
            CitedSourceUrls: []);

        var plan = AiAnswerPicturePlanner.Plan(
            answer.Images,
            new Dictionary<string, StoredPicture>
            {
                [first] = new("image/png", 200_000),
                [second] = new("image/png", 300_000)
            },
            new AiImageRelayConfiguration { PublicBaseUrl = "https://bot.example.net" });

        return Verify(Render(AiAnswerFormatter.Render(answer, plan)));
    }

    [Fact]
    public void Render_KeepsThePictureCount_WhenALongTitleIsCut()
    {
        var answer = new AiAnswer(
            "See [1].",
            [
                new AiAnswerImage(1, "https://i.imgur.com/a.png", "https://guide/a", new string('x', 400), Position: 1, Count: 2),
                new AiAnswerImage(1, "https://i.imgur.com/b.png", "https://guide/a", new string('x', 400), Position: 2, Count: 2)
            ],
            [new AiAnswerSource(1, "A", "https://guide/a", IsImage: true)],
            "test/model", ConversationId: 100, Depth: 1, IsLongThread: false, RetrievedSourceUrls: [], CitedSourceUrls: []);

        var titles = RenderLinked(answer).Embeds.Select(embed => embed.Title).ToList();

        titles.Should().HaveCount(2).And.AllSatisfy(title =>
            title.Should().HaveLength(256).And.StartWith("[1] x").And.EndWith("… (2 pictures)"));
    }

    [Fact]
    public void Render_TitlesPicturesSharingALink_AsTheOneGridDiscordShowsThem()
    {
        // Discord shows embeds with the same link as one, under the first one's title: two pictures cited
        // as [1] and [3] from one Google Doc came out as "[1] …" over both, and [3] was nowhere. They are
        // posted together and titled as the group they become.
        var answer = new AiAnswer(
            "Straps [1], tents [2], map [3].",
            [
                new AiAnswerImage(1, "https://i.imgur.com/straps.png", "https://docs.google.com/document/d/mongolia", "Mongolia Gen 4 Car Meta"),
                new AiAnswerImage(2, "https://i.imgur.com/tents.png", "https://www.plonkit.net/mongolia#t1", "Mongolia"),
                new AiAnswerImage(3, "https://i.imgur.com/map.png", "https://docs.google.com/document/d/mongolia", "Mongolia Gen 4 Car Meta")
            ],
            [],
            "test/model", ConversationId: 100, Depth: 1, IsLongThread: false, RetrievedSourceUrls: [], CitedSourceUrls: []);

        var embeds = RenderLinked(answer).Embeds;

        embeds.Select(embed => embed.Image!.Value.Url).Should().Equal(
            "https://i.imgur.com/straps.png", "https://i.imgur.com/map.png", "https://i.imgur.com/tents.png");
        embeds.Select(embed => embed.Title).Should().Equal(
            "[1][3] Mongolia Gen 4 Car Meta (2 pictures)", "[1][3] Mongolia Gen 4 Car Meta (2 pictures)", "[2] Mongolia");
    }

    [Fact]
    public void Render_ShowsAtMostTenPictures_AndUploadsOnlyThoseShown()
    {
        // Discord takes at most ten embeds and ten attachments per message; an eleventh is refused
        // with the whole message.
        var images = Enumerable.Range(1, 12)
            .Select(marker => new AiAnswerImage(
                marker, $"https://bot.example.net/api/v1/ai/images/{marker:x64}.png", $"https://guide/{marker}", null))
            .ToList();
        var answer = new AiAnswer(
            "Many pictures.", images, [], "test/model", ConversationId: 100, Depth: 1, IsLongThread: false,
            RetrievedSourceUrls: [], CitedSourceUrls: []);
        var plan = new AiAnswerPicturePlan([.. images.Select(image => new AiAnswerPicture(
            image, AiAnswerPictureDelivery.Uploaded, image.ImageUrl,
            new AiAnswerUpload($"picture-{image.Marker}.png", $"{image.Marker:x64}.png", 10)))]);

        var rendering = AiAnswerFormatter.Render(answer, plan);

        rendering.Embeds.Should().HaveCount(10);
        rendering.Uploads.Should().HaveCount(10);
        rendering.Uploads.Select(upload => upload.FileName).Should().NotContain(["picture-11.png", "picture-12.png"]);
    }

    /// <summary>Every picture linked as stored, as when nothing relayed is on disk.</summary>
    private static AiAnswerRendering RenderLinked(AiAnswer answer) =>
        AiAnswerFormatter.Render(
            answer,
            AiAnswerPicturePlanner.Plan(answer.Images, new Dictionary<string, StoredPicture>(), new AiImageRelayConfiguration()));

    /// <summary>Flattens the rendering into plain text so one snapshot captures the whole layout.</summary>
    private static string Render(AiAnswerRendering rendering)
    {
        var builder = new StringBuilder();

        for (var index = 0; index < rendering.MessageChunks.Count; index++)
        {
            builder.AppendLine($"--- message {index + 1} ---");
            builder.AppendLine(rendering.MessageChunks[index]);
        }

        foreach (var embed in rendering.Embeds)
        {
            builder.AppendLine("--- embed ---");
            builder.AppendLine($"Title: {embed.Title}");
            builder.AppendLine($"Image: {embed.Image?.Url}");
            builder.AppendLine($"Url:   {embed.Url}");
        }

        // Listed only when there are any, so a layout without uploads reads exactly as it did.
        foreach (var upload in rendering.Uploads)
        {
            builder.AppendLine("--- upload ---");
            builder.AppendLine($"File:   {upload.FileName}");
            builder.AppendLine($"Stored: {upload.StoredName} ({upload.Length} bytes)");
        }

        return builder.ToString();
    }
}
