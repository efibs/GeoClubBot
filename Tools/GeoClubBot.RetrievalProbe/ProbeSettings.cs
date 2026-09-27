using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Configuration;

namespace GeoClubBot.RetrievalProbe;

/// <summary>
/// Where the probe finds the index and the embedding key. Resolved from, in order: command-line
/// options, environment variables, <c>appsettings.Local.json</c> beside the project — covered by the
/// repository's <c>appsettings.*.json</c> ignore rule, so nothing in it can be committed by accident —
/// and, for the key alone, the bot's own development settings, so a machine that already runs the bot
/// locally needs nothing more.
/// </summary>
public sealed class ProbeSettings
{
    private const string KeyEnvironmentVariable = "OPENROUTER_API_KEY";
    private const string QdrantEnvironmentVariable = "QDRANT_GRPC_URL";
    private const string LocalSettingsFileName = "appsettings.Local.json";
    private const string DefaultQdrantAddress = "http://localhost:16334";

    private static readonly JsonDocumentOptions Lenient = new()
    {
        // The bot's settings files carry comments.
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public required Uri QdrantAddress { get; init; }

    /// <summary>The embedding model and request settings the bot uses, with defaults from its own configuration.</summary>
    public required OpenRouterConfiguration OpenRouter { get; init; }

    /// <summary>The collection to read: the one named on the command line, or the bot's own.</summary>
    public required string Collection { get; init; }

    public string? ApiKey { get; init; }

    /// <summary>Where <see cref="ApiKey"/> came from, for the run log. The key itself is never printed.</summary>
    public string? ApiKeySource { get; init; }

    public static ProbeSettings? Resolve(ProbeArguments arguments)
    {
        var local = ReadJson(Path.Combine(ProjectDirectory(), LocalSettingsFileName));

        var qdrantText = FirstNonEmpty(
            arguments.Qdrant,
            Environment.GetEnvironmentVariable(QdrantEnvironmentVariable),
            ReadString(local, "Qdrant", "Url"),
            DefaultQdrantAddress);

        if (!Uri.TryCreate(qdrantText, UriKind.Absolute, out var qdrantAddress))
        {
            Console.Error.WriteLine($"'{qdrantText}' is not a Qdrant address; expected e.g. {DefaultQdrantAddress}.");
            return null;
        }

        var (apiKey, apiKeySource) = ResolveApiKey(local);
        var aiDefaults = new AiConfiguration();

        return new ProbeSettings
        {
            QdrantAddress = qdrantAddress,
            OpenRouter = aiDefaults.OpenRouter,
            Collection = arguments.Collection ?? BotCollectionName(aiDefaults),
            ApiKey = apiKey,
            ApiKeySource = apiKeySource
        };
    }

    /// <summary>
    /// The collection the bot writes to — the same derivation as its <c>AiServices.BuildCollectionName</c>,
    /// from the embedding model and its width, because vectors from different models are not comparable.
    /// If the two ever disagree, the probe says so and lists the collections that do exist.
    /// </summary>
    public static string BotCollectionName(AiConfiguration configuration)
    {
        var openRouter = configuration.OpenRouter;
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(openRouter.EmbeddingModelId)))[..8]
            .ToLowerInvariant();

        return $"{configuration.KnowledgeCollectionPrefix}-{fingerprint}-{openRouter.EmbeddingDimensions}";
    }

    /// <summary>The directions printed when a command needs the key and none was found.</summary>
    public static string MissingKeyHelp =>
        $$"""
          No OpenRouter API key found. Questions have to be embedded to be searched for.

          Provide it in one of these ways:

            1. Environment variable:
                 export {{KeyEnvironmentVariable}}='sk-or-v1-...'

            2. {{LocalSettingsFileName}} beside the project (git-ignored):
                 { "OpenRouter": { "ApiKey": "sk-or-v1-..." } }

            3. The bot's own development settings, GeoClubBot.API/appsettings.Development.json,
               under AI:OpenRouter:ApiKey - already there if you run the bot locally.

          Embedding a batch of up to {{new OpenRouterConfiguration().EmbeddingBatchSize}} questions costs one request from the key's
          daily allowance. Vectors are cached, so a rerun costs nothing.
          """;

    private static (string? Key, string? Source) ResolveApiKey(JsonDocument? local)
    {
        if (Environment.GetEnvironmentVariable(KeyEnvironmentVariable) is { Length: > 0 } fromEnvironment)
        {
            return (fromEnvironment.Trim(), $"the {KeyEnvironmentVariable} environment variable");
        }

        if (ReadString(local, "OpenRouter", "ApiKey") is { Length: > 0 } fromLocal)
        {
            return (fromLocal.Trim(), LocalSettingsFileName);
        }

        var botSettings = RepositoryRoot() is { } root
            ? Path.Combine(root, "GeoClubBot.API", "appsettings.Development.json")
            : null;

        if (botSettings is not null
            && ReadString(ReadJson(botSettings), "AI", "OpenRouter", "ApiKey") is { Length: > 0 } fromBot)
        {
            return (fromBot.Trim(), "GeoClubBot.API/appsettings.Development.json (the bot's development key)");
        }

        return (null, null);
    }

    private static JsonDocument? ReadJson(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(File.ReadAllText(path), Lenient);
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"{path} is not valid JSON and was ignored: {ex.Message}");
            return null;
        }
    }

    private static string? ReadString(JsonDocument? document, params string[] path)
    {
        if (document is null)
        {
            return null;
        }

        var element = document.RootElement;
        foreach (var segment in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(segment, out element))
            {
                return null;
            }
        }

        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }

    /// <summary>
    /// The project directory, so <c>dotnet run</c> from anywhere still finds the settings file
    /// (AppContext.BaseDirectory points at bin/&lt;config&gt;/net10.0).
    /// </summary>
    private static string ProjectDirectory() =>
        FindUpwards(directory => directory.GetFiles("*.csproj").Length > 0) ?? AppContext.BaseDirectory;

    private static string? RepositoryRoot() =>
        FindUpwards(directory => directory.GetFiles("GeoClubBot.sln").Length > 0);

    private static string? FindUpwards(Func<DirectoryInfo, bool> isTheOne)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (isTheOne(directory))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    private static string FirstNonEmpty(params string?[] candidates) =>
        candidates.First(candidate => !string.IsNullOrWhiteSpace(candidate))!;
}
