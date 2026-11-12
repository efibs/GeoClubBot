using FluentAssertions;
using UseCases.UseCases.AI.Conversations;
using Xunit;

namespace GeoClubBot.Tests.AI;

/// <summary>
/// The first positive is, to the character, what nemotron-3-ultra returned as an answer on 2026-11-11.
/// The negatives matter as much: a detector that fired on a real answer would throw it away and spend
/// a request replacing it, and real answers quote every alphabet GeoGuessr's signs are written in.
/// </summary>
public sealed class GarbledTextDetectorTests
{
    private const string Recorded =
        ".reurs(.तuotfrac────ζ Beliaufu  fsh──)t.frac──\n\n\"*.\n\n"
        + "usernunderline\\\\\\ Konstant ├──σ—¿σfilmstokens**gelöstshe1 {(θnikenk.\n\n"
        + "(\\\"*.\n\n ├── tomon0première WedługNuméroθ";

    [Fact]
    public void IsGarbled_RecognisesTheReplyThatWasRecorded() =>
        GarbledTextDetector.IsGarbled(Recorded).Should().BeTrue();

    [Theory]
    [InlineData("Wedłuθ Numéрo")]
    [InlineData("theत roaд of σigns")]
    [InlineData("אbc اxyz and nothing else here")]
    public void IsGarbled_RecognisesWordsThatChangeAlphabetHalfWay(string reply) =>
        GarbledTextDetector.IsGarbled(reply).Should().BeTrue();

    [Theory]
    // Plain answers, with the markup models write.
    [InlineData("**West Virginia, Maryland, Delaware, Virginia** – “middle yellow lines that are thicker” [1][2].")]
    [InlineData("- **Vertical water tanks** – seen across most of the country [1].\n- *Trichocereus atacamensis* (cardón) [2].")]
    [InlineData("See <https://www.plonkit.net/india#lQsr> and `read_guide`; 50% of US-101 is I‑95-like (e.g., A/B).")]
    // Latin alphabets with their own letters.
    [InlineData("İskenderun, Şanlıurfa, Elâzığ and Ağrı; Azərbaycan; Việt Nam: Thừa Thiên Huế; Łódź; Þingvellir; Hawaiʻi and Oʻahu.")]
    [InlineData("Café written with a combining accent, and naïve too.")]
    // Other alphabets quoted as words of their own.
    [InlineData("Novosibirsk (Новосибирск) and Chelyabinsk/Челябинск; the sign reads «Добро пожаловать».")]
    [InlineData("Greek signs read Αθήνα and Θεσσαλονίκη; Bulgarian ones София; Georgian ones თბილისი; Armenian ones Երևան.")]
    [InlineData("Hindi मेघालय, Bengali মেঘালয়, Tamil தமிழ்நாடு, Sinhala ශ්‍රී ලංකා, Arabic الأردن, Hebrew ישראל, Amharic ኢትዮጵያ.")]
    [InlineData("Iqaluit is ᐃᖃᓗᐃᑦ in syllabics, and Morocco writes ⵜⴰⵎⴰⵣⵉⵖⵜ beside Arabic.")]
    // Scripts written without spaces run into Latin letters as a matter of course.
    [InlineData("JR東日本の東京タワーはTokyoにあります。国道1号。")]
    [InlineData("ซอยSukhumvit 11 และถนนPhetchaburi; ຖະໜົນLaneXang; ផ្លូវNational 6; 서울역KTX와 부산역SRT.")]
    // One slip is a slip.
    [InlineData("Resistors of 10 kΩ are not a GeoGuessr clue, but bollards are.")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("👍")]
    [InlineData("[1][2] 2024–2026: 12,345 km.")]
    public void IsGarbled_LeavesRealAnswersAlone(string reply) =>
        GarbledTextDetector.IsGarbled(reply).Should().BeFalse();

    [Fact]
    public void IsGarbled_LeavesNullAlone() =>
        GarbledTextDetector.IsGarbled(null).Should().BeFalse();

    [Fact]
    public void IsGarbled_TakesTwoSlipsInALongAnswerForSlips()
    {
        // Two look-alike letters in a long answer are typos, not token soup: the recorded reply had a
        // mixed word in every four.
        var reply = "The bollard in Нoвосибирск and the one in Чeлябинск differ. "
                    + string.Concat(Enumerable.Repeat("Bollards here are white with a red reflector. ", 12));

        GarbledTextDetector.IsGarbled(reply).Should().BeFalse();
    }

    [Fact]
    public void IsGarbled_StillRecognisesSoup_ThatOpensWithAFewRealWords()
    {
        var reply = "Sure, here is the answer: " + Recorded;

        GarbledTextDetector.IsGarbled(reply).Should().BeTrue();
    }
}
