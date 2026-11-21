using System.Globalization;
using System.Text;
using Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UseCases.OutputPorts.AI;
using UseCases.OutputPorts.Repositories;
using UseCases.UseCases.AI.Quiz;
using UseCases.UseCases.AI.Retrieval;
using UseCases.UseCases.AI.Tools;
using Utilities;

namespace GeoClubBot.RetrievalProbe;

/// <summary>
/// <c>/ai quiz</c>'s rating run for real (#491): pictures drawn from the production index at random, as
/// the nightly run draws them, each rated by the bot's own <see cref="QuizPictureRater"/> and prompt. The
/// report shows, per picture, what the guide says, the questions the model judged fair with their wrong
/// answers, and its raw reply, so the prompt can be judged before members see what it makes. Each picture
/// is saved beside the probe to look at. Stores nothing: the ratings stay in the report.
/// One chat request a picture.
/// </summary>
public static class QuizCommand
{
    public static async Task RunAsync(ProbeContext context)
    {
        var arguments = context.Arguments;
        if (!arguments.AllowChat)
        {
            throw new ProbeException("quiz has a vision model rate each picture, one chat request a picture, so it has to be allowed by name: pass --allow-chat.");
        }

        if (context.Settings.ApiKey is not { } apiKey)
        {
            throw new ProbeException(ProbeSettings.MissingKeyHelp);
        }

        var root = ProbeSettings.RepositoryRoot() ?? throw new ProbeException("GeoClubBot.sln was not found above the probe.");
        var settings = BotSettings.Load(BotSettings.DefaultPath(root), arguments.Overrides ?? [], apiKey);
        settings.Ai.Active = true;

        var count = Math.Clamp(arguments.Max, 1, 100);
        var ct = context.CancellationToken;

        using var stack = AnsweringStack.Create(context, settings);
        var directory = new KnowledgeCountryDirectory(context.Index, Options.Create(settings.Retrieval), TimeProvider.System,
            NullLogger<KnowledgeCountryDirectory>.Instance);

        // The country as a member would write it, filed under every spelling the index uses.
        IReadOnlyList<string>? spellings = null;
        if (arguments.Country is { Length: > 0 } wanted)
        {
            var mentions = (await directory.ReadAsync(ct).ConfigureAwait(false)).Resolve(wanted);
            if (mentions.Count != 1)
            {
                throw new ProbeException(mentions.Count == 0 ? $"No indexed guide covers '{wanted}'." : $"'{wanted}' is several countries; name one.");
            }

            spellings = mentions[0].IndexNames;
        }

        var refreshed = await stack.Catalog.RefreshAsync(ct).ConfigureAwait(false);
        if (refreshed.IsFailure)
        {
            throw new ProbeException($"The free model roster could not be read: {refreshed.Error.Message}");
        }

        using var images = new PictureFolder(Path.Combine(AppContext.BaseDirectory, "quiz-pictures"), settings.ImageRelay.PublicBaseUrl);
        var chat = new LastReplyChatModelClient(stack.Judge);
        var items = new InMemoryQuizItems();
        var budget = new InMemoryBudgetRepository();
        var rater = new QuizPictureRater(chat, stack.Catalog, images, budget, items, Options.Create(settings.Ai), Options.Create(settings.Quiz),
            TimeProvider.System, NullLogger<QuizPictureRater>.Instance);

        // As the nightly run draws: a random country at a time, so one large guide does not fill the report.
        var pictures = await new QuizPictureDraw(context.Index, items, directory, rater)
            .DrawUnratedAsync(count, spellings, excludedSourceType: null, count * 3, ct)
            .ConfigureAwait(false);
        if (pictures.Count == 0)
        {
            throw new ProbeException("No picture the quiz could rate was drawn; draw again, or another country.");
        }

        var report = context.Report;
        report.Line($"# Quiz ratings of {pictures.Count} picture(s){(arguments.Country is { } c ? $" of {c}" : string.Empty)}");
        report.Line();

        var outcomes = new Dictionary<QuizRatingOutcomeKind, int>();
        var asked = new Dictionary<(AiQuizQuestionKind, AiQuizDifficulty), int>();
        var started = DateTimeOffset.UtcNow;

        for (var index = 0; index < pictures.Count; index++)
        {
            var picture = pictures[index];
            Console.Error.WriteLine($"Rating {index + 1}/{pictures.Count}: {picture.SourceUrl}");

            var chain = await rater.ReadChainAsync(ct).ConfigureAwait(false);
            if (chain.Count == 0)
            {
                report.Line("No free model that reads pictures is available; stopping.");
                break;
            }

            chat.Clear();
            var outcome = await rater.RateAsync(picture, chain, ct).ConfigureAwait(false);
            outcomes[outcome.Kind] = outcomes.GetValueOrDefault(outcome.Kind) + 1;

            var country = QuizPictureRater.CountryCodeOf(picture) is { } code ? IsoCountries.NameOf(code) : picture.Country;
            report.Line($"## {index + 1}. {country} · {picture.Title} › {picture.SectionPath}");
            report.Line();
            report.Line($"- Picture: {images.PathOf(picture.ImageUrl!) ?? "(could not be downloaded; sent by link)"} — {picture.ImageUrl}");
            report.Line($"- Source: {picture.SourceUrl}");
            report.Line($"- The guide says: {Flatten(picture.Text, 400)}");
            if (picture.HasGeneratedCaption)
            {
                report.Line($"- Described by a model as: {Flatten(picture.GeneratedCaption!, 300)}");
            }

            report.Line();
            if (outcome.Item is { } item)
            {
                if (item.Questions.Count == 0)
                {
                    report.Line("**No question**: the model judged every kind unfair.");
                }
                else
                {
                    report.Line("| Question | Difficulty | Right answer | Wrong answers |");
                    report.Line("|---|---|---|---|");
                    foreach (var question in item.Questions)
                    {
                        asked[(question.Kind, question.Difficulty)] = asked.GetValueOrDefault((question.Kind, question.Difficulty)) + 1;
                        report.Line($"| {question.Kind} | {question.Difficulty} | {question.Answer} | {string.Join("; ", question.Decoys)} |");
                    }
                }
            }
            else
            {
                report.Line($"**{outcome.Kind}**: no rating.");
            }

            report.Line();
            report.Line($"Reply from {chat.LastModel ?? "no model"}:");
            report.Line("```");
            report.Line(chat.LastReply?.Trim() ?? chat.LastError ?? "(none)");
            report.Line("```");
            report.Line();
        }

        report.Line("## Summary");
        report.Line();
        report.Line($"- Outcomes: {string.Join(", ", outcomes.Select(pair => $"{pair.Key} {pair.Value}"))}");
        report.Line($"- With no question: {items.Stored.Count(item => item.Questions.Count == 0)} of {items.Stored.Count} rated");
        foreach (var kind in Enum.GetValues<AiQuizQuestionKind>())
        {
            report.Line($"- {kind}: " + string.Join(", ", Enum.GetValues<AiQuizDifficulty>()
                .Select(difficulty => $"{difficulty} {asked.GetValueOrDefault((kind, difficulty))}")));
        }

        report.Line($"- {stack.RequestsSent} request(s) sent in {(DateTimeOffset.UtcNow - started).TotalSeconds:N0} s; pictures in {images.Directory}");
        Console.Write(report.ToString());
    }

    private static string Flatten(string text, int maxLength)
    {
        var flat = string.Join(' ', text.Split(default(char[]), StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= maxLength ? flat : $"{flat[..maxLength]}…";
    }

    /// <summary>The model's raw reply to the last request, which the rater reads and does not keep.</summary>
    private sealed class LastReplyChatModelClient(IChatModelClient inner) : IChatModelClient
    {
        public string? LastReply { get; private set; }

        public string? LastModel { get; private set; }

        public string? LastError { get; private set; }

        public void Clear() => (LastReply, LastModel, LastError) = (null, null, null);

        public async Task<Result<AiChatResponse>> CompleteAsync(AiChatRequest request, CancellationToken cancellationToken = default)
        {
            var result = await inner.CompleteAsync(request, cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                (LastReply, LastModel) = (result.Value.Text, result.Value.ModelUsed);
            }
            else
            {
                LastError = $"{result.Error.Code}: {result.Error.Message}";
            }

            return result;
        }

        public Task<Result<IReadOnlyList<ChatModelDescriptor>>> ReadFreeModelsAsync(CancellationToken cancellationToken = default) =>
            inner.ReadFreeModelsAsync(cancellationToken);

        public Task<Result<ChatModelHealth>> ReadModelHealthAsync(string modelId, CancellationToken cancellationToken = default) =>
            inner.ReadModelHealthAsync(modelId, cancellationToken);
    }

    /// <summary>The ratings the rater would store, kept for the report.</summary>
    private sealed class InMemoryQuizItems : IAiQuizItemRepository
    {
        public List<AiQuizItem> Stored { get; } = [];

        public Task ReplaceAsync(AiQuizItem item, CancellationToken cancellationToken = default)
        {
            Stored.Add(item);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AiQuizItem>> DrawAsync(QuizItemDraw draw, int count, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The probe draws from the index.");

        public Task<IReadOnlyDictionary<Guid, string>> ReadContentHashesAsync(IReadOnlyCollection<Guid> chunkIds, int version, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());

        public Task RemoveAsync(IReadOnlyCollection<Guid> chunkIds, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<(int Rated, int WithQuestions)> CountAsync(int version, CancellationToken cancellationToken = default) =>
            Task.FromResult((Stored.Count, Stored.Count(item => item.Questions.Count > 0)));
    }

    /// <summary>
    /// The relay the rater reads pictures from, standing in for the bot's: each picture is downloaded
    /// from its link (GET only), saved here to look at, and handed to the model inline, as the bot hands it.
    /// </summary>
    private sealed class PictureFolder : IImageRelay, IDisposable
    {
        private readonly HttpClient _http = new(new ReadOnlyHttpHandler(new HttpClientHandler())) { Timeout = TimeSpan.FromSeconds(30) };
        private readonly Dictionary<string, string> _paths = new(StringComparer.Ordinal);
        private readonly string? _publicBaseUrl;

        public PictureFolder(string directory, string? publicBaseUrl)
        {
            Directory = directory;
            _publicBaseUrl = string.IsNullOrWhiteSpace(publicBaseUrl) ? null : publicBaseUrl;
            System.IO.Directory.CreateDirectory(directory);
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("GeoClubBot/1.0 retrieval probe (+https://github.com/efibs/geo-club-bot)");
        }

        public string Directory { get; }

        public bool IsEnabled => true;

        public string? PathOf(string imageUrl) => _paths.GetValueOrDefault(imageUrl);

        public async Task<string?> ReadAsDataUrlAsync(string imageUrl, CancellationToken cancellationToken = default)
        {
            try
            {
                // A relayed link names the host the index was built on; --set AI:ImageRelay:PublicBaseUrl moves it.
                var url = RelayedImageUrl.Rebase(imageUrl, _publicBaseUrl);
                using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    Console.Error.WriteLine($"  {url} answered {(int)response.StatusCode}; the model gets the link instead.");
                    return null;
                }

                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
                var path = Path.Combine(Directory, $"{_paths.Count + 1:D3}{RelayedImageUrl.ExtensionFor(contentType) ?? ".jpg"}");
                await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
                _paths[imageUrl] = path;

                return $"data:{contentType};base64,{Convert.ToBase64String(bytes)}";
            }
            catch (HttpRequestException exception)
            {
                Console.Error.WriteLine($"  {imageUrl} could not be read ({exception.Message}); the model gets the link instead.");
                return null;
            }
        }

        public Task<string> ResolveAsync(string imageUrl, CancellationToken cancellationToken = default) => throw Refused();

        public Task<Result<string>> StoreAsync(byte[] content, string? contentType, CancellationToken cancellationToken = default) => throw Refused();

        public Task<RelayedImageContent?> ReadAsync(string name, CancellationToken cancellationToken = default) => throw Refused();

        public Task<ImageRelayReachability> CheckPublicReachabilityAsync(CancellationToken cancellationToken = default) => throw Refused();

        public void Dispose() => _http.Dispose();

        private static InvalidOperationException Refused() => new("The probe's picture folder only reads pictures for the model.");
    }
}
