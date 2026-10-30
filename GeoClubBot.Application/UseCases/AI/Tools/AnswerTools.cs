using UseCases.OutputPorts.AI;

namespace UseCases.UseCases.AI.Tools;

/// <summary>
/// The tools the answering model may call, as it is told of them.
///
/// Each description says when to call the tool and when not to, because that is all a model reads
/// about it: the question already comes with excerpts, and a model that searches again for what they
/// say spends a request and the reader's time for nothing. Each points at a cause of a bad answer in
/// the 2026-10-23 feedback export:
/// <list type="bullet">
/// <item><see cref="SearchGuides"/>: a player's words are not the guides' — "snow bollards" found the
/// Finnish snow-pole tip 43rd, "snow poles" 4th — and a country the first search missed.</item>
/// <item><see cref="ReadGuide"/>: the answer was in a guide that was offered, in a part that was not
/// (Baja California's plants, under "Sonora and BCS" and "El Vizcaíno, BCS").</item>
/// <item><see cref="SpeciesRange"/>: no guide says where a tree grows ("I am pretty sure you can also
/// find Khasi pines in other countries such as probably Thailand").</item>
/// </list>
///
/// The answer replay of 2026-10-29 (49 rated questions, tools on) showed the first wording undersold
/// them: tools were called in 15 answers, species_range never — not for either Khasi-pine question —
/// and read_guide not for Baja California, whose rater wrote "the second guide definitely mentions
/// other plants". "Don't call a tool when the excerpts already answer the question" let excerpts that
/// answered part of it count as enough, so a partial answer is now named as the reason to call. And
/// every answer that took three rounds made one search per round, so the model is told it can make
/// several calls at once.
/// </summary>
public static class AnswerTools
{
    public const string SearchGuides = "search_guides";
    public const string ReadGuide = "read_guide";
    public const string SpeciesRange = "species_range";

    /// <summary>
    /// Added to the system prompt only when tools are offered — a model told of tools it cannot call
    /// tries to call them in prose. It closes the main prompt (<c>AiPromptBuilder.SystemPrompt</c>) and
    /// leans on it: the citing, the pictures and the rules for answering are said there once. And it
    /// leaves when each tool is worth a call to that tool's description, which the model reads where it
    /// calls it, so no condition is sent twice; this says only what holds across the tools. A partial
    /// answer is the reason to call. Several calls fit in one round: every answer of the 2026-10-29
    /// replay that took three rounds made one search per round. The asked thing is searched for before
    /// a related one is offered in its place — the main prompt says the two are not the same, and "I
    /// asked about snake traps. Not possum guards" was answered from the possum-guard excerpt without a
    /// search (2026-10-29). And searching comes before the main prompt's own knowledge.
    ///
    /// Kept short, as the main prompt is, because free models follow short prompts better; a test pins
    /// its length.
    /// </summary>
    public const string SystemPrompt = """
        Call tools when the excerpts answer only part of the question; their results are numbered
        excerpts too, cited the same way. You can make several calls at once: search for every place
        or term you need in one round. When the excerpts describe something related but different from
        what was asked, search the user's own words first; only if nothing turns up, say that the
        guides cover the one, not the other. Search before you say the guides don't cover something or
        answer from your own knowledge. Don't call a tool when the excerpts fully answer the question.
        """;

    /// <summary>
    /// Sent as the last message of the call that has to answer after the tool rounds, beside
    /// tool_choice "none". Told only by the request's parameter, nemotron-3-ultra wrote the call it
    /// still wanted as text in 6 of the 11 answers that reached that call (2026-10-29); a user message
    /// after the tool results is valid in the OpenAI chat schema, which OpenRouter speaks.
    /// </summary>
    public const string AnswerNow =
        "No more tool calls are possible, so don't write one. Answer the question now from the excerpts above, "
        + "citing them, or say briefly that the guides don't cover it.";

    private static readonly AiToolDefinition SearchGuidesTool = new(
        SearchGuides,
        "Search the community GeoGuessr guides again, in your own words. Use it when the excerpts answer only "
        + "part of the question: it asks for every, all or exactly which places and they name only some; it uses "
        + "other terms than the guides (they say \"snow poles\" where a player says \"snow bollards\"); it names a "
        + "country or region they miss; or they describe something related but different. Returns up to 6 new "
        + "numbered excerpts, cited like the others, and says so when none of them is from the country searched. "
        + "Don't use it when the excerpts fully answer the question.",
        """
        {
          "type": "object",
          "properties": {
            "query": { "type": "string", "description": "What to look for, in the words a guide would use, e.g. \"pole supports\"." },
            "country": { "type": "string", "description": "Optional. A country to prefer, e.g. \"Brazil\"; guides of other countries can still come back." },
            "pictures_only": { "type": "boolean", "description": "Optional. True to find only excerpts that come with a picture." }
          },
          "required": ["query"]
        }
        """);

    private static readonly AiToolDefinition ReadGuideTool = new(
        ReadGuide,
        "Read more of the guide one of the numbered excerpts came from: the sections around it. Use it when "
        + "that guide covers the question in more detail than its excerpt shows: a list of regions or plants cut "
        + "short, or the user says the guide says more. Returns up to 8 new numbered excerpts from that guide. "
        + "Don't use it when the excerpt fully answers the question.",
        """
        {
          "type": "object",
          "properties": {
            "excerpt": { "type": "integer", "description": "The number of the excerpt whose guide to read, e.g. 3." }
          },
          "required": ["excerpt"]
        }
        """);

    private static readonly AiToolDefinition SpeciesRangeTool = new(
        SpeciesRange,
        "Where a plant, tree or crop species is recorded, by country, or by region within one country, "
        + "from the GBIF biodiversity database. Use it for any question about where a plant, tree or crop grows "
        + "or in which countries or regions it is found: guides rarely list every country, so excerpts naming "
        + "some are a reason to call it, not to skip it. Returns one numbered excerpt, cited like the others. "
        + "Don't use it for anything but a plant.",
        """
        {
          "type": "object",
          "properties": {
            "name": { "type": "string", "description": "Scientific or common name, e.g. \"Pinus kesiya\" or \"khasi pine\"." },
            "country": { "type": "string", "description": "Optional. A country whose regions to break the records down by, e.g. \"Mexico\"." }
          },
          "required": ["name"]
        }
        """);

    /// <summary>Every tool's name, declared or not: a reply that writes a call of any of them out as text is not an answer.</summary>
    public static IReadOnlyList<string> Names { get; } = [SearchGuides, ReadGuide, SpeciesRange];

    /// <summary>The tools to declare, <see cref="SpeciesRange"/> only when it is on.</summary>
    public static IReadOnlyList<AiToolDefinition> Definitions(bool speciesRange) =>
        speciesRange ? [SearchGuidesTool, ReadGuideTool, SpeciesRangeTool] : [SearchGuidesTool, ReadGuideTool];
}
