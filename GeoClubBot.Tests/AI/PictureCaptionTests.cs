using FluentAssertions;
using UseCases.UseCases.AI.Conversations;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// The line under a picture's title says what the picture is, in the guide's own words, cut to a
/// glance. Without it five pictures of five clues read alike (2026-11-09).
/// </summary>
public sealed class PictureCaptionTests
{
    [Fact]
    public void IsTheExcerptsText_OnOneLine_WithoutItsEmphasisMarks()
    {
        PictureCaption.Of(
                "Texas little bumps *** (Photo : Hearne, Texas):\nThese little **rectangular** bumps are unique to Texas.",
                "28 Secret US Metas > Useful Metas", "28 Secret US Metas")
            .Should().Be("Texas little bumps (Photo : Hearne, Texas): These little rectangular bumps are unique to Texas.");
    }

    [Fact]
    public void KeepsAnUnderscoreInsideAName_AndDropsABulletsDash()
    {
        PictureCaption.Of("-Seen on the PR_45 near San_Juan.", null, null)
            .Should().Be("Seen on the PR_45 near San_Juan.");
    }

    [Fact]
    public void IsCutAtTheEndOfASentence_WhenTheTextRunsLong()
    {
        var text = "Khasi pines are told apart by their disorganised crowns. "
                   + string.Join(' ', Enumerable.Repeat("They are common in Meghalaya and around Shillong.", 6));

        var caption = PictureCaption.Of(text, null, null)!;

        caption.Length.Should().BeLessThanOrEqualTo(PictureCaption.MaxLength);
        caption.Should().StartWith("Khasi pines are told apart").And.EndWith("Shillong.");
    }

    [Fact]
    public void IsCutAtAWord_WhenNoSentenceEndsInReach()
    {
        var text = string.Join(' ', Enumerable.Repeat("bollards", 60));

        var caption = PictureCaption.Of(text, null, null)!;

        caption.Length.Should().BeLessThanOrEqualTo(PictureCaption.MaxLength);
        caption.Should().EndWith("bollards…");
    }

    [Theory]
    [InlineData("A Unique USA > 7.20 Maryland (MD) > 7.20.1 Signs > State Route Shield", "A Unique USA > 7.20 Maryland (MD) > 7.20.1 Signs > State Route Shield", "A Unique USA")]
    [InlineData("Sarawak pole numbers", "Malaysia", "Sarawak pole numbers")]
    [InlineData("   ", "Section", "Title")]
    [InlineData(null, "Section", "Title")]
    public void IsNothing_WhenTheTextSaysOnlyWhatTheTitleDoes(string? text, string? section, string? title)
    {
        PictureCaption.Of(text, section, title).Should().BeNull();
    }
}
