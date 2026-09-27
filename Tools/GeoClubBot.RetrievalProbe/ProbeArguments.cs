namespace GeoClubBot.RetrievalProbe;

/// <summary>Command line of the probe. Every command reads; there is nothing to write with.</summary>
/// <param name="Target">The export file for <c>replay</c> and <c>compare</c>, the pattern for <c>grep</c>.</param>
/// <param name="Rating">"positive" or "negative" when only one kind of rated answer should be replayed.</param>
public sealed record ProbeArguments(
    string Command,
    string? Target,
    string? Qdrant,
    string? Collection,
    int Limit,
    string? Rating,
    string? TargetsPath,
    string? Question,
    string? Source,
    IReadOnlyList<string> Variants,
    int Max,
    string? OutputPath)
{
    /// <summary>What the bot offers the model per question (its RetrievalLimit).</summary>
    public const int DefaultLimit = 8;

    public static ProbeArguments? Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return null;
        }

        var command = args[0];
        string? target = null, qdrant = null, collection = null, rating = null, targets = null;
        string? question = null, source = null, output = null;
        var variants = new List<string>();
        var limit = DefaultLimit;
        var max = 20;

        for (var i = 1; i < args.Length; i++)
        {
            var option = args[i];
            switch (option)
            {
                case "--qdrant":
                case "--collection":
                case "--targets":
                case "--question":
                case "--source":
                case "--variant":
                case "--out":
                    if (!TryTake(args, ref i, out var value))
                    {
                        Console.Error.WriteLine($"{option} needs a value.");
                        return null;
                    }

                    switch (option)
                    {
                        case "--qdrant": qdrant = value; break;
                        case "--collection": collection = value; break;
                        case "--targets": targets = value; break;
                        case "--question": question = value; break;
                        case "--source": source = value; break;
                        case "--variant": variants.Add(value!); break;
                        default: output = value; break;
                    }

                    break;

                case "--limit":
                    if (!TryTake(args, ref i, out var limitText) || !int.TryParse(limitText, out limit) || limit <= 0)
                    {
                        Console.Error.WriteLine("--limit needs a positive integer.");
                        return null;
                    }

                    break;

                case "--max":
                    if (!TryTake(args, ref i, out var maxText) || !int.TryParse(maxText, out max) || max <= 0)
                    {
                        Console.Error.WriteLine("--max needs a positive integer.");
                        return null;
                    }

                    break;

                case "--rating":
                    TryTake(args, ref i, out var ratingText);
                    rating = ratingText switch
                    {
                        "good" or "positive" => "positive",
                        "bad" or "negative" => "negative",
                        _ => null
                    };

                    if (rating is null)
                    {
                        Console.Error.WriteLine("--rating takes good or bad.");
                        return null;
                    }

                    break;

                default:
                    if (option.StartsWith("--", StringComparison.Ordinal))
                    {
                        Console.Error.WriteLine($"Unknown option '{option}'.");
                        return null;
                    }

                    target ??= option;
                    break;
            }
        }

        return new ProbeArguments(
            command, target, qdrant, collection, limit, rating, targets, question, source, variants, max, output);
    }

    public static void PrintUsage() =>
        Console.Error.WriteLine(
            """
            GeoClubBot.RetrievalProbe - replays questions through the bot's own retrieval. Read-only.

            Usage:
              dotnet run --project Tools/GeoClubBot.RetrievalProbe -- <command> [options]

            Commands:
              replay <export.jsonl>   For every rated answer in an /ai feedback-export file: what retrieval
                                      offers now, where each excerpt came from, and what changed since
                                      the answer was rated.
              compare <export.jsonl>  The same questions under other fusion weights and approximate search,
                                      counted side by side.
              grep <regex>            Every indexed chunk whose text matches - was the answer there to find?
              similarity              How close a chunk sits to a question, as stored and as re-worded.
                                      Needs --question and --source; --variant re-words the chunk.

            Options:
              --qdrant <url>          Qdrant's gRPC address (default: QDRANT_GRPC_URL, appsettings.Local.json,
                                      then http://localhost:16334)
              --collection <name>     Collection to read (default: the bot's own)
              --limit <n>             Excerpts per question (default 8, as the bot offers)
              --rating good|bad       replay/compare: only answers rated this way
              --targets <file>        compare: JSON of { "words in the question": "regex an on-target
                                      excerpt matches" }, to count on-target excerpts
              --question <text>       similarity: the question
              --source <url>          similarity: the chunk's source URL, as listed under an answer
              --variant <text>        similarity: re-worded chunk text to try (repeatable)
              --max <n>               grep: matches to show (default 20)
              --out <file>            Also write the report to a file

            Questions are embedded with OpenRouter (key: OPENROUTER_API_KEY, appsettings.Local.json, or the
            bot's development settings) and cached, so a rerun costs nothing. See README.md.
            """);

    private static bool TryTake(string[] args, ref int index, out string? value)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            value = null;
            return false;
        }

        value = args[++index];
        return true;
    }
}
