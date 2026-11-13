using System.Globalization;
using Configuration;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.Abstractions;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Conversations;
using UseCases.UseCases.AI.Retrieval;
using Utilities;

namespace UseCases.UseCases.AI.Quiz;

/// <summary>A quiz question: a random captioned guide picture, and the answer to hide.</summary>
/// <param name="Country">A country to draw from, as the member wrote it; null draws from every country.</param>
/// <param name="LesserKnownOnly">Leave out PlonkIt's pictures and the clues PlonkIt has a tip like.</param>
public sealed record DrawQuizQuestionQuery(string? Country = null, bool LesserKnownOnly = false) : IQuery<Result<QuizQuestion>>;

/// <param name="Clue">The picture chunk drawn.</param>
/// <param name="CountryName">The country the guide files it under, for a reader; null when the guide names none.</param>
/// <param name="Guide">The guide's title.</param>
/// <param name="Section">The section the picture stands in, cleaned; empty when it has none worth showing.</param>
/// <param name="Answer">What the guide says of the picture, in a line.</param>
public sealed record QuizQuestion(KnowledgeHit Clue, string? CountryName, string Guide, string Section, string Answer);

public static class QuizErrorCodes
{
    public const string Disabled = "ai.quiz_disabled";
    public const string UnknownCountry = "ai.quiz_unknown_country";
    public const string NothingFound = "ai.quiz_nothing_found";
}

/// <summary>
/// Draws one picture clue at random, the way the clue of the day and "a clue I don't know yet" do
/// (<see cref="ClueSampler.IsAClue"/>: a picture with words of its own), within one country when asked.
/// The country is read as the guides spell it (<see cref="CountryGazetteer"/>), and each spelling the
/// index files it under is tried. Costs no AI request.
/// </summary>
public sealed partial class DrawQuizQuestionHandler(
    IKnowledgeIndex knowledgeIndex,
    MainGuideOverlap mainGuide,
    KnowledgeCountryDirectory countryDirectory,
    IOptions<AiQuizConfiguration> configuration,
    IOptions<AiConfiguration> aiConfiguration,
    ILogger<DrawQuizQuestionHandler> logger)
    : IRequestHandler<DrawQuizQuestionQuery, Result<QuizQuestion>>
{
    public async Task<Result<QuizQuestion>> Handle(DrawQuizQuestionQuery request, CancellationToken cancellationToken)
    {
        if (!aiConfiguration.Value.Active || !configuration.Value.Enabled)
        {
            return Error.Validation(QuizErrorCodes.Disabled, "The quiz is turned off.");
        }

        IReadOnlyList<string?> countries = [null];
        if (!string.IsNullOrWhiteSpace(request.Country))
        {
            var gazetteer = await countryDirectory.ReadAsync(cancellationToken).ConfigureAwait(false);
            var mentions = gazetteer.Resolve(request.Country);
            if (mentions.Count != 1)
            {
                return Error.Validation(QuizErrorCodes.UnknownCountry,
                    mentions.Count == 0
                        ? $"No indexed guide covers '{request.Country.Trim()}'."
                        : $"'{request.Country.Trim()}' is several countries; name one.");
            }

            countries = [.. mentions[0].IndexNames];
        }

        var draws = Math.Max(1, configuration.Value.Draws);
        var excluded = request.LesserKnownOnly ? MainGuideOverlap.MainGuide : null;

        foreach (var country in countries)
        {
            var drawn = await knowledgeIndex
                .SampleAsync(new KnowledgeSample(draws, KnowledgeChunkKind.Image, excluded, country), cancellationToken)
                .ConfigureAwait(false);

            var clues = drawn.Where(ClueSampler.IsAClue).ToList();
            if (request.LesserKnownOnly)
            {
                var marked = await mainGuide.MarkAsync(clues, cancellationToken).ConfigureAwait(false);
                clues = [.. marked.Where(hit => !hit.AlsoInMainGuide)];
            }

            if (clues.Count > 0)
            {
                var clue = clues[0];
                LogDrawn(logger, clue.SourceUrl, country ?? "any country", drawn.Count);
                return ToQuestion(clue);
            }
        }

        LogNothing(logger, request.Country ?? "any country", request.LesserKnownOnly);
        return Error.NotFound(QuizErrorCodes.NothingFound,
            request.Country is null
                ? "No captioned picture could be drawn from the index just now; try again."
                : $"No captioned picture of {request.Country.Trim()} could be drawn just now; try again, or another country.");
    }

    /// <summary>The answer is what the guide says of the picture; the heading stands in for a picture that only has one.</summary>
    internal static QuizQuestion ToQuestion(KnowledgeHit clue)
    {
        var guide = string.IsNullOrWhiteSpace(clue.Title) ? "a community guide" : clue.Title.Trim();
        var section = SectionLabel.Clean(clue.SectionPath);
        if (section.StartsWith(guide, StringComparison.OrdinalIgnoreCase))
        {
            section = section[guide.Length..].TrimStart(' ', '>', '—').Trim();
        }

        var answer = PictureCaption.Of(clue.Text, clue.SectionPath, clue.Title) ?? section;
        var country = string.IsNullOrWhiteSpace(clue.Country) ? null : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(clue.Country.Trim());

        return new QuizQuestion(clue, country, guide, section, answer);
    }

    [LoggerMessage(LogLevel.Information, "Quiz: drew {SourceUrl} from {Country} ({Drawn} picture(s) sampled).")]
    static partial void LogDrawn(ILogger logger, string sourceUrl, string country, int drawn);

    [LoggerMessage(LogLevel.Information, "Quiz: nothing to ask from {Country} (lesser-known only: {LesserKnown}).")]
    static partial void LogNothing(ILogger logger, string country, bool lesserKnown);
}
