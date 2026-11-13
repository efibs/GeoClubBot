using FluentAssertions;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.ClueOfTheDay;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>The clue as a post: shaped like an answer so the same code renders and uploads it, the guide cited beneath.</summary>
public sealed class ClueOfTheDayPostTests
{
    [Fact]
    public void Build_HeadsWithTheGuide_QuotesItsWords_AndCitesThePictureAsOne()
    {
        var clue = Clue("In Lạng Sơn the poles carry a white band at eye height, seen nowhere else in the north.",
            imageUrl: "https://bot.example/api/v1/ai/images/abc.jpg");

        var post = ClueOfTheDayPost.Build(clue, "🧭 **Clue of the day** — from *{{guide}}*", 900);

        post.Text.Should().Be(
            "🧭 **Clue of the day** — from *Vietnam region guide*\n\n"
            + "In Lạng Sơn the poles carry a white band at eye height, seen nowhere else in the north. [1]");
        post.Images.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Marker = 1,
            ImageUrl = "https://bot.example/api/v1/ai/images/abc.jpg",
            SourceUrl = "https://docs.google.com/document/d/x#heading=h.1",
            Title = "Vietnam region guide > Poles",
            Caption = "In Lạng Sơn the poles carry a white band at eye height, seen nowhere else in the north."
        });
        post.Sources.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Marker = (int?)1,
            Label = "Vietnam region guide > Poles",
            Url = "https://docs.google.com/document/d/x#heading=h.1",
            IsImage = true
        });
        post.ModelUsed.Should().BeEmpty("no model wrote this, so no 'via' line");
    }

    [Fact]
    public void Build_PostsAClueWithoutItsPicture_AsTextCitingTheGuide()
    {
        var post = ClueOfTheDayPost.Build(Clue("Words only, long enough to be a clue of their own here.", imageUrl: null), "Clue", 900);

        post.Images.Should().BeEmpty();
        post.Sources.Should().ContainSingle().Which.IsImage.Should().BeFalse();
    }

    [Fact]
    public void Build_CutsALongClue_AtASentence()
    {
        var text = string.Join(' ', Enumerable.Repeat("A sentence about a pole.", 80));

        var post = ClueOfTheDayPost.Build(Clue(text, "https://img/x.jpg"), "Clue", 200);

        post.Text.Length.Should().BeLessThan(240);
        post.Text.Should().EndWith("pole. [1]");
    }

    [Fact]
    public void Build_NamesAnUntitledGuide_Plainly()
    {
        var clue = Clue("A clue long enough to count as one, about a bollard.", "https://img/x.jpg") with { Title = null, SectionPath = null };

        var post = ClueOfTheDayPost.Build(clue, "From *{{guide}}*", 900);

        post.Text.Should().StartWith("From *a community guide*");
        post.Images[0].Title.Should().Be("a community guide");
    }

    private static KnowledgeHit Clue(string text, string? imageUrl) =>
        new(Guid.NewGuid(), 0f, imageUrl is null ? KnowledgeChunkKind.Text : KnowledgeChunkKind.Image, text,
            "https://docs.google.com/document/d/x#heading=h.1", imageUrl, "Vietnam region guide", "vietnam",
            "Vietnam region guide > Poles", "Author", 0, "gdoc", "x", "p3-img");
}
