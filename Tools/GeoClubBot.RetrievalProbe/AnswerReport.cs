using System.Globalization;
using System.Text;
using Configuration;
using GeoClubBot.Discord.InputAdapters.Interactions.AI;
using UseCases.OutputPorts.AI;
using UseCases.UseCases.AI.Conversations;

namespace GeoClubBot.RetrievalProbe;

/// <summary>One rated answer in the report: the record, what it got now (null when it was not asked), and the judge's verdict.</summary>
public sealed record AnswerReportRow(RatedQuestion Rated, AnswerRun? Run, JudgeVerdict? Verdict);

/// <summary>What the report says about the run as a whole.</summary>
/// <param name="TextChain">The chain a text question is asked of today, best first.</param>
/// <param name="ToolChain">The chain a question with tools is asked of today; empty when tools are off.</param>
/// <param name="JudgeChain">The judge's chain; null without <c>--judge</c>.</param>
/// <param name="SetupRequests">Requests spent before any answer: the roster and the models' health.</param>
/// <param name="JudgeRequests">Requests the judge sent this run (cached verdicts cost none).</param>
public sealed record AnswerReportHeader(
    string ExportName,
    string Collection,
    DateTimeOffset RunAtUtc,
    BotSettings Settings,
    IReadOnlyList<string> TextChain,
    IReadOnlyList<string> ToolChain,
    IReadOnlyList<string>? JudgeChain,
    int SetupRequests,
    int JudgeRequests);

/// <summary>
/// The <c>answer</c> report: a summary, then every rated answer beside the one it gets now, rendered as
/// the bot's gateway would post it. Pure, so its layout is tested without a model.
/// </summary>
public static class AnswerReport
{
    private const int QuestionCellLength = 60;

    public static string Render(AnswerReportHeader header, IReadOnlyList<AnswerReportRow> rows)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(rows);

        var text = new StringBuilder();
        WriteHeader(text, header, rows);
        WriteSummary(text, header, rows);

        foreach (var row in rows)
        {
            WriteRecord(text, row, header.Settings.ImageRelay);
        }

        return text.ToString();
    }

    /// <summary>The answer as Discord shows it — message chunks with the sources beneath — and its pictures, one line each.</summary>
    public static (string Message, IReadOnlyList<string> Pictures) Posted(AiAnswer answer, AiImageRelayConfiguration relay)
    {
        ArgumentNullException.ThrowIfNull(answer);

        // Nothing is read from the bot's disk here, so a relayed picture plans as missing and is shown
        // by its link; in production the gateway uploads it from the disk instead.
        var plan = AiAnswerPicturePlanner.Plan(answer.Images, new Dictionary<string, StoredPicture>(), relay);
        var rendering = AiAnswerFormatter.Render(answer, plan);

        var pictures = rendering.Embeds
            .Zip(plan.Pictures
                .Take(AiAnswerPicturePlanner.MaxPicturesPerMessage)
                .GroupBy(picture => picture.Image.SourceUrl, StringComparer.Ordinal)
                .SelectMany(group => group))
            .Select(pair => $"{pair.First.Title} — {Link(pair.Second.Image.ImageUrl)}"
                            + (RelayedImageUrl.TryReadName(pair.Second.Image.ImageUrl, out _)
                                ? " (relayed: production uploads it from its disk)"
                                : " (linked to its host)")
                            + $" · from {Link(pair.Second.Image.SourceUrl)}")
            .ToList();

        return (string.Join("\n", rendering.MessageChunks), pictures);
    }

    private static void WriteHeader(StringBuilder text, AnswerReportHeader header, IReadOnlyList<AnswerReportRow> rows)
    {
        var settings = header.Settings;
        text.AppendLine($"# Answer replay: {rows.Count} rated answer(s) of {header.ExportName}");
        text.AppendLine();
        text.AppendLine($"Asked again on {header.RunAtUtc:yyyy-MM-dd HH:mm} UTC through the bot's own answering code — `AskAiHandler`, "
                        + "its retrieval, model chain and tool loop, the GBIF reader — against the index collection "
                        + $"`{header.Collection}`, read-only. Settings: `GeoClubBot.API/appsettings.json`"
                        + (settings.Overrides.Count == 0
                            ? ", unchanged"
                            : $" with {string.Join(", ", settings.Overrides.Select(setting => $"`{setting}`"))}")
                        + $" (fingerprint `{settings.Fingerprint}`, bot build `{AnswerCache.CodeVersion}`).");
        text.AppendLine();
        text.AppendLine($"- Tools: {(settings.Tools.Enabled ? $"on, up to {settings.Tools.MaxToolRounds} round(s), species range {(settings.Tools.SpeciesRange.Enabled ? "on" : "off")}" : "off")}; "
                        + $"excerpts per question: {settings.Retrieval.ExcerptLimit}; query planner: {(settings.Retrieval.QueryPlanner.Enabled ? "on" : "off")}; "
                        + $"country boost: {(settings.Retrieval.CountryBoost.Enabled ? "on" : "off")}; pictures per answer: {settings.Ai.MaxImagesInReply}.");
        text.AppendLine($"- Chain today, text: {Chain(header.TextChain)}; with tools: {(header.ToolChain.Count == 0 ? "–" : Chain(header.ToolChain))}.");
        text.AppendLine("- Each answer is asked as the first question of a day (an allowance that never runs out), in a branch rebuilt "
                        + "from the export: follow-ups get their branch, and the excerpts the answer before cited where the export kept them.");
        text.AppendLine("- Not the bot: no Discord, nothing stored, and pictures are not uploaded — a relayed picture is listed by its link.");

        if (header.JudgeChain is { } judge)
        {
            text.AppendLine($"- Judge: {Chain(judge)}. **Its verdicts are indicative**: one free model's reading of two answers, "
                            + "blind to pictures, possibly the model that wrote the new answer, and not the member who rated the old one.");
        }
    }

    private static void WriteSummary(StringBuilder text, AnswerReportHeader header, IReadOnlyList<AnswerReportRow> rows)
    {
        var answered = rows.Where(row => row.Run?.Succeeded == true).ToList();
        var failed = rows.Where(row => row.Run is { Succeeded: false }).ToList();
        var notAsked = rows.Count(row => row.Run is null);

        text.AppendLine();
        text.AppendLine("## Summary");
        text.AppendLine();

        if (header.JudgeChain is not null)
        {
            var negative = rows.Where(row => row.Rated.IsNegative).ToList();
            var positive = rows.Where(row => !row.Rated.IsNegative).ToList();

            text.AppendLine($"| judge's verdict on the new answer | all ({rows.Count}) | 👎 ({negative.Count}) | 👍 ({positive.Count}) |");
            text.AppendLine("|---|---|---|---|");
            foreach (var (label, verdict) in new[] { ("better", "better"), ("about the same", "same"), ("worse", "worse") })
            {
                text.AppendLine($"| {label} | {CountVerdict(rows, verdict)} | {CountVerdict(negative, verdict)} | {CountVerdict(positive, verdict)} |");
            }

            text.AppendLine($"| not judged | {CountUnjudged(rows)} | {CountUnjudged(negative)} | {CountUnjudged(positive)} |");

            var commented = rows.Where(row => !string.IsNullOrWhiteSpace(row.Rated.Record.Comment) && row.Verdict?.IsReadable == true).ToList();
            var negativeCommented = commented.Where(row => row.Rated.IsNegative).ToList();
            text.AppendLine();
            text.AppendLine($"- Addresses the member's comment: {commented.Count(row => row.Verdict!.AddressesComment == true)} of {commented.Count} "
                            + $"commented answers judged ({negativeCommented.Count(row => row.Verdict!.AddressesComment == true)} of {negativeCommented.Count} 👎).");
            text.AppendLine($"- Hallucination risk of the new answers: "
                            + string.Join(", ", new[] { "low", "medium", "high" }.Select(risk =>
                                $"{risk} {rows.Count(row => row.Verdict?.HallucinationRisk == risk)}"))
                            + ".");
        }

        var calls = answered.SelectMany(row => row.Run!.Answer!.ToolCalls ?? []).ToList();
        var seconds = answered.Select(row => row.Run!.Seconds).Order().ToList();
        var embeddings = rows.Sum(row => row.Run?.Cost.Embeddings ?? 0);
        var chats = rows.Sum(row => row.Run?.Cost.Chats ?? 0);

        text.AppendLine($"- Answered: {answered.Count}; failed: {failed.Count}"
                        + (failed.Count == 0 ? string.Empty : $" ({string.Join(", ", failed.Select(row => $"#{row.Rated.Number} {row.Run!.ErrorCode}"))})")
                        + (notAsked == 0 ? "." : $"; not asked: {notAsked}."));
        text.AppendLine($"- Tool calls: {calls.Count} in {answered.Count(row => row.Run!.Answer!.ToolCalls is { Count: > 0 })} answer(s)"
                        + (calls.Count == 0
                            ? "."
                            : $" — {string.Join(", ", calls.GroupBy(call => call.Tool).OrderByDescending(group => group.Count()).Select(group => $"{group.Key} {group.Count()}"))}."));
        text.AppendLine($"- Answers citing a picture: {answered.Count(row => row.Run!.Answer!.Images.Count > 0)} of {answered.Count}; "
                        + $"citing nothing: {answered.Count(row => row.Run!.Answer!.Sources.All(source => source.Marker is null))}.");
        text.AppendLine($"- Seconds per answer: median {Median(seconds)}, longest {(seconds.Count == 0 ? "–" : seconds[^1].ToString("0.0", CultureInfo.InvariantCulture))}.");
        text.AppendLine($"- Requests to OpenRouter: {embeddings + chats} for answers ({embeddings} embeddings, {chats} chat), "
                        + $"{header.SetupRequests} to read the roster and the models' health, {header.JudgeRequests} for the judge this run. "
                        + $"Answers reused from the cache are counted as they cost when first asked; "
                        + $"the bot would have claimed {rows.Sum(row => row.Run?.Cost.Claimed ?? 0)} from its daily allowance.");
        text.AppendLine($"- Models that answered: {string.Join(", ", answered.GroupBy(row => row.Run!.Answer!.ModelUsed).OrderByDescending(group => group.Count()).Select(group => $"{group.Key} {group.Count()}"))}.");

        text.AppendLine();
        text.AppendLine("| # | rating | question | old model | new model | tool calls | offered → cited (old) | pictures | s | requests"
                        + (header.JudgeChain is null ? " |" : " | verdict | comment | risk |"));
        text.AppendLine("|---|---|---|---|---|---|---|---|---|---" + (header.JudgeChain is null ? "|" : "|---|---|---|"));

        foreach (var row in rows)
        {
            var rated = row.Rated;
            var record = rated.Record;
            var run = row.Run;
            var answer = run?.Answer;
            var line = new StringBuilder();
            line.Append($"| [{rated.Number}](#{Anchor(rated)}) | {Rating(rated)} | {HitDescriptions.Cell(rated.Question, QuestionCellLength)}"
                        + $"{(rated.IsFollowUp ? " ↩" : string.Empty)}{(rated.QuestionHadScreenshot ? " 📷" : string.Empty)} | {ShortModel(record.ModelId)} | ");

            if (answer is null)
            {
                line.Append(run is null ? "not asked | | | | | |" : $"**failed** `{run.ErrorCode}` | | | | {Seconds(run.Seconds)} | {run.Cost.Requests} |");
            }
            else
            {
                line.Append($"{ShortModel(answer.ModelUsed)} | {answer.ToolCalls?.Count ?? 0} | "
                            + $"{answer.RetrievedSourceUrls.Count} → {answer.CitedSourceUrls.Count} ({record.RetrievedSourceUrls.Count} → {record.CitedSourceUrls.Count}) | "
                            + $"{answer.Images.Count} | {Seconds(run!.Seconds)} | {run.Cost.Requests} |");
            }

            if (header.JudgeChain is not null)
            {
                var verdict = row.Verdict;
                line.Append(verdict is null
                    ? " – | | |"
                    : $" {verdict.Verdict ?? "unreadable"} | {Comment(rated, verdict)} | {verdict.HallucinationRisk ?? "–"} |");
            }

            text.AppendLine(line.ToString());
        }

        text.AppendLine();
        text.AppendLine("↩ a follow-up, asked with its branch · 📷 the question carried a screenshot, asked by its text alone (its link has expired) · "
                        + "offered → cited: guides offered to the answer and guides it cited, now and (old) when it was rated.");
    }

    private static void WriteRecord(StringBuilder text, AnswerReportRow row, AiImageRelayConfiguration relay)
    {
        var rated = row.Rated;
        var record = rated.Record;
        var run = row.Run;

        text.AppendLine();
        text.AppendLine($"<a id=\"{Anchor(rated)}\"></a>");
        text.AppendLine($"## #{rated.Number} {Rating(rated)} {HitDescriptions.Cell(rated.Question, 200)}");
        text.AppendLine();
        text.AppendLine($"_Rated {record.CreatedAtUtc:yyyy-MM-dd}, answer depth {record.AnswerDepth}, feedback `{record.FeedbackId:N}`._");

        if (rated.Context.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("**Branch before the question**");
            text.AppendLine();
            foreach (var turn in rated.Context)
            {
                var who = turn.Role == Entities.AiTurnRole.Assistant ? "Bot" : "User";
                text.AppendLine($"> **{who}:** {HitDescriptions.Cell(turn.Content, 400)}{(turn.ImageUrls.Count > 0 ? " 📷" : string.Empty)}");
            }
        }

        text.AppendLine();
        text.AppendLine($"**Question:** {rated.Question.Trim()}");
        text.AppendLine();
        text.AppendLine($"**Rating:** {Rating(rated)}" + (string.IsNullOrWhiteSpace(record.Comment) ? " (no comment)" : string.Empty));
        if (!string.IsNullOrWhiteSpace(record.Comment))
        {
            text.AppendLine();
            Quote(text, record.Comment);
        }

        foreach (var note in run?.Notes ?? [])
        {
            text.AppendLine();
            text.AppendLine($"_{note}_");
        }

        var old = record.Transcript.OrderBy(turn => turn.Ordinal)
            .LastOrDefault(turn => string.Equals(turn.Role, "assistant", StringComparison.OrdinalIgnoreCase));
        text.AppendLine();
        text.AppendLine($"### Old answer ({record.ModelId ?? "model unknown"})");
        text.AppendLine();
        Quote(text, old?.Content ?? "(not in the export)");
        text.AppendLine();
        text.AppendLine($"Cited {record.CitedSourceUrls.Count} of the {record.RetrievedSourceUrls.Count} guides offered"
                        + (record.CitedSourceUrls.Count == 0 ? "." : $": {string.Join(", ", record.CitedSourceUrls.Select(Link))}"));

        if (run is null)
        {
            text.AppendLine();
            text.AppendLine("### New answer: not asked");
            return;
        }

        text.AppendLine();
        if (run.Answer is not { } answer)
        {
            text.AppendLine($"### New answer: failed after {Seconds(run.Seconds)} s");
            text.AppendLine();
            text.AppendLine($"`{run.ErrorCode}`: {run.ErrorMessage}");
            WriteCalls(text, run);
            WriteLog(text, run);
            return;
        }

        text.AppendLine($"### New answer ({answer.ModelUsed}, {Seconds(run.Seconds)} s, {run.Cost.Requests} requests: "
                        + $"{run.Cost.Embeddings} embedding, {run.Cost.Chats} chat)");
        text.AppendLine();

        var (message, pictures) = Posted(answer, relay);
        Quote(text, message);

        if (pictures.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("**Pictures attached**");
            text.AppendLine();
            foreach (var picture in pictures)
            {
                text.AppendLine($"- {picture}");
            }
        }

        if (row.Verdict is { } verdict)
        {
            text.AppendLine();
            text.AppendLine(verdict.IsReadable
                ? $"**Judge** ({verdict.Model}): **{verdict.Verdict}** · addresses the comment: {Comment(rated, verdict)} · "
                  + $"hallucination risk: {verdict.HallucinationRisk ?? "–"} — {verdict.Reasons}"
                : $"**Judge** ({verdict.Model}): reply unreadable — {HitDescriptions.Cell(verdict.Raw ?? string.Empty, 300)}");
        }

        WriteTools(text, answer);
        WriteSources(text, rated, answer);
        WriteCalls(text, run);
        WriteLog(text, run);
    }

    private static void WriteTools(StringBuilder text, AiAnswer answer)
    {
        if (answer.ToolCalls is not { Count: > 0 } calls)
        {
            text.AppendLine();
            text.AppendLine("_No tool calls._");
            return;
        }

        text.AppendLine();
        text.AppendLine("**Tool calls**");
        text.AppendLine();
        text.AppendLine("| round | tool | arguments | outcome | new excerpts | result chars | ms |");
        text.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var call in calls)
        {
            text.AppendLine($"| {call.Round} | {call.Tool} | `{HitDescriptions.Cell(call.Arguments, 140).Replace("`", "'", StringComparison.Ordinal)}` | "
                            + $"{call.Outcome} | {call.NewExcerpts} | {call.ResultCharacters} | {call.Milliseconds} |");
        }
    }

    private static void WriteSources(StringBuilder text, RatedQuestion rated, AiAnswer answer)
    {
        var cited = answer.CitedSourceUrls.ToHashSet(StringComparer.Ordinal);
        var offeredThen = rated.Record.RetrievedSourceUrls.ToHashSet(StringComparer.Ordinal);
        var offeredNow = answer.RetrievedSourceUrls.ToHashSet(StringComparer.Ordinal);
        var dropped = rated.Record.RetrievedSourceUrls.Where(url => !offeredNow.Contains(url)).ToList();

        text.AppendLine();
        text.AppendLine($"**Guides offered** ({answer.RetrievedSourceUrls.Count} now, {offeredThen.Count} then; ✓ cited now, ★ not offered then): "
                        + string.Join(", ", answer.RetrievedSourceUrls.Select(url =>
                            $"{Link(url)}{(cited.Contains(url) ? " ✓" : string.Empty)}{(offeredThen.Contains(url) ? string.Empty : " ★")}")));
        if (dropped.Count > 0)
        {
            text.AppendLine();
            text.AppendLine($"Offered then, not now: {string.Join(", ", dropped.Select(Link))}");
        }
    }

    private static void WriteCalls(StringBuilder text, AnswerRun run)
    {
        if (run.ChatCalls.Count == 0)
        {
            return;
        }

        text.AppendLine();
        text.AppendLine("<details><summary>Chat requests</summary>");
        text.AppendLine();
        text.AppendLine("| # | chain | tools | choice | answered by | finish | asked for | tokens in/out | s | error |");
        text.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        for (var index = 0; index < run.ChatCalls.Count; index++)
        {
            var call = run.ChatCalls[index];
            text.AppendLine($"| {index + 1} | {string.Join(", ", call.Chain.Select(ShortModel))} | {call.ToolsOffered} | {call.ToolChoice} | "
                            + $"{ShortModel(call.ModelUsed)} | {call.FinishReason ?? "–"} | {(call.ToolCalls.Count == 0 ? "–" : string.Join(", ", call.ToolCalls))} | "
                            + $"{call.PromptTokens}/{call.CompletionTokens} | {Seconds(call.Seconds)} | {call.Error ?? string.Empty} |");
        }

        text.AppendLine();
        text.AppendLine("</details>");
    }

    private static void WriteLog(StringBuilder text, AnswerRun run)
    {
        if (run.Log.Count == 0)
        {
            return;
        }

        text.AppendLine();
        text.AppendLine("<details><summary>Bot log</summary>");
        text.AppendLine();
        text.AppendLine("```");
        foreach (var line in run.Log)
        {
            text.AppendLine(line.Replace("```", "'''", StringComparison.Ordinal));
        }

        text.AppendLine("```");
        text.AppendLine();
        text.AppendLine("</details>");
    }

    private static void Quote(StringBuilder text, string content)
    {
        foreach (var line in content.Trim().Split('\n'))
        {
            text.AppendLine(line.TrimEnd().Length == 0 ? ">" : $"> {line.TrimEnd()}");
        }
    }

    private static string CountVerdict(IEnumerable<AnswerReportRow> rows, string verdict) =>
        rows.Count(row => row.Verdict?.Verdict == verdict).ToString(CultureInfo.InvariantCulture);

    private static string CountUnjudged(IEnumerable<AnswerReportRow> rows) =>
        rows.Count(row => row.Verdict?.IsReadable != true).ToString(CultureInfo.InvariantCulture);

    private static string Comment(RatedQuestion rated, JudgeVerdict verdict) =>
        string.IsNullOrWhiteSpace(rated.Record.Comment)
            ? "–"
            : verdict.AddressesComment switch { true => "yes", false => "no", null => "?" };

    private static string Rating(RatedQuestion rated) => rated.IsNegative ? "👎" : "👍";

    private static string Anchor(RatedQuestion rated) => $"r{rated.Number.ToString(CultureInfo.InvariantCulture)}";

    private static string Link(string url) => $"<{url}>";

    private static string Chain(IReadOnlyList<string> chain) => chain.Count == 0 ? "none" : string.Join(" → ", chain.Select(model => $"`{model}`"));

    private static string ShortModel(string? model) =>
        model is null ? "–" : model.Contains('/', StringComparison.Ordinal) ? model[(model.IndexOf('/') + 1)..] : model;

    private static string Seconds(double seconds) => seconds.ToString("0.0", CultureInfo.InvariantCulture);

    private static string Median(IReadOnlyList<double> sorted) =>
        sorted.Count == 0 ? "–" : sorted[sorted.Count / 2].ToString("0.0", CultureInfo.InvariantCulture);
}
