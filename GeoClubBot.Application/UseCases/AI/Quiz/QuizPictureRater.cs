using System.Security.Cryptography;
using System.Text;
using Configuration;
using Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.AI;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.AI.Conversations;
using UseCases.UseCases.AI.Retrieval;
using UseCases.UseCases.AI.Tools;
using Utilities;

namespace UseCases.UseCases.AI.Quiz;

public enum QuizRatingOutcomeKind
{
    /// <summary>The picture was rated; <see cref="QuizRatingOutcome.Item"/> holds what it makes, perhaps no question at all.</summary>
    Rated,

    /// <summary>The reply was no rating (a refusal, token soup, another form); left for a later run.</summary>
    Unusable,

    /// <summary>The request failed; the model is demoted and the picture left for a later run.</summary>
    Failed,

    /// <summary>The provider is rate-limiting; nothing more should be asked now.</summary>
    RateLimited
}

public readonly record struct QuizRatingOutcome(QuizRatingOutcomeKind Kind, AiQuizItem? Item = null);

/// <summary>
/// Has a free vision model rate one picture for the quiz (#491), and stores what it makes. Shared by the
/// nightly run and by a quiz that found no rated picture to ask; each claims the request from its own
/// share of the day before asking.
/// </summary>
public sealed partial class QuizPictureRater(
    IChatModelClient chatClient,
    IChatModelCatalog modelCatalog,
    IImageRelay imageRelay,
    IAiBudgetRepository budget,
    IAiQuizItemRepository items,
    IOptions<AiConfiguration> aiConfiguration,
    IOptions<AiQuizConfiguration> quizConfiguration,
    TimeProvider timeProvider,
    ILogger<QuizPictureRater> logger)
{
    /// <summary>Whether a picture can be rated at all: a clue with a picture and words, filed under a country the table knows.</summary>
    public static bool IsRateable(KnowledgeHit hit) =>
        ClueSampler.IsAClue(hit) && CountryCodeOf(hit) is not null;

    /// <summary>The ISO code of the country the picture is filed under; null when it is none the table knows.</summary>
    public static string? CountryCodeOf(KnowledgeHit hit) => IsoCountries.Resolve(hit.Country, IsoCountries.Gazetteer);

    /// <summary>
    /// What the rating is made of: the picture and its words. A guide re-indexed with other words under the
    /// same chunk id rates again.
    /// </summary>
    public static string ContentHashOf(KnowledgeHit hit)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{hit.ImageUrl}\n{hit.Text}"));
        return Convert.ToHexString(bytes, 0, 16);
    }

    public int Version => Math.Max(1, quizConfiguration.Value.Rating.Version);

    /// <summary>The models that read pictures, best first; empty when there is none today.</summary>
    public Task<IReadOnlyList<string>> ReadChainAsync(CancellationToken cancellationToken) =>
        modelCatalog.ReadChainAsync(
            new ChatModelRequirements(NeedsImageInput: true, MinContextLength: aiConfiguration.Value.OpenRouter.MinContextLength),
            cancellationToken);

    /// <summary>Claims requests against a share of the day's allowance; 100 is the whole of it, as an answer claims.</summary>
    public Task<bool> TryReserveAsync(int percent, CancellationToken cancellationToken)
    {
        var ceiling = Math.Max(1, aiConfiguration.Value.OpenRouter.DailyRequestBudget * Math.Clamp(percent, 1, 100) / 100);
        return budget.TryReserveRequestsAsync(Today(), 1, ceiling, cancellationToken);
    }

    /// <summary>
    /// Rates one picture with the request already claimed, and adds the rating to the repository (saved
    /// with the command). A rating made of an older generation or other words is replaced.
    /// </summary>
    public async Task<QuizRatingOutcome> RateAsync(KnowledgeHit picture, IReadOnlyList<string> chain, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(picture);
        ArgumentNullException.ThrowIfNull(chain);

        var code = CountryCodeOf(picture);
        if (code is null || picture.ImageUrl is null || chain.Count == 0)
        {
            return new QuizRatingOutcome(QuizRatingOutcomeKind.Unusable);
        }

        // Inline when the relay holds a copy, so rating never depends on the provider reaching this host.
        var inline = await imageRelay.ReadAsDataUrlAsync(picture.ImageUrl, cancellationToken).ConfigureAwait(false);
        var image = string.IsNullOrEmpty(inline) ? picture.ImageUrl : inline;
        var messages = QuizRatingPrompt.BuildMessages(picture, IsoCountries.NameOf(code), image);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, quizConfiguration.Value.Rating.TimeoutSeconds)));

        Result<AiChatResponse> completion;
        try
        {
            completion = await chatClient.CompleteAsync(
                    new AiChatRequest(chain, messages, QuizRatingPrompt.Temperature, Math.Max(100, quizConfiguration.Value.Rating.MaxReplyTokens),
                        Reasoning: ReasoningOf(quizConfiguration.Value.Rating.Reasoning)),
                    timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogTimedOut(logger, picture.ImageUrl, chain[0]);
            modelCatalog.ReportFailedRequest(chain, Error.Unexpected(ChatErrorCodes.TimedOut, "The model did not rate the picture in time."));
            return new QuizRatingOutcome(QuizRatingOutcomeKind.Failed);
        }

        if (completion.IsFailure)
        {
            LogRequestFailed(logger, picture.ImageUrl, completion.Error.Code, completion.Error.Message);
            modelCatalog.ReportFailedRequest(chain, completion.Error);
            return new QuizRatingOutcome(completion.Error.Code == ChatErrorCodes.RateLimited
                ? QuizRatingOutcomeKind.RateLimited
                : QuizRatingOutcomeKind.Failed);
        }

        var response = completion.Value;
        await budget.RecordTokenUsageAsync(Today(), response.Usage.PromptTokens, response.Usage.CompletionTokens, cancellationToken)
            .ConfigureAwait(false);
        modelCatalog.ReportChainOutcome(chain, response.ModelUsed);

        if (modelCatalog.IsUnfitToAnswer(response.ModelUsed))
        {
            LogUnusable(logger, picture.ImageUrl, response.ModelUsed, "the model is one that must not answer");
            return new QuizRatingOutcome(QuizRatingOutcomeKind.Unusable);
        }

        var rating = QuizRatingPrompt.Parse(response.Text, code);
        if (!rating.Understood)
        {
            LogUnusable(logger, picture.ImageUrl, response.ModelUsed, Shorten(response.Text));
            return new QuizRatingOutcome(QuizRatingOutcomeKind.Unusable);
        }

        var item = AiQuizItem.Create(
            picture.Id,
            Version,
            ContentHashOf(picture),
            picture.Country,
            picture.SourceType,
            response.ModelUsed,
            timeProvider.GetUtcNow(),
            rating.Questions.Select(question =>
                AiQuizItemQuestion.Create(question.Kind, question.Answer, question.Decoys, question.Difficulty)));

        await items.ReplaceAsync(item, cancellationToken).ConfigureAwait(false);
        LogRated(logger, picture.ImageUrl, response.ModelUsed,
            rating.Questions.Count == 0 ? "none" : string.Join(", ", rating.Questions.Select(question => $"{question.Kind} {question.Difficulty}")));

        return new QuizRatingOutcome(QuizRatingOutcomeKind.Rated, item);
    }

    /// <summary>The setting as the chat request takes it; "default" (or anything unknown) leaves the model's own.</summary>
    public static AiReasoning? ReasoningOf(string? setting) =>
        Enum.TryParse<AiReasoning>(setting, ignoreCase: true, out var reasoning) ? reasoning : null;

    private DateOnly Today() => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    private static string Shorten(string? text)
    {
        var flat = string.Join(' ', (text ?? string.Empty).Split(default(char[]), StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= 160 ? flat : $"{flat[..160]}…";
    }

    [LoggerMessage(LogLevel.Information, "Quiz: rated {ImageUrl} with {Model}; questions: {Questions}.")]
    static partial void LogRated(ILogger logger, string imageUrl, string model, string questions);

    [LoggerMessage(LogLevel.Warning, "Quiz: no usable rating of {ImageUrl} from {Model}; left for a later run ({Reply}).")]
    static partial void LogUnusable(ILogger logger, string imageUrl, string model, string reply);

    [LoggerMessage(LogLevel.Warning, "Quiz: could not rate {ImageUrl} ({Code}): {Reason}")]
    static partial void LogRequestFailed(ILogger logger, string imageUrl, string code, string reason);

    [LoggerMessage(LogLevel.Warning, "Quiz: {Model} did not rate {ImageUrl} in time.")]
    static partial void LogTimedOut(ILogger logger, string imageUrl, string model);
}
