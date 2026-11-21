using Entities;
using FluentAssertions;
using GeoClubBot.Tests.TestBuilders;
using Xunit;

namespace GeoClubBot.Tests.Domain;

/// <summary>A quiz round's rules (#491): one guess each, scored by difficulty, none once the answer is out.</summary>
public sealed class AiQuizRoundTests
{
    [Theory]
    [InlineData(AiQuizDifficulty.Easy, 1)]
    [InlineData(AiQuizDifficulty.Medium, 2)]
    [InlineData(AiQuizDifficulty.Hard, 3)]
    public void ARightGuess_ScoresTheDifficulty(AiQuizDifficulty difficulty, int points)
    {
        var round = QuizRounds.Round(difficulty: difficulty);

        var guess = round.Guess(7, round.CorrectIndex, QuizRounds.AskedAt);

        guess!.Correct.Should().BeTrue();
        guess.Points.Should().Be(points);
    }

    [Fact]
    public void AWrongGuess_ScoresNothing()
    {
        var round = QuizRounds.Round(difficulty: AiQuizDifficulty.Hard);

        var guess = round.Guess(7, 0, QuizRounds.AskedAt);

        guess!.Correct.Should().BeFalse();
        guess.Points.Should().Be(0);
    }

    [Fact]
    public void EachMemberGuessesOnce()
    {
        var round = QuizRounds.Round();
        round.Guess(7, 0, QuizRounds.AskedAt);

        round.Guess(7, round.CorrectIndex, QuizRounds.AskedAt).Should().BeNull();
        round.Guesses.Should().ContainSingle();
    }

    [Fact]
    public void NobodyGuesses_OnceTheAnswerIsOut()
    {
        var round = QuizRounds.Round();
        round.Reveal(QuizRounds.AskedAt).Should().BeTrue();

        round.Guess(7, round.CorrectIndex, QuizRounds.AskedAt).Should().BeNull();
        round.Reveal(QuizRounds.AskedAt).Should().BeFalse("it is revealed once");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void AnOptionThatIsNoneOfTheFour_IsNoGuess(int option) =>
        QuizRounds.Round().Guess(7, option, QuizRounds.AskedAt).Should().BeNull();

    [Fact]
    public void TheAnswerIsDue_AfterTheRevealDelay() =>
        QuizRounds.Round().RevealAtUtc.Should().Be(QuizRounds.AskedAt.AddMinutes(10));
}
