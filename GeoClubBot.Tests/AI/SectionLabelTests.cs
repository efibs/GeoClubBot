using FluentAssertions;
using UseCases.UseCases.AI.Conversations;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// Every path here is one a source list showed under an answer of the 2026-10-29 replay, as it is
/// stored in the index: the labels have to read well without the guides being indexed again.
/// </summary>
public sealed class SectionLabelTests
{
    [Theory]
    [InlineData("A Unique USA > Counties (33) ->", "A Unique USA > Counties (33)")]
    [InlineData("A Unique USA > You can view the flags in more detail here", "A Unique USA")]
    [InlineData("A Japan Guide > Image credit to PlonkIt", "A Japan Guide")]
    [InlineData("Useful License Plates: 2025 Update > @bagaboiebailey", "Useful License Plates: 2025 Update")]
    [InlineData("Kazakhstan Smudges > https://goo.gl/maps/X7gRPpP8pGweSTBP7", "Kazakhstan Smudges")]
    [InlineData("Complementary Guide of Spain > Near Albacete they look like this: https://goo.gl/maps/3XZPFyYWwSGj3EqYA", "Complementary Guide of Spain")]
    [InlineData("RegionGuessing Sri Lanka > Research by Yaron🐐", "RegionGuessing Sri Lanka")]
    [InlineData("Guide to Argentina Poletops > Author and all research done by: Kyleb_b", "Guide to Argentina Poletops")]
    [InlineData("Russian Car and Seasonal Metas > ________________", "Russian Car and Seasonal Metas")]
    [InlineData("More Japan Pole Metas (Doc) > (1)(2)", "More Japan Pole Metas (Doc)")]
    [InlineData("Traffic and Directional Stickers > you rn", "Traffic and Directional Stickers")]
    [InlineData("No Nonsense Guide To Japan > In the Kiso mountain region red roofs are very common", "No Nonsense Guide To Japan")]
    [InlineData("Simplified Northeast India Doc > Vegetation/landscape (credit to duskron)", "Simplified Northeast India Doc > Vegetation/landscape")]
    [InlineData("A Unique USA > Wide lines                                       Regular lines", "A Unique USA > Wide lines / Regular lines")]
    [InlineData("Brazilian Gen 4 Coverage Metas > ﻿Brazilian Gen 4 Coverage Metas", "Brazilian Gen 4 Coverage Metas")]
    public void Clean_DropsWhatIsNotAHeading_AndTidiesTheRest(string path, string expected) =>
        SectionLabel.Clean(path).Should().Be(expected);

    [Theory]
    [InlineData("Brazil > Regional and state-specific clues")]
    [InlineData("Mexico Deconstructed > Boojum tree")]
    [InlineData("Norwegian snowpoles > High mid")]
    [InlineData("Indian Scripts and Laguages > Geographic distribution of Assamese speakers")]
    [InlineData("Ecuador Gen 4 Guide > 2024 June")]
    [InlineData("The Ultimate Guide to New Zealand > Slide 25")]
    [InlineData("Philippines > Infrastructure > Poles and Signs")]
    // Real headings the first version of the rule dropped from the replay's prompts: an aside in brackets
    // is no part of a heading's length, and a casual heading may start in lower case.
    [InlineData("Slovakia Guide > Bollards : (they are also present in the Czech Republic)")]
    [InlineData("Simplified Northeast India Doc > Common roof in NE India (similar to the roofs in Bangladesh)")]
    [InlineData("Lithuania > blue gen 4 with antenna")]
    [InlineData("More Japan Pole Metas (Doc) > k;TL;DR Guide")]
    public void Clean_LeavesARealSectionPathAsItIs(string path) =>
        SectionLabel.Clean(path).Should().Be(path);

    [Fact]
    public void Clean_KeepsTheGuidesTitle_WhateverItLooksLike()
    {
        // The first segment names the guide, not a section of it.
        SectionLabel.Clean("california: the greatest country > Introduction")
            .Should().Be("california: the greatest country > Introduction");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Clean_IsEmpty_ForNoPath(string? path) =>
        SectionLabel.Clean(path).Should().BeEmpty();

    [Theory]
    [InlineData("Counties (33) ->", false, true)]
    [InlineData("Image credit to PlonkIt", true, false)]
    [InlineData("Tunisian Driving Directions", false, false)]
    [InlineData("Copyright", false, false)]
    [InlineData("Sabah", false, false)]
    public void TellsAHeadingFromALineShapedLikeOne(string line, bool notAHeading, bool endsWithArrow)
    {
        SectionLabel.IsNotAHeading(line).Should().Be(notAHeading);
        SectionLabel.EndsWithArrow(line).Should().Be(endsWithArrow);
    }

    [Theory]
    // Plausible headings that only share a word with a credit or a pointer.
    [InlineData("Credit unions and banks")]
    [InlineData("Roads made by the army")]
    [InlineData("Water tap covers")]
    [InlineData("Tap signs")]
    // Casual, but not a remark: a capital, a digit, or more than three words.
    [InlineData("km markers and road signs")]
    [InlineData("eSIM and mobile data")]
    [InlineData("blue gen 4 with antenna")]
    // A script without case is never "all lower case".
    [InlineData("জ ঝ")]
    // A heading wholly in brackets is judged by what they hold.
    [InlineData("(gen2)")]
    public void TakesAHeadingForOne_ThoughItLooksLikeACreditAPointerOrARemark(string line) =>
        SectionLabel.IsNotAHeading(line).Should().BeFalse();

    [Theory]
    [InlineData("Credits: Kyleb_b")]
    [InlineData("Photo credits PlonkIt")]
    [InlineData("Courtesy of the Oman Discord")]
    [InlineData("Made by Foo")]
    [InlineData("Notes: research by Yaron")]
    [InlineData("Click here")]
    [InlineData("tap here for the map")]
    [InlineData("see below")]
    [InlineData("you rn")]
    public void TakesACreditAPointerOrARemarkForNoHeading(string line) =>
        SectionLabel.IsNotAHeading(line).Should().BeTrue();

    [Theory]
    [InlineData("How to tell Spain and Portugal apart by road lines", false)]
    [InlineData("you rn", false)]
    [InlineData("Image credit to PlonkIt", true)]
    [InlineData("https://goo.gl/maps/X7gRPpP8pGweSTBP7", true)]
    [InlineData("________________", true)]
    public void JudgesAStyledHeading_OnlyByWhetherItIsACreditAURLOrNoWords(string line, bool neverAHeading)
    {
        // A heading element is its author's choice, so neither its length nor its case argues with it.
        SectionLabel.IsNeverAHeading(line).Should().Be(neverAHeading);
    }
}
