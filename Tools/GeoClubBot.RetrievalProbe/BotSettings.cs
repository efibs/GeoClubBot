using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Configuration;
using Microsoft.Extensions.Configuration;

namespace GeoClubBot.RetrievalProbe;

/// <summary>
/// The AI settings <c>answer</c> runs the bot with: the bot's own <c>GeoClubBot.API/appsettings.json</c>,
/// with <c>--set Key=Value</c> overrides on top, bound into the bot's configuration classes the way its
/// host binds them. Not its development settings: those pick another collection and other models, and
/// what is measured should be what production ships. The environment is not read either, so a
/// variable left set in a shell cannot change a measurement unseen.
/// </summary>
public sealed class BotSettings
{
    public const string SettingsFileName = "appsettings.json";

    public required AiConfiguration Ai { get; init; }

    public required AiConversationConfiguration Conversation { get; init; }

    public required AiRetrievalConfiguration Retrieval { get; init; }

    public required AiToolsConfiguration Tools { get; init; }

    public required AiImageRelayConfiguration ImageRelay { get; init; }

    /// <summary>The overrides applied, as given, for the report.</summary>
    public required IReadOnlyList<string> Overrides { get; init; }

    /// <summary>
    /// Short and stable: a hash of every setting the answer depends on, the key aside, so a cached answer
    /// is reused only under exactly the settings it was produced with.
    /// </summary>
    public required string Fingerprint { get; init; }

    /// <summary>
    /// Reads <paramref name="settingsPath"/> and applies <paramref name="overrides"/>; the key is set
    /// afterwards, so it never reaches the fingerprint or the report.
    /// </summary>
    /// <exception cref="ProbeException">An override is malformed, or a setting is out of the range the bot accepts.</exception>
    public static BotSettings Load(string settingsPath, IReadOnlyList<string> overrides, string? apiKey)
    {
        if (!File.Exists(settingsPath))
        {
            throw new ProbeException($"The bot's settings were not found at {settingsPath}.");
        }

        var pairs = new List<KeyValuePair<string, string?>>();
        foreach (var setting in overrides)
        {
            var separator = setting.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                throw new ProbeException($"--set {setting}: expected Key=Value, e.g. AI:Tools:Enabled=false.");
            }

            var key = setting[..separator].Trim().Replace("__", ":", StringComparison.Ordinal);
            if (!key.StartsWith(AiConfiguration.SectionName + ":", StringComparison.OrdinalIgnoreCase))
            {
                throw new ProbeException($"--set {setting}: only the bot's AI settings can be set (keys starting AI:).");
            }

            pairs.Add(new KeyValuePair<string, string?>(key, setting[(separator + 1)..]));
        }

        // The settings file carries comments; the configuration reader skips them, as the bot's host does.
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.GetFullPath(settingsPath), optional: false, reloadOnChange: false)
            .AddInMemoryCollection(pairs)
            .Build();

        var ai = Bind<AiConfiguration>(configuration, AiConfiguration.SectionName);
        var conversation = Bind<AiConversationConfiguration>(configuration, AiConversationConfiguration.SectionName);
        var retrieval = Bind<AiRetrievalConfiguration>(configuration, AiRetrievalConfiguration.SectionName);
        var tools = Bind<AiToolsConfiguration>(configuration, AiToolsConfiguration.SectionName);
        var relay = Bind<AiImageRelayConfiguration>(configuration, AiImageRelayConfiguration.SectionName);

        // Validated as the bot validates them at start-up: a typo in an override is worth stopping for.
        Validate(retrieval, AiRetrievalConfiguration.SectionName);
        Validate(tools, AiToolsConfiguration.SectionName);

        ai.OpenRouter.ApiKey = string.Empty;
        var fingerprint = ComputeFingerprint(ai, conversation, retrieval, tools);
        ai.OpenRouter.ApiKey = apiKey ?? string.Empty;

        return new BotSettings
        {
            Ai = ai,
            Conversation = conversation,
            Retrieval = retrieval,
            Tools = tools,
            ImageRelay = relay,
            Overrides = [.. overrides],
            Fingerprint = fingerprint
        };
    }

    /// <summary>The bot's settings file, found from the probe upwards.</summary>
    public static string DefaultPath(string repositoryRoot) =>
        Path.Combine(repositoryRoot, "GeoClubBot.API", SettingsFileName);

    private static T Bind<T>(IConfiguration configuration, string section)
        where T : new()
    {
        var bound = new T();
        configuration.GetSection(section).Bind(bound);
        return bound;
    }

    private static void Validate(object settings, string section)
    {
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(settings, new ValidationContext(settings), results, validateAllProperties: true))
        {
            throw new ProbeException(
                $"{section}: {string.Join("; ", results.Select(result => result.ErrorMessage))}");
        }
    }

    /// <summary>
    /// The image relay is left out: it decides how a picture is delivered, never what is answered. The
    /// chat and embedding pipelines' own limits are in, since they decide which requests get through.
    /// </summary>
    private static string ComputeFingerprint(
        AiConfiguration ai,
        AiConversationConfiguration conversation,
        AiRetrievalConfiguration retrieval,
        AiToolsConfiguration tools)
    {
        var json = JsonSerializer.Serialize(new { ai, conversation, retrieval, tools });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))[..12].ToLowerInvariant();
    }
}
