using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Infrastructure.OutputAdapters.AI;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Conversations;
using UseCases.UseCases.AI.Tools;

namespace GeoClubBot.RetrievalProbe;

/// <summary>What one answer cost: requests that left for OpenRouter, and what the bot claimed from its allowance.</summary>
/// <param name="Claimed">Requests the bot claimed from its daily allowance for the answer.</param>
public sealed record AnswerCost(int Embeddings, int Chats, int Claimed, long PromptTokens, long CompletionTokens)
{
    /// <summary>Requests that left: the embeddings and chat completions, retries included.</summary>
    public int Requests => Embeddings + Chats;
}

/// <summary>
/// A rated question asked again through the bot's answering code, and everything the report shows of it.
/// Kept in the cache when it answered, so a rerun — after a 429, or to judge — costs nothing.
/// </summary>
/// <param name="Key">The cache key: the record, the settings, and the bot's code (<see cref="AnswerCache.KeyFor"/>).</param>
/// <param name="Answer">The bot's answer, as its gateway would render and post it; null when it failed.</param>
/// <param name="ErrorCode">The failure's code when the bot answered with an error instead.</param>
/// <param name="ExcerptsShown">
/// The excerpts the answer was written from, as the model was shown them: the prompt's excerpts, then
/// every tool result. What the judge checks the answer against.
/// </param>
/// <param name="Notes">How the question was asked differently from production (an expired screenshot left out).</param>
/// <param name="Log">What the bot logged while answering.</param>
/// <param name="ModelText">
/// The answer as the model wrote it, its [n] the numbers of <paramref name="ExcerptsShown"/>; the posted
/// text renumbers them 1, 2, 3 in citation order. Null in runs cached before it was kept.
/// </param>
public sealed record AnswerRun(
    string Key,
    Guid FeedbackId,
    int Number,
    DateTimeOffset AnsweredAtUtc,
    AiAnswer? Answer,
    string? ErrorCode,
    string? ErrorMessage,
    IReadOnlyList<ChatCallRecord> ChatCalls,
    AnswerCost Cost,
    double Seconds,
    string ExcerptsShown,
    IReadOnlyList<string> Notes,
    IReadOnlyList<string> Log,
    string? ModelText = null)
{
    public bool Succeeded => Answer is not null;

    /// <summary>
    /// The text the model was shown to answer from: the excerpts of the prompt that answered (between
    /// "Guide excerpts:" and the question) and every tool result after it. Empty when no request succeeded.
    /// </summary>
    public static string DescribeExcerpts(IReadOnlyList<AiChatMessage>? messages)
    {
        if (messages is null || messages.Count == 0)
        {
            return string.Empty;
        }

        // The question's own prompt, not the "answer now" the call after the last tool round ends with.
        var text = new StringBuilder();
        var prompt = messages
            .Where(message => message.Role == AiChatRole.User)
            .Select(message => message.ToPlainText())
            .LastOrDefault(content => content != AnswerTools.AnswerNow) ?? string.Empty;
        var question = prompt.LastIndexOf("\nQuestion: ", StringComparison.Ordinal);
        text.AppendLine((question >= 0 ? prompt[..question] : prompt).Trim());

        var results = messages.Where(message => message.Role == AiChatRole.Tool).ToList();
        foreach (var result in results)
        {
            text.AppendLine().AppendLine("Tool result:").AppendLine(result.ToPlainText().Trim());
        }

        return text.ToString().Trim();
    }
}

/// <summary>
/// Answers and verdicts kept on disk, one file each, so a run interrupted by a 429 resumes where it
/// stopped, and a report can be rewritten without asking again. A failure is never kept: it says
/// something about the moment, not the question.
/// </summary>
public sealed class AnswerCache(string directory)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string Directory => directory;

    /// <summary>
    /// The record, the settings it was answered under, and the build of the bot's answering code: the
    /// same question under the same settings is answered again only after that code changed. Builds are
    /// deterministic, so the build stands for the code — rebuilding unchanged code keeps the cache.
    /// </summary>
    public static string KeyFor(Guid feedbackId, string settingsFingerprint) =>
        $"{feedbackId:N}-{settingsFingerprint}-{CodeVersion}";

    /// <summary>The bot's answering code (Application) and its adapters (Infrastructure), as built.</summary>
    public static string CodeVersion { get; } = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"{typeof(AskAiHandler).Assembly.ManifestModule.ModuleVersionId:N}{typeof(QdrantKnowledgeIndex).Assembly.ManifestModule.ModuleVersionId:N}")))[..8]
        .ToLowerInvariant();

    public AnswerRun? ReadAnswer(string key) => Read<AnswerRun>(Path.Combine(directory, $"{key}.json"));

    public void WriteAnswer(AnswerRun run) => Write(Path.Combine(directory, $"{run.Key}.json"), run);

    public JudgeVerdict? ReadVerdict(string key) => Read<JudgeVerdict>(Path.Combine(directory, "judge", $"{key}.json"));

    public void WriteVerdict(string key, JudgeVerdict verdict) => Write(Path.Combine(directory, "judge", $"{key}.json"), verdict);

    private static T? Read<T>(string path)
        where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json);
        }
        catch (JsonException)
        {
            // A cache is only ever a saving; a damaged entry is asked again rather than trusted.
            return null;
        }
    }

    private static void Write<T>(string path, T value)
    {
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, Json));
        File.Move(temporary, path, overwrite: true);
    }
}
